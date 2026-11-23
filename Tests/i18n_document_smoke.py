"""CLI import/export checks for Tests/fixtures/i18n samples."""
import json
import tempfile
from pathlib import Path

from cli_smoke_support import cli_executable, run_cli

ROOT = Path(__file__).resolve().parents[1]
FIXTURES = ROOT / "Tests" / "fixtures" / "i18n"


def preflight(model: Path) -> None:
    script = json.dumps({"commands": [{"command": "document.preflight", "params": {}}]})
    result = run_cli(["run", "--file", str(model), "--script", "-", "--print"], stdin=script.encode("utf-8"))
    assert result.returncode == 0, result.stderr.decode(errors="replace")[:800]
    assert b"document.preflight:" in result.stdout, result.stdout[:400]


def roundtrip(model: Path) -> None:
    with tempfile.TemporaryDirectory(prefix="papyrus-i18n-") as workspace:
        directory = Path(workspace)
        docx = directory / "out.docx"
        pdf = directory / "out.pdf"
        back = directory / "back.json"
        for args in (["convert", str(model), str(docx)], ["convert", str(model), str(pdf)]):
            result = run_cli(args, timeout=120)
            assert result.returncode == 0, result.stderr.decode(errors="replace")[:800]
            assert Path(args[2]).is_file() and Path(args[2]).stat().st_size > 0
        result = run_cli(["convert", str(docx), str(back)], timeout=120)
        assert result.returncode == 0 and back.is_file(), result.stderr.decode(errors="replace")[:800]


def import_text() -> None:
    source = FIXTURES / "mixed-scripts.txt"
    with tempfile.TemporaryDirectory(prefix="papyrus-i18n-txt-") as workspace:
        directory = Path(workspace)
        dest = directory / "imported.json"
        result = run_cli(["convert", str(source), str(dest)], timeout=120)
        assert result.returncode == 0 and dest.is_file(), result.stderr.decode(errors="replace")[:800]
        payload = json.loads(dest.read_text(encoding="utf-8"))
        assert isinstance(payload.get("body"), list) and payload["body"]


def main() -> None:
    _ = cli_executable()
    for model in sorted(FIXTURES.glob("*.json")):
        preflight(model)
        roundtrip(model)
    import_text()
    print(f"PASS: i18n fixtures ({len(list(FIXTURES.glob('*.json')))} models + text import) via {cli_executable().name}.")


if __name__ == "__main__":
    main()