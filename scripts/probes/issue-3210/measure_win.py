#!/usr/bin/env python3
"""Windows port of scripts/probes/issue-3210/measure.py and endpoints.py (#3210).

Same bisection and the same pass/fail rules: each trial is a fresh process, the thread's reported stack
must equal the requested size, a -guard stage fails only on a verified Error 28 and any other stage only on
a verified CLR stack overflow. Differences from the Linux driver: no RLIMIT_CORE (the runner sets
SEM_NOGPFAULTERRORBOX, inherited by every child, so a crash never raises a dialog), no PATH or glibc tuning,
and an overflow is recognised by .NET's "Stack overflow." on stderr or by exit code 0xC00000FD.
"""
import argparse, csv, ctypes, os, pathlib, subprocess, sys, time
from concurrent.futures import ThreadPoolExecutor, as_completed

p = argparse.ArgumentParser()
p.add_argument('--probe', default=pathlib.Path(__file__).resolve().parent, help='the probe directory; defaults to this one')
p.add_argument('--stages', nargs='+', required=True)
p.add_argument('--shapes', nargs='+', default=['if', 'parens'])
p.add_argument('--sizes', nargs='+', type=float, default=[1, 1.5, 4])
p.add_argument('--depths', nargs='+', type=int, help='endpoint mode: run exactly these depths')
p.add_argument('--repeats', type=int, default=1)
p.add_argument('--jobs', type=int, default=4)
p.add_argument('--timeout', type=int, default=900)
p.add_argument('--output', required=True)
a = p.parse_args()

ctypes.windll.kernel32.SetErrorMode(0x0001 | 0x0002)  # SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX
probe = pathlib.Path(a.probe).resolve()
dll = probe / 'bin/Debug/net10.0/StackProbe.dll'
logdir = probe / '.scratch' / 'trials'  # where measure.py keeps them, so they stay out of the tree
logdir.mkdir(parents=True, exist_ok=True)
env = dict(os.environ, DOTNET_TieredCompilation='0', DOTNET_DbgEnableMiniDump='0')
STACK_OVERFLOW = 0xC00000FD
ACCESS_VIOLATION = 0xC0000005


def run(stage, shape, size, depth, tag=''):
    bytes_ = int(size * 1024 * 1024)
    start = time.monotonic()
    try:
        r = subprocess.run(['dotnet', str(dll), stage, shape, str(depth), str(bytes_)],
                           env=env, capture_output=True, text=True, timeout=a.timeout)
    except subprocess.TimeoutExpired:
        return 'timeout', round(time.monotonic() - start, 2)
    seconds = round(time.monotonic() - start, 2)
    (logdir / f'{stage}-{shape}-{size}-{depth}{tag}.txt').write_text(
        f'exit={r.returncode}\n{r.stdout}\n{r.stderr[:4000]}', encoding='utf-8')
    ready = 'READY' in r.stdout
    if ready and f'bytes={bytes_}\n' not in r.stdout:
        raise RuntimeError(f'Unexpected actual stack size: {r.stdout}')
    if '-parse-' in stage and ready:
        mode = stage.rsplit('-', 1)[1].upper()
        if f'PREDICTION={mode}\n' not in r.stdout:
            raise RuntimeError(f'Wrong prediction mode: {stage}: {r.stdout}')
    if r.returncode == 0 and r.stdout.strip().endswith('PASS'):
        return 'pass', seconds
    if stage.endswith('-guard') and r.returncode == 28 and 'ERROR28:' in r.stdout:
        return 'error28', seconds
    code = r.returncode & 0xFFFFFFFF
    if ready and ('Stack overflow.' in r.stderr or code == STACK_OVERFLOW):
        return 'overflow', seconds
    # Measured: on these threads the CLR often dies of an access violation, with no message, at depths that
    # pass on a larger stack. Counted as stack exhaustion only because bisection proves it monotonic in depth.
    if ready and code == ACCESS_VIOLATION:
        return 'overflow-av', seconds
    raise RuntimeError(f'{stage} {shape} {size} {depth}: exit {r.returncode}\n{r.stdout}\n{r.stderr[:2000]}')


def failing(outcome, stage):
    return outcome == 'error28' if stage.endswith('-guard') else outcome in ('overflow', 'overflow-av')


def bisect(stage, shape, size):
    trials, started, kinds = 0, time.monotonic(), {}

    def ok(depth):
        nonlocal trials
        trials += 1
        outcome, _ = run(stage, shape, size, depth)
        kinds[depth] = outcome
        if outcome == 'pass':
            return True
        if failing(outcome, stage):
            return False
        raise RuntimeError(f'{stage} {shape} {size} {depth}: {outcome} is not a bound')

    low, high = 0, 128
    if not ok(0):
        raise RuntimeError('Zero depth failed')
    while ok(high):
        low, high = high, high * 2
        if high > 131072:
            raise RuntimeError('No failure before cap')
    while high - low > 1:
        mid = (low + high) // 2
        if ok(mid): low = mid
        else: high = mid
    if not ok(low) or ok(high):
        raise RuntimeError(f'Unstable boundary: {stage} {shape} {size}: [{low}, {high}]')
    return [stage, shape, size, low, high, kinds[high], trials, round(time.monotonic() - started, 2)]


with open(a.output, 'w', newline='') as out, ThreadPoolExecutor(a.jobs) as pool:
    w = csv.writer(out, lineterminator='\n')
    if a.depths:
        w.writerow(['stage', 'shape', 'stack_mib', 'depth', 'repeat', 'outcome', 'seconds'])
        futures = {pool.submit(run, s, sh, z, d, f'-r{i}'): (s, sh, z, d, i)
                   for s in a.stages for sh in a.shapes for z in a.sizes for d in a.depths for i in range(a.repeats)}
        for f in as_completed(futures):
            s, sh, z, d, i = futures[f]
            try: outcome, seconds = f.result()
            except Exception as e: outcome, seconds = 'error: ' + str(e).splitlines()[0], ''
            w.writerow([s, sh, z, d, i, outcome, seconds]); out.flush()
            print(s, sh, z, d, i, outcome, seconds, flush=True)
    else:
        w.writerow(['stage', 'shape', 'stack_mib', 'pass_depth', 'overflow_depth', 'failure', 'trials', 'seconds'])
        futures = [pool.submit(bisect, s, sh, z) for s in a.stages for sh in a.shapes for z in a.sizes]
        failures = 0
        for f in as_completed(futures):
            try:
                row = f.result()
            except Exception as e:
                failures += 1
                print('FAILED: ' + str(e), file=sys.stderr, flush=True)
                continue
            w.writerow(row); out.flush()
            print(','.join(map(str, row)), flush=True)
        sys.exit(1 if failures else 0)
