#!/usr/bin/env python3
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BLOCK = re.compile(
    r"<<<<<<< Inkstone\r?\n(.*?)\r?\n=======\r?\n(.*?)\r?\n>>>>>>> upstream after(?:\r?\n)?",
    re.DOTALL,
)
KEEP_INKSTONE = {
    ROOT / "README.md",
    ROOT / "ATTRIBUTION.md",
    ROOT / "crates/ui-egui/src/updates_ui.rs",
}


def resolve(path: Path, prefer_upstream: bool) -> bool:
    text = path.read_text(encoding="utf-8")
    if "<<<<<<< Inkstone" not in text:
        return False
    out = BLOCK.sub(lambda m: m.group(2) if prefer_upstream else m.group(1), text)
    if "<<<<<<< Inkstone" in out:
        sys.exit(f"unresolved conflicts remain in {path}")
    path.write_text(out, encoding="utf-8")
    return True


def main() -> None:
    changed = 0
    for path in ROOT.rglob("*"):
        if not path.is_file() or "target" in path.parts or path.suffix not in {".rs", ".toml", ".md", ".lock"}:
            continue
        if resolve(path, prefer_upstream=path not in KEEP_INKSTONE):
            changed += 1
            print(path.relative_to(ROOT.parent))
    print(f"resolved {changed} file(s)")


if __name__ == "__main__":
    main()