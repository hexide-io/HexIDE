#!/usr/bin/env python3
"""Repeat selected expensive endpoints; preserve timeouts separately from outcomes."""
import argparse, csv, os, pathlib, resource, subprocess, time
from concurrent.futures import ThreadPoolExecutor, as_completed
p = argparse.ArgumentParser()
p.add_argument('--stage', default='execute-guard')
p.add_argument('--shape', default='if')
p.add_argument('--depths', nargs='+', type=int, required=True)
p.add_argument('--stack-mib', type=float, default=8)
p.add_argument('--repeats', type=int, default=2)
p.add_argument('--jobs', type=int, default=4)
p.add_argument('--timeout', type=int, default=600)
p.add_argument('--output', default='round3-endpoints.csv')
a = p.parse_args()
here = pathlib.Path(__file__).resolve().parent
resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
env = dict(os.environ, DOTNET_TieredCompilation='0', GLIBC_TUNABLES='glibc.pthread.stack_cache_size=0')
env['PATH'] = ':'.join(x for x in env['PATH'].split(':') if not x.startswith('/mnt/'))
size = int(a.stack_mib*1024*1024)
def trial(depth, repeat):
    start = time.monotonic()
    try:
        r = subprocess.run(['dotnet', str(here/'bin/Debug/net10.0/StackProbe.dll'), a.stage, a.shape, str(depth), str(size)],
                           capture_output=True, text=True, env=env, timeout=a.timeout)
        text = f'exit={r.returncode}\n{r.stdout}\n{r.stderr[:4000]}'
        valid = 'READY\n' in r.stdout and f'bytes={size}\n' in r.stdout
        outcome = ('pass' if valid and r.returncode == 0 and r.stdout.rstrip().endswith('PASS') else
                   'error28' if valid and r.returncode == 28 and 'ERROR28:' in r.stdout else
                   'overflow' if valid and 'Stack overflow.' in r.stderr else
                   'depth-refusal' if valid and 'Procedure constant expression is too deeply nested' in r.stderr else 'other failure')
    except subprocess.TimeoutExpired as e:
        text = f'timeout\n{e.stdout!r}\n{e.stderr!r}'
        outcome = 'timeout'
    (here/'.scratch/trials'/f'endpoint-{a.stage}-{a.shape}-{a.stack_mib}-{depth}-{repeat}.txt').write_text(text)
    return [a.stage, a.shape, size, depth, repeat, outcome, round(time.monotonic()-start, 2)]
with (here/a.output).open('w', newline='') as output, ThreadPoolExecutor(a.jobs) as pool:
    writer = csv.writer(output, lineterminator='\n')
    writer.writerow(['stage','shape','stack_bytes','depth','repeat','outcome','seconds'])
    futures = [pool.submit(trial, d, r) for d in a.depths for r in range(a.repeats)]
    for future in as_completed(futures):
        row = future.result()
        writer.writerow(row)
        output.flush()
        print(row, flush=True)
