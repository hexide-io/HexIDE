"""Retain interpreter MoveNext prologue reservations, without addresses or disassembly."""
from pathlib import Path
import re

root = Path(__file__).resolve().parents[3]
rows = []
for path in sorted((root / "artifacts/2953").glob("*.jit")):
    for section in path.read_text().split("; Assembly listing for method ")[1:]:
        name = re.match(r"HexIDE.Runtime.Interpreter\.(\w+)\+<(\w+)>d__\d+:MoveNext", section)
        if name is None:
            continue
        prologue = section.split("G_M000_IG02:")[0]
        size = re.search(r"sub\s+rsp, (0x[0-9A-Fa-f]+|\d+)", prologue)
        if size is None:
            size = re.search(r"lea\s+r11, \[rsp-(0x[0-9A-Fa-f]+)\]", prologue)
        if size:
            method = f"{name[1]}.{name[2]}"
            rows.append(f"{path.stem[:8]}\t{method}\t{int(size[1], 0)}")
(root / "scripts/probes/2953/frames.tsv").write_text("\n".join(rows) + "\n")
