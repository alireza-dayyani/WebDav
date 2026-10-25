# Inkstone

A private document workspace with a separate Vue frontend, .NET 10 API, and Rust editor.

## Projects

| Directory | Purpose |
| --- | --- |
| Front | Vue account screens, document library, language selection and editor host |
| Back | Authentication, private document storage, file conversion and OpenAPI |
| Application | Rust document engine, WebAssembly editor and native converter |
| Tests | API, file-conversion, localization and editor-bridge integration checks |

## Run locally

Install .NET 10, Node.js 22.12 or later, Rust 1.95 or later, and Trunk.

```powershell
rustup target add wasm32-unknown-unknown
cargo install trunk --locked
cd Front
npm ci
npm run editor:build
```

From the repository root, start the backend:

```powershell
dotnet run --project Back/Inkstone.Api.csproj
```

The first backend build also builds and copies the Rust CLI converter. Subsequent builds reuse Cargo's cache. The backend runs independently of Vue; its published output includes the converter executable for the machine it was built on. Rust is required when building, but a deployed backend does not require the Rust toolchain. Cross-platform deployment requires building the converter for that platform.

In another terminal:

```powershell
cd Front
npm run dev
```

Open http://127.0.0.1:5173. The Vue development server proxies API calls to port 5080. Create an account to start writing. Vue and the API remain separate projects.

## Languages and documents

The site supports English and Persian, including mirrored layouts, localized dates and translated account, library and file controls. The language choice is saved in this browser. New documents can use English, Persian, Arabic or Urdu; the latter three start with right-to-left paragraphs and persist their document language. Imported documents retain their source formatting. Persian and Urdu are also available in the editor's document-language dialog.

Noto Naskh Arabic is bundled for Persian, Arabic and Urdu, including on systems without installed fonts. The Rust document engine handles contextual shaping and mixed-direction text. Advanced menus inside the Rust editor currently use its existing interface catalogs; the bundled web setup uses English menus there. Language selection in the Vue shell does not translate those canvas menus. Persian, Arabic and Urdu spell-check dictionaries are not bundled.

Upload DOCX, ODT, RTF, TXT, Markdown or native JSON through the library. Uploads become private editable documents. Download saved documents as DOCX from the library, or choose DOCX, PDF, TXT, Markdown or native JSON in the editor header. Save edits before downloading a server copy. Native JSON preserves the full document model; conversions to other formats depend on the engine's format support.

Save and Ctrl+S persist the complete native model. Automatic saves run every ten seconds. Version checks protect against overwrites between tabs. File → Save As in the editor can keep a local copy of unsaved work.

## Backend API

Development documentation is available at http://localhost:5080/swagger and http://localhost:5080/openapi/v1.json. Swagger obtains CSRF tokens automatically for interactive requests. These documentation routes are disabled outside Development.

Account, document CRUD, supported formats, upload and download are all backend endpoints. See Back/README.md for the route list and a standalone client example. API clients use a session cookie and an X-CSRF-TOKEN header on mutations; no frontend is required.

SQLite data, signing keys and the private bootstrap configuration live in Back/App_Data. Keep that directory private, exclude it from Git and back it up consistently. Set Storage__DataDirectory to change the location. Uploads default to 10 MiB; administrators can change the global limit from 1 to 128 MiB. Converted native models and exports have a separate 256 MiB processing ceiling so a file under the upload limit can expand during import. Compressed uploads also have entry-count and expanded-size limits. Conversion jobs have a concurrency limit and a timeout.

## Administration

The local bootstrap account is configured in Back/App_Data/bootstrap/superadmin.json, outside Git and publish output. It is created once on startup; restarting does not reset its password. The configured bootstrap identity is protected from demotion. A deployment must supply its own private bootstrap file, or set BootstrapAdmin__File to its location. No bootstrap credentials are embedded in source or frontend bundles.

Administrators use the Administration screen to search users, grant/revoke administrator access, inspect saved files through read-only previews/downloads, and change the upload limit. Account role checks are live: revocation applies to existing sessions. The ordinary document APIs stay owner-scoped; explicit administrator routes provide the authorized cross-account access. Existing databases are upgraded without removing accounts or documents. Upload-policy changes survive restarts and do not block saves/downloads of existing documents.

## Builds and checks

```powershell
dotnet build Back/Inkstone.Api.csproj
python Tests/api_smoke.py
python Tests/file_api_smoke.py
python Tests/admin_api_smoke.py
node --test Tests/editor-host.test.mjs Tests/locales.test.mjs
cd Front
npm run build:all
```

Use npm run editor:optimize for the optional production size pass. Normal development builds are larger and avoid that lengthy step. Generated browser bundles, fonts copied into Front/public/fonts and build outputs are excluded from source control.

The Rust workspace provides formatting, lint, asset attribution, layering, tests and WebAssembly checks through cargo xtask ci. Native workspace tests need substantial disk space on Windows; set CARGO_PROFILE_TEST_DEBUG=0 if test debug symbols exceed the linker limit. Use the focused checks when investigating a particular component.

## Licensing

Required copyright, license, font and third-party attribution records are retained with the Rust source. Frontend builds copy applicable notices into licenses.txt. Historical notices are kept separately from current product branding.

IIS and Docker deployment, sharing, revision history and account recovery remain later work.
