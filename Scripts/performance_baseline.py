"""Record reproducible performance baselines for docs/PERFORMANCE_BASELINE.md."""
from __future__ import annotations

import statistics
import subprocess
import sys
import tempfile
import time
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "docs" / "PERFORMANCE_BASELINE.md"
sys.path.insert(0, str(ROOT / "Tests"))

from cli_smoke_support import cli_executable, run_cli  # noqa: E402
from hostile_fixtures import minimal_docx  # noqa: E402


def time_convert(runs: int = 5) -> list[float]:
    docx = minimal_docx("Baseline timing")
    timings: list[float] = []
    with tempfile.TemporaryDirectory(prefix="papyrus-perf-") as workspace:
        source = Path(workspace) / "source.docx"
        dest = Path(workspace) / "model.json"
        source.write_bytes(docx)
        warmup = run_cli(["convert", str(source), str(dest)], timeout=120)
        if warmup.returncode != 0 or not dest.is_file():
            raise RuntimeError("warm-up convert failed")
        if dest.exists():
            dest.unlink()
        for _ in range(runs):
            if dest.exists():
                dest.unlink()
            started = time.perf_counter()
            result = run_cli(["convert", str(source), str(dest)], timeout=120)
            elapsed_ms = (time.perf_counter() - started) * 1000.0
            if result.returncode != 0 or not dest.is_file():
                raise RuntimeError(
                    result.stderr.decode(errors="replace")[:500] or f"convert failed ({result.returncode})"
                )
            timings.append(elapsed_ms)
    return timings


def front_bundle_bytes() -> dict[str, int | str]:
    dist = ROOT / "Front" / "dist" / "assets"
    if not dist.is_dir():
        return {"status": "Front/dist not built — run `npm run build` in Front/"}
    sizes = {}
    for path in sorted(dist.glob("*")):
        if path.is_file():
            sizes[path.name] = path.stat().st_size
    return sizes


def fmt_stats(values: list[float]) -> str:
    if not values:
        return "n/a"
    return (
        f"min {min(values):.0f} ms · median {statistics.median(values):.0f} ms · "
        f"max {max(values):.0f} ms (n={len(values)})"
    )


def main() -> int:
    stamp = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M UTC")
    cli = cli_executable()
    convert_ms = time_convert()
    bundles = front_bundle_bytes()

    if isinstance(bundles.get("status"), str):
        bundle_rows = f"| — | {bundles['status']} |"
    else:
        bundle_rows = "\n".join(f"| `{name}` | {size:,} bytes |" for name, size in bundles.items()) or "| — | (empty dist) |"

    body = f"""# Performance baseline (Phase 5)

**Generated:** {stamp} via `Scripts/performance_baseline.py` on the machine that ran it.  
**Not** a capacity or SLA promise. Use for regression comparison only.

## Environment

| Item | Value |
| --- | --- |
| CLI | `{cli}` |
| Fixture | `minimal_docx` hostile fixture (single paragraph DOCX) |
| Convert command | `papyrus-cli convert <docx> <json>` |

## Converter (subprocess)

| Metric | Result |
| --- | --- |
| DOCX → JSON | {fmt_stats(convert_ms)} |

Tune `Conversion:TimeoutSeconds` and slot counts in `Back/README.md` if medians approach timeout under load.

## Front bundle (static)

| Asset | Size |
| --- | --- |
{bundle_rows}

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
"""
    OUT.write_text(body, encoding="utf-8")
    print(f"Wrote {OUT}")
    print(f"Convert: {fmt_stats(convert_ms)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())