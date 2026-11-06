#!/usr/bin/env python3
"""Each trial is a fresh process. Only a verified stack-overflow is a failing bound."""
import argparse, csv, os, pathlib, resource, subprocess, time, sys
from concurrent.futures import ThreadPoolExecutor, as_completed
p = argparse.ArgumentParser()
p.add_argument('--stages', nargs='+', default=['syntax','runtime-parse-sll','runtime-parse-ll','lsp-parse-sll','lsp-parse-ll','prepass','declarations','scope','symbols','execute','debug','step'])
p.add_argument('--sizes', nargs='+', type=float, default=[0.0625,0.125,0.25])
p.add_argument('--output', default='results.csv')
p.add_argument('--jobs', type=int, default=6)
p.add_argument('--shapes', nargs='+', default=['if','parens'])
p.add_argument('--bounded-depth', type=int)
a = p.parse_args()
here = pathlib.Path(__file__).resolve().parent
resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
env = os.environ.copy()
env['PATH'] = ':'.join(x for x in env['PATH'].split(':') if not x.startswith('/mnt/'))
# Fixed JIT policy: no tier changes between depths or processes.
env['DOTNET_TieredCompilation'] = '0'
env['GLIBC_TUNABLES'] = 'glibc.pthread.stack_cache_size=0'
dll = here / 'bin/Debug/net10.0/StackProbe.dll'
def measure(stage, shape, size):
    trials = 0
    started = time.monotonic()
    def trial(depth):
        nonlocal trials
        trials += 1
        r = subprocess.run(['dotnet',str(dll),stage,shape,str(depth),str(int(size*1024*1024))], env=env, capture_output=True, text=True, timeout=180)
        logdir = here / '.scratch' / 'trials'
        logdir.mkdir(exist_ok=True)
        (logdir / f'{stage}-{shape}-{size}-{depth}.txt').write_text(f'exit={r.returncode}\n{r.stdout}\n{r.stderr[:4000]}')
        if 'READY' in r.stdout and f'bytes={int(size*1024*1024)}\n' not in r.stdout:
            raise RuntimeError(f'Unexpected actual stack size: {r.stdout}')
        if '-parse-' in stage and 'READY' in r.stdout:
            expected_mode = stage.rsplit('-', 1)[1].upper()
            if f'PREDICTION={expected_mode}\n' not in r.stdout:
                raise RuntimeError(f'Wrong prediction mode: {stage}: {r.stdout}')
        if r.returncode == 0 and r.stdout.strip().endswith('PASS'):
            return True
        if stage.endswith('-guard') and r.returncode == 28 and 'ERROR28:' in r.stdout:
            return False
        if not stage.endswith('-guard') and 'READY' in r.stdout and 'Stack overflow.' in r.stderr:
            return False
        raise RuntimeError(f'{stage} {shape} {size} {depth}: {r.returncode}\n{r.stdout}\n{r.stderr[:4000]}')
    if stage in ["folding", "inlay", "cc-runtime", "cc-lsp"] or (stage == "prepass" and shape == "parens"):
        target = a.bounded_depth or (14454 if shape == "if" else 15286)
        if not trial(target): raise RuntimeError("Expected bounded stage overflowed")
        return [stage,shape,size,target,"not reached",trials,round(time.monotonic()-started,2)]
    low, high = 0, 128
    if not trial(0):
        raise RuntimeError('Zero depth overflowed')
    while trial(high):
        low, high = high, high * 2
        if high > 131072:
            raise RuntimeError('No overflow before cap; report a bounded, nonrecursive stage separately')
    while high - low > 1:
        mid = (low + high) // 2
        if trial(mid): low = mid
        else: high = mid
    if not trial(low) or trial(high):
        raise RuntimeError(f'Unstable boundary: {stage} {shape} {size}: [{low}, {high}]')
    return [stage,shape,size,low,high,trials,round(time.monotonic()-started,2)]

failures = 0
with (here / a.output).open('w', newline='') as out, ThreadPoolExecutor(a.jobs) as pool:
    writer = csv.writer(out, lineterminator="\n")
    writer.writerow(['stage','shape','stack_mib','pass_depth','overflow_depth','trials','seconds'])
    out.flush()
    futures = [pool.submit(measure, stage, shape, size)
               for stage in a.stages for shape in a.shapes for size in a.sizes]
    for future in as_completed(futures):
        try:
            row = future.result()
        except Exception as error:
            failures += 1
            with (here / '.scratch' / (a.output + '.errors.txt')).open('a') as errors:
                errors.write(str(error) + '\n')
            print(str(error), file=sys.stderr, flush=True)
            continue
        writer.writerow(row)
        out.flush()
        print(','.join(map(str,row)), flush=True)

raise SystemExit(1 if failures else 0)
