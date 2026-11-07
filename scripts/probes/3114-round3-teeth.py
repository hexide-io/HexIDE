"""Replay round 3's tests against the preceding round's four product files, then restore."""
import os
from pathlib import Path
import subprocess

root = Path(__file__).resolve().parents[2]
paths = [root / path for path in (
    "IDE/HexIDE/Tools/ResultsToolHeight.cs",
    "IDE/HexIDE/Tools/ToolPaneHeight.cs",
    "IDE/HexIDE/MainView.axaml.cs",
    "IDE/HexIDE/MainViewViewModel.cs",
)]
originals = {p: p.read_bytes() if p.exists() else None for p in paths}
env = os.environ.copy()
env["PATH"] = ":".join(p for p in env["PATH"].split(":") if not p.startswith("/mnt/"))
env["HEXIDE_FOREIGN_LSP_DOWNLOAD"] = "0"
try:
    for path in paths:
        path.write_bytes(subprocess.check_output([
            "git", "show", "fe3331c8:" + path.relative_to(root).as_posix()], cwd=root))
    result = subprocess.run([
        "pwsh", "scripts/test.ps1", "IDE/HexIDE.Integration.Tests", "--no-restore", "--",
        "--filter-class", "*FindReferencesHeightTests", "--filter-class", "*ResultsLayoutRenderTests",
    ], cwd=root, env=env, check=False)
finally:
    for path, content in originals.items():
        if content is None:
            path.unlink(missing_ok=True)
        else:
            path.write_bytes(content)
raise SystemExit(result.returncode)
