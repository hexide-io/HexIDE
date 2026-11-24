#!/usr/bin/env python3
"""Move the Basic provider's text without reformatting the IDE packs (#5395). Run once."""
import json
from pathlib import Path
import re

root = Path(__file__).resolve().parents[1]
source = root / 'IDE/HexIDE/Localization/Packs'
target = root / 'IDE/HexIDE.Runtime/Localization/Packs'


def belongs(key):
    return (key.startswith(('Str.PropDesc.', 'Str.Vb6Toolchain.', 'Str.MakeProject.', 'Str.ProjectType.'))
            or key.startswith('Str.ProjectLoad.') and key not in {
                'Str.ProjectLoad.Heading', 'Str.ProjectLoad.MoreWarnings', 'Str.ProjectLoad.ActiveXDocuments'}
            or key == 'Str.ProjectRunner.NotImplementedYet')


def finish(lines):
    # Removing the last member must also remove the preceding member's comma.
    for index in range(len(lines) - 1, -1, -1):
        if re.match(r'    "Str\.', lines[index]):
            lines[index] = lines[index].rstrip('\n').removesuffix(',') + '\n'
            break
    return ''.join(lines)


if target.exists():
    raise SystemExit('Provider pack area already exists; this migration runs only once.')
canonical = json.loads((source / 'en.json').read_text(encoding='utf-8'))['strings']
assert sum(map(belongs, canonical)) == 112
for file in sorted(source.glob('*.json')):
    if file.stem in {'zz', 'zz-ZZ'}:
        continue
    lines = file.read_text(encoding='utf-8').splitlines(keepends=True)
    moved, kept = [], []
    split = None
    for line in lines:
        match = re.match(r'    "(Str\.[^"]+)":', line)
        if match and belongs(match[1]):
            moved.append(line)
        else:
            kept.append(line)
        if match and match[1] == 'Str.Startup.WaylandUnavailableEnvironment':
            split = line.replace('Str.Startup.', 'Str.Standalone.', 1)
    if not moved and not split:
        continue
    target.mkdir(parents=True, exist_ok=True)
    header = lines[:next(i for i, line in enumerate(lines) if '"strings": {' in line) + 1]
    entries = [line.rstrip('\n').removesuffix(',') + ',\n' for line in moved + ([split] if split else [])]
    (target / file.name).write_text(finish(header + entries + ['  }\n', '}\n']), encoding='utf-8')
    file.write_text(finish(kept), encoding='utf-8')
