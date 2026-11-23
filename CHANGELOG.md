# Changelog

All notable product-layer changes in this repository are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

Upstream WordCraft (`Application/`) carries its own history; this file covers **Papyrus** integration, CI, and documentation unless a release tag states otherwise.

## [Unreleased]

### Added

- Contributor docs: `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `ARCHITECTURE.md`, `docs/DEVELOPMENT.md`.
- GitHub CodeQL workflow for C# and JavaScript/TypeScript (`.github/workflows/codeql.yml`).

## [0.7.0] - 2026-11-23

### Added

- **Phase 6:** International document fixtures (`Tests/fixtures/i18n/`), `I18nFixtureTests`, `i18n_document_smoke.py`, `docs/I18N_SUPPORT.md`.
- **Phase 5:** Performance baseline script and `docs/PERFORMANCE_RELIABILITY.md`.
- **Phase 4:** Playwright CI job, `Scripts/quality_report.py`, `docs/TESTING.md`.
- **Phase 3:** Root `SECURITY.md`, `docs/SECURITY_REVIEW.md`, dependency vulnerability CI job.
- **Phase 2:** `docs/ARCHITECTURE_QUALITY.md`, `rust-engine` CI job.
- **Phase 1:** Papyrus product identity (`Papyrus.Api`, `papyrus-cli`, `papyrus.db`, frontend host naming), `docs/REBRANDING.md`.

### Changed

- GitHub Actions expanded from API-only smokes to multi-job checks (browser, dependencies, i18n smoke).
- Job/token integration test stabilized against revision races during queued work.

## [0.0.0] - Baseline

### Note

Pre-Papyrus maturity work is recorded in `docs/BASELINE_AUDIT.md` (Phase 0, 2026-11-23). No semver releases were published at that snapshot.