#!/usr/bin/env python3
"""Regenerate docs/lsp-client.md tables from the pinned LSP model, preserving coverage notes.

Usage: python3 scripts/regenerate-lsp-coverage.py MODEL [--set METHOD MARK NOTES]
MODEL is the metaModel.json cached by ProtocolCoverageDocTests. MARK is ✅, ◐ or ○.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('model', type=Path)
parser.add_argument('--set', nargs=3, action='append', default=[], metavar=('METHOD', 'MARK', 'NOTES'))
args = parser.parse_args()
raw = args.model.read_bytes()
if hashlib.sha256(raw).hexdigest() != 'c207890d9e0f54d8f9e462a924670f563423ec9af90a0a369645ff55384c8d6d':
    parser.error('model must match LspSpecificationModel.cs\'s pinned LSP 3.17 digest')
model = json.loads(raw)
messages = {m['method']: m['messageDirection'] for kind in ('requests', 'notifications') for m in model[kind]}
doc = Path(__file__).resolve().parent.parent / 'docs/lsp-client.md'
text = doc.read_text(encoding='utf-8')
row = re.compile(r'^\| ([✅◐○]) \| `([^`]+)` \| [→←↔] \| (.*?) \|$', re.M)
coverage = {method: (mark, notes) for mark, method, notes in row.findall(text)}
for method, mark, notes in args.set:
    if method not in messages or mark not in ('✅', '◐', '○'):
        parser.error('unknown method or coverage mark')
    coverage[method] = (mark, notes)
directions = {'clientToServer': '→', 'serverToClient': '←', 'both': '↔'}
seen = set()

def section(match):
    group = match[1]
    methods = sorted(m for m in messages if (m.split('/')[0] + '/*' if '/' in m else 'Lifecycle') == group)
    seen.update(methods)
    count = sum(coverage.get(m, ('○', ''))[0] != '○' for m in methods)
    lines = [f'### `{group}` — {count} of {len(methods)}', '', '| | Method | Dir | Notes |', '|---|---|---|---|']
    for method in methods:
        mark, notes = coverage.get(method, ('○', ''))
        lines.append(f'| {mark} | `{method}` | {directions[messages[method]]} | {notes} |')
    return '\n'.join(lines) + '\n\n'

text = re.sub(r'### `([^`]+)` — \d+ of \d+\n\n\|.*?(?=### )', section, text, flags=re.S)
if seen != messages.keys():
    parser.error('document section groups do not account for every model message')
count = sum(coverage.get(m, ('○', ''))[0] != '○' for m in messages)
text = re.sub(r'implements \d+ of the \d+', f'implements {count} of the {len(messages)}', text)
doc.write_text(text, encoding='utf-8')
