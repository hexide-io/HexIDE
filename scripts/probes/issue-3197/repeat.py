#!/usr/bin/env python3
"""Run the temporary instrumented EditorRedockTests before/after, with/without CPU contention."""
from concurrent.futures import ThreadPoolExecutor
import json
import os
from pathlib import Path
import shutil
import subprocess
import threading
import sys

root = Path(__file__).resolve().parents[3]
output = root / "artifacts" / "issue-3197"
output.mkdir(parents=True, exist_ok=True)
binary = root / "IDE/HexIDE.Integration.Tests/bin/Debug/net10.0/HexIDE.Integration.Tests"
available_cpus = sorted(os.sched_getaffinity(0))
worker_count = min(4, len(available_cpus) // 2)
write_lock = threading.Lock()
environment = os.environ.copy()
environment["PATH"] = ":".join(p for p in environment["PATH"].split(":") if not p.startswith("/mnt/"))
environment.pop("REDOCK_STRETCH", None)
environment.pop("REDOCK_SINGLE_TURN", None)

def run(name, env, binary, cpus):
    affinity = ["taskset", "-c", ",".join(map(str, cpus))]
    timeline = binary.parent / "redock-timeline.log"
    timeline.unlink(missing_ok=True)
    with (output / (name + ".log")).open("w") as log:
        result = subprocess.run(affinity + [str(binary), "-class", "HexIDE.Integration.Tests.Views.EditorRedockTests",
                                 "-xml", str(output / (name + ".xml")), "-noLogo"],
                                cwd=binary.parent, env=env, stdout=log, stderr=subprocess.STDOUT)
    if timeline.exists():
        timeline.rename(output / (name + "-timeline.log"))
    with write_lock, (output / "results.jsonl").open("a") as log:
        log.write(json.dumps({"name": name, "exit": result.returncode, "cpus": cpus}) + "\n")
    print(name, result.returncode, flush=True)


def worker(worker_index, version, loaded):
    cpus = available_cpus[worker_index * 2:worker_index * 2 + 2]
    worker_binary = output / f"worker-{worker_index}" / binary.name
    if not worker_binary.exists():
        shutil.copytree(binary.parent, worker_binary.parent, dirs_exist_ok=True,
                        ignore=shutil.ignore_patterns("TestResults", "redock-timeline.log"))
    env = environment.copy()
    if version == "after":
        env["REDOCK_SINGLE_TURN"] = "1"
    affinity = ["taskset", "-c", ",".join(map(str, cpus))]
    workers = []
    try:
        if loaded:
            workers = [subprocess.Popen(affinity + [sys.executable, "-c", "while True: pass"]) for _ in cpus]
            if worker_index == 0:
                forced = env.copy()
                forced["REDOCK_STRETCH"] = "1"
                run(version + "-forced-loaded", forced, worker_binary, cpus)
        for iteration in range(worker_index + 1, 21, worker_count):
            run(f"{version}-{'loaded' if loaded else 'idle'}-{iteration:02}", env, worker_binary, cpus)
    finally:
        for process in workers:
            process.terminate()
        for process in workers:
            process.wait()

(output / "results.jsonl").unlink(missing_ok=True)
for version in ("before", "after"):
    for loaded in (False, True):
        with ThreadPoolExecutor(max_workers=worker_count) as executor:
            futures = [executor.submit(worker, i, version, loaded) for i in range(worker_count)]
            for future in futures:
                future.result()
