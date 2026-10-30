"""Disposable real-process checks for private registration, readiness, upgrades and the instance lease."""
from contextlib import closing
import os
from pathlib import Path
import sqlite3
import subprocess
import tempfile
import time
from studio_test_support import Client, ROOT

BASE = "http://127.0.0.1:5098"


def start(data, log, **settings):
    env = {**os.environ, "ASPNETCORE_ENVIRONMENT": "Development", "Storage__DataDirectory": str(data), "BootstrapAdmin__File": str(data / "none.json"), **settings}
    process = subprocess.Popen(["dotnet", str(ROOT / "Back/bin/Debug/net10.0/Inkstone.Api.dll"), "--urls", BASE], cwd=ROOT / "Back", env=env, stdout=log, stderr=log)
    for _ in range(120):
        if process.poll() is not None: raise RuntimeError("Test API exited; inspect deployment-smoke.log")
        try:
            Client(BASE).json("/health")
            return process
        except OSError: time.sleep(.25)
    process.terminate(); process.wait(timeout=20)
    raise RuntimeError("Test API startup timed out")


def stop(process):
    process.terminate(); process.wait(timeout=20)


with tempfile.TemporaryDirectory(prefix="inkstone-deployment-") as folder, open(ROOT / "Tests/deployment-smoke.log", "w", encoding="utf-8") as log:
    data = Path(folder)
    process = start(data, log, Accounts__AllowRegistration="false", Rust__Executable=str(data / "missing-converter"))
    try:
        guest = Client(BASE)
        guest.json("/ready", expected=503)
        guest.json("/account/register", "POST", {"email": "disabled@example.test", "password": "StudioTests123"}, expected=403)
        duplicate = subprocess.Popen(["dotnet", str(ROOT / "Back/bin/Debug/net10.0/Inkstone.Api.dll"), "--urls", "http://127.0.0.1:5097"], cwd=ROOT / "Back", env={**os.environ, "ASPNETCORE_ENVIRONMENT": "Development", "Storage__DataDirectory": str(data)}, stdout=log, stderr=log)
        try: assert duplicate.wait(timeout=20) != 0, "A second process acquired the same data directory"
        finally:
            if duplicate.poll() is None: stop(duplicate)
    finally: stop(process)
    process = start(data, log, Accounts__AllowRegistration="true")
    try:
        owner = Client(BASE); owner.register("deployment-owner")
        doc = owner.json("/documents/", "POST", {"title": "Preserve across upgrade"}, expected=201)
        owner.json("/documents/" + doc["id"] + "/content", "PUT", {"version": 1, "document": {"body": []}})
        owner.json("/ready")
    finally: stop(process)
    # Simulate an installation predating the schema ledger, retaining actual accounts,
    # password hashes, documents, revisions and Data Protection keys.
    with closing(sqlite3.connect(data / "inkstone.db")) as db:
        db.execute("DROP TABLE SchemaVersions"); db.execute("DROP TABLE TrashedDocuments"); db.commit()
    process = start(data, log)
    try:
        assert owner.json("/documents/" + doc["id"])["title"] == "Preserve across upgrade"
        assert len(owner.json("/documents/" + doc["id"] + "/versions?version=2")["items"]) == 2
        with closing(sqlite3.connect(data / "inkstone.db")) as db:
            assert db.execute("SELECT COUNT(*) FROM SchemaVersions").fetchone()[0] == 4
    finally: stop(process)
print("PASS: disabled registration, missing-converter readiness, single-instance lease and existing-account/document/history upgrade")
