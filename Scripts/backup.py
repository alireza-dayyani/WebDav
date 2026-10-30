"""Consistent SQLite backup and verified restore into a new, empty directory.

Stop Inkstone while backing up so keys/configuration and document data form one
recovery point. Restore never overwrites an existing installation.
"""
import argparse
from contextlib import closing
import hashlib
import json
from pathlib import Path
import shutil
import sqlite3
import tempfile
import zipfile


def check_database(path):
    with closing(sqlite3.connect(path.as_uri() + "?mode=ro", uri=True)) as db:
        if db.execute("PRAGMA integrity_check").fetchone()[0] != "ok":
            raise ValueError("Database integrity check failed")


def backup(data, output):
    data, output = Path(data).resolve(), Path(output).resolve()
    if not (data / "inkstone.db").is_file():
        raise ValueError("The source database does not exist")
    if output.exists() or output.is_relative_to(data):
        raise ValueError("Choose a new backup file outside the data directory")
    with tempfile.TemporaryDirectory(prefix="inkstone-backup-") as temporary:
        stage = Path(temporary)
        with closing(sqlite3.connect((data / "inkstone.db").as_uri() + "?mode=ro", uri=True)) as source:
            with closing(sqlite3.connect(stage / "inkstone.db")) as destination:
                source.backup(destination)
        for name in ("keys", "bootstrap"):
            if (data / name).exists():
                shutil.copytree(data / name, stage / name)
        check_database(stage / "inkstone.db")
        files = [p for p in stage.rglob("*") if p.is_file()]
        manifest = {str(p.relative_to(stage).as_posix()): hashlib.sha256(p.read_bytes()).hexdigest() for p in files}
        with zipfile.ZipFile(output, "x", compression=zipfile.ZIP_DEFLATED) as archive:
            for file in files:
                archive.write(file, file.relative_to(stage).as_posix())
            archive.writestr("manifest.json", json.dumps({"format": 1, "files": manifest}))


def restore(archive_path, destination):
    destination = Path(destination).resolve()
    if destination.exists():
        raise ValueError("Restore requires a new destination directory")
    with tempfile.TemporaryDirectory(prefix="inkstone-restore-") as temporary:
        stage = Path(temporary)
        with zipfile.ZipFile(archive_path) as archive:
            names = archive.namelist()
            if len(names) != len(set(names)):
                raise ValueError("Duplicate archive entries")
            manifest = json.loads(archive.read("manifest.json"))
            if manifest.get("format") != 1 or set(names) != set(manifest["files"]) | {"manifest.json"} or "inkstone.db" not in manifest["files"]:
                raise ValueError("Invalid backup manifest")
            for name, digest in manifest["files"].items():
                path = (stage / name).resolve()
                if "\\" in name or not path.is_relative_to(stage) or path == stage:
                    raise ValueError("Invalid backup path")
                data = archive.read(name)
                if hashlib.sha256(data).hexdigest() != digest:
                    raise ValueError("Backup checksum mismatch")
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(data)
        check_database(stage / "inkstone.db")
        shutil.copytree(stage, destination)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=["backup", "restore"])
    parser.add_argument("source")
    parser.add_argument("destination")
    args = parser.parse_args()
    try:
        (backup if args.operation == "backup" else restore)(args.source, args.destination)
    except (OSError, ValueError, sqlite3.Error, zipfile.BadZipFile, KeyError) as error:
        parser.exit(1, f"{error}\n")
