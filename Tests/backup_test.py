import importlib.util
from contextlib import closing
from pathlib import Path
import sqlite3
import tempfile
import unittest
import zipfile
import json
import hashlib

spec = importlib.util.spec_from_file_location("backup", Path(__file__).resolve().parents[1] / "Scripts/backup.py")
backup = importlib.util.module_from_spec(spec)
spec.loader.exec_module(backup)


class BackupTests(unittest.TestCase):
    def test_round_trip_and_refuse_overwrite(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); source = root / "source"; source.mkdir()
            with closing(sqlite3.connect(source / "inkstone.db")) as db:
                db.execute("CREATE TABLE Documents (Content TEXT)")
                db.execute("INSERT INTO Documents VALUES ('سلام')")
                db.commit()
            (source / "keys").mkdir(); (source / "keys/key.xml").write_text("test-key")
            archive = root / "backup.zip"; destination = root / "restored"
            backup.backup(source, archive); backup.restore(archive, destination)
            with closing(sqlite3.connect(destination / "inkstone.db")) as db:
                self.assertEqual(db.execute("SELECT Content FROM Documents").fetchone()[0], "سلام")
            self.assertEqual((destination / "keys/key.xml").read_text(), "test-key")
            with self.assertRaises(ValueError): backup.restore(archive, destination)
            with self.assertRaises(ValueError): backup.backup(source, archive)

    def test_path_traversal_is_rejected_without_creating_destination(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); archive = root / "bad.zip"; destination = root / "restored"
            files = {"inkstone.db": b"bad", "../escape": b"escape"}
            with zipfile.ZipFile(archive, "w") as out:
                for name, data in files.items(): out.writestr(name, data)
                out.writestr("manifest.json", json.dumps({"format": 1, "files": {n: hashlib.sha256(d).hexdigest() for n, d in files.items()}}))
            with self.assertRaises(ValueError): backup.restore(archive, destination)
            self.assertFalse(destination.exists()); self.assertFalse((root / "escape").exists())


if __name__ == "__main__": unittest.main()
