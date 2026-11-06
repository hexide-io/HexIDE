"""Regenerate the LSP coverage rows from a locally cached protocol metaModel.

Run from the repository root: python3 scripts/probes/issue-3074-coverage.py <metaModel.json>
Existing implementation assessments and notes are retained, with #3074's two additions.
"""
import json
from pathlib import Path
import re
import sys

model = json.loads(Path(sys.argv[1]).read_text())
p = Path('docs/lsp-client.md')
text = p.read_text()
rows = {m[1]: (m[0], m[3]) for m in re.findall(
    r'^\| ([✅◐○]) \| `([^`]+)` \| ([→←↔]) \| (.*?) \|$', text, re.M)}
for name in ('implementation', 'typeDefinition'):
    rows['textDocument/' + name] = ('✅', 'Capability-gated navigation; all three location shapes, multiple results in Find References (#3074)')
messages = {m['method']: m for m in model['requests'] + model['notifications']}
if rows.keys() != messages.keys():
    raise ValueError('Coverage methods differ from the protocol model; add assessments for new messages first')
directions = {'clientToServer': '→', 'serverToClient': '←', 'both': '↔'}
def row(match):
    method = match[1]
    message = messages[method]
    mark, note = rows[method]
    return f'| {mark} | `{message["method"]}` | {directions[message["messageDirection"]]} | {note} |'
text = re.sub(r'^\| [✅◐○] \| `([^`]+)` \| [→←↔] \| .*? \|$', row, text, flags=re.M)
# Counts are derived from the regenerated rows, including each section heading.
text = re.sub(r'(### .*? — )\d+( of \d+\n)(.*?)(?=\n### |\n## |\Z)',
              lambda m: m[1] + str(len(re.findall(r'^\| [✅◐] \|', m[3], re.M))) + m[2] + m[3], text, flags=re.S)
count = sum(mark != '○' for mark, _ in rows.values())
text = re.sub(r'implements \d+ of the 93', f'implements {count} of the 93', text)
p.write_text(text)
