#!/usr/bin/env python3
"""Export verified per-trial observations without local paths or repeated CLR stack traces."""
import csv, pathlib, re
here = pathlib.Path(__file__).resolve().parent
rows = []
for path in (here / '.scratch/trials').glob('*.txt'):
    match = re.fullmatch(r'(.+)-(if|parens)-([\d.]+)-(\d+)\.txt', path.name)
    if not match: continue
    stage, shape, size, depth = match.groups()
    text = path.read_text()
    if f'bytes={int(float(size)*1024*1024)}\n' not in text: continue
    status = ('pass' if '\nPASS\n' in text else
              'overflow' if 'Stack overflow.' in text else
              'error28' if "Run-time error '28'" in text else 'other failure')
    rows.append([stage, shape, float(size), int(depth), status])
with (here / 'observations.csv').open('w', newline='') as out:
    writer = csv.writer(out, lineterminator="\n")
    writer.writerow(['stage','shape','stack_mib','depth','outcome'])
    writer.writerows(sorted(set(tuple(row) for row in rows)))

# Successful bisections and bounded endpoints, deduplicated across requested slices.
fields = ['stage','shape','stack_mib','pass_depth','overflow_depth','trials','seconds']
bounds = {}
# Never combine a previous host's committed measurements with a new partial run.
paths = sorted(p for p in here.glob('*.csv') if p.name != 'boundaries.csv')
for path in paths:
    with path.open(newline='') as source:
        reader = csv.DictReader(source)
        if reader.fieldnames != fields: continue
        for row in reader:
            size = float(row['stack_mib'])
            if row['overflow_depth'] != 'not reached' and int(row['overflow_depth']) != int(row['pass_depth'])+1:
                raise ValueError(f'Nonadjacent boundary: {row}')
            row['stack_mib'] = f'{size:g}'
            bounds[row['stage'],row['shape'],size] = row
if bounds:
    with (here / 'boundaries.csv').open('w', newline='') as out:
        writer = csv.DictWriter(out, fields, lineterminator='\n')
        writer.writeheader()
        writer.writerows(row for _,row in sorted(bounds.items()))
