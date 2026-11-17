"""Scan Office files for identifying strings, printing counts only, never the strings.

Needles come from the environment: CORPUS_NEEDLES (literal strings separated by U+001F) and
HEXIDE_IDENTITY_PATTERN (a regex). Each file is searched as raw bytes, as every zip entry, as every
OLE stream, and as every VBA stream decompressed (MS-OVBA), in UTF-8/ANSI and UTF-16LE.
"""
import io
import os
import re
import sys
import zipfile

import olefile
from oletools.olevba import decompress_stream


def blobs(name, data):
    yield name, data
    if data[:4] == b"PK\x03\x04":
        with zipfile.ZipFile(io.BytesIO(data)) as z:
            for entry in z.namelist():
                yield from blobs(f"{name}!{entry}", z.read(entry))
    elif data[:8] == b"\xd0\xcf\x11\xe0\xa1\xb1\x1a\xe1":
        ole = olefile.OleFileIO(io.BytesIO(data))
        for path in ole.listdir(streams=True, storages=False):
            stream = ole.openstream(path).read()
            label = f"{name}:{'/'.join(path)}"
            yield label, stream
            # VBA streams are compressed; try every start where a compressed container could begin (signature
            # byte 1, then a chunk header whose signature bits are 0b011). Only inside a VBA storage.
            if not any(part.upper() in ("VBA", "_VBA_PROJECT_CUR") for part in path[:-1]) and "vba" not in name.lower():
                continue
            for start in range(0, len(stream) - 3):
                if stream[start] == 1 and ((stream[start + 2] >> 4) & 7) == 3:
                    try:
                        out = decompress_stream(bytearray(stream[start:]))
                    except Exception:
                        continue
                    if out and len(out) > 16:
                        yield f"{label}@{start}(decompressed)", bytes(out)
                        if path[-1].lower() == "dir":
                            break
        ole.close()


def main(paths):
    literals = [n for n in os.environ.get("CORPUS_NEEDLES", "").split("\x1f") if n]
    pattern = os.environ.get("HEXIDE_IDENTITY_PATTERN")
    regex = re.compile(pattern, re.IGNORECASE) if pattern else None
    labels = os.environ.get("CORPUS_NEEDLE_LABELS", "").split("")
    forms = []
    for i, n in enumerate(literals):
        key = labels[i] if i < len(labels) and labels[i] else f"needle{i}"
        forms.append((key, n.lower().encode("utf-8"), n.lower().encode("utf-16-le")))
    total = 0
    for p in paths:
        data = open(p, "rb").read()
        hits = {}
        for label, blob in blobs(os.path.basename(p), data):
            low = blob.lower()
            for key, a, w in forms:
                c = low.count(a) + low.count(w)
                if c:
                    hits[key] = hits.get(key, 0) + c
                    print(f"  {label}: {key} x{c}")
            if regex:
                for text in (blob.decode("latin-1"), blob.decode("utf-16-le", errors="ignore")):
                    c = len(regex.findall(text))
                    if c:
                        hits["pattern"] = hits.get("pattern", 0) + c
        total += sum(hits.values())
        print(f"{os.path.basename(p)}: {'clean' if not hits else ', '.join(f'{k}={v}' for k, v in sorted(hits.items()))}")
    print(f"needles: {len(literals)} literal(s), pattern {'set' if regex else 'NOT SET'}; total hits {total}")


if __name__ == "__main__":
    main(sys.argv[1:])
