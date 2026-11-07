#!/usr/bin/env python3
"""Keep class outcomes and representative timelines outside disposable artifacts."""
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[3]
source = root / 'artifacts/issue-3205'
target = Path(__file__).resolve().parent
rows = ['run\ttotal\tfailed\tskipped\tsuspension_failures\tother_failures']
samples = set()
for path in sorted(source.glob('*.xml')):
    tests = list(ET.parse(path).getroot().iter('test'))
    failures = [t for t in tests if t.get('result') == 'Fail']
    suspension = [t for t in failures if 'a suspended document' in ''.join(t.itertext())]
    rows.append('\t'.join(map(str, [path.stem, len(tests), len(failures),
                sum(t.get('result') == 'Skip' for t in tests), len(suspension), len(failures)-len(suspension)])))
    condition = path.stem.rsplit('-', 1)[0]
    category = 'suspension' if suspension else 'other' if failures else 'pass'
    key = (condition, category)
    timeline = source / (path.stem + '-timeline.log')
    if key not in samples and timeline.exists():
        lines = timeline.read_text().replace(str(root), '<repo>').splitlines()
        # Keep the timing and the caller identifying each resume, rather than whole dispatcher stacks.
        excerpt = []
        stack_lines = 0
        for line in lines:
            if line.startswith('   at '):
                stack_lines += 1
                if stack_lines <= 4:
                    excerpt.append(line)
            else:
                stack_lines = 0
                excerpt.append(line)
        (target / (condition + '-' + category + '-timeline.txt')).write_text('\n'.join(excerpt) + '\n')
        samples.add(key)
(target / 'results.tsv').write_text('\n'.join(rows) + '\n')
for condition in sorted({row.split('\t')[0].rsplit('-', 1)[0] for row in rows[1:]}):
    selected = [row.split('\t') for row in rows[1:] if row.split('\t')[0].rsplit('-', 1)[0] == condition]
    print(condition, 'runs=', len(selected), 'suspension runs=', sum(int(r[4]) > 0 for r in selected),
          'suspension cases=', sum(int(r[4]) for r in selected), 'other cases=', sum(int(r[5]) for r in selected))
