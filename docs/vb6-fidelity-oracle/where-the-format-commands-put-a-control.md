## Where the Format commands put a control (2026-10-22, #929, design-time)

**The question.** HexIDE's Center in Form, Align Centers and Make Equal spacing worked in fractional pixels, so a
control centred on an odd difference landed at x.5 px, and a save wrote `Left = 4192.5`. The issue proposed whole
pixels with the midpoint rounded to even, by analogy with the run-time rule for `Label1.Left = 2.5`. What VB6's own
designer does had not been measured.

**The probes.** Two forms opened in VB6's IDE through the console agent, with controls sized so that each exact answer
is a half twip. Each command was run from the Format menu, with the mouse driven for Ctrl+click through the agent's
`exec` action. Each control was then selected on its own, and its position read from the toolbar's readout.

| Command | Case | Exact | VB6 |
|---|---|---|---|
| Center in Form, horizontally | form 4680 wide, button 1215 | 1732.5 | **1733** |
| Center in Form, horizontally | button 1200 | 1740 | 1740 |
| Center in Form, horizontally | button 1210, not a whole pixel | 1735 | 1735 |
| Center in Form, vertically | form 3195 high, button 510 | 1342.5 | **1342** |
| Align Centers | reference at 600, 1200 wide; other 1215 wide | 592.5 | **593** |
| Align Middles | reference at 300, 495 high; other 510 high | 292.5 | **292** |
| Horizontal Spacing, Make Equal | 1200-wide buttons at 300, 2000 and 4005 | 300, 2152.5, 4005 | **300, 2152, 4005** |
| Toolbox double-click (CommandButton) | form 6000 × 4500, new button 1215 × 495 | 2392.5, 2002.5 | **2400, 2040** |
| Toolbox double-click (CommandButton) | form 4365 × 3495: 13.125 and 12.5 grid units | 1575, 1500 | **1560, 1560** |

**What it shows:**
- **VB6 works in whole twips, not whole pixels.** 1733 is 115.53 px. The obvious guess, whole pixels rounded to even,
  is wrong twice over.
- **It halves each size on its own and truncates**, so no rounding rule fits. Across, 1732.5 went up to 1733; down,
  1342.5 went down to 1342. Both are ScaleWidth\2 - Width\2: 2340 - 607 and 1597 - 255. Align Centers is the
  reference's Left + its Width\2 - the control's Width\2 (600 + 600 - 607 = 593), and Align Middles the same in the
  other direction (300 + 247 - 255 = 292).
- **The reference is the control selected last**: the one with solid handles, which didn't move. The toolbar's
  readout went on showing the first control selected.
- **Make Equal truncates the gap and leaves the last control where it was.** 1305 twips of space between three buttons
  became gaps of 652 and 653, and the outermost two didn't move. With more than three controls, whether the leftover
  twips all go to the last gap was not measured; leaving the last control fixed implies that they do.
- **A control added by double-clicking a tool is centred, then put on the nearest grid line** (120 twips, with Align
  Controls to Grid on): 2392.5 became 2400, and 2002.5 became 2040, where 1920 was the line below. A third probe
  settled the two cases those left open: 13.125 grid units went to 13, so it is the nearest line and not the next
  one up, and 12.5 went to 13, so a position exactly between two lines goes to the higher. To-even rounding, .NET's
  default, gives 12 there.
- **VB6 named the first CommandButton Command1** (#540).
- Make Equal spacing was measured only across. HexIDE applies the same rule down.

