# Inkstone

A private document workspace with a separate Vue frontend, .NET 10 API, and Rust editor.

## Projects

For the next development stages and deferred database, search, storage, worker and
Docker work, see the [production roadmap](docs/PRODUCTION_ROADMAP.md).

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

## Interface

The workspace opens in dark mode with blue accents. Use the sun/moon button beside the language picker to switch themes; the browser remembers the choice. Documents, templates, letters, automation, reviews, storage and administration are grouped in the workspace navigation, which remains visible in Templates, Letters and Administration and scrolls horizontally on small screens. Studio drafts stay in memory while switching workspace sections; save them before closing the browser. The editor canvas has its own existing theme setting under View.

Frontend browser checks use Playwright with installed Chrome and mocked API fixtures: run `npm run build` then `npm run test:browser` in Front. Screenshots are written to ignored `Tests/generated/redesign/`. Tests cover English/Persian desktop and mobile layouts, theme persistence, dialog navigation and the RTL search icon regression.

## Languages and documents

The site supports English and Persian, including mirrored layouts, localized dates and translated account, library and file controls. The language choice is saved in this browser. New blank documents leave paragraph direction unset: the editor uses the first strong letters to start Persian, Arabic or Urdu paragraphs right-to-left. There is no language selector in the creation dialog. Explicit document language and direction remain available in the editor and standalone API. Imported documents retain their source formatting. Persian and Urdu are also available in the editor's document-language dialog.

The web interface bundles Vazirmatn (the continuation of Vazir) for Persian, Source Sans 3 for English controls and Source Serif 4 for editorial headings. Fonts are served locally with their license notices. Noto Naskh Arabic remains bundled in the document engine for Persian, Arabic and Urdu, including on systems without installed fonts. The Rust document engine handles contextual shaping and mixed-direction text. Advanced menus inside the Rust editor currently use its existing interface catalogs; the bundled web setup uses English menus there. Language selection in the Vue shell does not translate those canvas menus. Persian, Arabic and Urdu spell-check dictionaries are not bundled.

Upload DOCX, ODT, RTF, TXT, Markdown or native JSON through the library. Uploads become private editable documents. Download saved documents as DOCX from the library, or choose DOCX, PDF, TXT, Markdown or native JSON in the editor header. Save edits before downloading a server copy. Native JSON preserves the full document model; conversions to other formats depend on the engine's format support.

Save and Ctrl+S persist the complete native model. Automatic saves run every ten seconds. Version checks protect against overwrites between tabs. File → Save As in the editor can keep a local copy of unsaved work.

The library's History dialog downloads earlier revisions and restores them as a new version. Delete moves ordinary documents to Trash; restore them there or explicitly delete them and their history permanently. Trashed documents are excluded from ordinary document editing, downloads and automation. Templates and generated letters keep their own saved snapshots. `GET /api/storage` reports the authenticated owner's document, history, resource, generated-artifact and result bytes, including Trash; database overhead and queued job inputs are excluded.

Library search matches titles and document text with Persian/Arabic letter normalization and paginated results. It indexes up to the first one million characters of each native document's title/text; it does not OCR scanned images. Storage and retention provides an opt-in policy, a preview and explicit cleanup. The first version, current version, recent versions and all versions submitted for review are preserved. Enabling retention also applies it to subsequent saves; keep backups for recovery of removed history.

Sharing and review grants an existing registered account reader or editor access. Readers view/download the current document; editors can also save changes. Only owners manage sharing, complete revision history, Trash, retention and template copies. Reviews apply to an exact revision, retain that native revision as evidence, and become stale after edits. These are internal review decisions, not certificate signatures or external delivery. Owners see sharing/review/edit audit events. Revocation prevents future server access but cannot recall previously downloaded copies.

Automation offers a live job list with cancellation, private output downloads and reports; guided template generation for up to 100 records; and creation/revocation of scoped, expiring API tokens. A token secret is shown once and is not saved in browser storage. Tokens retain owner-only access even when their account has shared access to someone else's documents. All features are available through the standalone API; see Back/AUTOMATION_API.md.

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

Administrators use the Administration screen to search users, grant/revoke administrator access, inspect saved files through read-only previews/downloads, and change the upload limit. Account role checks are live: revocation applies to existing sessions. Ordinary document APIs require ownership or an explicit reader/editor grant; administrator roles do not bypass those checks. Explicit administrator routes provide administrative inspection. Existing databases are upgraded without removing accounts or documents. Upload-policy changes survive restarts and do not block saves/downloads of existing documents.

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
node --test Tests/workspace-ui.test.mjs Tests/automation-ui.test.mjs Tests/studio-state.test.mjs
python Tests/workspace_sequence_smoke.py
python Tests/production_readiness_smoke.py
cd Front
npm run build:all
```

Use npm run editor:optimize for the optional production size pass. Normal development builds are larger and avoid that lengthy step. Generated browser bundles, fonts copied into Front/public/fonts and build outputs are excluded from source control.

The Rust workspace provides formatting, lint, asset attribution, layering, tests and WebAssembly checks through cargo xtask ci. Native workspace tests need substantial disk space on Windows; set CARGO_PROFILE_TEST_DEBUG=0 if test debug symbols exceed the linker limit. Use the focused checks when investigating a particular component.

## Licensing

Required copyright, license, font and third-party attribution records are retained with the Rust source and in Back/legal. Frontend builds copy applicable notices into licenses.txt; backend builds include the QRCoder license. Historical notices are kept separately from current product branding.

See the [production roadmap](docs/PRODUCTION_ROADMAP.md) for future deployment work. The standalone `Scripts/backup.py` tool supports consistent backup and verified restore into a new directory; stop the API before taking a backup so data and keys form one recovery point. Run `python Scripts/backup.py --help` for usage. Account recovery and Docker deployment remain later work.
