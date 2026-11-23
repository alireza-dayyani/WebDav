# Development guide (Phase 7)

Conventions for contributors. Workflow and PR expectations: [CONTRIBUTING.md](../CONTRIBUTING.md).

## Repository layout

| Directory | Stack | Primary entry |
| --- | --- | --- |
| `Back/` | ASP.NET Core 10 | `Program.cs`, `Papyrus.Api.csproj` |
| `Front/` | Vue 3 + Vite | `src/main.js`, `src/App.vue` |
| `Application/` | Rust workspace | `Cargo.toml`, `crates/` |
| `Tests/` | xUnit, Node test runner, Python smokes | `Papyrus.Api.Tests/` |

## Backend

- **Clean architecture:** domain rules in services; controllers stay thin; all endpoints `async`.
- **EF Core** only in infrastructure; entities are not returned directly from APIs.
- **DTO mapping** via AutoMapper.
- **Document privacy:** verify owner (or grant) and version on every read/write/export path you touch.

Converter output is copied to **`papyrus-cli`** next to the API build (`Papyrus.Api.csproj` builds `inkstone-cli` then renames for product branding).

## Frontend

- **Vue 3 Composition API** only.
- **PrimeVue** for UI; custom components when PrimeVue has no fit.
- **Pinia** for shared state.
- **HTTP:** Axios wrappers in `Front/src/services/` (and related modules) — no ad hoc `fetch` in view components for API calls.
- **Forms:** PrimeVue + yup.
- **Tables:** PrimeVue DataTable; server-side pagination for library lists.

Editor bridge code (`editor-host`, `papyrus:` protocol) must preserve origin checks documented in `docs/SECURITY_MODEL.md`.

## Engine (`Application/`)

- Treat as **upstream WordCraft** with a thin Papyrus overlay (hosted build, `cloud.rs` host naming).
- Avoid drive-by renames of `inkstone-*` crates unless the rebrand plan explicitly approves it.
- Before engine commits: `cargo check --workspace --locked` in `Application/`.
- Full engine CI (`cargo xtask ci`) is documented in README; not every contributor machine runs it — see P0-2 in `docs/IMPROVEMENT_ROADMAP.md`.

## Studio composables

`Front/src/composables/` separates workspace navigation from letter/template studio state. Prefer extending existing composables over duplicating fetch/save logic in `App.vue` (see `docs/ARCHITECTURE_QUALITY.md` AQ-F2).

## Tests

See [TESTING.md](TESTING.md). Match new behavior with the smallest test at the right layer:

- Authorization / API contracts → `Tests/Papyrus.Api.Tests`
- HTTP client / locales / editor bridge → `Tests/*.test.mjs`
- Conversion and protocol → Python smokes in `Tests/`

## Docs

When changing user-visible behavior, update README or the relevant `Back/*.md` / `docs/*.md` in the **same PR**. Do not invent performance SLAs; link to measured baselines in `docs/PERFORMANCE_BASELINE.md` when relevant.