"""Measure revisions sequentially in this checkout; restore its starting branch on exit."""
import os
from pathlib import Path
import re
import subprocess
import sys
import ast

root = Path(__file__).resolve().parents[3]
os.chdir(root)
env = dict(os.environ)
env["PATH"] = ":".join(p for p in env["PATH"].split(":") if not p.startswith("/mnt/"))
logs = root / "artifacts/2953"
logs.mkdir(parents=True, exist_ok=True)
(logs / "runtime").mkdir(exist_ok=True)
env["XDG_RUNTIME_DIR"] = str(logs / "runtime")
env["NUGET_PACKAGES"] = str(logs / "packages")
env["NUGET_FALLBACK_PACKAGES"] = str(Path.home() / ".nuget/packages")
env["NUGET_HTTP_CACHE_PATH"] = str(logs / "http-cache")

def git(*args):
    return subprocess.check_output(["git", *args], text=True).strip()

starting = git("symbolic-ref", "--short", "HEAD")

def measure(revision):
    sha = git("rev-parse", revision)
    subprocess.run(["git", "checkout", "--detach", sha], check=True)
    build = subprocess.run(
        ["dotnet", "build", "scripts/probes/2953/StackProbe.csproj", "-c", "Debug", "--nologo"],
        env=env, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=600)
    (logs / f"{sha}.build.log").write_text(build.stdout)
    if build.returncode:
        print(build.stdout[-5000:], flush=True)
        raise RuntimeError(f"Build failed at {sha}")
    run_env = dict(env)
    run_env["DOTNET_JitDisasm"] = "*MoveNext*"
    run_env["DOTNET_JitStdOutFile"] = str(logs / f"{sha}.jit")
    Path(run_env["DOTNET_JitStdOutFile"]).unlink(missing_ok=True)
    result = subprocess.run(
        ["dotnet", "scripts/probes/2953/bin/Debug/net10.0/StackProbe.dll"],
        env=run_env, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=120)
    (logs / f"{sha}.log").write_text(result.stdout)
    depths = re.findall(r"^DEPTH (\d+) first-failure (\d+)$", result.stdout, re.M)
    row = f"{sha}\t{git('show', '-s', '--format=%cs', sha)}\t{depths}\t{result.returncode}"
    with (root / "scripts/probes/2953/measurements.tsv").open("a") as output:
        output.write(row + "\n")
    print(row, flush=True)
    if result.returncode or len(depths) != 2:
        print(result.stdout[-5000:], flush=True)
        raise RuntimeError(f"Probe failed at {sha}")
    return min(int(pair[0]) for pair in depths)

try:
    if sys.argv[1] == "--steps":
        # Use existing broad samples, then split only intervals with a material net drop.
        cache = {}
        for line in (root / "scripts/probes/2953/measurements.tsv").read_text().splitlines():
            sha, date, depths, status = line.split("\t")
            if status == "0":
                cache[sha] = min(int(pair[0]) for pair in ast.literal_eval(depths))
        history = git("rev-list", "--first-parent", "--reverse", f"{sys.argv[2]}^..{sys.argv[3]}").splitlines()
        def depth(sha):
            if sha not in cache:
                cache[sha] = measure(sha)
            return cache[sha]
        def split(a, b):
            drop = depth(history[a]) - depth(history[b])
            if drop <= 3:
                return
            if b == a + 1:
                print(f"STEP {history[a]} {history[b]} drop {drop}", flush=True)
                return
            middle = (a + b) // 2
            depth(history[middle])
            split(a, middle)
            split(middle, b)
        samples = sorted({0, len(history)-1, *(i for i, sha in enumerate(history) if sha in cache)})
        intervals = list(zip(samples, samples[1:]))
        intervals.sort(key=lambda pair: depth(history[pair[0]]) - depth(history[pair[1]]), reverse=True)
        for a, b in intervals:
            split(a, b)
    else:
        for revision in sys.argv[1:]:
            measure(revision)
finally:
    subprocess.run(["git", "checkout", starting], check=True)
