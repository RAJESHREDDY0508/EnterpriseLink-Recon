# ADR-001: Entra ID (Azure AD) as the Authentication Provider

| Field       | Value                              |
|-------------|------------------------------------|
| **Status**  | Accepted                           |
| **Date**    | 2026-03-25                         |
| **Authors** | Platform Team                      |
| **Reviewers** | Security, Compliance, Architecture |

---

## Context

EnterpriseLink Recon is a multi-tenant B2B SaaS platform targeting enterprises in
financial services and healthcare. These organisations:

- Already operate Microsoft Entra ID (formerly Azure Active Directory) as their corporate Identity Provider.
- Require SSO — employees must not maintain a separate EnterpriseLink username and password.
- Must comply with HIPAA / PCI-DSS, which mandate MFA and audited authentication events.
- Expect conditional access policies (IP restrictions, device compliance) enforced before access is granted.

The platform needs an authentication strategy that integrates with existing enterprise identity without
introducing a custom credential store.

---

## Decision

We will use **Microsoft Entra ID** as the Identity Provider via the **OAuth 2.0 / OpenID Connect** protocol.

Each enterprise tenant will register EnterpriseLink as an application in their Entra ID directory.
Tokens are issued by Entra ID and validated by the EnterpriseLink **Auth Service** using
**Microsoft.Identity.Web** (the official Microsoft library for ASP.NET Core).

We will **not** build a custom authentication system. No passwords are stored in EnterpriseLink.

---

## Authentication Flow

```
┌─────────────────────────────────────────────────────────────────────┐
│                     Authentication Flow                              │
└─────────────────────────────────────────────────────────────────────┘

 Enterprise User / Vendor
        │
        │  1. Redirect to Entra ID login
        ▼
 ┌─────────────────┐
 │  Entra ID       │  ← Corporate IdP (MFA, Conditional Access)
 │  (Azure AD)     │
 └────────┬────────┘
          │  2. JWT Access Token (RS256 signed)
          │     Claims: oid, tid, email, name, roles, scp
          ▼
 ┌─────────────────┐
 │  API Gateway    │  ← Validates token signature & expiry (YARP)
 │  (YARP)         │
 └────────┬────────┘
          │  3. Bearer token forwarded
          ▼
 ┌─────────────────┐
 │  Auth Service   │  ← POST /api/auth/token/exchange
 │                 │     • Microsoft.Identity.Web validates token
 │                 │     • Maps tid → internal TenantId
 │                 │     • Returns TokenExchangeResponse
 └────────┬────────┘
          │  4. { tenantId, userId, roles }
          ▼
 Downstream Services use TenantId for all data operations
```

---

## Key Claims Used

| Claim  | Source     | Purpose in EnterpriseLink                              |
|--------|------------|--------------------------------------------------------|
| `oid`  | Entra ID   | Stable, immutable user identifier (survives email changes) |
| `tid`  | Entra ID   | Customer's Entra directory GUID → mapped to internal TenantId |
| `email` / `preferred_username` | Entra ID | Display and audit logging |
| `name` | Entra ID   | Display name in the Dashboard UI |
| `roles` | App Registration | EnterpriseLink roles (Admin, Auditor, Vendor, Operator) |
| `scp`  | App Registration | OAuth2 scope — validates intended API audience |

---

## Tenant Identity Mapping

The `tid` claim in an Entra token is the **customer's Entra directory GUID**, not our internal
`TenantId`. The Auth Service maintains a mapping:

```
EntraTenantId (tid claim)  →  EnterpriseLink TenantId (Tenants.TenantId)
```

**Current implementation**: `ConfigurationTenantMappingService` — reads from `appsettings.json`.
**Future**: `DatabaseTenantMappingService` — queries the `Tenants` table by an `EntraDirectoryId` column for dynamic onboarding.

---

## Considered Alternatives

### Option A: Custom username/password with JWT
- ❌ Password storage is a HIPAA / PCI audit liability
- ❌ MFA must be built and maintained
- ❌ No SSO with existing enterprise identity
- ❌ Conditional access policies not enforceable

### Option B: Auth0 / Okta
- ✅ Multi-protocol support (SAML, OIDC)
- ❌ Additional vendor cost per monthly active user
- ❌ Adds a 3rd-party dependency to the authentication critical path
- ❌ Most enterprise clients already have Entra ID; Auth0 would federate back to it anyway

### Option C: Entra ID (Selected)
- ✅ Zero password storage in EnterpriseLink
- ✅ MFA enforced by the enterprise's existing policies
- ✅ Conditional access (IP whitelist, device compliance) out of the box
- ✅ Audit logs in the customer's Entra ID portal — regulators can review
- ✅ Seamless SSO for enterprise employees
- ✅ Official Microsoft.Identity.Web library — maintained, security-patched
- ⚠️ Requires each enterprise to register EnterpriseLink in their Entra directory (one-time onboarding)

---

## Consequences

### Positive
- No credential management — significantly reduces our compliance surface.
- MFA and conditional access are free features of the enterprise's existing Entra subscription.
- Token validation is handled by a battle-tested library (Microsoft.Identity.Web).
- Audit trail for authentication events is in the customer's Entra audit logs — satisfies HIPAA audit requirements.

### Negative / Mitigations
| Risk | Mitigation |
|------|------------|
| Customer must register app in their Entra directory | Provide a step-by-step onboarding guide |
| Entra ID service outage blocks login | Rate-limit failed validations; JWKS keys are cached locally by Microsoft.Identity.Web |
| Token replay attack | Short token lifetime (default 1 hour) + refresh token rotation configured in Entra |
| Misconfigured Audience allows wrong apps | `Audience` validated strictly in `EntraIdOptions` |

---

## Implementation References

| File | Purpose |
|------|---------|
| `src/Services/EnterpriseLink.Auth/Configuration/EntraIdOptions.cs` | Strongly-typed config |
| `src/Services/EnterpriseLink.Auth/Services/ITenantMappingService.cs` | Mapping contract |
| `src/Services/EnterpriseLink.Auth/Services/ConfigurationTenantMappingService.cs` | Config-backed mapping |
| `src/Services/EnterpriseLink.Auth/Controllers/AuthController.cs` | Token exchange & identity endpoints |
| `src/Services/EnterpriseLink.Auth/Program.cs` | `AddMicrosoftIdentityWebApi` registration |
| `docs/architecture/auth-flow.md` | Detailed flow diagrams and developer guide |
