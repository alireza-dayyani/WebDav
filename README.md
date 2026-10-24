# Inkstone

A private document workspace with a separate Vue frontend, .NET 10 API, and Rust editor.

## Projects

| Directory | Purpose |
| --- | --- |
| Front | Vue account screens, document library, and editor host |
| Back | Authentication, private storage, document APIs, and server integration |
| Application | Rust document engine, browser editor, and native conversion tools |
| Tests | API and editor integration checks |

## Development

Install .NET 10, Node.js 22.12 or later, Rust 1.95 or later, and Trunk.

```powershell
rustup target add wasm32-unknown-unknown
cargo install trunk --locked
cd Front
npm ci
npm run editor:build
```

From the repository root, start the API:

```powershell
dotnet run --project Back/Inkstone.Api.csproj
```

In another terminal:

```powershell
cd Front
npm run dev
```

Open http://127.0.0.1:5173. The frontend proxies API calls to port 5080. Create an account to start writing.

## Documents

Documents belong to the authenticated account. Save and Ctrl+S persist the complete native document model; automatic saves run every ten seconds. Version checks prevent silent overwrites between tabs. Save As and the editor’s export commands produce local copies.

The API keeps SQLite data and signing keys in Back/App_Data. Keep this directory private, exclude it from Git, and back it up consistently. Set Storage__DataDirectory to use another location. Documents are limited to 10 MiB each.

## Builds and checks

```powershell
dotnet build Back/Inkstone.Api.csproj
python Tests/api_smoke.py
node --test Tests/editor-host.test.mjs
cd Front
npm run build:all
```

Use npm run editor:optimize for the optional production size pass. Normal development builds are larger and avoid that lengthy step. Generated browser bundles and build outputs are excluded from source control.

The Rust workspace also provides formatting, lint, asset attribution, layering, and WebAssembly checks through cargo xtask ci.

## Licensing

Required copyright, license, font, and third-party attribution records are retained with the Rust source. Frontend builds copy applicable notices into licenses.txt. These historical records are retained separately from current product branding.

IIS and Docker deployment, sharing, revision history, and account recovery remain later work.
