"""Smoke-test the .NET ↔ inkstone-cli job protocol documented in docs/CLI_PROTOCOL.md."""
import json
import os
import subprocess
import tempfile
from pathlib import Path

from cli_smoke_support import cli_executable, run_cli
from hostile_fixtures import native_blank_model

ROOT = Path(__file__).resolve().parents[1]


def test_print_line(model):
    with tempfile.TemporaryDirectory(prefix="inkstone-cli-protocol-") as workspace:
        directory = Path(workspace)
        source = directory / "source.json"
        script = directory / "job.json"
        source.write_bytes(model)
        script.write_text(
            json.dumps({"commands": [{"command": "document.bookmarks", "params": {}}]}),
            encoding="utf-8",
        )
        completed = run_cli(["run", "--file", str(source), "--script", str(script), "--print"])
        assert completed.returncode == 0, completed.stderr.decode(errors="replace")[:800]
        lines = completed.stdout.decode("utf-8").splitlines()
        prefix = "document.bookmarks: "
        matches = [line for line in lines if line.startswith(prefix)]
        assert len(matches) == 1, lines
        report = json.loads(matches[0][len(prefix) :])
        assert "bookmarks" in report


def test_script_stdin():
    with tempfile.TemporaryDirectory(prefix="inkstone-cli-script-") as workspace:
        directory = Path(workspace)
        source = directory / "source.json"
        source.write_bytes(native_blank_model())
        script = json.dumps({"commands": [{"command": "document.preflight", "params": {}}]})
        completed = run_cli(["run", "--file", str(source), "--script", "-", "--print"], stdin=script.encode("utf-8"))
        assert completed.returncode == 0
        assert any(line.startswith("document.preflight: ") for line in completed.stdout.decode("utf-8").splitlines())


def test_version():
    completed = subprocess.run([str(cli_executable()), "--version"], capture_output=True, check=False, cwd=ROOT / "Application", timeout=30)
    assert completed.returncode == 0
    assert completed.stdout.strip(), completed.stderr


def main():
    test_print_line(native_blank_model())
    test_script_stdin()
    test_version()
    print("PASS: CLI --print prefix contract, script stdin jobs, and --version.")


if __name__ == "__main__":
    main()