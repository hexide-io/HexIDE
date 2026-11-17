"""Report whether each file's author-type properties are blank or generic, without printing other values."""
import io
import re
import sys
import zipfile

import olefile

ALLOWED = {"", "corpus", "author"}


def verdict(value):
    v = (value or "").strip()
    return "ok" if v.lower() in ALLOWED else f"OTHER(len={len(v)})"


for path in sys.argv[1:]:
    data = open(path, "rb").read()
    found = {}
    if data[:4] == b"PK\x03\x04":
        with zipfile.ZipFile(io.BytesIO(data)) as z:
            for entry in ("docProps/core.xml", "docProps/app.xml"):
                if entry in z.namelist():
                    xml = z.read(entry).decode("utf-8")
                    for tag in ("dc:creator", "cp:lastModifiedBy", "Company", "Manager"):
                        m = re.search(rf"<{tag}[^>]*>(.*?)</{tag}>", xml, re.S)
                        if m:
                            found[tag] = verdict(m.group(1))
    else:
        ole = olefile.OleFileIO(io.BytesIO(data))
        meta = ole.get_metadata()
        for attr in ("author", "last_saved_by", "company", "manager"):
            raw = getattr(meta, attr, None)
            if raw is not None:
                found[attr] = verdict(raw.decode("latin-1") if isinstance(raw, bytes) else str(raw))
        ole.close()
    print(path.replace("\\", "/").split("/")[-1], found or "no author properties")
