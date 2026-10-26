# Inkstone API

ASP.NET Core 10, Identity cookie authentication, SQLite document storage and the native Rust converter. Run from the repository root with:

```powershell
dotnet run --project Back/Inkstone.Api.csproj
```

No Vue server is needed to use the API. Development Swagger UI is at http://localhost:5080/swagger; the OpenAPI document is at http://localhost:5080/openapi/v1.json. Neither route is enabled in Production. Swagger's request interceptor refreshes CSRF tokens before each mutation, including registration and login.

## Endpoints

| Method | Route | Purpose |
| --- | --- | --- |
| GET | /api/health | Health check |
| GET | /api/csrf | Issue a token for the current session |
| POST | /api/account/register | Create an account and sign in |
| POST | /api/account/login | Sign in |
| POST | /api/account/logout | Sign out |
| GET | /api/account/me | Current account |
| GET | /api/documents/ | List the current account's documents |
| POST | /api/documents/ | Create a document; JSON title and optional language |
| GET | /api/documents/formats | Supported upload/download formats, languages and limit |
| POST | /api/documents/upload | Multipart file and optional title; creates a private document |
| GET | /api/documents/{id} | Document metadata |
| GET | /api/documents/{id}/snapshot | Consistent metadata and native document model |
| GET | /api/documents/{id}/content | Native JSON; 204 for an untouched legacy blank document |
| PUT | /api/documents/{id}/content | Save JSON containing version and document |
| PATCH | /api/documents/{id} | Rename with JSON title and version |
| DELETE | /api/documents/{id}?version=1 | Delete the matching revision |
| GET | /api/documents/{id}/download?format=docx&version=1 | Download a saved revision as an attachment |
| GET | /api/documents/{id}/bookmarks?version=1 | List named bookmarks and whether their ranges support replacement |
| POST | /api/documents/{id}/bookmarks/replace | Replace bookmark contents with JSON text values; saves the next revision |
| GET | /api/admin/users?search=&page=1&pageSize=25 | Search accounts, roles and storage usage |
| PUT | /api/admin/users/{id}/admin | Change administrator access with isAdmin and version |
| GET | /api/admin/users/{id}/documents?page=1 | List a user's files |
| GET | /api/admin/documents/{id}/preview?version=1 | Bounded plain-text preview |
| GET | /api/admin/documents/{id}/download?format=docx&version=1 | Download another user's saved file |
| GET | /api/admin/settings | Current upload policy and revision |
| PUT | /api/admin/settings | Set maxUploadMiB and version |

Ordinary document routes require authentication and scope reads/writes to the current owner. Another account's document returns 404. Mutations require CSRF protection; saves, renames and deletion also require the current version. Download's version parameter is optional and rejects a stale revision with 409. Upload creates a new document and never overwrites an existing one.

Language values for creation: en-US, fa-IR, ar-SA, ur-PK. Omitting language preserves the existing lazy-blank behavior. Upload formats: docx, odt, rtf, txt, md, json. Download formats: docx (default), pdf, txt, md, json. Unknown formats return 415. Failed conversions return 422; an unavailable converter returns 503. Uploads default to 10 MiB and may be configured by administrators from 1 to 128 MiB. Converted native models and exports have an independent 256 MiB processing ceiling. GET /api/documents/formats reports the current upload/model limits. Multipart transport includes an additional 1 MiB allowance; the file itself is still checked against the exact policy. Each conversion uses a temporary directory, a 60-second timeout, and one of two execution slots. Compressed uploads may contain at most 2048 entries, 256 MiB total expanded data, and 128 MiB per entry.

## Administrator access and bootstrap

The API loads the initial superadministrator from the private Back/App_Data/bootstrap/superadmin.json file. Its JSON has Email and Password fields; supply real values only in that ignored folder, never in tracked configuration. BootstrapAdmin__File can specify an external path. A missing file leaves bootstrap creation disabled. Existing superadministrators keep their password on restart. Startup refuses to elevate an ordinary account that already owns the configured email.

The private bootstrap path accepts the configured initial password; ordinary registration keeps the existing password composition requirements. Identity stores a salted password hash in the private database. The local bootstrap folder is restricted to its owner, SYSTEM and local administrators. Deployed instances should apply equivalent filesystem permissions for their service identity. App_Data is excluded from Git and build/publish content.

Admin and SuperAdmin can use /api/admin; only the private bootstrap process creates SuperAdmin. Administrators can grant/revoke Admin, including from other administrators, but cannot demote the bootstrap superadministrator. The policy checks persisted roles on every privileged request rather than trusting old role cookies. GET /api/account/me and sign-in responses include isAdmin and isSuperAdmin. Role mutations require the user's opaque version from the user list; settings mutations require the settings' numeric version. Stale updates return 409. All mutations still require X-CSRF-TOKEN.

User lists are paginated (25 by default, up to 100); per-user file lists return 50 at a time. Administrative previews/downloads require the listed document version and are read-only. Administrative file reads and role/settings changes are logged with actor and target IDs. Existing owner write routes do not permit cross-account edits, even for administrators.

## Standalone PowerShell client

Use one cookie jar throughout. Fetch a new CSRF token after the identity changes. Replace the sample credentials and upload path before running.

```powershell
$apiBase = 'http://localhost:5080'
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$csrf = Invoke-RestMethod "$apiBase/api/csrf" -WebSession $session
$credentials = @{ email = 'writer@example.test'; password = 'ChooseYourPassword123' } | ConvertTo-Json
Invoke-RestMethod "$apiBase/api/account/register" -Method Post -WebSession $session -ContentType 'application/json' -Headers @{ 'X-CSRF-TOKEN' = $csrf.token } -Body $credentials
$csrf = Invoke-RestMethod "$apiBase/api/csrf" -WebSession $session
$document = Invoke-RestMethod "$apiBase/api/documents/" -Method Post -WebSession $session -ContentType 'application/json' -Headers @{ 'X-CSRF-TOKEN' = $csrf.token } -Body (@{ title = 'سند من'; language = 'fa-IR' } | ConvertTo-Json)
Invoke-WebRequest "$apiBase/api/documents/$($document.id)/download?format=docx" -WebSession $session -OutFile './document.docx'
# PowerShell 7 multipart upload:
Invoke-RestMethod "$apiBase/api/documents/upload" -Method Post -WebSession $session -Headers @{ 'X-CSRF-TOKEN' = $csrf.token } -Form @{ file = Get-Item './document.docx' }
```

The Vue development proxy supplies the same-origin browser arrangement. For separate production hosts, place a reverse proxy in front of /api or explicitly configure trusted origins, cookies and CSRF for that topology.

## Fill a DOCX template by document ID

Create named bookmarks in Word around the text to replace, then upload the DOCX using `/api/documents/upload`. The returned `id` identifies the saved native document; the API does not accept a filesystem path. Use the same authenticated cookie jar and CSRF workflow as above. All bookmark operations are owner-scoped, including calls made by an administrator.

1. Get `/api/documents/{id}` for the current version, then `GET /api/documents/{id}/bookmarks?version=1`. The version query parameter is required; stale versions return 409. The response includes `id`, `version` and `bookmarks`, whose entries have `name`, `replaceable` and an optional `issue` explaining unsupported ranges.
2. Send `POST /api/documents/{id}/bookmarks/replace` with `Content-Type: application/json` and `X-CSRF-TOKEN`:

```json
{
  "version": 1,
  "values": {
    "CustomerName": "علی رضایی",
    "InvoiceNumber": "INV-2026-001",
    "Notes": "First line\nSecond line",
    "OptionalText": ""
  }
}
```

3. The response contains `metadata` with the incremented version and `replaced`, the names that were filled. Download `/api/documents/{id}/download?format=docx&version=2`, choose another supported export format, or reopen the saved document in the editor. An already open editor must reload the saved revision; its old version cannot overwrite the server change.

The API calls the Rust command registry (`document.bookmarks` and `document.fillBookmarks`) through the bounded native converter. JSON keys match bookmark names exactly, including case. Values must be strings; numbers/dates should be formatted by the caller, nested JSON is not expanded, and text is inserted literally rather than interpreted as HTML or commands. Unspecified bookmarks stay unchanged. Empty strings clear a bookmark's contents. Tabs and line breaks are supported; CRLF becomes a single line break. Start/end markers remain so the same field can be replaced again. Existing formatting outside replaced contents is retained; inserted text uses the engine's formatting at the replacement range.

Supported locations include the body, table cells, headers, footers and other secondary stories. A bookmark can span consecutive paragraphs in the same container. Replacement rejects missing keys, duplicate bookmark names, missing/reversed markers, ranges crossing tables/cells, or ranges containing another bookmark marker. It does not remove a surrounding bookmark when filling a nested inner field. Stop document protection and disable Track Changes before filling. `replaceable` describes the range, not the document's current protection state.

Validation errors return 400; unknown or unsupported bookmark keys return 422 with `invalidKeys`. Engine-level refusal also returns 422. No partial batch is saved, and a concurrent save/delete returns 409. The original document ID is retained, its version advances once, and unspecified content is left to the engine's normal import/export handling. Uploaded DOCX files are already converted to the native model, so exports have the same format-fidelity limits as other editor downloads. This endpoint changes the saved document in place; upload a separate copy when keeping an unchanged template is required.

Each request accepts 1–256 entries, at most 512 KiB of UTF-8 value text in total, and names up to 256 UTF-8 bytes. The request body is capped at 4 MiB (including JSON escaping). Inline-object markers and control characters other than tabs/line breaks are rejected. Bookmark discovery is capped at 4096 names; normal converter time, concurrency and document-size limits still apply. Replacement inputs and generated files are temporary and are not logged.

```powershell
# Continue with the authenticated $session and an uploaded template's $document.
$revision = Invoke-RestMethod "$apiBase/api/documents/$($document.id)" -WebSession $session
Invoke-RestMethod "$apiBase/api/documents/$($document.id)/bookmarks?version=$($revision.version)" -WebSession $session
$csrf = Invoke-RestMethod "$apiBase/api/csrf" -WebSession $session
$payload = @{ version = $revision.version; values = @{ CustomerName = 'علی رضایی' } } | ConvertTo-Json
$filled = Invoke-RestMethod "$apiBase/api/documents/$($document.id)/bookmarks/replace" -Method Post -WebSession $session -ContentType 'application/json; charset=utf-8' -Headers @{ 'X-CSRF-TOKEN' = $csrf.token } -Body ([Text.Encoding]::UTF8.GetBytes($payload))
Invoke-WebRequest "$apiBase/api/documents/$($document.id)/download?format=docx&version=$($filled.metadata.version)" -WebSession $session -OutFile './filled.docx'
```

## Converter and storage configuration

Normal dotnet builds build inkstone-cli from Application and copy it beside the API. dotnet publish copies it into the published output. Build for the deployment operating system and architecture. Set Rust__Executable to an absolute executable path to use an independently installed converter. Advanced builds may use -p:SkipRustBuild=true and supply that executable themselves.

Set Storage__DataDirectory to change the private SQLite/key directory. Back up the database and keys together. No database, user file, signing key or converter build output belongs in source control.

For an alternative development API address, set INKSTONE_API_URL before starting the Vue development/preview server. The default remains http://localhost:5080.

Hosting request limits (for example IIS request filtering or a reverse proxy) must also allow the configured file size plus multipart overhead. Application settings do not override an upstream hosting limit.
