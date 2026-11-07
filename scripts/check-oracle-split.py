#!/usr/bin/env python3
"""Verify #1216 preserves the oracle. Optionally pass a pre-split git revision."""
import collections
import pathlib
import re
import subprocess
import sys

root = pathlib.Path(__file__).resolve().parents[1]
revision = sys.argv[1] if len(sys.argv) > 1 else '50e8246c13654da5fe1df10c136911bd3538a42e'
old = subprocess.check_output(
    ['git', 'show', f'{revision}:docs/vb6-fidelity-oracle.md'], cwd=root).decode('utf-8-sig')
files = sorted((root / 'docs/vb6-fidelity-oracle').glob('*.md'))
assert files, 'split oracle directory is missing'

def rows(text):
    return collections.Counter(line for line in text.splitlines() if line.startswith('|'))

before = rows(old)
after = sum((rows(path.read_text(encoding='utf-8-sig')) for path in files), collections.Counter())
assert before == after, f'rows lost/changed: {before - after}; rows added: {after - before}'
print(f'PASS: {sum(before.values())} table rows preserved across {len(files)} files verbatim.')

def canonical(text):
    text = re.sub(r'\]\([^)]*\)', ']()', text)
    return re.sub(r'vb6-fidelity-oracle/[\w-]+\.md', 'vb6-fidelity-oracle.md', text)

# Also preserve prose, probes, and overturned findings, not only table rows.
sections = re.split(r'(?=^## )', old, flags=re.MULTILINE)
contents = [path.read_text(encoding='utf-8-sig') for path in files]
for section in sections:
    assert any(canonical(section) in canonical(content) for content in contents), (
        'source section changed or missing: ' + section.splitlines()[0])
print(f'PASS: all {len(sections)} source blocks preserved (only references retargeted).')
