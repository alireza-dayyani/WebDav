# Performance and reliability (Phase 5)

Honest summary of how Papyrus behaves under load and failure **today**. For numeric baselines see [`PERFORMANCE_BASELINE.md`](PERFORMANCE_BASELINE.md).

## Performance focus areas

| Area | Current behavior | Bottleneck risk |
| --- | --- | --- |
| **Converter** | Subprocess per job; 2+2 default slots; 60s default timeout | CPU-bound PDF/export; queue wait → 429 |
| **SQLite** | Single file; FTS5 search | Write amplification on history + autosave |
| **API** | In-process job worker (1s poll) | One worker; no horizontal scale |
| **Front** | Vue SPA + WASM iframe | Large `dist` JS; editor build separate |
| **Autosave** | Full model PUT | Payload size + converter preflight |

Do not scale users without running the mixed workload plan in `PRODUCTION_ROADMAP.md` § Capacity validation.

## Reliability under failure

| Scenario | Expected behavior |
| --- | --- |
| API process restart while job `running` | Job marked failed with restart message; client may retry |
| Document version changes while job queued | Worker fails job; `Worker_fails_when_queued_source_revision_changes…` test |
| Converter timeout | Process tree killed; metrics `papyrus.converter.timeouts` |
| Two API processes same data dir | Second instance blocked by `instance.lock` (by design) |
| Client disconnect during upload | Request cancellation propagates when linked to HTTP token |
| Editor tab account switch | `X-Papyrus-Account` mismatch → 403 `account_changed` |

## Instrumentation today

OpenTelemetry meters: `Papyrus.Conversion` — `papyrus.converter.queue_wait`, `active`, `rejected`, `timeouts`, `failures` (pool label only).

HTTP latency dashboards and alerting are **not** shipped in-repo (infrastructure work in roadmap).

## Commands

```powershell
python Scripts/performance_baseline.py
python Scripts/quality_report.py
```