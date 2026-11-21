"""Field-by-field dump and diff of a .twinproj or .twinpack container, for the twinBASIC oracle (#5139, #5140).

usage:
  python -I tbdump.py dump <file>             every field: offset, width, entry path, field, value
  python -I tbdump.py diff <before> <after>   what changed between two saves, by entry path and field
  python -I tbdump.py show <file> <path>      one file entry's content, as text if it decodes as UTF-8

This reads the container's bytes and nothing else. The layout it walks is the hypothesis under test, not a source:
every field it names is checked against saves the IDE made after one known change. It stops at the first byte that
doesn't fit, and says where.
"""
import hashlib
import struct
import sys

MAGIC = 0xEA0BA51C


class Misfit(Exception):
    pass


def walk(data):
    """Yields (offset, width, path, field, value) for every field, in file order."""
    pos = 0

    def take(n, path, what):
        nonlocal pos
        if pos + n > len(data):
            raise Misfit(f"truncated at {pos:#x} reading {what} of /{path} ({n} bytes wanted, {len(data) - pos} left)")
        b = data[pos:pos + n]
        pos += n
        return b

    def u(n, fmt, path, field):
        at = pos
        (v,) = struct.unpack(fmt, take(n, path, field))
        return at, v

    at, magic = u(4, "<I", "", "magic")
    yield at, 4, "", "magic", f"{magic:#010x}"
    if magic != MAGIC:
        raise Misfit(f"magic {magic:#x} is not {MAGIC:#x}")

    def entry(parent, depth):
        at, kind = u(2, "<h", parent, "kind")
        at_len, nlen = u(4, "<I", parent, "name length")
        at_name = pos
        name = take(nlen, parent, "name").decode("utf-8", "strict")
        path = name if depth == 1 else (f"{parent}/{name}" if depth > 1 else "")
        label = path or "(root)"
        yield at, 2, label, "version" if depth == 0 else "kind", kind
        yield at_len, 4, label, "name length", nlen
        yield at_name, nlen, label, "name", name
        at_rev, rev = u(8, "<Q", label, "revision")
        yield at_rev, 8, label, "revision", rev
        at_fl, flags = u(4, "<I", label, "flags")
        yield at_fl, 4, label, "flags", f"{flags:#010x}"
        at_cat = pos
        cat = take(1, label, "category")[0]
        yield at_cat, 1, label, "category", cat
        if depth > 0 and kind == 1:
            at_cl, clen = u(4, "<I", label, "content length")
            yield at_cl, 4, label, "content length", clen
            at_c = pos
            content = take(clen, label, "content")
            yield at_c, clen, label, "content", "sha256:" + hashlib.sha256(content).hexdigest()[:16]
            at_n, count = u(4, "<I", label, "revision count")
            yield at_n, 4, label, "revision count", count
            for i in range(count):
                at_r, r = u(4, "<I", label, f"revision[{i}]")
                yield at_r, 4, label, f"revision[{i}]", r
        elif depth == 0 or kind == 2:
            at_cc, children = u(4, "<I", label, "children")
            yield at_cc, 4, label, "children", children
            for _ in range(children):
                yield from entry(path, depth + 1)
        else:
            raise Misfit(f"/{label}: kind {kind} at {at:#x} is neither 1 (file) nor 2 (folder)")

    yield from entry("", 0)
    if pos != len(data):
        raise Misfit(f"{len(data) - pos} bytes left over after the root entry, from {pos:#x}")


def fields(data):
    out = {}
    order = []
    for at, width, path, field, value in walk(data):
        out[(path, field)] = (at, width, value)
        if field == "name":
            order.append(path)
    return out, order


def content(data, want):
    pos = None
    for at, width, path, field, value in walk(data):
        if path == want and field == "content":
            pos = (at, width)
    if pos is None:
        raise SystemExit(f"no file entry /{want}")
    return data[pos[0]:pos[0] + pos[1]]


def main(argv):
    if len(argv) >= 2 and argv[0] == "dump":
        data = open(argv[1], "rb").read()
        try:
            for at, width, path, field, value in walk(data):
                print(f"{at:#08x} {width:6d}  /{path}  {field} = {value}")
        except Misfit as e:
            print(f"MISFIT: {e}")
            return 1
        print(f"{len(data)} bytes, all accounted for")
        return 0
    if len(argv) >= 3 and argv[0] == "diff":
        a_data, b_data = open(argv[1], "rb").read(), open(argv[2], "rb").read()
        a, a_order = fields(a_data)
        b, b_order = fields(b_data)
        print(f"sizes {len(a_data)} -> {len(b_data)}; identical bytes: {a_data == b_data}")
        if a_order != b_order:
            print("entry order or set differs:")
            print("  only before:", [p for p in a_order if p not in b_order])
            print("  only after: ", [p for p in b_order if p not in a_order])
            common = [p for p in a_order if p in b_order]
            if common != [p for p in b_order if p in a_order]:
                print("  common entries reordered")
        for key in sorted(set(a) | set(b)):
            va, vb = a.get(key), b.get(key)
            if va is None or vb is None or va[2] != vb[2]:
                path, field = key
                print(f"  /{path}  {field}: {va[2] if va else '-'} -> {vb[2] if vb else '-'}")
        return 0
    if len(argv) >= 3 and argv[0] == "show":
        body = content(open(argv[1], "rb").read(), argv[2])
        try:
            sys.stdout.write(body.decode("utf-8"))
        except UnicodeDecodeError:
            print(f"{len(body)} bytes, not UTF-8; first 64: {body[:64].hex(' ')}")
        return 0
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
