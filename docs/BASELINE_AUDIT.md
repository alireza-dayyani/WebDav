# Baseline audit (Phase 0)

**Repository:** [alireza-dayyani/WebDav](https://github.com/alireza-dayyani/WebDav)  
**Product name:** Papyrus (Phase 0 baseline captured pre-rebrand; Phase 1 applied product identity)  
**Audit date:** 2026-11-23  
**Upstream WordCraft baseline:** `c369c3a20befef8742f8913128cacd79002435d6` (`Application/upstream.rev`)  
**craft-libs pin:** `750a9bd` (see `Application/Cargo.toml`)

This document records the state of the repository **before** rebranding and large-scale refactors. Findings are based on inspection, local commands run on 2026-11-23, and existing documentation. Items **not** run locally are called out explicitly.

---

## 1. Architecture overview

Papyrus is a **three-project** document platform:

| Layer | Path | Role |
| --- | --- | --- |
| **Frontend shell** | `Front/` | Vue 3 SPA: accounts, library, templates, Studio letters, admin, editor host (iframe). PrimeVue-style patterns; Axios via `httpCore.js`. |
| **API & persistence** | `Back/` | ASP.NET Core Web API (.NET 10): Identity + SQLite, document CRUD, conversion, automation jobs, sharing/review, letters/studio, admin. Usable without the frontend. |
| **Document engine** | `Application/` | Rust workspace: native model, layouts, formats, PDF, WASM web editor (`Papyrus-web`), desktop app, CLI converter. |

**Integration:**

- Browser loads WASM editor from `Front` build output; `editor-host-bridge.js` and `postMessage` connect shell ↔ iframe (`docs/SECURITY_MODEL.md`).
- API invokes `Papyrus-cli` for import/export and automation; converter runs in isolated temp dirs with capacity limits (`DocumentConverter`, `ConverterCapacity`).
- Documents are **owner-scoped**; version checks on save; native JSON stored in SQLite + filesystem under `Back/App_Data` (configurable).

```text
[Vue Front] --cookie+CSRF--> [Papyrus.Api / SQLite]
       |                           |
       v                           v
 [WASM Papyrus-web]         [Papyrus-cli subprocess]
       ^                           ^
       +---- Application/ Rust ----+
```

---

## 2. Upstream-derived vs original work

### Derived from WordCraft (`Application/`)

- Bulk of Rust engine, UI-egui, formats, tests, and docs under `Application/docs/*-parity.md`.
- Sync via `Application/scripts/upstream-sync.py` and `upstream` remote (`storytold/wordcraft`).
- **Fork adaptations** (documented in `Application/docs/UPSTREAM_SYNC.md`): Papyrus README/attribution, operator-managed updates stub, LaTeX banner strip, Vue cloud save (`PapyrusHost`), retained `dialogs/dispatch.rs`, hosted async saves.

### Original to this repository (Papyrus / future Papyrus)

- Entire **Back/** API surface (auth, documents, FTS search, trash/retention, sharing, reviews, automation tokens/jobs, Studio letters, admin).
- Entire **Front/** workspace UX (bilingual EN/fa, RTL, themes, Playwright redesign coverage).
- **Tests/** integration: `Papyrus.Api.Tests`, Python smokes, Node unit tests, fixtures.
- **docs/** at repo root: `SECURITY_MODEL.md`, `CLI_PROTOCOL.md`, `PRODUCTION_ROADMAP.md`.
- Hardening added on top of upstream: authorization coverage tests, hostile ZIP/converter smokes, CSP/iframe sandbox, job revision pinning.

**Attribution:** WordCraft/craft-libs remain MIT/Apache-2.0 with ArtCraft/WordCraft notices. Do not present upstream engine/UI as solely Papyrus-authored.

---

## 3. Existing functionality (product)

- Private multi-user workspace (register/login, CSRF on mutations).
- Document library: upload (DOCX, ODT, RTF, TXT, MD, JSON), edit in WASM editor, revision history, trash, search (FTS with normalization).
- Export formats; automation API and background jobs; scoped API tokens.
- Internal sharing (reader/editor) and review-on-revision workflow.
- Studio: templates, letter generation, recipients, PDF preview.
- English and Persian UI; RTL layout; document language from file content.
- Administrator: roles, upload limits, storage reports.

See root `README.md` and `Back/README.md` / `Back/AUTOMATION_API.md` / `Back/STUDIO_API.md` for detail.

---

## 4. Build status (2026-11-23)

| Command | Result | Notes |
| --- | --- | --- |
| `dotnet build Back/Papyrus.Api.csproj` | **OK** (~19.5 min) | Exit 0; four MSBuild warnings about unused egui/hayro patches in the embedded Rust CLI graph (no errors). |
| `dotnet test Tests/Papyrus.Api.Tests -p:SkipRustBuild=true` | **265 passed** | First run: **1 failure** (`Worker_fails_when_queued_source_revision_changes_before_execution` — job status `succeeded` vs expected `failed`); **immediate re-run: 265/265 passed** (possible flake or timing). |
| `cargo check --workspace` (`Application/`) | **OK** | Verified earlier same day after WordCraft `c369c3a2` integration. |
| `npm run build` (`Front/`) | **OK** | Vite production build ~4s. |
| `python Tests/Papyrus_cli_protocol_smoke.py` | **PASS** | |
| `python Tests/converter_hostile_smoke.py` | **PASS** | |
| `cargo xtask ci` (`Application/`) | **Not run** | Requires `CRAFT_I18N_DIR` per `craft-i18n.rev`; CI on developer machine not executed in Phase 0. |
| `npm run build:all` / full WASM editor build | **Not run** | `editor:build` not executed in Phase 0 (CI runs `editor-host:build`). |
| `npm run test:browser` (Playwright) | **Not run** | Requires Chrome + prior `build`; leave for Phase 4 baseline. |
| GitHub Actions (`.github/workflows/checks.yml`) | **Configured** | Ubuntu: Front build, Node tests, `dotnet build`/`test`, Python smokes. **No Rust job**, no CodeQL, no Front Playwright in CI. |

---

## 5. Test status summary

| Suite | Approx. count | Phase 0 |
| --- | --- | --- |
| `Papyrus.Api.Tests` | 265 | Run — 265 pass (one flaky observation) |
| Node (`Tests/*.test.mjs`) | 9 files in CI | Not re-run locally |
| Python smokes | 3 in CI | 2 run — PASS |
| Rust (`Application/` workspace) | Thousands | `cargo check` only; full `cargo test` / `xtask ci` not run |
| Playwright (`Front/tests/`) | Design/regression | Not run |

---

## 6. Security findings (high level)

**Strengths (implemented):**

- Cookie session + CSRF header on mutations; rate limiting (global + account policy).
- Owner-scoped documents; version checks; admin policy; authorization coverage tests.
- Upload size limits, ZIP entry limits, converter timeouts and slot caps; hostile conversion smoke tests.
- CSP on API responses; iframe `sandbox` + origin checks (`docs/SECURITY_MODEL.md`).
- Secrets in `App_Data` (DB, data protection keys) — gitignored by policy.

**Gaps / follow-up (not claiming certification):**

- No root `SECURITY.md` or formal vulnerability disclosure process (Phase 7).
- No CodeQL / dependency audit automation in CI (Phase 3–7).
- `cargo audit` / `dotnet list package --vulnerable` not run in Phase 0.
- Job worker test flake suggests race/timing in revision guard — needs deterministic test (P0/P1).
- Multi-instance deployment, object storage, and worker isolation described in `PRODUCTION_ROADMAP.md` — not implemented.

---

## 7. Technical debt and performance

From code and `docs/PRODUCTION_ROADMAP.md`:

- SQLite + local disk storage only; no blob store abstraction in production shape yet.
- Single-node job runner; no horizontal worker story.
- Large WordCraft merge surface (`Application/`) — ongoing upstream sync cost.
- Rust full CI (`xtask ci`) heavy; not wired to GitHub Actions.
- Editor build split (`editor-host` vs full `editor:build`) easy to mismatch locally vs CI.

Performance: no benchmarks recorded in Phase 0. Converter and large-DOCX behavior covered indirectly by API tests and smokes.

---

## 8. Documentation gaps (OSS readiness)

| Document | Status |
| --- | --- |
| Root `README.md` | Present — Papyrus branding |
| `CONTRIBUTING.md` | **Missing** (added Phase 7) |
| `CODE_OF_CONDUCT.md` | **Missing** (added Phase 7) |
| `SECURITY.md` | **Missing** (added Phase 3); `docs/SECURITY_MODEL.md` is editor-focused |
| `CHANGELOG.md` | **Missing** (added Phase 7) |
| `ARCHITECTURE.md` | Partial — `Application/docs/architecture.md` + product `ARCHITECTURE.md` (Phase 7) |
| `DEVELOPMENT.md` | Partial — README run instructions; `docs/DEVELOPMENT.md` (Phase 7) |
| `ROADMAP.md` | Partial — `docs/PRODUCTION_ROADMAP.md` |
| Issue/PR templates | **Missing** |
| OSS readiness assessment | **Missing** — planned `docs/OPEN_SOURCE_READINESS.md` (Phase 9) |

---

## 9. Branding inventory (Papyrus → Papyrus)

Approximate reference counts (ripgrep, 2026-11-23):

| Area | Files with matches | Notes |
| --- | --- | --- |
| `Application/` | ~713+ | Mostly upstream ids (`Papyrus-engine`, `Papyrus_*` env, UI strings). Rebrand must distinguish **fork overlay** vs **upstream vocabulary**. |
| `Back/` | ~46 | Namespaces `Papyrus.Web`, `Papyrus.Api`, DB file `Papyrus.db`, cookie names. |
| `Front/` | ~18 | `package.json` name `Papyrus-web`, UI copy, bridge messages. |
| `docs/` (root) | 3 | CLI protocol, security model, roadmap. |
| `.github/workflows/checks.yml` | Workflow title **Papyrus checks** | |

**Do not blind-replace:** WordCraft attribution, `craft` framework ids, historical `upstream.rev` notes, existing DB filename without migration, `PapyrusHost` bridge contract until versioned alias exists.

---

## 10. Git history (recent)

Recent commits emphasize **WordCraft upstream integration**, Studio letters, Vue refactors, API hardening, and contributor-metadata cleanup. `main` was force-updated after removing `Co-authored-by` trailers from rewritten history (local and remote aligned as of push on 2026-11-23).

---

## 11. Open-source readiness assessment

| Criterion | State |
| --- | --- |
| Clear license / attribution | Yes — MIT/Apache-2.0 lineage; `Application/ATTRIBUTION.md` |
| Runnable locally | Yes — documented; Rust/i18n env for full engine CI |
| Automated checks | Partial — .NET + Front + Python in CI; Rust/Playwright not in CI |
| Contributor docs | Weak — no CONTRIBUTING / CoC |
| Security disclosure | Weak — no SECURITY.md |
| Scope honesty | Good — private workspace positioning; upstream clearly sourced |
| Maintenance signal | Real commits; avoid fabricated metrics |

**Verdict:** Suitable foundation for a serious OSS effort after rebranding plan, CI expansion, and contributor/security docs. Further OSS readiness work tracked in Phases 7–9.

---

## 12. Phase 0 completion criteria

- [x] Architecture and upstream boundary documented  
- [x] Baseline builds/tests executed (with noted gaps)  
- [x] Security, debt, branding, and docs gaps recorded  
- [x] `docs/IMPROVEMENT_ROADMAP.md` created  
- [ ] **Your review** before Phase 1 (rebrand) starts  

**Explicitly not done in Phase 0:** source rebranding, dependency upgrades, architecture refactors, or new features.