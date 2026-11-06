## List Members and Complete Word: Enter, Tab and Space (2026-11-04, #3056)

**The question.** When VB6's code window shows a completion list, either List Members after a `.` or Complete Word
after Ctrl+Space, what do Enter, Tab and Space do? Does each one insert the item, and does Enter also start a new
line? Asked for #3056, before HexIDE's Enter was changed to accept a completion item.

**The obvious guess was that Enter accepts the item, as Tab does. Enter accepts it and then breaks the line.** And
whether a key inserts anything at all depends on whether an item is *selected*, which is not the same as the list
being open. Tab inserts an item even when none is selected, and Enter and Space don't.

### The probe

VB6's IDE, SP6 (VB6.EXE 6.00.9782), on the Windows 10 oracle guest, in its interactive console session. The editor
options were the guest's own: Auto List Members was on, since the list opened by itself when `.` was typed, and Auto
Syntax Check was on, since its boxes appear below. The editor isn't compiled code, so native code and p-code don't
arise.

The project was a Standard EXE whose only module was this, with ten empty lines in `Sub Main`, one per case:

```vb
Attribute VB_Name = "Module1"
Option Explicit
Dim zeta1 As Long, zeta2 As Long, zebra As Long

Sub Main()










End Sub
```

Each case was typed on its own empty line inside `Sub Main`. The driver went from the bottom case up, so a line break
made by one case couldn't move a line still to come.

**How the result was read.** After the keys under test, the driver typed `q` as a sentinel where the caret had been
left. Then Shift+Home and Ctrl+C copied from the start of that line to the caret, without leaving the line, so nothing
VB6 does when it commits a line could change what was read. "Read back" below is that copy. The whole module was copied
with Ctrl+A and Ctrl+C at the end of each run, which gives the "Lines" column, and there was a screenshot after every
key. The screenshots show which item, if any, was selected, and the toolbar's `Ln`, `Col` readout shows where the caret
went.

### What VB6 did

"Selected" means the item was highlighted. "Outlined" means it had only a dotted focus rectangle and no highlight.

| Keys, in order | The list before the last key | Read back | Lines, as VB6 left them |
|---|---|---|---|
| `Debug.Pr` Enter | Print selected | `q`, on a new line | `Debug.Print`, then `q` |
| `Debug.Print` Enter | Print selected | `q`, on a new line | `Debug.Print`, then `q` |
| `Debug.Pr` Tab | Print selected | `Debug.Printq` | `Debug.Printq` |
| `Debug.Pr` Space | Print selected | `Debug.Print q` | `Debug.Print q` |
| `Debug.Pr` Esc | Print selected | `Debug.Prq` | `Debug.Prq` |
| `Debug.` Down Enter | Assert selected, after the Down | a syntax box, see below | `Debug.Assert`, then an empty line |
| `Debug.` Enter | nothing selected, Assert outlined | a syntax box, see below | `Debug.`, then an empty line |
| `Debug.` Tab | nothing selected, Assert outlined | `Debug.Assertq` | `Debug.Assertq` |
| `Debug.` Space | nothing selected, Assert outlined | `Debug. q` | `Debug. q` |
| `zz` Home `Debug.Pr` Enter, so the caret sits between `Pr` and `zz` | nothing selected, Assert outlined | a syntax box, see below | `Debug.Pr`, then `zz` |
| `zz` Home `Debug.Pr` Tab | nothing selected, Assert outlined | `Debug.Assertq` | `Debug.Assertq`, and the `zz` is gone |
| `zet` Ctrl+Space Enter | zeta1 selected | `q`, on a new line | `zeta1`, then `q` |
| `zet` Ctrl+Space Tab | zeta1 selected | `zeta1q` | `zeta1q` |
| `zet` Ctrl+Space Space | zeta1 selected | `zeta1 q` | `zeta1 q` |
| `zz` Home `zet` Ctrl+Space Enter, so the caret sits between `zet` and `zz` | nothing selected, zeta1 outlined | `q`, at the start of a new line | `zet`, then `qzz` |
| `zeb` Ctrl+Space | no list: `zebra` was completed at once | `zebraq` | `zebraq` |

**Enter's line break is a real one.** After `Debug.Pr` Enter, the line that had been below moved down one, and the
caret went to column 1 of a new empty line. The line Enter left was committed straight away. Three rows show it: Enter
raised Auto Syntax Check's box at once, before anything else was typed.
- On `Debug.Assert`, the box said "Compile error: Expected: expression".
- On `Debug.` and on `Debug.Pr`, it said "Compile error: Expected: Print or ? or Assert".

The probe raised other boxes of the same kind with its own sentinel. `Debug.Printq` and `Debug.Prq` are invalid, so
moving to the next case complained about them. Those say nothing about the keys.

### The rule, and the part that resists one

**With an item selected, List Members and Complete Word agree:**

- **Enter** inserts the item, then breaks the line.
- **Tab** inserts the item and nothing more.
- **Space** inserts the item followed by a space.
- **Esc** inserts nothing, closes the list, and leaves what was typed.

**With the list open and nothing selected, the keys part company:**

- **Enter** inserts nothing. The list closes, and Enter breaks the line at the caret, splitting it if there is text
  after the caret.
- **Space** inserts nothing. The list closes, and a space is typed.
- **Tab** inserts the outlined item anyway, and it replaces the whole word around the caret, characters after the caret
  included. `Debug.Pr`, caret, `zz` became `Debug.Assert`.

There is no single rule of the form "Tab and Enter both accept, and Enter adds a line break". They agree only while
something is selected.

**When nothing is selected.** Two situations were seen:
- **Nothing has been typed after the dot.** `Debug.` alone opens the list with Assert outlined and nothing selected.
  The first Down selects the outlined item, Assert, not the one below it.
- **The word around the caret matches no item.** The list matches the whole word the caret is in, including the
  characters after the caret. So `Debug.Pr` with `zz` after the caret is matched as `Przz`, which selects nothing, though
  `Pr` alone selects Print.

**Complete Word** on a prefix with several matches (`zet`: `zeta1` and `zeta2`) opens the whole list of names in
scope, not a filtered one. It is scrolled to the first match, which is selected. From there Enter, Tab and Space do what
they do in List Members. On a prefix with one match (`zeb`: only `zebra`) it completes at once, shows no list, and
leaves the caret after the word.

### Not measured

- A selected item with text after the caret. Every case here that had text after the caret selected nothing. A word
  that still matches with its tail counted, such as `Debug.Pri` with `nt` after the caret, wasn't tried.
- Indentation. Every case started in column 1, so whether the new line Enter makes takes the line's indent (Auto
  Indent) isn't shown.
- Ctrl+Enter, a double-click on an item, Quick Info and parameter lists, and the Immediate window.

### How it was driven

A script ran in the guest's console session through the long-lived agent described in [Reaching the VB6 *IDE*, not
just the compiler](README.md#reaching-the-vb6-ide-not-just-the-compiler-2026-09-20).
- Every key was a real key event (`keybd_event`). For a chord, Ctrl or Shift was pressed and held, the key tapped,
  and the modifier released last. SendKeys has dropped modifiers on this rig before.
- Before each case, the script checked that the foreground window belonged to the VB6 it had started, and that its
  title named `- [Module1 (Code)]`.
- Four runs were made:
  - the first stopped at its first case, on a fault in the script;
  - the second stopped at the box in item 3 below, after five cases;
  - the third ran ten of the rows above;
  - the fourth ran the other six, which are the three where nothing was typed after `Debug.` and the three with text
    after the caret.

  The second and third overlapped on five rows, the four Complete Word rows with an item selected and the Esc row. All
  five matched exactly.

Three things about the rig, for the next person:

1. **The Data View palette took the foreground at every launch** on this guest. Close it before sending keys, or they
   go to the palette.
2. **A `.vbw` line of `Module1 = 40, 40, 700, 500, Z` opened the code window, but not maximised.** The main title
   names the code window only when it is maximised, so click its maximise box once per launch, as
   [Member attributes](member-attributes-where-vb6-attaches-them-and-what-its-ide-writes.md#rig-shortcomings) found.
3. **A sentinel that leaves a line invalid raises Auto Syntax Check's box when the driver moves to another line.**
   Every key after that goes to the box. A driver should look for a message box after every key, log its text,
   dismiss it, and stop that case, rather than type on.

### In HexIDE

#3056 decides how HexIDE's completion list follows these rows. A snippet's choice picker has no VB6 counterpart, since
VB6 has no snippets.
