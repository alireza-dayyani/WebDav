# Inkstone

A private document workspace with a separate Vue frontend, .NET 10 API, and Rust editor.

## Projects

| Directory | Purpose |
| --- | --- |
| Front | Vue accounts, document/template libraries, letter workflow, languages and editor host |
| Back | Authentication, private storage, templates, letters, file conversion and OpenAPI |
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

For programmatic DOCX editing without opening the editor, see the [Document automation API](Back/AUTOMATION_API.md): inspect/search, apply atomic batches, preview PDF/PNG, edit tables/controls/images, review changes, restore revisions, compare/assemble documents and generate in bulk. Private background jobs and revocable scoped tokens support standalone automation clients.

Development documentation is available at http://localhost:5080/swagger and http://localhost:5080/openapi/v1.json. Swagger obtains CSRF tokens automatically for interactive requests. These documentation routes are disabled outside Development.

Account, document CRUD, supported formats, upload and download are all backend endpoints. See Back/README.md for the route list and a standalone client example. API clients use a session cookie and an X-CSRF-TOKEN header on mutations; no frontend is required.

DOCX templates can also be filled through the API. Upload the template, use its returned ID and version to list bookmarks, then send a flat map of bookmark names to text values to `POST /api/documents/{id}/bookmarks/replace`. The Rust engine replaces the contents and saves the next revision, which can be reopened in the editor or downloaded as DOCX/PDF. Bookmark names match exactly, markers remain reusable, and the entire batch succeeds or fails together. This edits the saved document; keep a separate template document if it needs to remain unchanged. See the bookmark example and supported range limits in Back/README.md.

SQLite data, signing keys and the private bootstrap configuration live in Back/App_Data. Keep that directory private, exclude it from Git and back it up consistently. Set Storage__DataDirectory to change the location. Uploads default to 10 MiB; administrators can change the global limit from 1 to 128 MiB. Converted native models and exports have a separate 256 MiB processing ceiling so a file under the upload limit can expand during import. Compressed uploads also have entry-count and expanded-size limits. Conversion jobs have a concurrency limit and a timeout.

## Templates and letters

Templates are private snapshots of existing documents, with an option to create a new ordinary file without changing the template. Use Templates to upload/select a source document, or choose a starter. New document also offers saved ordinary templates.

Persian letter starters use the referenced administrative typography, with A5 for short letters and an A4 option. English starters use a 12 pt Times New Roman block layout on US Letter paper. Choose the paper size when creating a letter starter. See [letter layouts and font requirements](Application/docs/LETTER_TEMPLATES.md) for sources, exact styles and fallback behavior. Existing drafts keep their original layout.

Letters are a separate workflow and library. Save sender identities/titles and private signature/logo images, choose registered users as main/copy recipients, prepare the body as text or an uploaded/saved rich document, and select attachments. Validate the draft, preview its PDF, then generate frozen DOCX/PDF files. Optional copy variants preserve recipient display choices. Generated letters retain their template, sender and recipient snapshot and can be duplicated into another draft. Recipient selection does not send letters or share files.

**Preview PDF** displays the PDF in a new browser tab. Explicit download buttons still save files. Allow pop-ups for this site if the browser blocks the preview tab.

Every step is available through standalone APIs. See [Templates and Letters API](Back/STUDIO_API.md) for routes, payloads, validation, limits and retry behavior. Original test examples are generated in ignored `Tests/generated/`.

Choose a letter template and select **Open template and write letter** to open a private editable copy in the Rust editor. You can complete recipient and signing details before or after writing. Save and return to the letter details; **Fill selected fields** refreshes the selected identity fields without replacing the body, and final generation applies them again. The original template stays unchanged. Edited letters remain separate from ordinary documents.

To populate the local database with eight demo accounts and sample letter resources, run `python Scripts/seed_letter_demo.py` after building. It stops its temporary API when finished. Private credentials are stored in ignored `Back/App_Data/demo/accounts.json`. See [demo setup](Scripts/README.md).

## Administration

The local bootstrap account is configured in Back/App_Data/bootstrap/superadmin.json, outside Git and publish output. It is created once on startup; restarting does not reset its password. The configured bootstrap identity is protected from demotion. A deployment must supply its own private bootstrap file, or set BootstrapAdmin__File to its location. No bootstrap credentials are embedded in source or frontend bundles.

Administrators use the Administration screen to search users, grant/revoke administrator access, inspect saved files through read-only previews/downloads, and change the upload limit. Account role checks are live: revocation applies to existing sessions. The ordinary document APIs stay owner-scoped; explicit administrator routes provide the authorized cross-account access. Existing databases are upgraded without removing accounts or documents. Upload-policy changes survive restarts and do not block saves/downloads of existing documents.

## Builds and checks

```powershell
dotnet build Back/Inkstone.Api.csproj
dotnet test Tests/Inkstone.Api.Tests/Inkstone.Api.Tests.csproj -p:SkipRustBuild=true
python Tests/api_smoke.py
python Tests/file_api_smoke.py
python Tests/admin_api_smoke.py
python Tests/bookmark_api_smoke.py
python Tests/document_automation_smoke.py
python Tests/template_api_smoke.py
python Tests/letter_api_smoke.py
python Tests/letter_editor_api_smoke.py
python Tests/letter_template_layout_smoke.py
python Tests/demo_seed_smoke.py
node --test Tests/editor-host.test.mjs Tests/locales.test.mjs Tests/pdf-preview.test.mjs
cd Front
npm run build:all
```

Use npm run editor:optimize for the optional production size pass. Normal development builds are larger and avoid that lengthy step. Generated browser bundles, fonts copied into Front/public/fonts and build outputs are excluded from source control.

The Rust workspace provides formatting, lint, asset attribution, layering, tests and WebAssembly checks through cargo xtask ci. Native workspace tests need substantial disk space on Windows; set CARGO_PROFILE_TEST_DEBUG=0 if test debug symbols exceed the linker limit. Use the focused checks when investigating a particular component.

## Licensing

Required copyright, license, font and third-party attribution records are retained with the Rust source and in Back/legal. Frontend builds copy applicable notices into licenses.txt; backend builds include the QRCoder license. Historical notices are kept separately from current product branding.

IIS and Docker deployment, sharing and account recovery remain later work.
