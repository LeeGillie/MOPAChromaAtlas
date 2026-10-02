#!/usr/bin/env python3
"""Fail when a file in the repository does not appear in MOPAChromaAtlas.slnx.

Visual Studio only shows files the solution lists (outside project folders), so a document
added without a solution entry is invisible there. Run from the repository root:

    python tools/check_solution.py

Checks every file git tracks, plus untracked files that are not ignored. Files inside a
project's folder belong to that project and need no entry. Also reports solution entries
whose file no longer exists. Standard library only.
"""
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def main() -> int:
    sln = open(os.path.join(ROOT, "MOPAChromaAtlas.slnx"), encoding="utf-8").read()
    listed = set(re.findall(r'<File Path="([^"]+)"', sln))
    projects = [os.path.dirname(p) + "/" for p in re.findall(r'<Project Path="([^"]+)"', sln)]
    files = subprocess.run(["git", "ls-files", "--cached", "--others", "--exclude-standard"],
                           cwd=ROOT, capture_output=True, text=True, check=True).stdout.splitlines()
    missing = sorted(f for f in files
                     if f not in listed and f != "MOPAChromaAtlas.slnx" and not any(f.startswith(p) for p in projects))
    gone = sorted(f for f in listed if not os.path.exists(os.path.join(ROOT, f)))
    for f in missing:
        print("not in the solution:", f)
    for f in gone:
        print("in the solution but missing:", f)
    if missing or gone:
        print("Add these to MOPAChromaAtlas.slnx (or remove the stale entries).")
        return 1
    print("Solution lists every file.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
