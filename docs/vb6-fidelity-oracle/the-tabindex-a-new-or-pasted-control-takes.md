## The TabIndex a new or pasted control takes (2026-10-22, #925, design-time)

**The question.** HexIDE gave a new control the highest TabIndex of every component plus one, the Form's and a
Timer's included, and a paste kept its source's. VB6's own files show what it saves (every Label, Frame and focusable
control numbered densely from 0; no Timer, Image, Line or Shape numbered), but not what its designer hands out as a form
is built, nor what a paste takes.

**The probe.** A form holding Label1 (0), Command1 (1), Frame1 (2) with Check1 inside (3), and Timer1, opened in VB6's
IDE through the console agent. These steps were taken in order: double-click the CommandButton tool, the Timer tool,
then the CommandButton tool again; copy Command1 and paste it, answering No to "create a control array?"; copy Frame1
and paste it, answering No twice, once for the Frame and once for its CheckBox; then File > Save Form1.frm. The values
below are VB6's saved file.

| Control | How it arrived | TabIndex |
|---|---|---|
| Command2 | toolbox double-click | 4 |
| Timer2 | toolbox double-click | none |
| Command3 | toolbox double-click, after the Timer | 5 |
| Command4 | paste of Command1 (TabIndex 1) | 6 |
| Frame2 | paste of Frame1 (TabIndex 2) | 7 |
| Check2 | inside the pasted Frame2, from Check1 (TabIndex 3) | 8 |

**What it shows:**
- **A new control takes the next number after the highest.** A Timer takes none and uses none up. On a form numbered
  densely, as this one was, "the next after the highest" and "the count of numbered controls" agree; a form with gaps
  was not tried, and VB6 renumbers those when it loads one (`docs/vb6-designer-geometry.md`).
- **A pasted control takes a new number, not its source's.** A pasted container takes one, and then what it holds
  takes the ones after.
- **Two things outside #925, recorded so they are not lost:**
  - VB6 pasted both copies at the form's top-left, `Left = 0`, `Top = 0`, not offset from the original as HexIDE
    pastes (#1480).
  - VB6 wrote every control added in the session ahead of the ones the file already held, the newest first: Frame2,
    Command4, Command3, Timer2, Command2, then Frame1 and the rest as read. HexIDE appends a new control's block last
    (#1481).

## What a form's saved TabIndexes become when it loads (2026-11-19, #4843, SP6, run time)

**The question.** A form saved with gaps or repeats in its TabIndexes doesn't keep them. `docs/vb6-designer-geometry.md`
read `0,0,9,9,4,(absent)` back as `0,1,2,3,4,5`. The [SSTab page](in-box-controls-the-sstab.md)'s IDE re-save ("TabIndex is
renumbered in the saved file") proposed a rule from that and one more form, and checked it against those two only:
- each in-range TabIndex keeps its place;
- a repeated one takes the next place free;
- the out-of-range ones fill the remaining places in file order.

The same two forms also fit "renumber every control in file order". Which is it, and what is "the next place free"?

**The probe.**
- **Seven forms in one program,** each holding three or four TextBoxes, Text1 onwards in that file order, with the
  TabIndex values below saved in the `.frm`.
- **Each form's `Form_Load` logged its TabIndex values.** Form1 loaded and unloaded the others.
- **Run in session 0,** as a made exe. p-code and native code logged the same lines. This is a load at run time, not in
  the designer.

| Saved, in file order | Read at load | What it tells apart |
|---|---|---|
| 2, 0, 1 | 2, 0, 1 | kept as saved, so not renumbered in file order |
| 1, 1, 0 | 1, 2, 0 | the first of two 1s keeps it |
| 0, 5, 1 | 0, 2, 1 | an out-of-range value takes the free number |
| 0, 0, 1 | 0, 2, 1 | not "move each control to its saved place as it loads", which gives 2, 0, 1 |
| 9, 7, 0 | 1, 2, 0 | the controls without a place take the free numbers in file order, not by saved value |
| 2, 2, 0, 9 | 2, 1, 0, 3 | the repeated 2 takes 1, the smallest free number, not 3, the next free after 2 |
| 1, 1, 3, 9 | 1, 0, 3, 2 | the repeated 1 takes 0, again the smallest free number |

**What it shows:**
- **A control keeps its saved value when it's within 0..N−1 and no control earlier in the file holds it.**
- **Every other control, repeated or out of range alike, takes the numbers left free, smallest first, in file order.**
- **"The next place free" means the smallest free number, not the next one after the control's own value.** The
  natural reading of the SSTab page's rule is overturned by the last two rows. Its own rows can't tell the two apart.
- **The rule fits every row so far:** this table's seven, the geometry page's `0,0,9,9,4,(absent)`, and the SSTab page's
  IDE re-save. A control saved without a TabIndex appears only in the geometry page's row, where it took the last free
  number.
- **Not measured:**
  - the designer's own load: these rows are a made exe's, and the SSTab re-save is the only IDE row;
  - controls inside containers;
  - classes other than TextBox.

## Renaming a control to a name in use, and pasting a copied control (2026-11-21, #1397, #1480, design-time, IDE, SP6)

**The probe.** VB6.EXE 6.00.9782 (SP6) in a clone of the oracle VM, driven on its console through Hyper-V's synthetic
keyboard and mouse, with the screen read from the host's framebuffer.
- **The form:** five CommandButtons, Command1 to Command5, none with an Index; Frame1 holding Check1; and Text1.
- **A rename:** select a control, click the `(Name)` value in the Properties window, type the name, press Return.
- **A paste:** Ctrl+C on a control, select a container, Ctrl+V.

Each result was read from the saved `.frm`.

**A rename to a name in use (#1397).** Renaming Command2 to `Command1` showed a message box:
- titled "Microsoft Visual Basic", with the exclamation icon;
- reading `You already have a control named 'Command1'.  Do you want to create a control array?`, with two spaces after
  the first full stop;
- with the buttons Yes, which is the default, No and Help.

| What was done | What VB6 did |
|---|---|
| Answered Yes | The original Command1 got `Index = 0`, and the renamed button became `Command1` with `Index = 1`, keeping its caption `Command2`. Positions and TabIndex didn't change. |
| Renamed Command3 to `Command1`, now an array | **No message.** It became `Command1(2)`, `Index = 2`. |
| Renamed Command5 to `Command4`, and answered No | The rename was undone: the control stayed `Command5`, and no other message followed. |

**A paste (#1480).** Command4 was copied, then pasted three times. Each paste showed the same message box, naming
`Command4`.

| Selected before Ctrl+V | Answer | Where the copy landed | Name |
|---|---|---|---|
| Frame1 | No | **inside Frame1**, at Left 0, Top 0 | `Command2`, TabIndex 8 |
| the form, a second paste of the same copy | No | the form, at 0, 0 | `Command3`, TabIndex 9 |
| the form, a third paste | Yes | the form, at 0, 0 | `Command4`, `Index = 1`, TabIndex 10; the original became `Index = 0` |

- **A paste lands at the top-left of the selected container.** That holds whether the container is a Frame or the
  form. The second and third pastes, both onto the form, landed at 0, 0, one on top of the other.
- **Answering No gives the copy the lowest free default name.** That was `Command2` and then `Command3`, both free by
  then. The caption stays `Command4`.
- **The next TabIndex** goes to each copy.
- **Each pasted block is written first in its container.** The save listed `Command4(1)`, then `Command3`, before the
  original controls, and `Command2` before Check1 inside Frame1.

### Not measured

- **Help** in the message box.
- **A paste while a control that isn't a container is selected.**
- **A rename by typing in the code window,** or to a name that isn't a control's.

## Pasting a container whose OCX control's name is taken (2026-11-21, #1458, design-time, IDE, SP6)

**The question.** A pasted control whose name is already on the form gets the control-array question (above). Does a
control from an OCX, pasted inside its container, get the same treatment as an intrinsic one, or does VB6 rename it,
refuse it, or ask something else?

**The probe.** VB6.EXE 6.00.9782 (SP6) in a clone of the oracle VM, started elevated and driven on its console.
- **The project:** a Standard EXE referencing `MSCOMCTL.OCX` 2.0.
- **Its one form:** `Picture1`, a PictureBox holding a Label `lbl` and an `MSComctlLib.ImageList` named `imlIcons`.
- **The steps:** Picture1 selected with a click on its empty area, Ctrl+C, a click on the form, Ctrl+V. The project was
  run twice from the same file, answering every question No the first time and Yes the second. Each was saved with
  Ctrl+S.

**What VB6 asked.** Three questions, one after another, each `You already have a control named '<name>'.  Do you want
to create a control array?`, with the warning icon and Yes, No and Help:
1. for `Picture1`;
2. for `imlIcons`;
3. for `lbl`.

The ImageList got the same question as the intrinsic controls. Nothing was renamed or refused before the question was
answered.

**What a save wrote.** The pasted copy went to the form's top-left, `Left = 0` and `Top = 0`, with the next two
TabIndexes, 2 and 3.

| Answer | The pasted PictureBox | Its ImageList | Its Label |
|---|---|---|---|
| No to all | `Picture2` | `ImageList1` | `Label1`, Caption still `"lbl"` |
| Yes to all | `Picture1`, `Index = 1` | `imlIcons`, `Index = 1` | `lbl`, `Index = 1` |

- **With No,** each control takes the next default name for its class: `Picture2`, since `Picture1` was taken, and
  `ImageList1` and `Label1`. The ImageList is treated like the others.
- **With Yes,** each original gains `Index = 0` and each copy `Index = 1`.
- **Where `Index` goes differs:**
  - in the intrinsic controls' blocks, in its alphabetical place (`Height`, `Index`, `Left`);
  - in the ImageList's, first, before `Left` and `Top` and the ImageList's own properties.
- **The ImageList's block came before the Label's** in both containers. The hand-written file had the Label first, and
  VB6 also rewrote the original's order. That's the order the questions came in.

**What this doesn't answer.** VB6 had the ImageList's OCX loaded, so this doesn't show what a paste does with a control VB6
can't load. VB6 draws such a control as an empty box from the moment the form loads
([A control VB6 cannot load renders as an empty box](a-control-vb6-cannot-load-renders-as-an-empty-box.md)), and pasting
one wasn't tried.

### Not measured

- **A mixed answer,** such as Yes for the container and No for what it holds.
- **Cut and paste into another form,** #1458's second case.
- **The release build.**

## Mixed answers to the control-array questions (2026-11-21, #1458, design-time, IDE, SP6)

**The question.** Above, every question was answered the same way, No to all or Yes to all. Does an answer for the
container carry to what it holds?

**The probe.** The same form, a fresh copy of the same file, run in the same way. The three questions came in the same
order, `Picture1`, `imlIcons`, `lbl`, and were answered **Yes**, **No**, **No**.

**What a save wrote:**

| Control | Pasted copy | Original |
|---|---|---|
| the PictureBox | `Picture1`, `Index = 1` | `Picture1`, `Index = 0` |
| its ImageList | `ImageList1` | `imlIcons` |
| its Label | `Label1`, TabIndex 3 | `lbl` |

So each answer applies to its own control alone. A control array of containers can hold copies whose contents have
names of their own: `Picture1(1)` holds `ImageList1` and `Label1`, where `Picture1(0)` holds `imlIcons` and `lbl`.
