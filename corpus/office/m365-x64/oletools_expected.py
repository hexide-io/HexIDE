"""Records what oletools 0.60.2 (BSD-2-Clause, used here as an independent reader, never shipped) reads from each real
Office file's VBA project, as the expectation #4330's reader is tested against. Usage: py -3 oracle_ovba.py <dir> <out.json>"""
import hashlib, io, json, os, sys, zipfile
import olefile
from oletools import olevba

src, out = sys.argv[1], sys.argv[2]
result = {}
for name in sorted(os.listdir(src)):
    if not name.endswith((".xlsm", ".xlsb", ".xls", ".docm", ".doc", ".pptm")):
        continue
    data = open(os.path.join(src, name), "rb").read()
    if data[:2] == b"PK":
        z = zipfile.ZipFile(io.BytesIO(data))
        part = next(n for n in z.namelist() if n.lower().endswith("vbaproject.bin"))
        ole = olefile.OleFileIO(io.BytesIO(z.read(part)))
        root = ""
    else:
        ole = olefile.OleFileIO(io.BytesIO(data))
        root = "_VBA_PROJECT_CUR/" if ole.exists("_VBA_PROJECT_CUR") else "Macros/"
    project = olevba.VBA_Project(ole, root, root + "PROJECT", root + "VBA/dir", relaxed=False)
    project.parse_project_stream()
    modules = []
    list(project.parse_modules())
    for module in project.modules:
        raw = module.code_raw if isinstance(module.code_raw, (bytes, bytearray)) else bytes(module.code_raw or b"")
        def uni(v):
            return v.decode("utf-16-le") if isinstance(v, (bytes, bytearray)) and v else (v or None)
        modules.append({
            "nameUnicode": uni(getattr(module, "_name_unicode", None)),
            "streamNameUnicode": uni(getattr(module, "_streamname_unicode", None)),
            "moduleType": module.type,
            "textOffset": module.textoffset,
            "sourceBytes": len(raw),
            "sourceSha256": hashlib.sha256(raw).hexdigest(),
        })
    def text(v):
        return v.decode(project.codec, errors="replace") if isinstance(v, (bytes, bytearray)) else v
    result[name] = {
        "projectName": text(project.projectname),
        "declaredCodePage": project.codepage,
        "sysKind": project.syskind,
        "modules": modules,
    }
    ole.close()
with open(out, "w", encoding="utf-8", newline="\n") as f:
    json.dump(result, f, indent=2, ensure_ascii=False, sort_keys=True)
    f.write("\n")
print({k: (v["projectName"], v["declaredCodePage"], v["sysKind"], len(v["modules"])) for k, v in result.items()})
