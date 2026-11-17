"""Writes #1119's compound-file fixtures with Windows' own structured storage (ole32), so the reader HexIDE writes from
[MS-CFB] is tested against files a writer it didn't write produced. Usage: py -3 make_cfb_fixtures.py <out-dir>"""
import gzip, hashlib, io, json, os, sys, zipfile

import pythoncom
from win32com import storagecon as sc

OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
MODE = sc.STGM_CREATE | sc.STGM_READWRITE | sc.STGM_SHARE_EXCLUSIVE
CHILD = sc.STGM_CREATE | sc.STGM_READWRITE | sc.STGM_SHARE_EXCLUSIVE
manifest = {}


def data(length, seed):
    return bytes((i * 7 + seed) % 251 for i in range(length))


def write_tree(storage, tree, path, record):
    for name, value in tree.items():
        if isinstance(value, dict):
            child = storage.CreateStorage(name, CHILD, 0, 0)
            write_tree(child, value, path + [name], record)
            child.Commit(0)
        else:
            stream = storage.CreateStream(name, CHILD, 0, 0)
            stream.Write(value)
            record["/".join(path + [name])] = {"length": len(value), "seed": None}


def make(file_name, tree, sector_size=512):
    full = os.path.join(OUT, file_name)
    if os.path.exists(full):
        os.remove(full)
    if sector_size == 512:
        root = pythoncom.StgCreateDocfile(full, MODE, 0)
    else:
        root = pythoncom.StgCreateStorageEx(full, MODE, sc.STGFMT_DOCFILE, 0, pythoncom.IID_IStorage,
                                            {"Version": 1, "reserved": 0, "SectorSize": sector_size,
                                             "TemplateFile": ""})
    record = {}
    write_tree(root, tree, [], record)
    root.Commit(0)
    del root
    return full, record


def mixed(seed_base):
    tree = {
        "Small": data(100, seed_base + 1),
        "Exactly4095": data(4095, seed_base + 2),
        "Exactly4096": data(4096, seed_base + 3),
        "Large": data(20000, seed_base + 4),
        "Empty": b"",
        "Nested": {
            "Inner": data(300, seed_base + 5),
            "Deeper": {"Leaf": data(5000, seed_base + 6)},
            "EmptyStorage": {},
        },
    }
    # Enough siblings that the directory's red-black tree has depth, with names that sort differently by length first.
    for i in range(24):
        tree[f"S{i:02d}" + "x" * (i % 5)] = data(37 * i + 1, seed_base + 10 + i)
    return tree


seeds = {}


def remember(file_name, tree, prefix=""):
    for name, value in tree.items():
        key = prefix + name
        if isinstance(value, dict):
            seeds.setdefault(file_name, {})[key + "/"] = "storage"
            remember(file_name, value, key + "/")
        else:
            seeds.setdefault(file_name, {})[key] = {"length": len(value), "sha256": hashlib.sha256(value).hexdigest()}


for name, tree, size in [("v3-mixed.cfb", mixed(0), 512), ("v4-mixed.cfb", mixed(0), 4096)]:
    make(name, tree, size)
    remember(name, tree)

# More than 109 FAT sectors, so the header's DIFAT overflows into a DIFAT sector. Kept gzipped: it is ~7.6 MB of pattern.
big = {"Big": data(7_600_000, 3), "After": data(700, 4)}
path, _ = make("v3-difat.cfb", big)
with open(path, "rb") as f, gzip.open(path + ".gz", "wb", compresslevel=9) as g:
    g.write(f.read())
os.remove(path)
remember("v3-difat.cfb.gz", big)

# Where Office keeps a VBA project, with placeholder bytes: real [MS-OVBA] content comes from real Office files later.
vba = {"PROJECT": b'ID="{00000000-0000-0000-0000-000000000000}"\r\nModule=Module1\r\n',
       "PROJECTwm": b"Module1\x00M\x00o\x00d\x00u\x00l\x00e\x001\x00\x00\x00\x00\x00",
       "VBA": {"dir": data(600, 21), "_VBA_PROJECT": data(7, 22), "Module1": data(900, 23)}}
xls = {"Workbook": data(3000, 30), "_VBA_PROJECT_CUR": vba}
doc = {"WordDocument": data(3000, 31), "Macros": vba, "1Table": data(500, 32)}
plain = {"WordDocument": data(3000, 33), "1Table": data(500, 34)}
for name, tree in [("xls-shaped.cfb", xls), ("doc-shaped.cfb", doc), ("no-vba.cfb", plain)]:
    make(name, tree)
    remember(name, tree)

bin_path, _ = make("vbaProject.bin", vba)
with open(bin_path, "rb") as f:
    vba_bin = f.read()
os.remove(bin_path)
content_types = ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\r\n'
                 '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
                 '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
                 '<Default Extension="xml" ContentType="application/xml"/>'
                 '<Override PartName="/xl/workbook.xml" ContentType="application/vnd.ms-excel.sheet.macroEnabled.main+xml"/>'
                 '<Override PartName="/xl/vbaProject.bin" ContentType="application/vnd.ms-office.vbaProject"/>'
                 '</Types>')
no_vba_types = content_types.replace(
    '<Override PartName="/xl/vbaProject.bin" ContentType="application/vnd.ms-office.vbaProject"/>', "")
for name, types, with_bin in [("xlsm-shaped.zip", content_types, True), ("xlsx-no-vba.zip", no_vba_types, False)]:
    with zipfile.ZipFile(os.path.join(OUT, name), "w", zipfile.ZIP_DEFLATED) as z:
        for entry, text in [("[Content_Types].xml", types), ("_rels/.rels", "<Relationships/>"),
                            ("xl/workbook.xml", "<workbook/>")]:
            info = zipfile.ZipInfo(entry, date_time=(2026, 11, 16, 0, 0, 0))
            z.writestr(info, text, zipfile.ZIP_DEFLATED)
        if with_bin:
            z.writestr(zipfile.ZipInfo("xl/vbaProject.bin", date_time=(2026, 11, 16, 0, 0, 0)), vba_bin,
                       zipfile.ZIP_DEFLATED)
    seeds[name] = {"xl/vbaProject.bin": {"length": len(vba_bin), "sha256": hashlib.sha256(vba_bin).hexdigest()}}         if with_bin else {}

with open(os.path.join(OUT, "manifest.json"), "w", encoding="utf-8", newline="\n") as f:
    json.dump(seeds, f, indent=2, sort_keys=True)
    f.write("\n")
print("written:", sorted(os.listdir(OUT)))
