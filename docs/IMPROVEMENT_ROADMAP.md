# Improvement roadmap

Prioritized backlog for the **Papyrus** program. Each item should be delivered in **small, verifiable milestones** with tests and docs updated together. Status: **Phase 5 complete** (milestone 5.1, 2026-11-23).

Legend: **P0** critical · **P1** architecture/reliability · **P2** testing/docs · **P3** product · **P4** long-term

---

## P0 — Critical security and correctness

| ID | Item | Evidence / trigger |
| --- | --- | --- |
| P0-1 | Stabilize `JobAndTokenEndpointTests.Worker_fails_when_queued_source_revision_changes_before_execution` | **Done** (Phase 1) |
| P0-2 | Complete baseline run: `cargo xtask ci` + `CRAFT_I18N_DIR` in CI or documented local gate | Not run in Phase 0; large merge risk |
| P0-3 | Root `SECURITY.md` + supported disclosure channel | **Done** (Phase 3) |
| P0-4 | Dependency vulnerability scan in CI (`dotnet list package`, `cargo audit` or equivalent) | **Done** (Phase 3 CI job) |
| P0-5 | Product identity (cookies, DB `papyrus.db`, converter `papyrus-cli`) | Done in Phase 1 |

---

## P1 — Architecture and reliability

| ID | Item | Notes |
| --- | --- | --- |
| P1-1 | **Product identity** (UI, README, OpenAPI, `PAPYRUS_*` / `papyrus-cli`) | **Done** (Phase 1) |
| P1-2 | Optional **tier C/D** crate/assembly rename (`papyrus-*`) | High cost vs upstream sync; needs explicit approval |
| P1-3 | Add Rust job to `.github/workflows/checks.yml` (at least `cargo check --workspace`) | **Done** (Phase 2) |
| P1-4 | Document converter capacity and failure modes (`ConverterCapacity`, metrics) | Partially done; document ops |
| P1-5 | Object storage interface (from `PRODUCTION_ROADMAP.md`) | Not started |
| P1-6 | Deterministic job worker behavior under revision change | Tied to P0-1 |

---

## P2 — Testing and documentation

| ID | Item | Notes |
| --- | --- | --- |
| P2-1 | Playwright in CI (or nightly) with mocked API | **Done** (Phase 4 `browser` job) |
| P2-2 | `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `CHANGELOG.md` | OSS Phase 7 |
| P2-3 | `docs/REBRANDING.md` after Phase 1 | **Done** |
| P2-4 | `docs/SECURITY_REVIEW.md` (Phase 3 assessment) | **Done** (Phase 3) |
| P2-5 | Quality report artifact from CI (test counts, no fabrication) | **Done** (`Scripts/quality_report.py`, `docs/QUALITY_REPORT.md`) |
| P2-6 | i18n fixture documents (fa/ar/ur/en) under `Tests/fixtures` | Phase 6 |

---

## P3 — Product differentiation

| ID | Item | Notes |
| --- | --- | --- |
| P3-1 | Self-hosted deployment story (Docker) with **papyrus** naming | When approved |
| P3-2 | Public API examples aligned with Papyrus branding | |
| P3-3 | Studio letters + automation as documented differentiators | Already strong |
| P3-4 | Search/retention/sharing UX polish | Per PRODUCTION_ROADMAP |
| P3-5 | Accessibility pass on Vue shell | Phase 2/6 |

---

## P4 — Long-term

| ID | Item | Notes |
| --- | --- | --- |
| P4-1 | `docs/CODEX_OPEN_SOURCE_READINESS.md` honest assessment | Phase 9 |
| P4-2 | GitHub repo rename to `papyrus` | **Requires user authorization** |
| P4-3 | Separate release train from WordCraft | |
| P4-4 | Multi-worker / horizontal scaling | PRODUCTION_ROADMAP |

---

## Recommended next milestone (Phase 6 → Phase 7)

1. **Phase 6** — i18n fixture documents (fa/ar/ur/en) and regression tests (P2-6).  
2. **Phase 4b** — `file_api_smoke.py` in CI.  
3. **P0-2** — `cargo xtask ci` gate with `CRAFT_I18N_DIR`.  
4. **Phase 7** — `CONTRIBUTING.md`, CodeQL (SR-1).

---

## Deferred until approved

- GitHub repository rename or public releases  
- License change  
- Database destructive migrations  
- Force-push or history rewrite (avoid unless requested)