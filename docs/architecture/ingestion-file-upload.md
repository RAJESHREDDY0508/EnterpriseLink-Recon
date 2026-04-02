# Ingestion Service — File Upload API

**Sprint:** 5–6 · **Story:** 1 · **Status:** Implemented

---

## Overview

The File Upload API is the entry point for high-volume CSV ingestion into the
EnterpriseLink Recon platform. It accepts multipart/form-data uploads from
authenticated, tenant-scoped callers, validates the file and its metadata, and
returns an `UploadId` that the caller uses to track asynchronous processing.

The API is designed so that **accepting a file never blocks the processing pipeline**.
File parsing, business-rule validation, and persistence are performed asynchronously
by the Worker service after the HTTP response is returned (Story 2: Queue Dispatch).

---

## Upload Pipeline

```
Client
  │
  │  POST /api/ingestion/upload
  │  Content-Type: multipart/form-data
  │  Authorization: Bearer <entra-jwt with tenant_id claim>
  │  Form fields: file (binary), sourceSystem, description?
  │
  ▼
┌─────────────────────────────────────────────────┐
│  Kestrel  (connection layer)                    │
│  • Enforces MaxRequestBodySize from config      │
│  • Closes connection if limit exceeded → 413    │
│  • Files ≤ MemoryBufferThreshold → memory       │
│  • Files > MemoryBufferThreshold → temp file    │
└───────────────────┬─────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────────────┐
│  JWT Validation  (UseAuthentication)            │
│  • Microsoft.Identity.Web validates Entra token │
│  • EnterpriseLinkClaimsTransformation adds:     │
│      tenant_id  ← tid claim                     │
│      ClaimTypes.Role ← roles claim              │
│  • Invalid/expired token → 401                  │
└───────────────────┬─────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────────────┐
│  IngestionController.UploadAsync()              │
│                                                 │
│  Step 1 — FluentValidation                      │
│    • File: not null, not empty, ≤ MaxFileSize   │
│    • Extension: must be .csv (case-insensitive) │
│    • ContentType: text/csv | text/plain |       │
│        application/octet-stream |               │
│        application/vnd.ms-excel                 │
│    • SourceSystem: required, ≤100 chars,        │
│        [a-zA-Z0-9\-_\s] only                   │
│    • Description: optional, ≤500 chars          │
│    • Fails → 400 with field/message pairs       │
│                                                 │
│  Step 2 — Tenant Resolution                     │
│    • Reads tenant_id from ClaimsPrincipal       │
│    • Not present / not GUID → 401               │
│                                                 │
│  Step 3 — Streaming Row Count                   │
│    • Opens file stream (not buffered to string) │
│    • StreamReader reads line-by-line            │
│    • Header line skipped; data rows counted     │
│    • Proves streaming: O(1) memory regardless   │
│      of file size                               │
│                                                 │
│  Step 4 — Return UploadResult                   │
│    { uploadId, tenantId, fileName,              │
│      fileSizeBytes, dataRowCount,               │
│      sourceSystem, acceptedAt }  → 200 OK       │
└───────────────────┬─────────────────────────────┘
                    │
                    │  [Story 2 — Not yet implemented]
                    ▼
        Publish FileUploadedEvent → RabbitMQ
                    │
                    ▼
           Worker Service
    (parse CSV, validate rows, persist)
```

---

## Defence-in-Depth: Size Limiting

Two independent layers enforce the maximum file size, preventing resource exhaustion:

| Layer | Mechanism | When enforced | Failure response |
|---|---|---|---|
| **Kestrel** (connection) | `options.Limits.MaxRequestBodySize` | Before controller runs | TCP close → 413 |
| **FluentValidation** (application) | `FileUploadRequestValidator` | Inside controller | 400 JSON error |

The Kestrel limit is the outer guard. FluentValidation is the inner guard for cases
where a proxy has already buffered the request or the limit is changed dynamically.

---

## Streaming Strategy

ASP.NET Core's form pipeline buffers uploaded files in two stages:

1. **Small files** (`length ≤ MemoryBufferThresholdBytes`, default 1 MB):
   Held in a `MemoryStream`. Fast, no disk I/O.

2. **Large files** (`length > MemoryBufferThresholdBytes`):
   Automatically spooled to a temporary file on disk by `FileBufferingReadStream`.
   This prevents heap exhaustion for 100 MB–500 MB uploads.

The controller opens the spool stream via `IFormFile.OpenReadStream()` and reads
line-by-line using `StreamReader`. The complete file content is **never** allocated
as a single `string` or `byte[]`. Memory use is bounded by the read buffer size
(default 4 KB per `StreamReader` read), not by file size.

```
File size: 400 MB           Memory used by controller: ~4 KB
                                                        ──────
                            StreamReader reads one line at a time.
                            The rest lives in a temp file on disk.
```

---

## Authentication & Tenant Scoping

Every upload is tenant-scoped. The pipeline enforces this at two points:

1. **`UseAuthentication()`** — validates the Entra ID JWT signature and expiry.
   `EnterpriseLinkClaimsTransformation` maps the raw `tid` claim to the internal
   `tenant_id` claim (populated by the Auth service's token exchange endpoint).

2. **`IngestionController`** — reads `tenant_id` from `ClaimsPrincipal` and
   returns `401` if absent or malformed. This prevents any upload that bypasses
   the token exchange step.

The `TenantId` is included in `UploadResult` so downstream systems can assert
that the tenant context was correctly propagated.

---

## Configuration Reference

All limits are configurable via `appsettings.json` under the `Ingestion` section.
No limits are hard-coded in controller attributes.

```json
{
  "Ingestion": {
    "MaxFileSizeBytes": 524288000,
    "MemoryBufferThresholdBytes": 1048576
  }
}
```

| Key | Default | Description |
|---|---|---|
| `MaxFileSizeBytes` | 524 288 000 (500 MB) | Hard ceiling enforced at Kestrel + validator |
| `MemoryBufferThresholdBytes` | 1 048 576 (1 MB) | Files above this are spooled to disk |

Options are validated at startup (`ValidateDataAnnotations` + `ValidateOnStart`);
the service will not start if values are out of range.

---

## API Contract

### `POST /api/ingestion/upload`

**Request**

```
Content-Type: multipart/form-data
Authorization: Bearer <entra-jwt>

Form fields:
  file         — required, binary .csv file
  sourceSystem — required, string 1–100 chars [a-zA-Z0-9\-_\s]
  description  — optional, string max 500 chars
```

**Response 200 OK**

```json
{
  "uploadId":      "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "tenantId":      "11111111-1111-1111-1111-111111111111",
  "fileName":      "q1-transactions.csv",
  "fileSizeBytes": 10485760,
  "dataRowCount":  98432,
  "sourceSystem":  "Salesforce",
  "acceptedAt":    "2026-04-02T09:15:00Z"
}
```

**Error responses**

| Status | Condition |
|---|---|
| 400 | Validation failed (extension, size, sourceSystem, etc.) |
| 401 | Missing/invalid JWT or missing `tenant_id` claim |
| 413 | File exceeds `MaxFileSizeBytes` (Kestrel-level rejection) |

---

## Local Development

```powershell
# 1. Start dependencies
docker compose up sqlserver rabbitmq -d

# 2. Set dev secrets
dotnet user-secrets set "AzureAd:TenantId" "<your-entra-tenant-id>" `
  --project src/Services/EnterpriseLink.Ingestion

# 3. Run the service
dotnet run --project src/Services/EnterpriseLink.Ingestion

# 4. Upload a test file (exchange token first via Auth service)
curl -X POST http://localhost:5003/api/ingestion/upload `
  -H "Authorization: Bearer <token>" `
  -F "file=@tests/fixtures/sample.csv;type=text/csv" `
  -F "sourceSystem=Salesforce" `
  -F "description=Local dev test"
```

---

## Test Coverage

| Test class | Scope | Count |
|---|---|---|
| `FileUploadValidatorTests` | Validator unit tests — size, extension, content-type, metadata | 18 |
| `IngestionControllerTests` | Controller unit tests — happy path, row count, tenant, auth | 9 |

Run tests:

```powershell
dotnet test tests/EnterpriseLink.Ingestion.Tests --logger "console;verbosity=normal"
```

---

## Architectural Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Transport | `multipart/form-data` | File + metadata in a single atomic request; standard for binary uploads |
| Validation | FluentValidation (not DataAnnotations) | Richer rules, testable in isolation, no HTTP pipeline dependency |
| Size enforcement | Kestrel + validator (two layers) | Defence-in-depth; Kestrel blocks at TCP before memory is allocated |
| Buffering | ASP.NET Core `FormOptions` (disk spool) | Prevents heap exhaustion for large files without custom middleware |
| Row counting | `StreamReader` line-by-line | O(1) memory; validates the streaming claim in acceptance criteria |
| Async processing | `UploadId` + Worker (Story 2) | API never blocks on I/O-bound processing; supports back-pressure |
| Auth | Entra ID JWT + `tenant_id` claim | Consistent with platform-wide identity strategy (ADR-001) |
