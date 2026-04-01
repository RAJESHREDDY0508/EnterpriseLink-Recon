# Role-Based Access Control (RBAC) — Architecture Reference

**Component:** `EnterpriseLink.Auth` — `Authorization/`, `Controllers/RbacController.cs`
**Sprint:** 4 — Story 3
**Relates to:** [JWT Claims Mapping](jwt-claims-mapping.md), [ADR-001 Entra ID Authentication](adr/ADR-001-entra-id-authentication.md)

---

## Overview

EnterpriseLink uses **Entra ID application roles** as the source of truth for role
assignments. The auth pipeline bridges Entra roles to ASP.NET Core's policy engine so
that standard `[Authorize(Policy = "...")]` attributes protect every endpoint.

```
Entra App Registration
    roles[] in JWT
        │
        ▼
EnterpriseLinkClaimsTransformation
    "roles" → ClaimTypes.Role
        │
        ▼
AddAuthorization() — named policies
    e.g. RequireAuditAccess = Admin OR Auditor
        │
        ▼
[Authorize(Policy = PolicyNames.RequireAuditAccess)]
    on controller action
```

---

## Role Definitions

| Role | String value | Responsibilities |
|---|---|---|
| **Admin** | `Admin` | Full control: tenant settings, user management, all data |
| **Auditor** | `Auditor` | Read-only: transactions and compliance reports |
| **Vendor** | `Vendor` | Write: upload files, submit transactions |
| **Operator** | `Operator` | Operations: day-to-day processing and reconciliation |

Role string values **must exactly match** the App Role display names configured in the
Azure portal (App Registration → App roles), because Entra ID emits them verbatim in
the `roles` JWT claim.

---

## Named Policies

Policies are defined in `Program.cs` using `AddAuthorization()` and referenced by name
from `PolicyNames`. This centralises composite rules and avoids scattered raw strings.

| Policy constant | Allowed roles | Use case |
|---|---|---|
| `RequireAdmin` | Admin | Tenant administration |
| `RequireAuditor` | Auditor | Compliance-only views |
| `RequireVendor` | Vendor | File and transaction submission |
| `RequireOperator` | Operator | Operations dashboard |
| `RequireAuditAccess` | Admin, Auditor | Reporting and audit data |
| `RequireOperationAccess` | Admin, Operator, Vendor | Transaction processing |

`RequireRole(role1, role2, ...)` is an **OR** — any one of the listed roles satisfies
the policy. Every policy also includes `RequireAuthenticatedUser()`.

---

## Endpoint → Role Matrix

```
Endpoint                       │ Admin │ Auditor │ Operator │ Vendor
───────────────────────────────┼───────┼─────────┼──────────┼───────
GET  /api/rbac/admin-panel     │  ✓    │         │          │
GET  /api/rbac/audit-reports   │  ✓    │    ✓    │          │
POST /api/rbac/transactions    │  ✓    │         │    ✓     │  ✓
GET  /api/rbac/operations      │       │         │    ✓     │
```

### HTTP status codes

| Scenario | Status |
|---|---|
| No Bearer token | `401 Unauthorized` |
| Valid token, wrong role | `403 Forbidden` |
| Valid token, correct role | `200 OK` |

The `401` / `403` distinction is enforced by ASP.NET Core's authorization middleware
automatically — no custom code is required.

---

## Adding Roles to a New Endpoint

1. Choose the appropriate `PolicyNames` constant (or define a new one).
2. Apply `[Authorize(Policy = PolicyNames.RequireXxx)]` to the action.
3. Document allowed roles in the XML `<summary>` and `<response>` tags.
4. Add a test case in `RbacPolicyTests` for the new policy if it is novel.

Do **not** use raw `[Authorize(Roles = "Admin")]` strings on controllers — always go
through `PolicyNames` so composite rules are maintained in one place.

---

## Adding a New Role

1. Add the role display name to the Entra App Registration (Azure portal → App roles).
2. Add the constant to `Roles.cs`.
3. Add a single-role policy to `PolicyNames.cs` and register it in `Program.cs`.
4. Add the role to any composite policies it should participate in.
5. Update `UserRole` enum in `EnterpriseLink.Shared.Domain` if the role maps to a DB user record.
6. Add `RbacPolicyTests` coverage for both the new single-role policy and affected composite policies.

---

## Security Considerations

| Concern | Mitigation |
|---|---|
| Role claim forged | Microsoft.Identity.Web validates JWT signature; forged tokens are rejected before any claim is read |
| Elevation via claim injection | `EnterpriseLinkClaimsTransformation` reads `roles` from the Entra-validated token; it cannot add roles that were not in the original JWT |
| Missing role claim | Absent `ClaimTypes.Role` → policy `RequireRole` fails → `403` |
| Unauthenticated request | `RequireAuthenticatedUser()` in every policy → `401` before role check |

---

## Test Coverage

All policies are tested at the `IAuthorizationService` level in `RbacPolicyTests.cs`.

| Test group | Policies covered |
|---|---|
| `RequireAdmin_*` | Single-role, unauthenticated |
| `RequireAuditor_*` | Single-role |
| `RequireVendor_*` | Single-role |
| `RequireOperator_*` | Single-role |
| `RequireAuditAccess_*` | Admin OR Auditor composite |
| `RequireOperationAccess_*` | Admin OR Operator OR Vendor composite, unauthenticated |
