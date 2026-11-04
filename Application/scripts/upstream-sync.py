#!/usr/bin/env python3
"""Bring storytold/wordcraft changes into Application/ without losing Inkstone's own edits.

Every upstream file is mapped to this workspace (paths under Application/, `wordcraft`
identifiers renamed to `inkstone`) and three-way merged into the working copy: the base is the
mapped upstream file before the change, "theirs" the mapped file after it, and "ours" the local
file. Local edits survive unless upstream changed the same lines; those files get conflict
markers and are listed at the end.

The integrated upstream commit is recorded in Application/upstream.rev (first line), followed by
any upstream commits picked individually since then.

  python Application/scripts/upstream-sync.py list           commits not yet integrated
  python Application/scripts/upstream-sync.py show SHA       files a commit changes, mapped
  python Application/scripts/upstream-sync.py pick SHA...    apply single commits (fixes)
  python Application/scripts/upstream-sync.py update [--to REV]
                                                            apply everything up to REV
Add --dry-run to `pick` or `update` to report without writing. Run `git fetch upstream` first.
"""

import argparse
import fnmatch
import os
import subprocess
import sys
import tempfile
from pathlib import Path

UPSTREAM = "upstream/main"
PREFIX = "Application"
REV_FILE = "upstream.rev"

# Upstream paths that are the fork's own business: CI, agent instructions, roadmaps, release
# process and the About window's contributor credits. Changes to them are reported, never applied.
EXCLUDED = [
    ".github/*",
    "contributors/*",
    "AGENTS.md",
    "CLAUDE.md",
    "ROADMAP.md",
    "docs/releasing.md",
    "docs/roadmap.md",
    "*.DS_Store",
]

# Names that must keep the upstream spelling: links to the upstream project, commit references
# and craft-i18n's catalog file name.
PROTECTED = ["storytold/wordcraft", "wordcraft.tsv", "wordcraft@"]
RENAMES = [("WORDCRAFT", "INKSTONE"), ("WordCraft", "Inkstone"), ("Wordcraft", "Inkstone"), ("wordcraft", "inkstone")]


def git(*args, check=True, root=None):
    r = subprocess.run(["git", *args], cwd=root, capture_output=True)
    if check and r.returncode != 0:
        sys.exit(f"git {' '.join(args)} failed:\n{r.stderr.decode(errors='replace')}")
    return r


def git_text(*args, root=None):
    return git(*args, root=root).stdout.decode("utf-8", errors="replace").strip()


def map_path(p):
    return p.replace("wordcraft", "inkstone")


def map_text(text):
    for i, keep in enumerate(PROTECTED):
        text = text.replace(keep, f"\0{i}\0")
    for old, new in RENAMES:
        text = text.replace(old, new)
    for i, keep in enumerate(PROTECTED):
        text = text.replace(f"\0{i}\0", keep)
    return text


def as_text(data):
    if data is None or b"\0" in data:
        return None
    try:
        return data.decode("utf-8")
    except UnicodeDecodeError:
        return None


def blob(rev, path, root):
    r = git("cat-file", "blob", f"{rev}:{path}", check=False, root=root)
    return r.stdout if r.returncode == 0 else None


def mapped(data, path, root):
    """Upstream bytes as they would read in this workspace."""
    text = as_text(data)
    if text is None:
        return data
    out = map_text(text).encode("utf-8")
    if path.endswith(".rs"):
        # Renamed crates sort differently in `use` lists; formatting keeps them where the fork's
        # rustfmt put them, so import order alone never conflicts.
        config = root / PREFIX / "rustfmt.toml"
        r = subprocess.run(["rustfmt", "--edition", "2024", "--emit", "stdout", "--config-path", str(config)], input=out, capture_output=True)
        if r.returncode == 0 and r.stdout:
            out = r.stdout.replace(b"\r\n", b"\n")
    return out


def excluded(path):
    return any(fnmatch.fnmatch(path, pat) for pat in EXCLUDED)


class State:
    def __init__(self, path):
        self.path = path
        lines = [ln.split("#")[0].strip() for ln in path.read_text(encoding="utf-8").splitlines()]
        lines = [ln for ln in lines if ln]
        if not lines:
            sys.exit(f"{path} has no baseline commit")
        self.baseline = lines[0]
        self.picked = lines[1:]

    def save(self):
        out = [self.baseline] + [f"{sha}  # picked" for sha in self.picked]
        self.path.write_text("\n".join(out) + "\n", encoding="utf-8", newline="\n")


def changes(base, new, root):
    """(status, old upstream path, new upstream path) for every file that differs."""
    raw = git("diff", "-z", "--name-status", "-M", base, new, root=root).stdout.decode("utf-8").split("\0")
    out, i = [], 0
    while i < len(raw) and raw[i]:
        st = raw[i]
        if st[0] in "RC":
            out.append((st[0], raw[i + 1], raw[i + 2]))
            i += 3
        else:
            out.append((st[0], raw[i + 1], raw[i + 1]))
            i += 2
    return out


def write(path, data, crlf, dry):
    if dry:
        return
    if crlf and as_text(data) is not None:
        data = data.replace(b"\r\n", b"\n").replace(b"\n", b"\r\n")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)


def merge3(ours, base, theirs):
    """git merge-file on the three texts: (merged bytes, conflict count)."""
    with tempfile.TemporaryDirectory() as tmp:
        files = []
        for name, data in (("ours", ours), ("base", base), ("theirs", theirs)):
            f = Path(tmp) / name
            f.write_bytes(data.replace(b"\r\n", b"\n"))
            files.append(str(f))
        r = subprocess.run(["git", "merge-file", "-L", "Inkstone", "-L", "upstream before", "-L", "upstream after", *files], capture_output=True)
        if r.returncode < 0:
            sys.exit(f"git merge-file failed: {r.stderr.decode(errors='replace')}")
        return Path(files[0]).read_bytes(), r.returncode


def apply(base, new, root, dry):
    """Merge upstream's base..new into the working copy; returns per-outcome file lists."""
    report = {k: [] for k in ("updated", "merged", "added", "deleted", "conflict", "kept", "excluded", "not in fork", "unchanged")}
    for st, old, cur in changes(base, new, root):
        if excluded(cur) and excluded(old):
            report["excluded"].append(cur)
            continue
        local_old = root / PREFIX / map_path(old)
        local = root / PREFIX / map_path(cur)
        before = mapped(blob(base, old, root), old, root) if st != "A" else None
        after = mapped(blob(new, cur, root), cur, root) if st != "D" else None
        src = local_old if local_old.exists() else local
        ours = src.read_bytes() if src.exists() else None
        crlf = ours is not None and b"\r\n" in ours
        rel = str(local.relative_to(root)).replace("\\", "/")

        if st == "D":
            if ours is None:
                report["unchanged"].append(rel)
            elif ours.replace(b"\r\n", b"\n") == before:
                report["deleted"].append(rel)
                if not dry:
                    local.unlink()
            else:
                report["kept"].append(f"{rel} (deleted upstream, edited here)")
            continue

        if ours is None:
            if st == "A" or st == "C":
                report["added"].append(rel)
                write(local, after, False, dry)
            else:
                report["not in fork"].append(rel)
            continue

        norm = ours.replace(b"\r\n", b"\n")
        if norm == after:
            result, conflicts, kind = norm, 0, "unchanged"
        elif before is None:
            report["conflict"].append(f"{rel} (added upstream, already exists here)")
            continue
        elif norm == before:
            result, conflicts, kind = after, 0, "updated"
        elif as_text(ours) is None or as_text(before) is None or as_text(after) is None:
            report["conflict"].append(f"{rel} (binary, edited here and upstream)")
            continue
        else:
            result, conflicts = merge3(ours, before, after)
            kind = "conflict" if conflicts else "merged"

        report[kind].append(rel)
        if kind != "unchanged":
            write(local, result, crlf, dry)
        if st == "R" and local_old != local and local_old.exists() and not dry:
            local_old.unlink()
    return report


def print_report(report, dry):
    print("Dry run: nothing written.\n" if dry else "")
    for kind in ("updated", "merged", "added", "deleted", "conflict", "kept", "not in fork", "excluded"):
        files = report[kind]
        if files:
            print(f"{kind} ({len(files)}):")
            for f in files:
                print(f"  {f}")
    print(f"\n{len(report['unchanged'])} file(s) already matched upstream.")
    if report["conflict"]:
        print("\nResolve the <<<<<<< Inkstone / >>>>>>> upstream after markers above, then build and test.")
    if any(f.endswith("Cargo.lock") for k in ("merged", "conflict") for f in report[k]):
        print("Cargo.lock changed: run `cargo check --workspace` in Application/ to settle it.")


def commit_size(sha, root):
    stat = git_text("show", "--first-parent", "--shortstat", "--format=", sha, root=root)
    return stat.replace(" changed", "").replace("(+)", "").replace("(-)", "") or "no file changes"


def cmd_list(state, root, _args):
    picked = set(state.picked)
    log = git_text("log", "--first-parent", "--reverse", "--format=%H %s", f"{state.baseline}..{UPSTREAM}", root=root)
    if not log:
        print(f"Up to date with {UPSTREAM} (baseline {state.baseline[:10]}).")
        return
    for line in log.splitlines():
        sha, subject = line.split(" ", 1)
        mark = "picked " if any(p.startswith(sha[:10]) or sha.startswith(p) for p in picked) else "       "
        print(f"{mark}{sha[:10]}  {subject}\n                   {commit_size(sha, root)}")


def cmd_show(_state, root, args):
    sha = git_text("rev-parse", args.sha, root=root)
    print(git_text("log", "-1", "--format=%H%n%an, %ad%n%n%B", sha, root=root))
    for st, old, cur in changes(f"{sha}^1", sha, root):
        note = "  (excluded)" if excluded(cur) else ""
        print(f"  {st} {PREFIX}/{map_path(cur)}{note}")


def cmd_pick(state, root, args):
    total = {}
    for ref in args.sha:
        sha = git_text("rev-parse", ref, root=root)
        if git("merge-base", "--is-ancestor", sha, state.baseline, check=False, root=root).returncode == 0:
            print(f"{sha[:10]} is already part of the baseline; skipped.")
            continue
        print(f"== {sha[:10]} {git_text('log', '-1', '--format=%s', sha, root=root)}")
        report = apply(f"{sha}^1", sha, root, args.dry_run)
        for k, v in report.items():
            total.setdefault(k, []).extend(v)
        if not args.dry_run and sha not in state.picked:
            state.picked.append(sha)
    print_report(total, args.dry_run)
    if not args.dry_run:
        state.save()


def cmd_update(state, root, args):
    new = git_text("rev-parse", args.to, root=root)
    if git("merge-base", "--is-ancestor", state.baseline, new, check=False, root=root).returncode != 0:
        sys.exit(f"{args.to} does not contain the baseline {state.baseline[:10]}")
    print(f"Merging upstream {state.baseline[:10]}..{new[:10]}")
    report = apply(state.baseline, new, root, args.dry_run)
    print_report(report, args.dry_run)
    if not args.dry_run:
        state.baseline = new
        state.picked = [p for p in state.picked if git("merge-base", "--is-ancestor", p, new, check=False, root=root).returncode != 0]
        state.save()


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("list", help="upstream commits not yet integrated, with their size")
    p = sub.add_parser("show", help="what one upstream commit changes, as local paths")
    p.add_argument("sha")
    p = sub.add_parser("pick", help="apply individual upstream commits")
    p.add_argument("sha", nargs="+")
    p.add_argument("--dry-run", action="store_true")
    p = sub.add_parser("update", help="apply all upstream changes up to a revision")
    p.add_argument("--to", default=UPSTREAM)
    p.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    root = Path(git_text("rev-parse", "--show-toplevel", root=os.path.dirname(os.path.abspath(__file__))))
    if git("rev-parse", "--verify", "-q", UPSTREAM, check=False, root=root).returncode != 0:
        sys.exit("No upstream/main: run `git remote add upstream https://github.com/storytold/wordcraft` and `git fetch upstream`.")
    state = State(root / PREFIX / REV_FILE)
    {"list": cmd_list, "show": cmd_show, "pick": cmd_pick, "update": cmd_update}[args.cmd](state, root, args)


if __name__ == "__main__":
    main()
