# Quality report

**Generated:** 2026-11-23 08:36 UTC by `Scripts/quality_report.py` (counts come from commands on this machine).

## API integration (.NET)

| Metric | Value |
| --- | --- |
| Total | 265 |
| Passed | 265 |
| Failed | 0 |
| Skipped | 0 |
| Exit code | 0 |

## Front unit (Node test runner)

| Metric | Value |
| --- | --- |
| Total | 28 |
| Passed | 28 |
| Failed | 0 |
| Skipped | 0 |
| Exit code | 0 |

## Playwright (browser, mocked API)

Run locally: `npm run test:browser` in `Front/` (not executed by this script by default).

## Build gates

| Check | Exit code |
| --- | --- |
| `cargo check --workspace --locked` | 0 |
| NuGet vulnerable packages | none reported |

## CI

See `.github/workflows/checks.yml` for jobs: `api-and-web`, `rust-engine`, `dependencies`, `browser`.
