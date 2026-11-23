"""Converter boundary checks: archive validation at the API and inkstone-cli subprocess limits."""
import json
import tempfile
from pathlib import Path

from cli_smoke_support import run_cli
from hostile_fixtures import (
    minimal_docx,
    zip_entry_count,
    zip_inflated_metadata,
    zip_traversal_docx,
)
from studio_test_support import Client, server

PORT = 5095


def upload_expect(client, name, payload, status):
    client.multipart("/documents/upload", name, payload, expected=status)


def main():
    with server(PORT, "converter-hostile") as (base, _):
        client = Client(base)
        client.json("/account/register", "POST", {"email": "hostile@example.test", "password": "StudioTests123"})

        good = minimal_docx()
        upload_expect(client, "good.docx", good, 201)

        upload_expect(client, "many.docx", zip_entry_count(2049), 413)
        upload_expect(client, "slide.docx", zip_traversal_docx(), 422)
        upload_expect(client, "inflated.docx", zip_inflated_metadata(), 413)
        upload_expect(client, "broken.docx", b"not-a-zip", 422)

        with tempfile.TemporaryDirectory(prefix="inkstone-hostile-cli-") as workspace:
            source = Path(workspace) / "source.docx"
            destination = Path(workspace) / "result.json"
            source.write_bytes(good)
            convert = run_cli(["convert", str(source), str(destination)])
            assert convert.returncode == 0 and destination.is_file(), convert.stderr.decode(errors="replace")[:800]
            model = json.loads(destination.read_text(encoding="utf-8"))
            assert isinstance(model.get("body"), list)

            bad = Path(workspace) / "inflated.docx"
            bad.write_bytes(zip_inflated_metadata())
            bad_convert = run_cli(["convert", str(bad), str(Path(workspace) / "bad.json")])
            assert bad_convert.returncode != 0

    print("PASS: archive rejection at upload, healthy DOCX import, and bounded hostile CLI conversion attempts.")


if __name__ == "__main__":
    main()