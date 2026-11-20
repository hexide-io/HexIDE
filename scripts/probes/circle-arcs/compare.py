#!/usr/bin/env python3
"""Compare independent geometric candidates with #4771's measured Point grids.

Only the comparison reads measured pixels; rasterisers receive geometry alone.
Python standard library only. Run from any working directory.
"""
from dataclasses import dataclass
import math
from pathlib import Path
import re
import struct

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
TAU = 2 * math.pi


@dataclass(frozen=True)
class Case:
    name: str
    radius: float
    start: float
    end: float
    aspect: float
    filled: bool
    expected: dict


def read_cases(path):
    cases = []
    for match in re.finditer(r'\*\*(.*?)\*\*: `(.*?)`.*?```text\n(.*?)```',
                             path.read_text(encoding='utf-8-sig'), re.S):
        name, statement, grid = match.groups()
        if not (name.startswith(('arc ', 'sector ', 'filled arc ', 'filled sector '))):
            continue
        args = statement.split('Circle (40, 40), ')[1].split(',')
        if len(args) < 4:
            raise ValueError(f'Missing angles: {name}')
        pixels = {}
        for row in grid.splitlines():
            y, cells = row.split()
            if len(cells) != 53 or not 14 <= int(y[1:]) <= 66:
                raise ValueError(f'Bad grid row: {row}')
            for x, colour in enumerate(cells, 14):
                if colour != '.':
                    pixels[x, int(y[1:])] = colour
        cases.append(Case(name, float(args[0]), float(args[2]), float(args[3]),
                          float(args[4]) if len(args) > 4 else 1,
                          'FillStyle = 0' in statement, pixels))
    return cases


def single(x):
    return struct.unpack('f', struct.pack('f', x))[0]


def whole(x):
    return round(x)


def line(a, b):
    """RuntimeDrawing.Line's integer, terminal-excluded rasterisation."""
    x, y = a
    dx, dy = abs(b[0] - x), abs(b[1] - y)
    sx, sy = (b[0] > x) - (b[0] < x), (b[1] > y) - (b[1] < y)
    horizontal = dx >= dy
    major, minor = (dx, dy) if horizontal else (dy, dx)
    bias = major // 2 if (sy if horizontal else sx) < 0 else (major - 1) // 2
    for i in range(major):
        offset = (i * minor + bias) // major
        yield x + (i if horizontal else offset) * sx, y + (offset if horizontal else i) * sy


def geometry(case):
    # Follow today's Circle conversion for the measured pixel-scale inputs.
    r = whole(single(case.radius) * 15)
    aspect = abs(single(case.aspect))
    rx = whole((r / aspect if aspect > 1 else r) / 15)
    ry = whole((r if aspect > 1 else r * aspect) / 15)
    start, end = abs(single(case.start)), abs(single(case.end))
    while end <= start:
        end += TAU
    return rx, ry, start, end


def point(rx, ry, angle):
    return 40 + rx * math.cos(angle), 40 - ry * math.sin(angle)


def bezier(rx, ry, start, end, tolerance, split_quadrants, control_grid=0, control_round='round', adaptive=False):
    """Cubic arc approximation, flattened at dyadic parameter intervals."""
    if split_quadrants:
        angles = [start]
        boundary = (math.floor(start / (math.pi / 2)) + 1) * math.pi / 2
        while boundary < end:
            angles.append(boundary)
            boundary += math.pi / 2
        angles.append(end)
    else:
        n = max(1, math.ceil((end - start) / (math.pi / 2)))
        angles = [start + (end - start) * i / n for i in range(n + 1)]
    result = []
    for a, b in zip(angles, angles[1:]):
        k = 4 / 3 * math.tan((b - a) / 4)
        p0, p3 = point(rx, ry, a), point(rx, ry, b)
        p1 = p0[0] - k * rx * math.sin(a), p0[1] - k * ry * math.cos(a)
        p2 = p3[0] + k * rx * math.sin(b), p3[1] + k * ry * math.cos(b)
        if control_grid:
            rounding = whole if control_round == 'round' else math.floor
            p1, p2 = [tuple(rounding(v * control_grid) / control_grid for v in p) for p in (p1, p2)]
        if adaptive:
            def flatten(a, b, c, d):
                dx, dy = d[0] - a[0], d[1] - a[1]
                distance = max(abs(dx * (p[1]-a[1]) - dy * (p[0]-a[0])) for p in (b, c))
                if distance * distance <= tolerance * tolerance * (dx*dx + dy*dy):
                    result.append(a)
                    return
                def mid(v, w):
                    return tuple((v[j]+w[j])/2 for j in range(2))
                ab, bc, cd = mid(a, b), mid(b, c), mid(c, d)
                abc, bcd = mid(ab, bc), mid(bc, cd)
                m = mid(abc, bcd)
                flatten(a, ab, abc, m)
                flatten(m, bcd, cd, d)
            flatten(p0, p1, p2, p3)
            continue
        count = 1
        while max(rx, ry) * (1 - math.cos((b - a) / (2 * count))) > tolerance:
            count *= 2
        for i in range(count):
            t = i / count
            u = 1 - t
            result.append(tuple(u**3 * p0[j] + 3*u*u*t*p1[j] + 3*u*t*t*p2[j] + t**3*p3[j]
                                for j in range(2)))
    return result + [point(rx, ry, end)]


def fractional_lines(points):
    """Interpolate the minor coordinate at integer major-axis crossings."""
    pixels = set()
    previous = None
    for a, b in zip(points, points[1:]):
        dx, dy = b[0] - a[0], b[1] - a[1]
        horizontal = abs(dx) >= abs(dy)
        axis = 0 if horizontal else 1
        if a[axis] == b[axis]:
            continue
        for major in range(math.ceil(min(a[axis], b[axis])), math.floor(max(a[axis], b[axis])) + 1):
            minor = whole(a[1-axis] + (major - a[axis]) * (b[1-axis] - a[1-axis]) / (b[axis] - a[axis]))
            pixels.add((major, minor) if horizontal else (minor, major))
        if previous is not None and previous != horizontal:
            pixels.add(tuple(map(whole, a)))
        previous = horizontal
    return pixels


def midpoint(rx, ry, start, end):
    """Integer midpoint ellipse, then select the requested angular interval."""
    quadrant = set()
    x, y = 0, ry
    dx, dy = 0, 2 * rx * rx * y
    decision = ry * ry - rx * rx * ry + rx * rx / 4
    while dx < dy:
        quadrant.add((x, y))
        x += 1
        dx += 2 * ry * ry
        if decision < 0:
            decision += dx + ry * ry
        else:
            y -= 1
            dy -= 2 * rx * rx
            decision += dx - dy + ry * ry
    decision = ry * ry * (x + 0.5)**2 + rx * rx * (y - 1)**2 - rx * rx * ry * ry
    while y >= 0:
        quadrant.add((x, y))
        y -= 1
        dy -= 2 * rx * rx
        if decision > 0:
            decision += rx * rx - dy
        else:
            x += 1
            dx += 2 * ry * ry
            decision += dx - dy + rx * rx
    result = set()
    for x, y in quadrant:
        for sx in (-1, 1):
            for sy in (-1, 1):
                angle = math.atan2(sy * y / ry, sx * x / rx) % TAU
                if (angle - start) % TAU <= end - start:
                    result.add((40 + sx * x, 40 - sy * y))
    return result


def fill_polygon(points):
    """Even-odd scan conversion at integer pixel centres, excluding edges."""
    pixels = set()
    for y in range(14, 67):
        edges = []
        for a, b in zip(points, points[1:] + points[:1]):
            if min(a[1], b[1]) <= y < max(a[1], b[1]):
                edges.append(a[0] + (y-a[1]) * (b[0]-a[0]) / (b[1]-a[1]))
        edges.sort()
        for left, right in zip(edges[::2], edges[1::2]):
            pixels.update((x, y) for x in range(math.floor(left) + 1, math.ceil(right)))
    return pixels


def draw(case, curve, raster, tolerance=0.5, split_quadrants=True, quantize='round', endpoint='angle', vertex_grid=0, exclude_start=False, control_grid=0, control_round='round', adaptive=False):
    rx, ry, start, end = geometry(case)
    if endpoint != 'angle':
        rounding = whole if endpoint == 'round' else math.trunc
        def ray(angle):
            x, y = point(rx, ry, angle)
            return math.atan2((40-rounding(y))/ry, (rounding(x)-40)/rx) % TAU
        start, end = ray(start), ray(end)
        while end <= start:
            end += TAU
    if curve == 'cubic':
        points = bezier(rx, ry, start, end, tolerance, split_quadrants, control_grid, control_round, adaptive)
    else:
        # An ideal ellipse, sampled finely enough to avoid skipped pixel cells.
        count = max(1, math.ceil((end-start) * max(rx, ry) * 32))
        points = [point(rx, ry, start+(end-start)*i/count) for i in range(count+1)]
    if vertex_grid:
        points = [tuple(whole(v * vertex_grid) / vertex_grid for v in p) for p in points]
    rounding = whole if quantize == 'round' else math.trunc
    rounded = [tuple(map(rounding, p)) for p in points]
    if curve == 'midpoint':
        edge = midpoint(rx, ry, start, end)
    elif raster == 'fractional':
        edge = fractional_lines(points)
    elif raster == 'points':
        edge = set(rounded)
    else:
        edge = set()
        for a, b in zip(rounded, rounded[1:]):
            edge.update(line(a, b))
        edge.add(rounded[-1])
    # Both endpoint inclusion and exclusion are candidates, not asserted VB6 rules.
    if raster.endswith('open') or exclude_start:
        edge.discard(rounded[0])
    sector = case.start < 0 or case.end < 0
    if case.start < 0:
        edge.update(line((40, 40), rounded[0]))
    if case.end < 0:
        edge.update(line((40, 40), rounded[-1]))
    result = {}
    if case.filled and sector:
        for p in fill_polygon([(40, 40)] + points):
            result[p] = 'G'
    result.update((p, 'R' if case.filled else '#') for p in edge)
    return {p: c for p, c in result.items() if 14 <= p[0] <= 66 and 14 <= p[1] <= 66}


def candidates():
    for exclude in (False, True):
        yield f'midpoint-start-{"excluded" if exclude else "included"}', dict(
            curve='midpoint', raster='points', exclude_start=exclude)
    yield 'ideal-samples', dict(curve='ideal', raster='points')
    for raster in ('integer', 'integer-open'):
        yield f'ideal-{raster}', dict(curve='ideal', raster=raster)
    for tolerance in (0.5, 0.25, 0.125):
        for split in (True, False):
            for raster in ('fractional', 'integer', 'integer-open'):
                name = f'cubic-{tolerance:g}-{"quadrants" if split else "equal"}-{raster}'
                yield name, dict(curve='cubic', raster=raster, tolerance=tolerance, split_quadrants=split)
    for endpoint in ('angle', 'round', 'truncate'):
        for grid in (0, 16, 64):
            for exclude in (False, True):
                yield f'cubic-ray-{endpoint}-grid-{grid}-start-{"excluded" if exclude else "included"}', dict(
                    curve='cubic', raster='fractional', endpoint=endpoint, vertex_grid=grid, exclude_start=exclude)
    for grid in (1, 16, 64):
        for rounding in ('round', 'floor'):
            for exclude in (False, True):
                yield f'cubic-controls-{grid}-{rounding}-start-{"excluded" if exclude else "included"}', dict(
                    curve='cubic', raster='fractional', control_grid=grid, control_round=rounding, exclude_start=exclude)
    for tolerance in (0.5, 0.25, 0.125):
        for endpoint in ('angle', 'round', 'truncate'):
            yield f'cubic-adaptive-{tolerance:g}-ray-{endpoint}', dict(
                curve='cubic', raster='fractional', adaptive=True, tolerance=tolerance,
                endpoint=endpoint, exclude_start=True)
    yield 'cubic-truncated-vertices', dict(curve='cubic', raster='integer-open', quantize='truncate')


def difference(expected, actual):
    return sum(expected.get(p, '.') != actual.get(p, '.') for p in expected.keys() | actual.keys())


def main():
    cases = read_cases(ROOT / 'docs/vb6-fidelity-oracle/circles-arcs-sectors-and-thick-pens-pixel-by-pixel.md')
    cases += read_cases(HERE / 'issue-4771-grids.md')
    if len(cases) != 22 or len({c.name for c in cases}) != 22:
        raise ValueError('Expected 22 distinct measured cases')
    results = [(name, [difference(c.expected, draw(c, **options)) for c in cases])
               for name, options in candidates()]
    print('| Candidate | ' + ' | '.join(f'{i+1:02}' for i in range(len(cases))) + ' | Total |')
    print('|---|' + '---:|' * (len(cases)+1))
    for name, diffs in results:
        print('| ' + name + ' | ' + ' | '.join(map(str, diffs)) + f' | {sum(diffs)} |')
    print('\nClosest candidates per grid (ties retained):\n')
    for i, c in enumerate(cases):
        best = min(diffs[i] for _, diffs in results)
        names = ', '.join(name for name, diffs in results if diffs[i] == best)
        print(f'{i+1:02}. {c.name}: {best} differing pixels — {names}')
    print('\nExact matches across all 22: ' + (', '.join(name for name, diffs in results if not any(diffs)) or 'none'))


if __name__ == '__main__':
    main()
