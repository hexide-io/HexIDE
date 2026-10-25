## A click with real input: where Click and DblClick fall (2026-10-22, #1364)

*Mouse events* above measured with synthetic messages, which raised no Click for a windowless Label, an Image, a
PictureBox or the form. So where Click falls for those, and whether the form raises one at all, needed real input.

Two `/make` probes, each built native and p-code, all four builds agreeing line for line. Each ran in the guest's
interactive console session and was driven with real input, `SetCursorPos` and `mouse_event`, so Windows did its own
hit-testing, capture and double-click detection.
- The double-click time was 500 ms, and the rectangle 4 by 4 pixels.
- A press lasted 40 to 50 ms, the two clicks of a double click were 80 ms apart, and sequences 800 ms apart.

The first probe is a twips form holding Command1. The second adds a windowless Label, an Image with no picture, a
PictureBox, a TextBox and a ListBox with three items. Every handler appends its name and arguments to a file.

**The form**, pressed on its own area, over no control, unless the row says otherwise:

| Input | VB6 |
|---|---|
| left click | MouseDown `1`, MouseUp `1`, **Click** |
| right click, and middle click | the same with Button `2`, and `4`: **Click** both times |
| left double click | MouseDown, MouseUp, Click, **DblClick**, MouseUp |
| right double click | the same, with Button `2` |
| triple click | as the double click, then MouseDown, MouseUp, Click |
| press, move 50 pixels across the form, release | MouseDown, MouseUp at the release point, Click |
| press, release outside the window | MouseDown, MouseUp at X `7800`, past the client's 6000; no Click |
| press, release over Command1 | MouseDown, MouseUp in the form's coordinates (`600 360`); no Click, and nothing on Command1 |
| press, release over the windowless Label1 | MouseDown, MouseUp in the form's coordinates (`600 1050`); no Click, and nothing on Label1 |
| press on Command1, release on the form | Command1 MouseDown and MouseUp; no Click anywhere, and nothing on the form |

**The controls:**

| Input | VB6 |
|---|---|
| click on Label1, on Image1, on Picture1, on Text1 | MouseDown, MouseUp, Click |
| click on List1's first item | MouseDown, **Click**, MouseUp |
| double click on Label1, on Image1, on Picture1, on Text1 | MouseDown, MouseUp, Click, DblClick, MouseUp |
| double click on List1 | MouseDown, Click, MouseUp, DblClick, MouseUp |
| press on Label1, release on the form | Label1 MouseDown, MouseUp from Label1's corner (`4380 900`); no Click on either |
| press on Picture1, release on the form | Picture1 MouseDown, MouseUp; no Click on either |

What these show:
- **A form raises Click on the release, after MouseUp, for every button.** A CommandButton raises it for the left
  button alone (*Mouse events*), so the form does not follow it.
- **A click has to start and end on one object's own area.** MouseUp goes to the object the press was on, in its
  coordinates, wherever the release is. Click comes only when the release is over that object again: over a control
  on it, windowless or not, or outside the window, there is none. A move within the object does not matter.
- **In a double click, Click comes with the first release and DblClick with the second press.** That press raises no
  MouseDown, and its release no Click. A third press starts a new click.
- **A ListBox raises Click before MouseUp,** as a CommandButton and a CheckBox do. A TextBox, a PictureBox, a Label, an
  Image and the form raise it after.
- **This answers the question *Mouse events* left open.** Synthetic messages raised no Click on a windowless Label, an
  Image, a PictureBox or the form because VB6 raises their Click from real input, and from where the cursor is, not
  from the message.

**And the controls with no DblClick, or a different one** (2026-10-22, #1031). Two more probes the same way, both
built native and p-code, all four builds agreeing line for line. The third holds Command1, an empty Frame1, Option1 and
Check1. The fourth holds Option1 and Option2, neither selected when the form loads.

| Input | VB6 |
|---|---|
| click on Frame1, inside it | MouseDown, MouseUp, Click |
| double click on Frame1 | MouseDown, MouseUp, Click, DblClick, MouseUp |
| double click on Command1 | MouseDown, Click, MouseUp, then the same again: two clicks, no DblClick |
| double click on Check1 | MouseDown, Click, MouseUp, twice; `Value` 1, then 0, then 1 |
| click on Option1, not selected | MouseDown, **Click** (`Value` True), MouseUp |
| double click on Option1, not selected | MouseDown, Click, MouseUp, **MouseDown, DblClick**, MouseUp |
| double click on Option1, already selected | MouseDown, MouseUp, MouseDown, DblClick, MouseUp: no Click |

What these add:
- **A CommandButton and a CheckBox have no DblClick,** and a double click on either is two whole clicks, as VB6.OLB's
  event lists say.
- **A Frame double-clicks as a Label does.**
- **An OptionButton's second press raises MouseDown, then DblClick.** Every other control with a DblClick raises it in
  place of that MouseDown.
- **An OptionButton raises Click only when it becomes selected,** before MouseUp, as a CheckBox does. A click on one
  already selected raises none.

**And a press released somewhere else** (2026-10-22, #1373). One more probe the same way, native and p-code agreeing
line for line, holding a windowless Label1, an Image1 with no picture, a TextBox Text1, a Frame1, a PictureBox Picture1
and a ListBox List1 with three items. Each was clicked, then pressed and released on the form's empty area:

| Input | VB6 |
|---|---|
| click on Label1, Image1, Text1, Frame1, Picture1 | MouseDown, MouseUp, Click |
| press on Label1, Image1, Frame1 or Picture1, release on the form | MouseDown, MouseUp from the control's origin (Label1 `5580 2430`); **no Click** |
| press on Text1, release on the form | MouseDown, MouseUp (`5550 1080`), **Click** |
| press on List1's first item, release on the form | MouseDown, **Click**, MouseUp (`2070 2400`) |
| press on Label1, move onto the form, back onto Label1, release | MouseDown, MouseUp, Click |
| press on Text1, move onto the form, back onto Text1, release | MouseDown, MouseUp, Click |
| press on Label1, release on Text1 | Label1 MouseDown, Label1 MouseUp (`480 1470`); no Click, and nothing on Text1 |

What these add:
- **A Label, an Image, a Frame and a PictureBox raise Click only for a release over them**, whether or not the pointer
  left them on the way, as the form does.
- **A TextBox and a ListBox raise Click wherever the release lands.** The obvious guess, that a TextBox checks as a
  Label does, was wrong. Both are controls that keep the pointer for a drag of their own, a text selection or a
  list selection, which may be why, but that is a guess.
- **MouseUp always goes to the pressed control,** in its own coordinates.

### HexIDE

Before #1364, nothing raised the form's own Click.

Since #1364, the form raises Click on the release, after MouseUp, for the left, right and middle buttons.
- **Where:** only when the press was on the form's own area and the release is over it again. The release is
  hit-tested where it is, since the pointer stays captured by the form. So a release over a control, or outside the
  client, gives the form its MouseUp and no Click.
- **Double click:** the press's click count leaves out the Click VB6 does not raise on a double click's second release.
  A third click is a new Click.
- **Pinned** in `MouseEventTests`, with real headless pointer input, from the form's rows above.

Since #1031, DblClick is raised on a double click's second press, by the press's click count, on the form and on each
control VB6 gives one: a Label, an Image, a PictureBox, a TextBox, a Frame, a ListBox and an OptionButton.
- **In place of that press's MouseDown,** and its release raises no Click. An OptionButton raises MouseDown, then
  DblClick, as measured.
- **A CommandButton and a CheckBox** raise none, so a double click on either stays two clicks.
- **The code window** offers DblClick wherever it is declared.
- **Pinned** in `MouseEventTests`: the form with a third click after, a Label, a TextBox, a Frame, an OptionButton, a
  CommandButton, and a control array's Index.

What it still does not do:
- **A ComboBox and a FileListBox** have a DblClick in VB6, and HexIDE has neither control's events yet.
- **A press on a Label, an Image, a PictureBox or a TextBox** raises that control's Click wherever the release lands.
  Only a CommandButton, through Avalonia's own Button, checks (#1373). Since #1373, a Label, an Image, a Frame and a
  PictureBox raise it only for a release over them: `AttachClick` with `ClickRelease.OverTheControl` hit-tests the
  release through the window, since the pressed control has the pointer. A TextBox keeps `Anywhere`, as measured, and
  so does a ListBox (*A ListBox's Click* below). Pinned in `MouseEventTests`, one case per row of *And a press released
  somewhere else*.
- **A ListBox raises Click after MouseUp** (#1374). Since #1374 it raises it before: *A ListBox's Click* below.

