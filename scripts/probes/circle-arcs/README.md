# Circle arc rasterisation comparison (#4771)

Run from any directory with Python 3 (standard library only):

```sh
python3 scripts/probes/circle-arcs/compare.py
python3 -m unittest discover -s scripts/probes/circle-arcs -p 'test_*.py'
```

`compare.py` reads the five DrawWidth-1 arcs/sectors from the
[existing oracle page](../../../docs/vb6-fidelity-oracle/circles-arcs-sectors-and-thick-pens-pixel-by-pixel.md)
and the seventeen additional grids supplied in the
[#4771's brief](https://github.com/hexide-io/HexIDE/issues/4771), copied into
[issue-4771-grids.md](issue-4771-grids.md) from oracle PR #4904. It does not change or duplicate
an oracle page. All grids use centre (40,40), pixel scale, and x/y 14–66 inclusive.
Unlisted rows are background. Colour mismatches count as one differing pixel, as do
missing or extra pixels. The two filled grids distinguish red outlines and green fill.

The rasterisers receive only radius, aspect, angles and fill state. No candidate reads
measured pixels or branches on an individual case. These are hypotheses, not new VB6
measurements. Radius/aspect conversion follows today's `RuntimeDrawing.Circle`, including
Single coercion, whole-twip radius and whole-pixel half-axes. `whole` uses ties-to-even,
as the runtime does. Negative angles add the corresponding radius line; only sectors
fill. Fill uses even-odd scan intersections at integer pixel centres, before the edge.
Radius lines use today's terminal-excluded integer `RuntimeDrawing.Line` rule.

## Candidates

- `midpoint-start-*`: integer midpoint ellipse, selected by each pixel's elliptical
  angle. This checks an alternative to curve flattening, although the measurements
  already show that filtering the full-circle outline cannot explain every arc.
- `ideal-samples`: ideal ellipse sampled at 32 steps per unit of arc length,
  rounding each sample to a pixel.
- `ideal-integer[-open]`: join those rounded samples with integer lines.
- `cubic-{tolerance}-{quadrants|equal}-{fractional|integer|integer-open}`:
  cubic Bézier approximation with tangent control distance `4/3 tan(sweep/4)`.
  Split at quadrant boundaries, or into equal sweeps no greater than a quadrant.
  Subdivide each cubic into a power-of-two number of segments until the circular
  sagitta bound is at most 0.5, 0.25 or 0.125 pixels. `fractional` interpolates the
  minor coordinate at integer major-axis crossings, retaining fractional vertices
  and adding a rounded vertex where the major axis changes, as the full-circle
  implementation does. `integer` rounds vertices before joining them with lines.
- `cubic-ray-{angle|round|truncate}-grid-{0|16|64}-start-*`: the 0.5-pixel,
  quadrant-split fractional candidate, varying endpoint handling. `angle` retains
  the supplied angle; `round` or `truncate` converts the ideal endpoint to integer
  device coordinates, then recovers the elliptical angle of that ray. Grid 0 retains
  floating vertices; 16 and 64 snap vertices to 1/16 and 1/64 pixel, respectively.
- `cubic-controls-{1|16|64}-{round|floor}-start-*`: snap the two control points
  of each cubic to integer, 1/16 or 1/64 pixel coordinates before uniform dyadic
  flattening at tolerance 0.5. Compare rounding to nearest (ties to even) with floor.
- `cubic-adaptive-{tolerance}-ray-{angle|round|truncate}`: recursively split each
  cubic at its parameter midpoint with De Casteljau subdivision until both control
  points are within the tolerance of the endpoint chord. Use fractional line
  rasterisation and exclude the initial arc pixel. This tests control-polygon
  flatness rather than a circular sagitta bound for deciding how far to flatten.
- `cubic-truncated-vertices`: truncate the cubic's vertices to integer device
  coordinates before integer line rasterisation, excluding the initial pixel.

`open` / `start-excluded` removes the rounded initial arc pixel before radii are drawn;
`start-included` retains whatever the rasteriser produced. The final integer vertex is
included for integer curves. These endpoint variants are deliberately hypotheses: a
near-axis input may give a fractional endpoint just beyond a quadrant boundary.
Midpoint and ideal candidates use the sampled ideal polygon for sector fill; cubic
candidates use their flattened polygon.

## Result

[results.md](results.md) is the output of the command above, including the closest
candidate(s) for every grid. **None of the 63 candidates matches all 22.** The lowest
total is 52 differing pixels, shared by the `cubic-controls-16-floor-start-excluded`
and `cubic-controls-64-floor-start-excluded` candidates. A candidate matching one grid does not justify selecting it for that grid;
there is no per-grid dispatch.

As #4771's brief allows, the runtime keeps its exact #988 refusal. No product code,
full-circle rasterisation or thick-pen handling changes. One-angle defaults remain
unmeasured; unequal scale units and DrawWidth above 1 also remain refused. This result
rules out these candidates only; it does not claim no exact rasteriser exists.
