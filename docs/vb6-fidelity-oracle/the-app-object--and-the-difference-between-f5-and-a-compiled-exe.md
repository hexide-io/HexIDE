## The `App` object — and the difference between F5 and a compiled exe (2026-09-01, #136)

One probe project, three deliberately different `.vbp` keys — `Name="NameKey"`, `Title="TitleKey"`,
`ExeName32="ExeNameKey.exe"`, saved as `DesignTime.vbp` — so every answer names its own source instead of
identical strings agreeing by coincidence. Run twice: compiled with `/make`, and under **F5 in the IDE**.

| | compiled `.exe` | design time (F5) |
|---|---|---|
| `App.Title` | `TitleKey"` — the `Title=` key | `TitleKey"` — same |
| `App.EXEName` | `ExeNameKey` — the exe's filename, no extension | **`DesignTime` — the `.vbp` filename** |
| `App.Path` | the exe's folder | the project's folder |
| `App.ProductName` | follows `Title` | **empty** |
| `App.PrevInstance` | `False` | `False` |
| `App.Major` / `Minor` / `Revision` | `1` / `0` / `0` | same |
| `CompanyName`, `FileDescription`, `LegalCopyright`, `Comments` | empty when the version keys are unset | same |

**`App.EXEName` at design time is the `.vbp` filename** — not `ExeName32`, and not the project `Name`.
Both of those were present and different, and neither is what came back. This is the row worth having
measured: guessing "the project name" is the obvious answer and it is wrong.

**`App.ProductName` is empty at design time** but follows `Title` once compiled — it is read from the
version resource, which only a built exe has.

### Re-measured: the fallback is the `.vbp` file's name (2026-10-27, #2413)

The September probe returned `verify` with `Name="verify"` and no `Title=`, but did not
establish a distinction between the project name and its filename. The September closing-quote
observations in the table above were also not reproduced by these later probes; they are superseded.

Issue #2413 records vb6.exe SP6 probes compiled as p-code, saved as `probe.vbp`:

| Project keys | `App.Title` | `App.EXEName` |
|---|---|---|
| `Name="Alpha"`, `ExeName32="beta.exe"`, no `Title=` | `probe` | `beta` |
| `Title="TitleKey"`, `ExeName32="ExeNameKey.exe"`, `Name="NameKey"` | `TitleKey` | `ExeNameKey` |
| `Title="Titled App"`, `Title=Unquoted`, or final-line `Title="LastKey"` | `Titled App`, `Unquoted`, or `LastKey`, respectively | `probe` |

The later issue comment records runs on the guest console using `vb6.exe /run`:

| Project file and keys | `App.Title` | `App.EXEName` | `App.ProductName` |
|---|---|---|---|
| `probe.vbp`, `Name="Alpha"`, `ExeName32="beta.exe"`, no `Title=` | `probe` | `probe` | empty |
| `Gamma.vbp`, same keys | `Gamma` | `Gamma` | empty |
| `probe.vbp`, `Title="TitleKey"` | `TitleKey` | `probe` | empty |

Thus the absent-title fallback is the project filename without extension in both modes.
Both remove the closing quote; HexIDE's quote stripping matches these measurements and is
no longer recorded as a deliberate divergence. Unsaved projects were not measured.

### Note on method

`scripts/vb6-oracle.ps1` covers the compiled column; the design-time column cannot be automated from here.
A `MsgBox` is modal, so a probe that opens one never writes its file unless something answers it, and reading a
caption from outside does not work either — a PowerShell Direct session cannot see the guest's interactive desktop and
enumerates zero windows session-wide. The F5 column was produced by pushing the project into the VM with
`Copy-Item -ToSession`, having a human press F5, and reading the output file back over the same session.
That is the pattern for any future design-time question.

**A compiled probe can answer its own `MsgBox` (2026-10-12).** A form `Timer` keeps firing while a `MsgBox` is
open in a compiled program. So the probe's own timer can find the box with `FindWindow`, read which buttons it
holds with `GetDlgItem`, and close or answer it with `PostMessage`. That needs no person and no view of the
desktop. It is how "RGB, QBColor and MsgBox's style" was measured, and its probe is the template. Only the F5
column still needs a person.
