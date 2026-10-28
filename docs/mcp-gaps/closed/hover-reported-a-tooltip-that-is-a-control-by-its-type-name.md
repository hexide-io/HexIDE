# `hover` reported a tooltip that is a control by its type name — **CLOSED** (#2336, 2026-10-27)

**Symptom (2026-10-27, Windows, #2309's re-check).** `hover` on the designer's box for a carried control,
`…/List[ControlsContainer]/Pane[Toolbar1]`, answered `hovered (200, 22) through the window's input; tip:
Avalonia.Controls.StackPanel`. The box's tip is a StackPanel of three text blocks, the control's name, its class and
"Not supported in HexIDE" (`FormEditView.axaml`, `CarriedControlBox`). A person sees those; the reply read as an answer
and wasn't one.

**Cause.** `hover` read a tip by its `ToString`, for an open tip and a declared one alike, and a control's is its type's
name.

**Fixed (#2336).** `UiAutomationDriver.TipText` reads a string as it is, and a control by the text of its visible text
blocks, in order, joined with ` / `. Anything that gives only a type name is reported as having no text to read. The
fix's first live run answered `tip: a StackPanel with no text to read`: the tip counts as open a moment before its
blocks have bound their text, and the first poll caught it then. So a tip read with no text keeps `hover` polling, and
is reported only if no text comes before the dwell ends. `TipTextTests` covers the reading, and fails with control
tips read by `ToString` again. The poll is guarded only by the live run below.

**Live (2026-10-27, a Debug build of the fix on Windows, headless `claude -p` through the hexide tools).** The
project's Form1 held a Toolbar, which main carries. Three `hover`s on its designer box each answered `tip: Toolbar1 /
MSComctlLib.Toolbar / Not supported in HexIDE`.
