## The Components dialog and an in-box library's lines (2026-10-26, #1103, design-time)

**The probe.** VB6 SP6's IDE, driven on the oracle VM's console by keystrokes and photographed at each step. The project
held two forms:
- **Form1** had a ProgressBar, and so MSCOMCTL.OCX's lines in the `.vbp` and in its header.
- **Form2** had no control.

In the Components dialog (Ctrl+T), "Microsoft Windows Common Controls 5.0 (SP2)" was checked, and the project saved with
Save Project. Form2's Caption was then changed and the form saved with Ctrl+S. Last, "Microsoft Windows Common Controls
6.0 (SP6)" was unchecked, which Form1 uses.

**The `.vbp` line.** Checking 5.0 and saving wrote:

```text
Type=Exe
Object={831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0; MSCOMCTL.OCX
Form=Form1.frm
Form=Form2.frm
Object={6B7E6392-850A-101B-AFC0-4210102A8DA7}#1.3#0; COMCTL32.OCX
Startup="Form1"
```

The new line went **after the last `Form=` line**, not beside the `Object=` line already there after `Type=`, which
stayed where it was. A project file VB6 writes from scratch puts its `Object=` lines after `Type=`, so where a line lands
depends on whether it was read or added. The obvious guess, that VB6 regroups its `Object=` lines on save, is wrong.

**A form's header.** Form2 was saved after both libraries were referenced, and its file has no `Object =` line. A form
names a library in its header only once it holds one of its controls; the project referencing it is not enough. Form1's
header, which VB6 had written when its ProgressBar was placed, reads
`Object = "{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0"; "MSCOMCTL.OCX"`. The same form of line for COMCTL32.OCX is in
`corpus/inbox-controls`, written by vb6.exe: `Object = "{6B7E6392-850A-101B-AFC0-4210102A8DA7}#1.3#0"; "COMCTL32.OCX"`.

**The descriptions.** The dialog lists the two libraries as "Microsoft Windows Common Controls 6.0 (SP6)" and "Microsoft
Windows Common Controls 5.0 (SP2)". Those are the default values of the type libraries' registry keys,
`HKCR\TypeLib\{guid}\2.0` and `\1.3`, character for character. The dialog adds nothing, and the service pack is part of
the registry's text, so a machine with another service pack installed would show another suffix.

**Unchecking a library in use.** Refused **at once, when the box is unchecked**, not at OK or Apply:

| | |
|---|---|
| Title | Microsoft Visual Basic |
| Text | Can't remove control or reference; in use |
| Icon | Warning |
| Buttons | OK, Help |

After OK, the box is checked again and the dialog stays open.

**In HexIDE.** The Components dialog lists `ControlLibraries.All` under these descriptions, checked where the project
references them. Any other library the project's `Object=` lines name is listed, checked, and can't be changed there.
Checking one adds its line, which the project writer puts after the last `Form=` line. Unchecking one a form uses is
refused with VB6's text, and the box goes back. The toolbox offers each referenced library's ProgressBar, and placing
one adds the library's header line to its form if the form has none.

**Not measured:**
- whether VB6 drops a form's header line once the form's last control of that library is deleted;
- the order of two `Object=` lines added in one Apply.
