# Architecture (developers)

High-level map of **Papyrus** in this repository. User-facing behavior is in [README.md](README.md); security boundaries in [docs/SECURITY_MODEL.md](docs/SECURITY_MODEL.md).

## Three projects

```text
┌─────────────────┐     HTTPS (cookie + CSRF)      ┌──────────────────┐
│  Front (Vue 3)  │ ◄────────────────────────────► │  Back (.NET 10)  │
│  PrimeVue/Pinia │                                │  SQLite + jobs   │
└────────┬────────┘                                └────────┬─────────┘
         │ postMessage + papyrus: iframe                               │
         ▼                                                   │ spawn   │
┌─────────────────┐                                ┌─────────▼─────────┐
│ editor host     │                                │ papyrus-cli       │
│ (WASM bundle)   │                                │ (inkstone-cli     │
└─────────────────┘                                │  build product)   │
         ▲                                         └─────────┬─────────┘
         │ WASM / native                                    │
┌────────┴────────┐                                         │
│ Application/    │ ◄─────────────────────────────────────────┘
│ Rust engine     │
└─────────────────┘
```

| Path | Responsibility |
| --- | --- |
| `Front/` | Accounts, libraries, letters studio, automation UI, editor iframe host |
| `Back/` | Identity, authorization, storage, conversion orchestration, OpenAPI |
| `Application/` | WordCraft document engine, WASM editor, CLI crate graph (upstream naming) |

The API is designed to be **usable without the frontend** (session cookie + CSRF on mutations).

## Data and trust boundaries

- **Documents** live as native JSON models plus optional exports; ownership is per authenticated account.
- **Authorization** is checked on every file and metadata operation (owner, grants, admin read-only routes are separate).
- **Converter** runs as a subprocess with timeouts and slot limits; hostile archive tests guard ZIP abuse.
- **Editor embed** uses sandboxed iframe + origin-checked `postMessage` (`Tests/editor-host.test.mjs`).

## Where to read more

| Topic | Doc |
| --- | --- |
| API automation | `Back/AUTOMATION_API.md` |
| Templates / letters | `Back/STUDIO_API.md` |
| Quality / layering notes | `docs/ARCHITECTURE_QUALITY.md` |
| Testing matrix | `docs/TESTING.md` |
| Rebrand boundaries | `docs/REBRANDING.md` |
| Production backlog | `docs/PRODUCTION_ROADMAP.md` |

## CI

`.github/workflows/checks.yml` — build, unit/integration tests, Python smokes, Playwright (mocked API), Rust `cargo check`, dependency audits.

`.github/workflows/codeql.yml` — static analysis for C# and JavaScript/TypeScript.