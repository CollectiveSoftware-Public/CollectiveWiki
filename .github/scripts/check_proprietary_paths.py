#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Fail if any given file matches an active proprietary-path glob.

Usage: check_proprietary_paths.py <denylist-file> [file ...]
Files may be args or, if none, read from stdin (one per line).
Patterns are fnmatch globs matched against each repo-relative path; '*' also
matches '/', so 'ads/*' blocks everything under ads/ -- over-matching is the
safe (fail-closed) direction for a denylist. Blank lines and '#' comments are
ignored. Exit 1 (naming every offender) if any file matches; 0 if none or the
active denylist is empty; 2 on usage error."""
import fnmatch, sys

def load_patterns(path):
    out = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            s = line.strip()
            if s and not s.startswith("#"):
                out.append(s)
    return out

def main(argv):
    if len(argv) < 2:
        print("usage: check_proprietary_paths.py <denylist-file> [file ...]", file=sys.stderr)
        return 2
    patterns = load_patterns(argv[1])
    files = argv[2:] or [ln.strip() for ln in sys.stdin if ln.strip()]
    if not patterns:
        return 0
    offenders = [f for f in files if any(fnmatch.fnmatch(f, p) for p in patterns)]
    if offenders:
        print("PROPRIETARY-PATH GUARD FAILED: these files match a proprietary "
              "denylist pattern and must not exist in the public repo:", file=sys.stderr)
        for f in offenders:
            print(f"  {f}", file=sys.stderr)
        return 1
    return 0

if __name__ == "__main__":
    sys.exit(main(sys.argv))
