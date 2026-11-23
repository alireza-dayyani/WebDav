# Performance baseline (Phase 5)

**Generated:** 2026-11-23 09:38 UTC via `Scripts/performance_baseline.py` on the machine that ran it.  
**Not** a capacity or SLA promise. Use for regression comparison only.

## Environment

| Item | Value |
| --- | --- |
| CLI | `C:\Users\Alireza\source\repos\WebDav\Back\bin\Debug\net10.0\papyrus-cli.exe` |
| Fixture | `minimal_docx` hostile fixture (single paragraph DOCX) |
| Convert command | `papyrus-cli convert <docx> <json>` |

## Converter (subprocess)

| Metric | Result |
| --- | --- |
| DOCX → JSON | min 21 ms · median 23 ms · max 24 ms (n=5) |

Tune `Conversion:TimeoutSeconds` and slot counts in `Back/README.md` if medians approach timeout under load.

## Front bundle (static)

| Asset | Size |
| --- | --- |
| `index-CC25pMBg.css` | 42,503 bytes |
| `index-ChWmCRE0.js` | 207,352 bytes |

## Reliability mechanisms (verified elsewhere)

| Mechanism | Where |
| --- | --- |
| Converter pool isolation (validation vs conversion) | `ConverterCapacity`, `CapacityTests` |
| Queue wait cap → HTTP 429 `converter_busy` | 5 s wait in `ConverterCapacity.EnterAsync` |
| Job restart marks `running` → `failed` | `DocumentJobRunner.ExecuteAsync` startup |
| Revision guard on queued jobs | `JobAndTokenEndpointTests` |
| Single-instance data directory | `instance.lock` in `Program.cs` |
| Process-tree kill on CLI timeout | `DocumentConverter` |

## What is not measured here

- Concurrent editor load (see `docs/PRODUCTION_ROADMAP.md` capacity validation plan).
- Large (500+ page) documents — engine gaps tracked in upstream `Application/docs/gaps.md`.
- WASM editor frame time — run upstream/ui-egui perf tests locally when needed.

## Re-run

```powershell
python Scripts/performance_baseline.py
dotnet test Tests/Papyrus.Api.Tests/Papyrus.Api.Tests.csproj -p:SkipRustBuild=true
```
