"""Regenerate existing coverage groups from the pinned LSP model, preserving implementation notes."""
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parents[3]
model = json.loads(next((root / 'artifacts/lsp-metamodel').glob('*/metaModel.json')).read_text())
methods = {m['method']: m['messageDirection'] for kind in ('requests', 'notifications') for m in model[kind]}
p = root / 'docs/lsp-client.md'
text = p.read_text()
rows = {m: (mark, note) for mark, m, note in re.findall(r'^\| ([✅◐○]) \| `([^`]+)` \| [→←↔] \| (.*?) \|$', text, re.M)}
rows['textDocument/prepareRename'] = ('✅', 'Asks every claimant advertising prepareProvider before the prompt; range/placeholder seeds it, null/error refuses it (#3073)')
directions = {'clientToServer': '→', 'serverToClient': '←', 'both': '↔'}

def group(match):
    title, body = match.groups()
    names = re.findall(r'^\| [✅◐○] \| `([^`]+)`', body, re.M)
    if not names:
        return match.group()
    generated = '\n'.join(f'| {rows[m][0]} | `{m}` | {directions[methods[m]]} | {rows[m][1]} |' for m in sorted(names))
    count = sum(rows[m][0] != '○' for m in names)
    title = re.sub(r'— \d+ of \d+', f'— {count} of {len(names)}', title)
    body = re.sub(r'^\| [✅◐○] \|.*\n?', '', body, flags=re.M)
    body = body.replace('|---|---|---|---|\n', '|---|---|---|---|\n' + generated + '\n', 1)
    return title + body.rstrip() + "\n"

text = re.sub(r'(### .*?\n)(.*?)(?=\n### |\n## |\Z)', group, text, flags=re.S)
text = re.sub(r'implements \d+ of the 93', f'implements {sum(mark != "○" for mark, _ in rows.values())} of the 93', text)
p.write_text(text)
