#!/usr/bin/env python3
"""Fit stack bytes = overhead + bytes/level * first overflowing depth (ordinary least squares)."""
import csv, pathlib, statistics, collections
here = pathlib.Path(__file__).resolve().parent
groups = collections.defaultdict(dict)
for row in csv.DictReader((here / 'boundaries.csv').open()):
    groups[row['stage'], row['shape']][float(row['stack_mib'])] = row
print('| Stage | Shape | Stack KiB: last passing depth | Fitted bytes/level | 2x model at VB6 reference (MiB) |')
print('|---|---|---|---:|---:|')
for (stage, shape), cases in sorted(groups.items()):
    values = [row for _,row in sorted(cases.items())]
    depths = '; '.join(f"{float(r['stack_mib'])*1024:g}: {r['pass_depth']}" for r in values)
    if any(r['overflow_depth'] == 'not reached' for r in values):
        target = 7227 if shape == 'if' else 7643
        note = '≤0.125; 64 KiB also passed 2x depth' if min(int(r['pass_depth']) for r in values) >= 2*target else 'not established at VB6 reference depth'
        print(f'| {stage} | {shape} | {depths} (bounded) | no depth-dependent call stack in this shape | {note} |')
        continue
    if len(values) < 3:
        print(f'| {stage} | {shape} | {depths} (incomplete) | — | — |')
        continue
    x = [float(r['overflow_depth']) for r in values]
    y = [float(r['stack_mib'])*1024*1024 for r in values]
    xm, ym = statistics.mean(x), statistics.mean(y)
    slope = sum((a-xm)*(b-ym) for a,b in zip(x,y)) / sum((a-xm)**2 for a in x)
    intercept = ym - slope*xm
    target = 7227 if shape == 'if' else 7643
    need = 2*(intercept+slope*target)/1024/1024
    print(f'| {stage} | {shape} | {depths} | {slope:.0f} | {need:.2f} |')
