#!/usr/bin/env python3
"""Repeat the process-record class under contention, using an already built test executable."""
import os
from pathlib import Path
import subprocess

root = Path(__file__).resolve().parents[2]
logs = root / 'artifacts' / '4273'
logs.mkdir(parents=True, exist_ok=True)
cpus = sorted(os.sched_getaffinity(0))[:2]
os.sched_setaffinity(0, cpus)
workers = [subprocess.Popen(['python3', '-c', 'while True: pass']) for _ in cpus]
failed = 0
try:
    for run in range(1, 51):
        result = subprocess.run([
            str(root / 'IDE/HexIDE.Tests/bin/Debug/net10.0/HexIDE.Tests'),
            '-class', '*TheRecordSeesTheProcessTests',
            '-method-', '*A_stop_that_finishes_before*',
            '-method-', '*A_failed_wait_lists*',
        ], cwd=root / 'IDE', stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        (logs / f'stress-{run:02}.log').write_text(result.stdout)
        failed += result.returncode != 0
        print(f'run {run}/50: exit {result.returncode}', flush=True)
finally:
    for worker in workers:
        worker.terminate()
    for worker in workers:
        worker.wait()
print(f'{failed}/50 runs failed; affinity {cpus}', flush=True)
