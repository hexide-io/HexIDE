## A form's StartUpPosition when its file has no line (2026-10-13, #932)

**The obvious guess, and HexIDE's for as long as it had the property: a missing `StartUpPosition` line means 3,
Windows Default.** That is the value VB6 gives a new form, so it looked like the default. It is not what VB6 reads.
A missing line is 0, Manual. HexIDE's one default had been doing two jobs that VB6 keeps apart: the value a new form
gets, and the meaning of a line that is not there.

**The corpus first.** Of the 20 `.frm` files in VB6's Template tree, read on the oracle machine, 13 have no
`StartUpPosition` line, and all 13 carry explicit `ClientLeft`/`ClientTop`, which is the Manual shape: About Dialog
(2340/1935), Tip of the Day (2370/2400), and the six Menus forms (ClientLeft -9996). Of the other seven, five write 2
(CenterScreen) and two write 3 (Windows Default). The new project's `Template/Projects/Form1.frm` writes
`ClientLeft = 60`, `ClientTop = 348`, `LinkTopic = "Form1"` and `StartUpPosition = 3 'Windows Default` explicitly.

**The probe.** VB6.EXE 6.00.8176, the original release, `/make` (native code), the exe run headless in session 0, where the screen is
15360 x 11520 twips at 15 twips a pixel. A `Sub Main` project with six forms. Each designer block is HexIDE's, byte
for byte, plus the lines below, and each form carries the usual five `Attribute` lines:

| Form | Added to HexIDE's block |
|---|---|
| FA | nothing (what HexIDE wrote) |
| FB | `StartUpPosition = 3 'Windows Default` |
| FC | `StartUpPosition = 0 'Manual` |
| FD | `ClientLeft = 2000` and `ClientTop = 1500`, no `StartUpPosition` |
| FE | FD's two lines, plus `StartUpPosition = 0` |
| FF | `StartUpPosition = 2 'CenterScreen` |

For each form, `Sub Main` reads `o.StartUpPosition` late-bound, which loads it, then Left and Top, then calls `Show`
and `DoEvents` and reads Left, Top, Width and Height. Output, verbatim:

```
Screen 15360 11520 15
FA SUP=0 SUPErr=0 loadL=-60 loadT=-345 ShowErr=0 L=-60 T=-345 W=9720 H=7605
FB SUP=3 SUPErr=0 loadL=1320 loadT=1320 ShowErr=0 L=1320 T=1320 W=9720 H=7605
FC SUP=0 SUPErr=0 loadL=-60 loadT=-345 ShowErr=0 L=-60 T=-345 W=9720 H=7605
FD SUP=0 SUPErr=0 loadL=1935 loadT=1155 ShowErr=0 L=1935 T=1155 W=9720 H=7605
FE SUP=0 SUPErr=0 loadL=1935 loadT=1155 ShowErr=0 L=1935 T=1155 W=9720 H=7605
FF SUP=2 SUPErr=0 loadL=-60 loadT=-345 ShowErr=0 L=2820 T=1950 W=9720 H=7605
```

- **A missing line reads back as 0.** FA equals FC, and FD equals FE.
- **Under Manual, `ClientLeft`/`ClientTop` place the window** (FD). With neither (FA), the client area lands at (0,0)
  and the outer rectangle at (-60,-345), so the caption is above the top of the screen. The -60/-345 frame offset is
  #104's client-to-outer measurement.
- **Under 3 they are ignored**, and the form cascades (FB, at 1320,1320).
- Under 2 the form is placed only when shown (FF): Left and Top read the Manual origin until then.

So a form HexIDE created before this, once given the `Attribute` block it also lacked (#516), was opened by VB6 at
the screen's origin, caption off the top, because HexIDE wrote no line and meant 3. The reverse went wrong too: VB6 writes no line for a Manual form, which HexIDE ran
centred and showed as "3 - Windows Default" in the grid. That half is from reading the code, not run.

**In HexIDE (#932):** the property's default is 0, Manual, so a form with no line reads and runs as VB6 reads it, and is
written back with no line. A form HexIDE creates (Add Form, the Project Explorer's Add Form, a new Standard EXE) is
given `StartUpPosition = 3` explicitly, which the writer then emits as VB6 does. A new UserControl or PropertyPage is
not: no VB6-authored `.ctl` or `.pag` in the corpus carries the line, and whether vb6.exe refuses one there is not
measured. Forms HexIDE saved earlier, with no line, now read as Manual, which is what VB6 already did with them. Not
reproduced: Windows Default's cascade, which HexIDE still runs as CenterScreen (`VBStartupPosition.ToAvalonia`).

