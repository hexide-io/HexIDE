#!/usr/bin/env python3
"""Check licence declarations in tracked files, without treating prose mentions as notices."""
import pathlib
import re
import subprocess
import sys

PERMITTED = {"MIT", "Apache-2.0", "BSD-2-Clause", "BSD-3-Clause", "ISC", "0BSD", "Unlicense", "MS-PL"}
ROOT = pathlib.Path(__file__).resolve().parent.parent
# Exceptions must name a file in the notices, rather than exempting a whole directory.
notices = ROOT / "THIRD-PARTY-NOTICES.md"
credited = set(re.findall(r"`([^`\n]+)`", notices.read_text(encoding="utf-8"))) if notices.exists() else set()
# Licence texts this tree carries for another repository and never applies to its own files: the oracle mirror
# publishes .github/oracle-mirror/ as hexide-io/vb6-behaviour, whose prose is CC BY 4.0 (see its README).
carried = {".github/oracle-mirror/LICENSE"}
paths = subprocess.check_output(["git", "ls-files", "-z"], cwd=ROOT).decode().split("\0")
failed = False
for name in paths:
    path = ROOT / name
    if not name or not path.is_file() or name in credited or name in carried:
        continue
    data = path.read_bytes()
    if b"\0" in data:
        continue
    for number, line in enumerate(data.decode("utf-8", errors="replace").splitlines(), 1):
        text = re.sub(r"^\s*(?:(?://|/\*+|\*|#|<!--)\s*)?", "", line).strip()
        spdx = re.match(r"SPDX-License-Identifier:\s*(.*?)(?:\s*\*/|\s*-->)?$", text, re.I)
        notice = re.match(
            r"(?:This (?:Source Code Form|file|software|work|code) (?:is |has been )?(?:subject to|licensed under|distributed under)|"
            r"(?:Licensed|Distributed) under (?:the )?|(?:GNU (?:AFFERO |LESSER )?GENERAL PUBLIC LICENSE|"
            r"Mozilla Public License|SIL OPEN FONT LICENSE|Creative Commons|Eclipse Public License|"
            r"Microsoft (?:Reciprocal|Research|Reference Source) License|Server Side Public License|"
            r"Business Source License))", text, re.I)
        if not spdx and not notice:
            continue
        allowed = spdx and spdx.group(1).strip() in PERMITTED
        if not spdx:
            allowed = bool(re.search(r"\b(?:MIT|Apache(?: License)?(?:,? Version)? 2\.0|BSD[ -][23][ -]Clause|ISC|0BSD|Unlicense|MS-PL)\b", text, re.I))
        if not allowed:
            print(f"  licence notice is not permitted: {name}:{number}: {text}")
            failed = True
sys.exit(1 if failed else 0)
