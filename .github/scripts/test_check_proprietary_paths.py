#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Self-test for check_proprietary_paths.py. Plain script: prints FAIL lines and
exits non-zero if any expectation is unmet; asserts on MESSAGE TEXT, not just codes."""
import os, subprocess, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
CHECKER = os.path.join(HERE, "check_proprietary_paths.py")
fails = []

def run(denylist_lines, files):
    with tempfile.NamedTemporaryFile("w", suffix=".paths", delete=False) as f:
        f.write("\n".join(denylist_lines))
        path = f.name
    try:
        p = subprocess.run([sys.executable, CHECKER, path, *files],
                           capture_output=True, text=True)
        return p.returncode, p.stdout + p.stderr
    finally:
        os.unlink(path)

# 1) a file matching an active pattern -> exit 1 AND the offender is named
code, out = run(["src/Wiki.Ads/**"], ["src/Wiki.Ads/AdBanner.cs", "README.md"])
if code != 1:            fails.append(f"match: expected exit 1, got {code}")
if "src/Wiki.Ads/AdBanner.cs" not in out: fails.append("match: offender not named in output")
if "GUARD FAILED" not in out: fails.append("match: missing GUARD FAILED text")

# 2) no file matches -> exit 0
code, out = run(["src/Wiki.Ads/**"], ["README.md", "src/Wiki.Core/Note.cs"])
if code != 0:            fails.append(f"clean: expected exit 0, got {code}")

# 3) empty denylist (comments/blank only) -> exit 0 even for a would-be match
code, out = run(["# nothing proprietary yet", ""], ["src/Wiki.Ads/AdBanner.cs"])
if code != 0:            fails.append(f"empty-denylist: expected exit 0, got {code}")

if fails:
    print("SELF-TEST FAILED:")
    for m in fails: print("  -", m)
    sys.exit(1)
print("proprietary-path checker self-test: PASS")
