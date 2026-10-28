# 7. `hover`'s move through the window's input was measured headless only — **CLOSED** (#1303, 2026-10-27)

**The gap (narrowed 2026-10-21).** `hover` sends its move through the window's own input, so a declared `ToolTip.Tip`
opens (#1265). That was shown headless only. Not yet run against the IDE: that a toolbar button reports `tip: Add Form`;
where the tip is placed, now that the move updates the pointer position Avalonia tracks; that quick info and Auto Data
Tips still open on Windows and Linux, the same identifier twice in a row; that a real mouse behaves normally after a
hover ends; and that a disabled toolbar button says it is disabled.

**Placement needed a change to measure (#1303).** A tip closes when the hover ends, before any snapshot could show it,
so "a snapshot taken during the watch" could never be taken. `hover` now reads where an open tip is while it is open:
`tipBounds` gives x, y, width and height in the window's device-independent units, as `inspect_element`'s `boundingRect`
does, and `detail` gives its offset from the point hovered. `TipPlacementTests` covers both.

**Measured (2026-10-27, headless `claude -p` through the hexide tools, on a Debug build of #1303's branch).** On Windows
at 150%, and on Linux under WSLg (Ubuntu 26.04, X11). The project's Form1 held `Dim count As Integer`, `count = 42`,
`Stop`.

- **Toolbar tip.** `hover` on `…/Button[Standard.AddForm]`, whose `boundingRect` is `[23.3, 26, 22, 22]`, answered
  `hovered (11, 11) through the window's input; tip: Add Form (its top-left 0.3 left of and 19.7 below the point
  hovered)` with `"tipBounds":[34,56.7,63.3,22.7]` on Windows. On Linux the offset was 0 right and 20 below, with
  `[34,54,68,20]`. Each was the same three times running.
- **Placement.** The tip opens about 20 below the point hovered, as Avalonia places a tip at the pointer, not at the
  real pointer's last position, as the old route did (the archived gap "7. A declarative `ToolTip.Tip` cannot be made to
  open").
  - A test harness then moved the real mouse to the same point. A screen capture showed its tip at about (34.7, 58.7)
    in the window, where `tipBounds` put it.
  - Quick info at the caret on `count` gave `tip: count As Integer (its top-left 0.5 left of and 19.4 below the point
    hovered)`, `[108.7,224,98.7,22.7]`.
- **Quick info and Auto Data Tips, on both systems.** Each was hovered three times running: quick info in design mode
  (`count As Integer`), and Auto Data Tips paused at `Stop` (`count = 42`). All twelve reported their tip.
  - With the real cursor resting over the IDE's window, a second toolbar hover and a first Auto Data Tip reported no
    tip. Windows' real mouse moves put Avalonia's pointer back under the cursor, and the tip went with it, as it does
    for a person.
  - The reply can't say that was the cause: #2376.
- **The real mouse afterwards.** A harness moved the real cursor onto Add Form, then Add Project, then over the
  designer. Screen captures showed each button's tip and hover highlight, and nothing left behind once the cursor
  moved away.
- **A disabled toolbar button.** `hover` on `Standard.End`, and on `Standard.Break`, in design mode answered `hovered
  (11, 11) through the window's input; the target is disabled, so it takes no pointer input and shows a tip only when
  it sets ToolTip.ShowOnDisabled; no tip opened within 1500ms; declared tip: End` (and `Break`).

**Also found.** On Linux, the project named on the command line by its absolute path wasn't opened, because a
positional argument starting with `/` is taken for a flag: #2375.
