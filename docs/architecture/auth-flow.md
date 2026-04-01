# Authentication Flow — EnterpriseLink Recon

**Last updated:** 2026-03-25
**Owner:** Platform / Security Team
**Related ADR:** [ADR-001 — Entra ID Authentication](./adr/ADR-001-entra-id-authentication.md)

---

## Overview

EnterpriseLink Recon delegates all authentication to **Microsoft Entra ID (Azure AD)**.
No passwords are stored in EnterpriseLink. Every API call carries an Entra ID JWT that is
validated and tenant-mapped by the **Auth Service** before downstream processing begins.

---

## End-to-End Token Flow

```
 ┌──────────────────────────────────────────────────────────────────────┐
 │  Step 1 — User / Vendor authenticates with Entra ID                  │
 │                                                                       │
 │  Client App  ──── redirect ────▶  Entra ID Login                     │
 │             ◀──── JWT Token ─────  (MFA enforced, Conditional Access) │
 └──────────────────────────────────────────────────────────────────────┘
                          │
                          │ Bearer JWT in Authorization header
                          ▼
 ┌──────────────────────────────────────────────────────────────────────┐
 │  Step 2 — API Gateway validates and routes                           │
 │                                                                       │
 │  YARP Gateway (port 5000)                                            │
 │    • Validates token signature against Entra JWKS endpoint           │
 │    • Rejects expired or tampered tokens (401)                        │
 │    • Routes /api/auth/* → Auth Service                               │
 │    • Routes /api/ingestion/* → Ingestion Service                     │
 └──────────────────────────────────────────────────────────────────────┘
                          │
                          ▼
 ┌──────────────────────────────────────────────────────────────────────┐
 │  Step 3 — Auth Service: token exchange                               │
 │                                                                       │
 │  POST /api/auth/token/exchange                                        │
 │                                                                       │
 │  Microsoft.Identity.Web middleware:                                   │
 │    1. Downloads JWKS from Entra ID (cached)                          │
 │    2. Validates token signature (RS256)                               │
 │    3. Validates: iss, aud, exp, nbf                                  │
 │    4. Populates HttpContext.User with decoded claims                  │
 │                                                                       │
 │  AuthController.ExchangeToken():                                      │
 │    1. Reads "tid" claim → Entra directory GUID                       │
 │    2. Calls ITenantMappingService → internal TenantId                │
 │    3. Returns TokenExchangeResponse { tenantId, userId, roles }      │
 └──────────────────────────────────────────────────────────────────────┘
                          │
                          │ { tenantId, userId, roles, email }
                          ▼
 ┌──────────────────────────────────────────────────────────────────────┐
 │  Step 4 — Downstream services use TenantId for all operations        │
 │                                                                       │
 │  TenantMiddleware reads TenantId from:                               │
 │    1. JWT "tenant_id" claim  (primary)                               │
 │    2. X-Tenant-Id header     (internal service-to-service fallback)  │
 │                                                                       │
 │  HttpTenantContext provides TenantId to:                             │
 │    • AppDbContext global query filters (EF Core)                     │
 │    • TenantSessionContextInterceptor (SQL Server RLS)                │
 └──────────────────────────────────────────────────────────────────────┘
```

---

## Token Contents

A validated Entra ID JWT decoded by the Auth Service contains:

```json
{
  "oid":  "11111111-0000-0000-0000-000000000001",
  "tid":  "22222222-0000-0000-0000-000000000001",
  "email": "john.doe@contoso.com",
  "preferred_username": "john.doe@contoso.com",
  "name": "John Doe",
  "roles": ["Operator"],
  "scp":  "access_as_user",
  "iss":  "https://login.microsoftonline.com/22222222-.../v2.0",
  "aud":  "api://your-client-id",
  "exp":  1800000000,
  "iat":  1799996400
}
```

---

## Token Exchange Response

A successful `POST /api/auth/token/exchange` returns:

```json
{
  "tenantId":    "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "userId":      "11111111-0000-0000-0000-000000000001",
  "email":       "john.doe@contoso.com",
  "displayName": "John Doe",
  "roles":       ["Operator"],
  "issuedAt":    "2026-03-25T10:00:00Z"
}
```

---

## Tenant Mapping

```
Entra Directory GUID (tid claim)
           │
           │  ITenantMappingService.MapEntraTenant()
           ▼
EnterpriseLink Internal TenantId (Tenants.TenantId)
```

| Source | Value | Notes |
|--------|-------|-------|
| Entra `tid` claim | `22222222-...` | Customer's Entra directory — Entra-scoped |
| Internal TenantId | `aaaaaaaa-...` | Our DB primary key — EnterpriseLink-scoped |

---

## Error Responses

| Scenario | HTTP Status | Body |
|----------|-------------|------|
| No Bearer token | `401` | WWW-Authenticate header with Entra challenge |
| Token expired | `401` | Microsoft.Identity.Web error response |
| Token for unregistered Entra tenant | `401` | `{"error": "Tenant is not registered"}` |
| Missing required claims (oid/tid) | `401` | `{"error": "Required claims are missing"}` |
| Missing required scope | `403` | Microsoft.Identity.Web scope error |

> **Note:** Unregistered tenants return `401`, not `403`, to avoid revealing whether
> a tenant directory is known to EnterpriseLink (information leakage).

---

## Developer: Local Testing Without Entra ID

For local development when Entra ID is not configured:

1. **Skip auth per-request** using a test JWT with a development tool such as [jwt.io](https://jwt.io)
   (requires configuring a symmetric key instead of Entra JWKS — only in test environments).

2. **Integration tests** use `WebApplicationFactory` with a mock JWT middleware that
   injects pre-set claims without contacting Entra ID.

3. **Direct header injection** (internal service calls only): pass `X-Tenant-Id: <guid>` header.
   This bypasses Entra validation and should never reach production endpoints.

---

## Onboarding a New Enterprise Tenant

1. Enterprise IT registers EnterpriseLink as an **App Registration** in their Entra directory.
2. They provide us their **Entra directory GUID** (`tid`).
3. We create an internal `Tenant` record in the database.
4. We add the mapping `{ entraTenantId → internalTenantId }` to configuration (or database).
5. The enterprise assigns **application roles** (`Admin`, `Auditor`, `Vendor`, `Operator`) to users
   in their Entra App Registration.
6. Users can now log in via their corporate Entra credentials.

---

## Security Checklist

- [x] No passwords stored in EnterpriseLink
- [x] Token validation via official Microsoft.Identity.Web library
- [x] JWKS cached locally — no per-request Entra roundtrip
- [x] Audience (`aud`) strictly validated per `EntraIdOptions.Audience`
- [x] Unregistered tenants return `401` (not `403`) to prevent information leakage
- [x] `RequiredScope` enforced on token exchange endpoint
- [x] Token expiry validated by Microsoft.Identity.Web (no clock-skew bypass)
- [ ] Token revocation via Entra CAEP (future — requires Entra P2 licence)
- [ ] Custom claims encryption for PII fields (future)
