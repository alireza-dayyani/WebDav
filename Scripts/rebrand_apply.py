#!/usr/bin/env python3
"""One-shot Papyrus → Papyrus text rebrand (run after reading REBRANDING.md)."""
from __future__ import annotations

import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SKIP_DIRS = {
    ".git",
    "node_modules",
    "target",
    "bin",
    "obj",
    "dist",
    "__pycache__",
    "Tests/generated",
}
SKIP_SUFFIX = {".png", ".jpg", ".jpeg", ".gif", ".webp", ".ico", ".pdf", ".woff", ".woff2", ".db", ".dll", ".exe", ".pdb", ".wasm", ".lock"}

PROTECTED = [
    "storytold/wordcraft",
    "storytold/craft-libs",
    "wordcraft.tsv",
    "wordcraft@",
    "getartcraft.com/apps/wordcraft",
]

# Longer patterns first.
REPLACEMENTS = [
    ("papyrus-ui-egui", "papyrus-ui-egui"),
    ("papyrus-control-key", "papyrus-control-key"),
    ("papyrus-web", "papyrus-web"),
    ("papyrus-cli", "papyrus-cli"),
    ("papyrus-docbin", "papyrus-docbin"),
    ("papyrus-docx", "papyrus-docx"),
    ("papyrus-formats", "papyrus-formats"),
    ("papyrus-engine", "papyrus-engine"),
    ("papyrus-fonts", "papyrus-fonts"),
    ("papyrus-layout", "papyrus-layout"),
    ("papyrus-render", "papyrus-render"),
    ("papyrus-zotero", "papyrus-zotero"),
    ("papyrus-geom", "papyrus-geom"),
    ("papyrus-doc", "papyrus-doc"),
    ("papyrus-mcp", "papyrus-mcp"),
    ("papyrus-pdf", "papyrus-pdf"),
    ("papyrus-proof", "papyrus-proof"),
    ("papyrusHost", "papyrusHost"),
    ("PAPYRUS_", "PAPYRUS_"),
    ("Papyrus", "Papyrus"),
    ("papyrus", "papyrus"),
    ("PAPYRUS", "PAPYRUS"),
]


def should_skip(path: Path) -> bool:
    if any(part in SKIP_DIRS for part in path.parts):
        return True
    return path.suffix.lower() in SKIP_SUFFIX


def map_text(text: str) -> str:
    for i, keep in enumerate(PROTECTED):
        text = text.replace(keep, f"\0prot{i}\0")
    for old, new in REPLACEMENTS:
        text = text.replace(old, new)
    for i, keep in enumerate(PROTECTED):
        text = text.replace(f"\0prot{i}\0", keep)
    return text


def main() -> int:
    changed = 0
    for dirpath, dirnames, filenames in os.walk(ROOT):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for name in filenames:
            path = Path(dirpath) / name
            if should_skip(path):
                continue
            try:
                raw = path.read_text(encoding="utf-8")
            except (UnicodeDecodeError, OSError):
                continue
            if "papyrus" not in raw.lower() and "PAPYRUS" not in raw and "Papyrus" not in raw:
                continue
            updated = map_text(raw)
            if updated != raw:
                path.write_text(updated, encoding="utf-8", newline="\n")
                changed += 1
                print(path.relative_to(ROOT))
    print(f"Updated {changed} files.", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())