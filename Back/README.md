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

All document routes require authentication and scope reads/writes to the current owner. Another account's document returns 404. Mutations require CSRF protection; saves, renames and deletion also require the current version. Download's version parameter is optional and rejects a stale revision with 409. Upload creates a new document and never overwrites an existing one.

Language values for creation: en-US, fa-IR, ar-SA, ur-PK. Omitting language preserves the existing lazy-blank behavior. Upload formats: docx, odt, rtf, txt, md, json. Download formats: docx (default), pdf, txt, md, json. Unknown formats return 415. Failed conversions return 422; an unavailable converter returns 503. Uploads and converted native models are limited to 10 MiB. Exports are limited to 50 MiB. Each conversion uses a temporary directory, a 60-second timeout, and one of two execution slots. Compressed uploads may contain at most 2048 entries, 64 MiB total expanded data, and 32 MiB per entry.

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

## Converter and storage configuration

Normal dotnet builds build inkstone-cli from Application and copy it beside the API. dotnet publish copies it into the published output. Build for the deployment operating system and architecture. Set Rust__Executable to an absolute executable path to use an independently installed converter. Advanced builds may use -p:SkipRustBuild=true and supply that executable themselves.

Set Storage__DataDirectory to change the private SQLite/key directory. Back up the database and keys together. No database, user file, signing key or converter build output belongs in source control.
