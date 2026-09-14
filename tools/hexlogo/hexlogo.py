#!/usr/bin/env python3
"""Rasterise the HexIDE logo to ANSI terminal art.

The geometry below is transcribed from IDE/HexIDE/Icons/AppIcon/hexide-logo.svg. If that file changes,
update the constants here and regenerate (see README.md alongside).

Modes:
  half   - one cell = 2 vertical pixels using U+2580/U+2584 with 24-bit fg/bg (best fidelity, needs UTF-8)
  quad   - one cell = 2x2 pixels using quadrant blocks, 2 colours per cell (finer edges, needs UTF-8)
  space  - one cell = 1 pixel, ASCII space with background colour only (maximum compatibility)
Colour depth: --depth 24 (truecolor) or --depth 256 (xterm 256 palette).
"""
import math, sys, argparse

# ---------------------------------------------------------------- geometry (SVG viewBox 0..200)
def quad_bezier(p0, p1, p2, n=8):
    pts = []
    for i in range(1, n + 1):
        t = i / n
        x = (1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t * t * p2[0]
        y = (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t * t * p2[1]
        pts.append((x, y))
    return pts

HEX = [(171.0, 59.0), (112.0, 18.0)]
HEX += quad_bezier((112.0, 18.0), (100.0, 13.0), (88.0, 18.0))
HEX += [(29.0, 59.0), (29.0, 141.0), (88.0, 182.0)]
HEX += quad_bezier((88.0, 182.0), (100.0, 187.0), (112.0, 182.0))
HEX += [(171.0, 141.0)]

def in_poly(x, y, poly):
    inside = False
    n = len(poly)
    j = n - 1
    for i in range(n):
        xi, yi = poly[i]; xj, yj = poly[j]
        if (yi > y) != (yj > y):
            xint = (xj - xi) * (y - yi) / (yj - yi) + xi
            if x < xint:
                inside = not inside
        j = i
    return inside

def seg_dist(px, py, ax, ay, bx, by):
    dx, dy = bx - ax, by - ay
    l2 = dx * dx + dy * dy
    t = ((px - ax) * dx + (py - ay) * dy) / l2
    t = max(0.0, min(1.0, t))
    cx, cy = ax + t * dx, ay + t * dy
    return math.hypot(px - cx, py - cy)

BONDS = [((110.4, 94.0), (151.96, 70.0)), ((100.0, 88.0), (100.0, 40.0)), ((89.6, 94.0), (48.04, 70.0)),
         ((89.6, 106.0), (48.04, 130.0)), ((100.0, 112.0), (100.0, 160.0)), ((110.4, 106.0), (151.96, 130.0))]
ATOMS = [(151.96, 70.0), (100.0, 40.0), (48.04, 70.0), (48.04, 130.0), (100.0, 160.0), (151.96, 130.0)]
BOND_HALF = 5.5
ATOM_R = 12.5

def lerp(a, b, t): return a + (b - a) * t
def lerp3(c0, c1, t): return tuple(lerp(c0[i], c1[i], t) for i in range(3))

STOPS = [(0.0, (0xFF, 0xB3, 0x47)), (0.45, (0xF0, 0x78, 0x20)), (1.0, (0xC0, 0x4A, 0x00))]

def grad(d):
    if d <= STOPS[0][0]: return STOPS[0][1]
    for (d0, c0), (d1, c1) in zip(STOPS, STOPS[1:]):
        if d <= d1:
            return lerp3(c0, c1, (d - d0) / (d1 - d0))
    return STOPS[-1][1]

def sample(x, y):
    """Return (r,g,b) or None (transparent) for SVG coordinate (x,y)."""
    for (ax, ay) in ATOMS:
        if math.hypot(x - ax, y - ay) <= ATOM_R:
            return (255, 255, 255)
    for (a, b) in BONDS:
        if seg_dist(x, y, a[0], a[1], b[0], b[1]) <= BOND_HALF:
            return (255, 255, 255)
    if not in_poly(x, y, HEX):
        return None
    u = (x - 29.0) / 142.0
    v = (y - 13.0) / 174.0
    d = math.hypot(u - 0.38, v - 0.35) / 0.65
    r, g, b = grad(d)
    d2 = math.hypot(u - 0.5, v - 0.5) / 0.5
    alpha = 0.0 if d2 <= 0.6 else min(1.0, (d2 - 0.6) / 0.4) * 0.18
    k = 1.0 - alpha
    return (r * k, g * k, b * k)

# ---------------------------------------------------------------- raster
HEX_BOX = (29.0, 13.0, 171.0, 187.0)  # the hexagon's bounding box in SVG units

def render_pixels(w, h, ss=4, margin=0.0, crop=False):
    """Return h rows of w pixels, each (r,g,b) or None.

    Without crop, the whole 200x200 SVG box is mapped onto the w x h pixel grid. With crop, only the
    hexagon's bounding box is, scaled uniformly to fill the grid's height or width (whichever binds) and
    centred, so a mark of 12 rows really is 12 rows of hexagon rather than 10 with the SVG's margins.
    """
    if crop:
        x0, y0, x1, y1 = HEX_BOX
        x0 -= margin; y0 -= margin; x1 += margin; y1 += margin
    else:
        x0, y0, x1, y1 = -margin, -margin, 200 + margin, 200 + margin
    scale = min(w / (x1 - x0), h / (y1 - y0))          # pixels per SVG unit, uniform
    ox = (w - (x1 - x0) * scale) / 2                     # letterbox offsets in pixels
    oy = (h - (y1 - y0) * scale) / 2
    rows = []
    for j in range(h):
        row = []
        for i in range(w):
            acc = [0.0, 0.0, 0.0]; cov = 0
            for sj in range(ss):
                for si in range(ss):
                    px = i + (si + 0.5) / ss
                    py = j + (sj + 0.5) / ss
                    x = x0 + (px - ox) / scale
                    y = y0 + (py - oy) / scale
                    c = sample(x, y)
                    if c is not None:
                        cov += 1
                        acc[0] += c[0]; acc[1] += c[1]; acc[2] += c[2]
            n = ss * ss
            if cov * 2 >= n:
                row.append(tuple(int(round(v / cov)) for v in acc))
            else:
                row.append(None)
        rows.append(row)
    return rows

# ---------------------------------------------------------------- colour encoding
_LEVELS = [0, 95, 135, 175, 215, 255]
_PAL256 = {}
for _i in range(216):
    _PAL256[16 + _i] = (_LEVELS[_i // 36], _LEVELS[(_i // 6) % 6], _LEVELS[_i % 6])
for _i in range(24):
    _PAL256[232 + _i] = (8 + 10 * _i,) * 3
_CACHE256 = {}

def to256(rgb):
    key = tuple(int(v) for v in rgb)
    if key in _CACHE256: return _CACHE256[key]
    r, g, b = key
    best, bd = 16, 1e18
    for idx, (pr, pg, pb) in _PAL256.items():
        # weighted RGB distance (perceptual-ish)
        d = 3 * (r - pr) ** 2 + 4 * (g - pg) ** 2 + 2 * (b - pb) ** 2
        if d < bd: best, bd = idx, d
    _CACHE256[key] = best
    return best

class Enc:
    def __init__(self, depth): self.depth = depth
    def fg(self, c): return f"\x1b[38;2;{c[0]};{c[1]};{c[2]}m" if self.depth == 24 else f"\x1b[38;5;{to256(c)}m"
    def bg(self, c): return f"\x1b[48;2;{c[0]};{c[1]};{c[2]}m" if self.depth == 24 else f"\x1b[48;5;{to256(c)}m"
    RESET = "\x1b[0m"

PAD = False

def finish(line, enc):
    return (line if PAD else line.rstrip()) + enc.RESET

def emit_half(px, enc):
    out = []
    for j in range(0, len(px), 2):
        top = px[j]; bot = px[j + 1] if j + 1 < len(px) else [None] * len(top)
        line = []
        for t, b in zip(top, bot):
            if t is None and b is None:
                line.append(enc.RESET + " ")
            elif t is not None and b is None:
                line.append(enc.RESET + enc.fg(t) + "▀")
            elif t is None and b is not None:
                line.append(enc.RESET + enc.fg(b) + "▄")
            else:
                line.append(enc.bg(b) + enc.fg(t) + "▀")
        out.append(finish("".join(line), enc))
    return "\n".join(out)

QUAD = {  # bits: TL=8 TR=4 BL=2 BR=1 -> char with those quadrants set (fg)
    0: " ", 1: "▗", 2: "▖", 3: "▄", 4: "▝", 5: "▐", 6: "▞", 7: "▟",
    8: "▘", 9: "▚", 10: "▌", 11: "▙", 12: "▀", 13: "▜", 14: "▛", 15: "█"}

def emit_quad(px, enc):
    out = []
    for j in range(0, len(px), 2):
        top = px[j]; bot = px[j + 1] if j + 1 < len(px) else [None] * len(top)
        line = []
        for i in range(0, len(top), 2):
            q = [top[i], top[i + 1] if i + 1 < len(top) else None,
                 bot[i], bot[i + 1] if i + 1 < len(bot) else None]
            present = [c for c in q if c is not None]
            if not present:
                line.append(enc.RESET + " "); continue
            # two-colour clustering: split by luminance threshold between the two most distant colours
            def lum(c): return 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2]
            lo = min(present, key=lum); hi = max(present, key=lum)
            if lum(hi) - lum(lo) < 40:  # all one colour class -> average
                avg = tuple(int(sum(c[k] for c in present) / len(present)) for k in range(3))
                mask = sum(8 >> n for n, c in enumerate(q) if c is not None)
                line.append(enc.RESET + enc.fg(avg) + QUAD[mask]); continue
            mid = (lum(hi) + lum(lo)) / 2
            hi_set = [c for c in present if lum(c) >= mid]; lo_set = [c for c in present if lum(c) < mid]
            hi_avg = tuple(int(sum(c[k] for c in hi_set) / len(hi_set)) for k in range(3))
            lo_avg = tuple(int(sum(c[k] for c in lo_set) / len(lo_set)) for k in range(3))
            if len(present) == 4:
                mask = sum(8 >> n for n, c in enumerate(q) if lum(c) >= mid)
                line.append(enc.bg(lo_avg) + enc.fg(hi_avg) + QUAD[mask])
            else:
                # transparency present: bg must stay default, so fg carries the dominant class only
                dom, dom_avg = (hi_set, hi_avg) if len(hi_set) >= len(lo_set) else (lo_set, lo_avg)
                mask = sum(8 >> n for n, c in enumerate(q) if c is not None and (c in dom))
                line.append(enc.RESET + enc.fg(dom_avg) + QUAD[mask])
        out.append(finish("".join(line), enc))
    return "\n".join(out)

def emit_space(px, enc):
    out = []
    for row in px:
        line = []
        for c in row:
            line.append(enc.RESET + " " if c is None else enc.bg(c) + " ")
        out.append(finish("".join(line), enc))
    return "\n".join(out)

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--mode", choices=["half", "quad", "space"], default="half")
    ap.add_argument("--width", type=int, default=40, help="output width in cells")
    ap.add_argument("--depth", type=int, choices=[24, 256], default=24)
    ap.add_argument("--ss", type=int, default=4, help="supersample factor per pixel axis")
    ap.add_argument("--margin", type=float, default=0.0, help="extra SVG units of padding around the 200x200 box")
    ap.add_argument("--crop", action="store_true", help="fit the hexagon's bounding box to the grid instead of the whole SVG canvas (bigger mark, same box)")
    ap.add_argument("--pad", action="store_true", help="keep every row exactly --width visible cells (no trailing trim), for side-by-side layouts")
    ap.add_argument("-o", "--out", help="write to file (UTF-8, no BOM) instead of stdout")
    a = ap.parse_args()
    global PAD
    PAD = a.pad
    enc = Enc(a.depth)
    if a.mode == "half":
        px = render_pixels(a.width, a.width, a.ss, a.margin, a.crop)        # cell aspect 1:2 -> square pixels
        text = emit_half(px, enc)
    elif a.mode == "quad":
        px = render_pixels(a.width * 2, a.width, a.ss, a.margin, a.crop)    # 2x2 per cell
        text = emit_quad(px, enc)
    else:
        px = render_pixels(a.width, a.width // 2, a.ss, a.margin, a.crop)   # 1 px per cell, cell is 1:2
        text = emit_space(px, enc)
    if a.out:
        with open(a.out, "w", encoding="utf-8", newline="\n") as f:
            f.write(text + "\n")
    else:
        sys.stdout.reconfigure(encoding="utf-8")
        print(text)

if __name__ == "__main__":
    main()
