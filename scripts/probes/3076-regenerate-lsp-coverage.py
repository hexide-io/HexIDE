#!/usr/bin/env python3
"""Regenerate the LSP coverage rows/directions/counts from the pinned model.

Usage: python3 scripts/probes/3076-regenerate-lsp-coverage.py path/to/metaModel.json
Existing marks and notes describe implementation; #3076 adds selection-range editor commands.
"""
import hashlib
import json
from pathlib import Path
import re
import sys

raw = Path(sys.argv[1]).read_bytes()
assert hashlib.sha256(raw).hexdigest() == 'c207890d9e0f54d8f9e462a924670f563423ec9af90a0a369645ff55384c8d6d'
model = json.loads(raw)
messages = {item['method']: item for kind in ('requests', 'notifications') for item in model[kind]}
path = Path(__file__).resolve().parents[2] / 'docs/lsp-client.md'
text = path.read_text()
directions = {'clientToServer': '→', 'serverToClient': '←', 'both': '↔'}
seen = set()
def row(match):
    mark, method, notes = match.groups()
    seen.add(method)
    if method == 'textDocument/selectionRange':
        mark = '✅'
        notes = ' Expand/Shrink Selection commands and server parent chains (#3076) '
    return f'| {mark} | `{method}` | {directions[messages[method]["messageDirection"]]} |{notes}|'
text = re.sub(r'^\| ([✅◐○]) \| `([^`]+)` \| [→←↔] \|(.*)\|$', row, text, flags=re.M)
assert seen == messages.keys()
def counts(match):
    heading, rows = match.groups()
    marks = re.findall(r'^\| ([✅◐○]) \|', rows, re.M)
    return re.sub(r'\d+ of \d+$', f'{sum(mark != "○" for mark in marks)} of {len(marks)}', heading) + rows
text = re.sub(r'(### `[^`]+` — \d+ of \d+)(.*?)(?=\n### |\Z)', counts, text, flags=re.S)
implemented = len(re.findall(r'^\| [✅◐] \|', text, re.M))
text = re.sub(r'HexIDE implements \d+ of the 93', f'HexIDE implements {implemented} of the 93', text)
path.write_text(text)
