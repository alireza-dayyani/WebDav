"""Shared subprocess helpers for inkstone-cli smoke tests."""
import os
import subprocess
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEADLINE_SECONDS = 65


def cli_executable():
    override = os.environ.get("INKSTONE_CLI")
    if override and Path(override).is_file():
        return Path(override)
    for relative in (
        "Back/bin/Debug/net10.0/inkstone-cli.exe",
        "Back/bin/Release/net10.0/inkstone-cli.exe",
        "Back/bin/Debug/net10.0/inkstone-cli",
        "Application/target/debug/inkstone-cli.exe",
        "Application/target/debug/inkstone-cli",
    ):
        path = ROOT / relative
        if path.is_file():
            return path
    raise RuntimeError("Build Back/Inkstone.Api.csproj (or cargo build -p inkstone-cli) before running this smoke.")


def run_cli(args, stdin=None, timeout=DEADLINE_SECONDS):
    executable = cli_executable()
    start = time.monotonic()
    completed = subprocess.run(
        [str(executable), *args],
        cwd=ROOT / "Application",
        input=stdin,
        text=isinstance(stdin, str),
        capture_output=True,
        timeout=timeout,
        check=False,
    )
    elapsed = time.monotonic() - start
    assert elapsed < timeout, f"CLI exceeded {timeout}s: {' '.join(args)}"
    return completed