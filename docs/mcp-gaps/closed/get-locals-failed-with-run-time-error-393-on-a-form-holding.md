# `get_locals` failed with run-time error 393 on a form holding a Timer — **CLOSED** (#2289, 2026-10-27)

**Symptom (2026-10-27, Windows, #2281's live check).** Paused in any procedure of a form with a Timer, `get_locals {}`
answered `'get_locals' failed with an unhandled HexIDE.Runtime.Interpreter.VBRunTimeException: Run-time error '393': Get
not supported at runtime`. `get_locals {"maxDepth":1}` worked, because it doesn't expand `Me`. The Locals window
listed Timer1 under `Me` with no expander.

**Cause.** Not the tool's. The default depth of 3 expands `Me`, then Timer1, and a control expands to every property
`AvaloniaInteroperability.ReadProperties` can read. A Timer's Left and Top raise 393 when read, as VB6's do (#2069),
and the raise ended the whole read. The Locals window met the same throw when it asked for Timer1's children, and
showed no expander.

**Workaround.** `maxDepth` 1, which loses everything under `Me`.

**Fixed (#2289).** `ReadProperties` catches a raising read one property at a time. The Locals tree lists that property
with the raised error's own description in angle brackets and no type, and lists the rest as before. The angle brackets
are VB6's own convention for a listed member whose read raises (measured on #2289), though VB6 lists no Left or Top for a
Timer at all: which members Locals lists is #2329. Guarded by `ControlLocalsTests`, whose two new tests fail with the
catch removed.

**Live (2026-10-27, a Debug build of the fix on Windows, headless `claude -p` through the hexide tools).** The project
was a form holding Timer1, paused by a `Stop` in Form_Load.

- `get_locals {}` succeeded. Under `Me`, Timer1 listed 13 rows, Left and Top among them as
  `<Get not supported at runtime>` with an empty type, and Interval as `250`.
- In the Locals window, Timer1 had an expandCollapse provider, and expanding it showed the same rows.
- The same rows showed properties a VB6 Timer doesn't have, two of them reading `NaN`. That belongs to #801 and #2329, and
  is recorded on #801.
