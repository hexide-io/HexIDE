#!/usr/bin/env python3
"""Compare round-capsule and stamped-dot candidates with #4813's recorded line grids.

No VB6 measurements are made here. The oracle pages remain the source of truth.
Run from any directory; Python's standard library is sufficient.
"""
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[3]
PAGE = ROOT / 'docs/vb6-fidelity-oracle/circles-arcs-sectors-and-thick-pens-pixel-by-pixel.md'


def capsule(x, y, a, b, width):
    dx, dy = b[0] - a[0], b[1] - a[1]
    length = dx * dx + dy * dy
    t = min(1, max(0, ((x - a[0]) * dx + (y - a[1]) * dy) / length)) if length else 0
    return (x - a[0] - t * dx) ** 2 + (y - a[1] - t * dy) ** 2 <= (width / 2) ** 2


def stamped(x, y, a, b, width):
    # The measured PSet footprint is deliberately not substituted for the Line end rule.
    steps = max(abs(b[0] - a[0]), abs(b[1] - a[1]))
    for i in range(steps + 1):
        t = i / steps if steps else 0
        point = (round(a[0] + (b[0] - a[0]) * t), round(a[1] + (b[1] - a[1]) * t))
        if capsule(x, y, point, point, width):
            return True
    return False


def main():
    pattern = r'\*\*([^\n]+)\*\*: `([^`]+)`\. CurrentX, CurrentY [^\n]+\.\n\n```text\n(.*?)\n```'
    counts = [0, 0]
    total = 0
    cases = re.findall(pattern, PAGE.read_text(), re.S)
    # The earlier drawing-methods page reports these grids starting at x=6.
    for width, start, rows in [
        (3, 9, ["....###########....", "...#############...", "....###########...."]),
        (4, 8, [".....##########....", "...#############...", "...#############...", "...#############..."]),
    ]:
        grid = '\n'.join(f'y{start+i+20:03} ' + row + '.' * 34 for i, row in enumerate(rows))
        # The common reader below starts at x14; shift the input geometry by eight pixels.
        cases.append((f'earlier line w{width} (10,10)-(20,10)',
                      f'DrawWidth = {width}: Line (18, 30)-(28, 30)', grid))
    for title, statement, grid in cases:
        if not (title.startswith('line w') or title.startswith('earlier line w')):
            continue
        numbers = list(map(int, re.findall(r'\d+', statement)))
        width, x1, y1, x2, y2 = numbers
        expected = {(14 + x, int(row[1:4])) for row in grid.splitlines()
                    for x, char in enumerate(row[5:]) if char == '#'}
        # Only compare coordinates the corresponding page actually records.
        left, right, top, bottom = (14, 32, 29 if width == 3 else 28, 31) if title.startswith('earlier ') else (14, 66, 14, 66)
        expected = {(x, y) for x, y in expected if left <= x <= right and top <= y <= bottom}
        differences = []
        for index, model in enumerate((capsule, stamped)):
            actual = {(x, y) for y in range(top, bottom + 1) for x in range(left, right + 1)
                      if model(x, y, (x1, y1), (x2, y2), width)}
            differences.append(len(actual ^ expected))
            counts[index] += actual == expected
        total += 1
        print(f'{title}: differing pixels capsule={differences[0]}, stamped={differences[1]}')
    print(f'Exact grids: capsule={counts[0]}/{total}, stamped={counts[1]}/{total}')


if __name__ == '__main__':
    main()
