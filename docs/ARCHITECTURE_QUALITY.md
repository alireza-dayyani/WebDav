# Architecture and code quality (Phase 2)

**Date:** 2026-11-23  
**Scope:** Papyrus product layers (`Back/`, `Front/`, hosted editor integration) and boundaries with the WordCraft engine (`Application/`).  
**Method:** Static review against Phase 2 mission criteria; cross-check with `docs/BASELINE_AUDIT.md` and live code paths. No large refactors in this milestone.

---

## 1. Architecture summary

| Layer | Responsibility | Strength |
| --- | --- | --- |
| **Front** (Vue 3) | Session UI, libraries, letters studio, editor iframe host | HTTP isolated in `api.js` / `httpCore.js`; composables split studio vs workspace |
| **Back** (.NET 10) | Identity, authorization, SQLite persistence, jobs, conversion orchestration | Minimal API style in `Program.cs`; engine logic stays in Rust subprocess |
| **Application** (Rust) | Document model, layout, conversion, WASM editor | Single workspace; CLI protocol for .NET boundary |

**Boundary rule (preserve):** .NET never parses document semantics beyond size/archive guards and JSON envelope checks; Rust owns conversion and engine commands.

---

## 2. Backend (.NET)

### 2.1 What is in good shape

- **Async-first endpoints:** Handlers use `async`/`await`; blocking `.Result` / `.Wait()` not found in `Back/`.
- **Cancellation:** `CancellationToken` appears on converter, jobs, and data access paths; job worker links host shutdown and per-job cancellation.
- **DI:** Services registered in `Program.cs`; `DocumentJobRunner` as hosted service; scoped `AppDbContext`.
- **Separation:** Endpoints grouped by feature files (`DocumentEndpoints`, `LetterEndpoints`, …); converter isolated in `DocumentConverter` + `ConverterCapacity`.
- **Failure mapping:** `ConversionException` maps engine failures to HTTP status and problem details.

### 2.2 Findings

| ID | Severity | Finding | Recommendation |
| --- | --- | --- | --- |
| AQ-B1 | Low | Endpoints concentrated in `Program.cs` wiring plus large service files | Defer split until a feature team needs it; document module map here |
| AQ-B2 | Medium | `Program.cs` carries cross-cutting middleware stack | Acceptable for modular monolith; extract only if testing requires |
| AQ-B3 | Low | Job worker polls every 1s | OK for single-node; document multi-worker in `PRODUCTION_ROADMAP.md` |
| AQ-B4 | Done | Flaky job/revision race test | Fixed in Phase 1 (`queued` before bump) |

### 2.3 Next backend tasks (incremental)

1. Add structured logging scopes on job id / document id (no PII in labels).
2. Optional: centralize ProblemDetails helpers for consistent `code` extension fields.
3. Phase 3: dependency vulnerability scan in CI.

---

## 3. Frontend (Vue)

### 3.1 What is in good shape

- **Composition API** with Pinia/composables for studio and library.
- **API boundary:** Components use `api.js` + `httpCore.js` (CSRF, account header).
- **Editor isolation:** `editor-host-bridge.js` same-origin `postMessage`; sandbox via `editorEmbed.js`.
- **i18n:** `locales.js` / `workspaceLocales.js` / `studioLocales.js` separated.

### 3.2 Findings

| ID | Severity | Finding | Recommendation |
| --- | --- | --- | --- |
| AQ-F1 | Medium | `App.vue` is large (routing, library, editor, auth) | Phase 2b: extract route-level views without behavior change |
| AQ-F2 | Low | Studio composables overlap patterns | Document conventions in `DEVELOPMENT.md` (Phase 7) |
| AQ-F3 | Medium | Accessibility not systematically tested | Phase 6 / P3-5: focus order, aria on modals |
| AQ-F4 | Low | Full WASM `editor:build` not in default CI | Keep `editor-host:build` in CI; document local editor build in README |

### 3.3 Next frontend tasks

1. Split `App.vue` into `WorkspaceView` / `AuthView` shells (no API changes).
2. Add one Playwright smoke for login + library (Phase 4).

---

## 4. Rust / WASM boundary

### 4.1 What is in good shape

- **CLI contract** documented in `docs/CLI_PROTOCOL.md`; smokes in `Tests/papyrus_cli_protocol_smoke.py`.
- **Hosted save path:** WASM editor `cloud.rs` calls `papyrusHost` in the parent page (product bridge).
- **Deny panics** in web app crate attributes (`main.rs`).

### 4.2 Findings

| ID | Severity | Finding | Recommendation |
| --- | --- | --- | --- |
| AQ-R1 | Medium | Workspace not checked on every GitHub push (was Phase 0 gap) | **Addressed:** `rust-engine` job in CI (`cargo check --workspace`) |
| AQ-R2 | High | Full `cargo xtask ci` not in CI (needs `CRAFT_I18N_DIR`) | Document local gate; optional nightly later |
| AQ-R3 | Low | Upstream crate names ≠ product name | Intentional for WordCraft sync; product ships `papyrus-cli` copy |

### 4.3 Next engine tasks

1. Wire `cargo test` subset for converter-critical crates in CI (after xtask gate is defined).
2. WASM: audit large buffer copies on save path (profile before changing).

---

## 5. Phase 2 milestone log (2026-11-23)

| Task | Status |
| --- | --- |
| Architecture quality assessment (this document) | **Done** |
| CI: `cargo check --workspace` on Ubuntu | **Done** |
| Roadmap updates (P0-1, P1-3) | **Done** |
| `App.vue` decomposition | **Backlog** (next increment) |
| Root `ARCHITECTURE.md` (developer-oriented) | **Backlog** (Phase 7) |

---

## 6. Verification

```powershell
dotnet test Tests/Papyrus.Api.Tests/Papyrus.Api.Tests.csproj -p:SkipRustBuild=true
cd Application
cargo check --workspace --locked
cd ../Front
npm run build
```

GitHub Actions: `.github/workflows/checks.yml` — `api-and-web` and `rust-engine` jobs.