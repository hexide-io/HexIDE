import io, sys, zipfile, olefile
from oletools.olevba import decompress_stream
name = "".join(chr(c) for c in (0x65E5, 0x672C, 0x8A9E, 0x30E2, 0x30B8, 0x30E5, 0x30FC, 0x30EB))
hello = "".join(chr(c) for c in (0x3053, 0x3093, 0x306B, 0x3061, 0x306F))
for path in sys.argv[1:]:
    data = open(path, "rb").read()
    if data[:2] == b"PK":
        data = zipfile.ZipFile(io.BytesIO(data)).read("xl/vbaProject.bin")
        prefix = []
    else:
        prefix = ["_VBA_PROJECT_CUR"]
    ole = olefile.OleFileIO(io.BytesIO(data))
    d = bytes(decompress_stream(bytearray(ole.openstream(prefix + ["VBA", "dir"]).read())))
    streams = [p for p in ole.listdir() if len(p) >= 2 and p[-2] == "VBA"]
    in_source = False
    for p in streams:
        raw = ole.openstream(p).read()
        for start in range(len(raw) - 3):
            if raw[start] == 1 and ((raw[start + 2] >> 4) & 7) == 3:
                try:
                    out = bytes(decompress_stream(bytearray(raw[start:])))
                except Exception:
                    continue
                if hello.encode("cp932") in out:
                    in_source = True
                    break
    print(path.split("\\")[-1], "| dir has the name in cp932:", name.encode("cp932") in d,
          "| in UTF-16LE:", name.encode("utf-16-le") in d,
          "| a module stream holds the string in cp932:", in_source,
          "| stream names:", sorted("/".join(p[-1:]) for p in streams if p[-1] not in ("dir", "_VBA_PROJECT"))[:6] == sorted(["ThisWorkbook", "Sheet1", name]) or [len(p[-1]) for p in streams])
