# A Timer's default Interval (#2068)

Provenance: the implementation brief and supervisor comment on hexide-io/HexIDE#2068,
reporting the #1556 probe on vb6.exe SP6, with native and p-code agreeing. The comment
locates the original record under “Found along the way” in the controls-add-and-controls-remove
subject on the then-unmerged #1556 branch. This records the supplied measurements, not a new run.

The hand-written form omitted Timer1's Interval line and gave Timer2 an Interval of 100.
Timer1 read Interval 0 and Enabled True. During 1.5 seconds of DoEvents, Timer1 raised
no Timer events and Timer2 raised more than five. A Timer created with
`Controls.Add("VB.Timer", "a12")` also read Interval 0 and Enabled True; its events
were not counted.

Thus an omitted Interval reads 0, and an enabled Timer at 0 does not tick. The old
HexIDE default of 1000 made that same form tick once a second instead.

The exact tick count and what VB6's designer writes for a newly drawn Timer are
unmeasured. The form used for this measurement was written by hand.
