# Thick-line candidates (#4813)

Run `python3 scripts/probes/drawing-thick-pens/compare.py` from the repository root.
This compares models with existing measurements, not with a new VB6 run. Sources:

- [Drawing-methods oracle](../../../docs/vb6-fidelity-oracle/the-drawing-methods-line-pset-circle-cls-and-point.md), “Thick pens”.
- [Circle/thick-pen oracle](../../../docs/vb6-fidelity-oracle/circles-arcs-sectors-and-thick-pens-pixel-by-pixel.md), the line grids after “circle r10 DrawWidth 2”.

The continuous Euclidean round capsule matches 2 of 13 line grids: width-5 horizontal and shallow diagonal.
Stamping Euclidean discs at integer line samples matches only the width-5 horizontal grid.
Both include the terminal endpoint; changing the width-one endpoint rule alone is insufficient.
Both miss every width-2/3/4/6 line grid, including the reversed and zero-length width-3 lines.
The script prints the differing-pixel count for every grid, including the two earlier horizontal lines.
It does not claim that no better rasteriser exists.

The zero-length width-3 line also rules out simply stamping PSet's footprint: the measured line is a
five-pixel plus, while PSet lights eight pixels. No tested general line model matches the full set,
so the Line family, including B/BF, remains refused together under #4813's all-grids-together instruction.
A later rasteriser must pass all the recorded line and box grids before that refusal is removed.

PSet's finite measured widths and Circle's explicitly restricted radius/width subset instead use their
recorded pixel footprints, translated to the converted point or centre. These tables deliberately
preserve the measured asymmetry; they do not assert an ideal-ellipse or shared-pen explanation.
Width 9 has no PSet measurement and remains refused. No oracle page is changed by this probe.
