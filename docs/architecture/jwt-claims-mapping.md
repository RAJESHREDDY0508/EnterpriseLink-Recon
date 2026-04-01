# JWT Claims Mapping — Architecture Reference

**Component:** `EnterpriseLink.Auth` — `EnterpriseLinkClaimsTransformation`
**Sprint:** 4 — Story 2
**Relates to:** [ADR-001 Entra ID Authentication](adr/ADR-001-entra-id-authentication.md), [Auth Flow](auth-flow.md)

---

## Overview

After Microsoft.Identity.Web validates an Entra ID JWT, the token's raw claims are
available on `HttpContext.User`. Two of those claims need enrichment before any
downstream service or middleware can use them:

| Raw Entra claim | Problem | Enriched form |
|---|---|---|
| `tid` (directory GUID) | External identifier; services use internal GUIDs | `tenant_id` (internal `Guid`) |
| `roles` (string array) | Not recognised by `[Authorize(Roles=...)]` | `ClaimTypes.Role` |

`EnterpriseLinkClaimsTransformation` (`IClaimsTransformation`) performs both
mappings on every authenticated request inside the `UseAuthentication()` pipeline.

---

## Execution Order in the Pipeline

```
HTTP Request
    │
    ▼
UseAuthentication()
    │   ┌─ Microsoft.Identity.Web ──────────────────────────────────┐
    │   │  1. Validates JWT signature against Entra JWKS            │
    │   │  2. Validates issuer, audience, expiry                    │
    │   │  3. Populates HttpContext.User with raw Entra claims       │
    │   └───────────────────────────────────────────────────────────┘
    │   ┌─ IClaimsTransformation ───────────────────────────────────┐
    │   │  EnterpriseLinkClaimsTransformation.TransformAsync()      │
    │   │  4. Maps tid → tenant_id                                  │
    │   │  5. Maps roles → ClaimTypes.Role                          │
    │   └───────────────────────────────────────────────────────────┘
    │
    ▼
UseTenantResolution() (TenantMiddleware)
    │   6. Reads tenant_id claim → stores in HttpContext.Items
    │
    ▼
UseAuthorization()
    │   7. [Authorize(Roles="Operator")] reads ClaimTypes.Role
    │
    ▼
Controller Action
```

---

## Step 1 — `tid` → `tenant_id` Mapping

### Why two identifiers?

| Identifier | Source | Meaning |
|---|---|---|
| `tid` | Entra ID | Customer's Azure Active Directory GUID (external, Entra-owned) |
| `tenant_id` | EnterpriseLink | Internal domain identifier used across all services |

Coupling internal services to `tid` would bind the domain model to Entra's
identifier space. Using an internal `tenant_id` decouples the two: a customer can
re-register in Entra (new `tid`) without affecting their historical data.

### Mapping source

`ConfigurationTenantMappingService` reads from `appsettings.json`:

```json
"TenantMappings": {
  "22222222-2222-2222-2222-222222222222": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
}
```

The dictionary key is the Entra `tid`; the value is the internal `TenantId`.

### Behaviour matrix

| Condition | Result |
|---|---|
| `tid` present, mapping registered | `tenant_id` claim added; transformation succeeds |
| `tid` present, mapping **not** registered | No `tenant_id` claim; controller returns `401` |
| `tid` absent (malformed/spoofed token) | No `tenant_id` claim; controller returns `401` |

The response is always `401` (not `403`) for unregistered tenants to avoid leaking
whether a tenant is known to EnterpriseLink.

---

## Step 2 — `roles` → `ClaimTypes.Role` Mapping

### Why the mapping is necessary

Microsoft.Identity.Web parses Entra application roles into individual claims with
the type `"roles"` (a raw string). ASP.NET Core's `[Authorize(Roles = "...")]`
attribute and `User.IsInRole()` both read `ClaimTypes.Role`
(`http://schemas.microsoft.com/ws/2008/06/identity/claims/role`).

Without this bridge, role-based authorization would silently fail for all users.

### Example

**Token payload (abbreviated):**
```json
{
  "tid": "22222222-2222-2222-2222-222222222222",
  "oid": "11111111-1111-1111-1111-111111111111",
  "roles": ["Operator", "Auditor"]
}
```

**Claims after transformation:**

| Type | Value |
|---|---|
| `tid` | `22222222-2222-2222-2222-222222222222` |
| `oid` | `11111111-1111-1111-1111-111111111111` |
| `roles` | `Operator` |
| `roles` | `Auditor` |
| `tenant_id` | `aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa` |
| `ClaimTypes.Role` | `Operator` |
| `ClaimTypes.Role` | `Auditor` |

---

## Implementation Notes

### Idempotency

ASP.NET Core does not guarantee `TransformAsync` is called exactly once per request.
The transformer checks for an existing `tenant_id` claim before proceeding. A second
call on an already-enriched principal is a no-op and returns the same principal.

### Clone isolation

The transformation never mutates the original principal. It clones the source
`ClaimsIdentity` via `ClaimsIdentity.Clone()` and wraps it in a new
`ClaimsPrincipal`. All claim additions target the cloned identity only.

> **Why not `ClaimsPrincipal.Clone()`?**
> On some .NET runtimes, `ClaimsPrincipal.Clone()` can share the underlying claims
> list between the original and the clone, causing either mutation of the original or
> `InvalidOperationException` ("collection was modified") during lazy enumeration.
> Using `ClaimsIdentity.Clone()` directly guarantees an independent `_claims` list.

### Lazy enumeration guard

`ClaimsPrincipal.FindAll(string)` returns a lazy iterator backed by the live claims
list. The roles step materialises this with `.ToList()` before iterating to avoid
modifying the collection while it is being enumerated.

---

## Configuration Reference

### `appsettings.json` (Auth Service)

```json
{
  "AzureAd": {
    "Instance":  "https://login.microsoftonline.com/",
    "TenantId":  "common",
    "ClientId":  "<app-registration-client-id>",
    "Audience":  "api://<app-registration-client-id>"
  },
  "TenantMappings": {
    "<entra-tid-guid>": "<internal-tenant-id-guid>"
  }
}
```

### Adding a new tenant

1. Register the customer's Entra App Registration.
2. Note their Entra `tid` from the Azure Portal → Azure Active Directory → Overview.
3. Generate a new internal `TenantId` GUID.
4. Add `"<tid>": "<internal-id>"` to `TenantMappings` in the Auth Service configuration.
5. Insert a matching row in the `Tenants` table (EF migration or seed).
6. In production, inject the mapping via Azure Key Vault / App Configuration — do not
   commit real GUIDs to source control.

---

## Security Considerations

| Concern | Mitigation |
|---|---|
| Token forged with arbitrary `tid` | Microsoft.Identity.Web validates signature via Entra JWKS; forged tokens are rejected before transformation runs |
| Unregistered tenant probing | `401` is returned regardless of whether the tenant is unknown or the mapping is absent — no information leakage |
| Claim injection | `ClaimsIdentity.AddClaim` only appends; the original Entra-validated claims are unchanged on the cloned identity |
| Roles not in App Registration | Entra only emits `roles` claims for roles explicitly assigned in the App Registration; no claims are fabricated |

---

## Test Coverage

| Test | File |
|---|---|
| `TransformAsync_adds_tenant_id_claim_when_tid_maps_to_registered_tenant` | `ClaimsTransformationTests.cs` |
| `TransformAsync_omits_tenant_id_claim_when_tid_is_not_registered` | `ClaimsTransformationTests.cs` |
| `TransformAsync_omits_tenant_id_when_tid_claim_is_absent` | `ClaimsTransformationTests.cs` |
| `TransformAsync_maps_entra_roles_to_ClaimTypes_Role` | `ClaimsTransformationTests.cs` |
| `TransformAsync_succeeds_without_roles_claim` | `ClaimsTransformationTests.cs` |
| `TransformAsync_is_idempotent_and_returns_original_principal_on_second_call` | `ClaimsTransformationTests.cs` |
| `TransformAsync_returns_a_clone_and_does_not_mutate_original` | `ClaimsTransformationTests.cs` |
