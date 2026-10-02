#!/usr/bin/env python3
"""Print the GitHub release notes for one version: its CHANGELOG.md section, then the
evergreen packaging/RELEASE-FOOTER.md (which download, unsigned builds, checksums).

    python packaging/release_notes.py 0.1.0 > notes.md

Exits 1 if CHANGELOG.md has no section for that version. Standard library only.
"""
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def section(changelog: str, version: str) -> str | None:
    lines = changelog.replace("\r\n", "\n").split("\n")
    start = None
    for i, line in enumerate(lines):
        m = re.match(r"^##\s+v?(\d+(?:\.\d+)*)", line)
        if m and start is not None:
            return "\n".join(lines[start:i]).strip()
        if m and m.group(1) == version:
            start = i
    return "\n".join(lines[start:]).strip() if start is not None else None


def main() -> int:
    version = sys.argv[1].lstrip("vV")
    notes = section(open(os.path.join(ROOT, "CHANGELOG.md"), encoding="utf-8").read(), version)
    if notes is None:
        print(f"CHANGELOG.md has no section for {version}.", file=sys.stderr)
        return 1
    notes = re.sub(r"^##\s+", "## What's new in ", notes, count=1)
    footer = open(os.path.join(ROOT, "packaging", "RELEASE-FOOTER.md"), encoding="utf-8").read()
    print(notes + "\n\n" + footer.strip())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
