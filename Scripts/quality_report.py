"""Emit docs/QUALITY_REPORT.md from real test commands (no fabricated counts)."""
from __future__ import annotations

import re
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
NODE_TESTS = [
    "Tests/editor-host.test.mjs",
    "Tests/http-core.test.mjs",
    "Tests/locales.test.mjs",
    "Tests/library-api.test.mjs",
    "Tests/use-studio-workspace.test.mjs",
    "Tests/pdf-preview.test.mjs",
    "Tests/workspace-ui.test.mjs",
    "Tests/automation-ui.test.mjs",
    "Tests/studio-state.test.mjs",
]
OUT = ROOT / "docs" / "QUALITY_REPORT.md"


def run(command: list[str], *, cwd: Path | None = None) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        command,
        cwd=cwd or ROOT,
        text=True,
        capture_output=True,
        check=False,
    )


def parse_dotnet(summary: str) -> dict[str, int]:
    match = re.search(
        r"Passed!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+(\d+)",
        summary,
    )
    if match:
        failed, passed, skipped, total = (int(x) for x in match.groups())
        return {"failed": failed, "passed": passed, "skipped": skipped, "total": total}
    match = re.search(
        r"Total tests:\s+(\d+).*?Passed:\s+(\d+).*?Failed:\s+(\d+).*?Skipped:\s+(\d+)",
        summary,
        re.S,
    )
    if match:
        total, passed, failed, skipped = (int(x) for x in match.groups())
        return {"failed": failed, "passed": passed, "skipped": skipped, "total": total}
    return {"failed": -1, "passed": -1, "skipped": -1, "total": -1}


def parse_node(summary: str) -> dict[str, int]:
    passed = failed = skipped = 0
    def metric(name: str) -> int | None:
        for line in summary.splitlines():
            m = re.search(rf"\b{name} (\d+)\b", line)
            if m and "tests" not in line:
                return int(m.group(1))
        return None

    if (passed := metric("pass")) is not None:
        failed = metric("fail") or 0
        skipped = metric("skipped") or 0
        return {"failed": failed, "passed": passed, "skipped": skipped, "total": passed + failed + skipped}
    for line in summary.splitlines():
        stripped = line.strip()
        if stripped.startswith("ok "):
            passed += 1
        elif stripped.startswith("not ok "):
            failed += 1
    total = passed + failed + skipped
    return {"failed": failed, "passed": passed, "skipped": skipped, "total": total if total else -1}


def main() -> int:
    stamp = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M UTC")
    dotnet = run(
        [
            "dotnet",
            "test",
            "Tests/Papyrus.Api.Tests/Papyrus.Api.Tests.csproj",
            "-p:SkipRustBuild=true",
            "--nologo",
        ],
    )
    node = run(["node", "--test", *NODE_TESTS])
    nuget = run(
        ["dotnet", "list", "Back/Papyrus.Api.csproj", "package", "--vulnerable", "--include-transitive"],
    )
    rust = run(["cargo", "check", "--workspace", "--locked"], cwd=ROOT / "Application")

    api = parse_dotnet((dotnet.stdout or "") + (dotnet.stderr or ""))
    unit = parse_node((node.stdout or "") + (node.stderr or ""))

    vulnerable = "no vulnerable packages" in nuget.stdout.lower() or "has no vulnerable" in nuget.stdout.lower()
    if not vulnerable and nuget.returncode != 0:
        vulnerable = None

    body = f"""# Quality report

**Generated:** {stamp} by `Scripts/quality_report.py` (counts come from commands on this machine).

## API integration (.NET)

| Metric | Value |
| --- | --- |
| Total | {api['total']} |
| Passed | {api['passed']} |
| Failed | {api['failed']} |
| Skipped | {api['skipped']} |
| Exit code | {dotnet.returncode} |

## Front unit (Node test runner)

| Metric | Value |
| --- | --- |
| Total | {unit['total']} |
| Passed | {unit['passed']} |
| Failed | {unit['failed']} |
| Skipped | {unit['skipped']} |
| Exit code | {node.returncode} |

## Playwright (browser, mocked API)

Run locally: `npm run test:browser` in `Front/` (not executed by this script by default).

## Build gates

| Check | Exit code |
| --- | --- |
| `cargo check --workspace --locked` | {rust.returncode} |
| NuGet vulnerable packages | {"none reported" if vulnerable else "see `dotnet list package --vulnerable`" if vulnerable is not None else "audit failed"} |

## CI

See `.github/workflows/checks.yml` for jobs: `api-and-web`, `rust-engine`, `dependencies`, `browser`.
"""
    OUT.write_text(body, encoding="utf-8")
    print(f"Wrote {OUT}")
    return dotnet.returncode or node.returncode or rust.returncode


if __name__ == "__main__":
    raise SystemExit(main())