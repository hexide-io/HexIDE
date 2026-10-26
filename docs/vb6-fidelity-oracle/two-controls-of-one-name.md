## Two controls of one name (2026-10-12, #854)

Measured when #854 was filed, and recorded here by its fix. The probe was a Standard EXE built with `vb6.exe /make`
(6.00.8176, the original release) from a form holding two `Begin` lines of one name and no `Index`, the shape HexIDE's save could write.
Its `Form_Load` wrote `Me.Controls.Count` and each control's Name and TypeName to a file.

**The build does not fail.**
- `/make` exits 0.
- Its `/out` log says `Errors during load. Refer to '...\Form1.log' for details`, then that the build succeeded.
- `Form1.log` holds `Line 48: Did not find an index property and control Data1 already exists.` The line number
  follows the file's layout.

**Both controls carrying the name are missing from the program,** the original as well as the second.
- A form of Timer1, Data2, Data1, and a TextBox renamed `Data1`: the program has 2 controls, Timer1 and Data2. With
  unique names it has 4.
- A CommandButton renamed `OLE1` beside an OLE control `OLE1`: `Me.Controls.Count` is 0, against 2. `TypeName(OLE1)` is
  "Empty", and `Action`, `DoVerb`, `CreateEmbed` and `Delete` on it each raise 424.

**Case does not matter.**
- `Begin VB.CommandButton ole1` beside `Begin VB.OLE OLE1` loses both.
- `Begin VB.TextBox timer1` after Timer1, Data2 and Data1 logs `control timer1 already exists`, and the program has
  only Data2 and Data1.

So vb6.exe compares control names without regard to case when it loads a form.

**What was and was not measured.** The controls before the second block survived, including those between the two
blocks. Each probe put the second block last, so what happens to controls after it is not measured. Opening, running
or re-saving such a form in the VB6 IDE was not measured either. The fuller table from the same survey is #856's to
record: a pair of CommandButtons that never finished building, and the OLE and Data controls' own rows.

HexIDE could write such a form. Its designer checked a new name only against the controls it models, which leaves out
a control kept as preserved text, and mostly with case. A rename, a control drawn from the toolbox and a paste now
check against every name on the form, without regard to case (`ComponentNaming.NamesInUse`).

