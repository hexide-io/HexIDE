#!/usr/bin/env python3
"""Round 3 models keep safe Error 28 boundaries distinct from fatal overflows."""
import collections, csv, pathlib, re, statistics
here = pathlib.Path(__file__).resolve().parent
groups = collections.defaultdict(list)
files = ['execution', 'conditional-parse', 'conditional-eval', 'bounds', 'bounds-procedure']
for name in files:
    for row in csv.DictReader((here/f'round3-{name}.csv').open()):
        groups[row['stage'],row['shape']].append(row)
# The expensive 8 MiB If boundary was measured with repeated selected endpoints.
endpoint_rows = list(csv.DictReader((here/'round3-endpoints.csv').open()))
passes = [r for r in endpoint_rows if r['outcome']=='pass']
refusals = [r for r in endpoint_rows if r['outcome']=='error28']
if len(passes)==2 and len(refusals)==2:
    assert len({r['depth'] for r in passes})==1 and len({r['depth'] for r in refusals})==1
    assert int(refusals[0]['depth'])==int(passes[0]['depth'])+1
    groups['execute-guard','if'].append(dict(stack_mib=8, pass_depth=passes[0]['depth'], overflow_depth=refusals[0]['depth']))
print('| Stage | Shape | Stack KiB: last passing depth | Boundary | Bytes/level | 2x model at reference (MiB) |')
print('|---|---|---|---|---:|---:|')
models = []
for (stage, shape), rows in sorted(groups.items()):
    rows.sort(key=lambda r: float(r['stack_mib']))
    depths = '; '.join(f"{float(r['stack_mib'])*1024:g}: {r['pass_depth']}" for r in rows)
    outcome = 'Error 28' if stage.endswith('-guard') else 'CLR overflow'
    if len(rows) < 3:
        print(f'| {stage} | {shape} | {depths} | {outcome} | incomplete | — |')
        continue
    x = [float(r['overflow_depth']) for r in rows]
    y = [float(r['stack_mib'])*1048576 for r in rows]
    xm, ym = statistics.mean(x), statistics.mean(y)
    slope = sum((a-xm)*(b-ym) for a,b in zip(x,y))/sum((a-xm)**2 for a in x)
    intercept = ym-slope*xm
    reference = 7227 if shape == 'if' else 7643
    need = 2*(intercept+slope*reference)/1048576
    models.append([stage,shape,outcome,len(rows),f'{slope:.4f}',f'{intercept:.4f}',reference,f'{need:.4f}'])
    print(f'| {stage} | {shape} | {depths} | {outcome} | {slope:.0f} | {need:.2f} |')
with (here/'round3-models.csv').open('w', newline='') as f:
    w=csv.writer(f, lineterminator='\n')
    w.writerow(['stage','shape','boundary','sizes','bytes_per_level','intercept','reference_depth','twice_model_mib'])
    w.writerows(models)

# Preserve new stage observations without changing the round 1/2 dataset.
observations = []
for path in (here/'.scratch/trials').glob('*.txt'):
    match = re.fullmatch(r'(cc-.+|bounds-.+|execute-guard)-(if|parens|const)-([\d.]+)-(\d+)\.txt', path.name)
    if not match: continue
    stage, shape, size, depth = match.groups()
    text=path.read_text()
    if f'bytes={int(float(size)*1048576)}\n' not in text: continue
    outcome = ('pass' if '\nPASS\n' in text else 'error28' if 'ERROR28:' in text else
               'overflow' if 'Stack overflow.' in text else 'directive-refusal' if
               ('VBCompileErrorException' in text or 'DIRECTIVES_LEFT=True' in text) else 'other failure')
    observations.append((stage,shape,float(size),int(depth),outcome))
with (here/'round3-observations.csv').open('w', newline='') as f:
    w=csv.writer(f,lineterminator='\n');w.writerow(['stage','shape','stack_mib','depth','outcome'])
    w.writerows(sorted(set(observations)))
