#!/usr/bin/env python3
"""Original directive guards: bisect a refusal, never mistake an overflow for one."""
import csv, os, pathlib, resource, subprocess
here = pathlib.Path(__file__).resolve().parent
resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
env = dict(os.environ, DOTNET_TieredCompilation='0', GLIBC_TUNABLES='glibc.pthread.stack_cache_size=0')
env['PATH'] = ':'.join(p for p in env['PATH'].split(':') if not p.startswith('/mnt/'))
dll = here / 'bin/Debug/net10.0/StackProbe.dll'
with (here / 'round3-conditional-limits.csv').open('w', newline='') as output:
    writer = csv.writer(output, lineterminator='\n')
    writer.writerow(['stage', 'shape', 'stack_bytes', 'last_recognized', 'first_refused', 'refusal'])
    for stage in ['cc-runtime', 'cc-lsp']:
        for shape in ['parens', 'const']:
            def trial(depth):
                r = subprocess.run(['dotnet', str(dll), stage, shape, str(depth), str(8*1024*1024)],
                                   capture_output=True, text=True, env=env, timeout=180)
                (here / '.scratch' / 'trials' / f'{stage}-{shape}-8-{depth}.txt').write_text(
                    f'exit={r.returncode}\n{r.stdout}\n{r.stderr[:4000]}')
                if 'READY' not in r.stdout or 'bytes=8388608\n' not in r.stdout or 'Stack overflow.' in r.stderr:
                    raise RuntimeError(f'Invalid trial: {r.stdout}\n{r.stderr[:1000]}')
                if r.returncode == 0 and r.stdout.rstrip().endswith('PASS'):
                    return True, ''
                if r.returncode == 29 or 'VBCompileErrorException' in r.stderr:
                    reason = r.stdout + r.stderr
                    if 'VBCompileErrorException: ' in reason:
                        reason = reason.split('VBCompileErrorException: ', 1)[1]
                    reason = reason.split('   at ', 1)[0].strip()
                    if 'DIRECTIVES_LEFT' in reason:
                        reason = reason[reason.index('DIRECTIVES_LEFT'):]
                    return False, reason
                raise RuntimeError(f'Unexpected outcome: {r.returncode}: {r.stdout}\n{r.stderr[:1000]}')
            lo, hi = 0, 512
            assert trial(lo)[0] and not trial(hi)[0]
            while hi-lo > 1:
                mid = (lo+hi)//2
                if trial(mid)[0]: lo = mid
                else: hi = mid
            assert trial(lo)[0]
            passed, reason = trial(hi)
            assert not passed
            writer.writerow([stage, shape, 8*1024*1024, lo, hi, reason])
            output.flush()
            print(stage, shape, lo, hi, flush=True)
