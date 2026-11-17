## The VB6 IDE's own windows: the designer's Tab, dialog keys and run mode (2026-11-16, IDE, SP6)

Six questions about VB6's IDE itself rather than about a program's output, asked for the chrome-polish line (#3293).
They were all read in one session on vb6.exe SP6's IDE, in the oracle guest's console session.

**How they were read.** The guest's logon agent ran each step in the console session. Keys went in with
`SendKeys`, and only where the step named a window was it brought forward first. After each step:
- the visible top-level windows were listed in z-order, with owner, class and enabled state, and the foreground window
  named;
- a dialog's controls were read through Win32 as raw window text, so an `&` survives, in creation (tab) order, with
  each one's tab-stop style and a check box's state from `BM_GETCHECK`;
- the keyboard focus was read by attaching to the IDE's input thread;
- list and combo rows were read with `LB_GETTEXT` and `CB_GETLBTEXT`;
- the designer's selection was read from screenshots of the Properties window's object box and the toolbar's
  position and size.

UI Automation names, which drop the `&`, weren't used for keys.

### Tab in the form designer (2026-11-16, #3399, design-time, IDE, SP6)

**The setup:** a form with three controls whose TabIndex differs from their creation order (the order of their blocks
in the `.frm`): Command1 created first with TabIndex 2, Text1 second with 0, Check1 third with 1. The designer was
opened from the Project Explorer with Shift+F7, with the form selected.

| From | Key | Selected |
|---|---|---|
| the form (no control selected) | Tab | **Text1**, TabIndex 0 |
| Text1 | Tab | Check1 (1) |
| Check1 | Tab | Command1 (2) |
| Command1 | Tab | **Text1**: it wraps, and the form isn't in the cycle |
| Text1 | Shift+Tab | **Command1**: it wraps backwards |
| Command1 | Shift+Tab | Check1 |
| Check1 | Shift+Tab | Text1 |
| a control | Esc | the form |
| the form | Shift+Tab | **Command1**, the highest TabIndex |

- **Order:** TabIndex, not creation order. Command1, created first, came last.
- **The keyboard focus never left the designer.** The foreground window stayed the IDE's, and the focused element stayed
  the designer's form window (`ThunderForm`). Nothing on the designer's title bar is a stop.
- **Not measured:** TabStop False, invisible or disabled controls, controls inside a Frame or PictureBox, and a
  multiple selection.

### Find and Replace: their access keys (2026-11-16, #2738, IDE, SP6)

Edit → Find (Ctrl+F) and Edit → Replace (Ctrl+H), opened from a code window. Where a caption's key moves the focus,
that was checked by sending Alt+key and reading which control then held the focus.

| Control, in tab order | Find | Replace |
|---|---|---|
| label, then combo | `&Find What:` (F focuses the combo's edit) | the same |
| label, then combo | | `Replace &With:` (W focuses its combo's edit) |
| group, with options | `Search`, with `Current &Procedure`, `Current &Module` (the tab stop), `&Current Project`, `Selected &Text` (disabled with no selection) | the same |
| label, then combo | `&Direction:` (D focuses the combo, which reads All) | the same |
| check boxes | `Find Whole Word &Only`, `Match Ca&se`, `&Use Pattern Matching` | the same |
| buttons | `Find &Next` (default, disabled until Find What has text), `Cancel`, `&Replace...`, `&Help` | `Find &Next`, `Cancel`, `&Replace`, `Replace &All` (both disabled until Find What has text), `&Help` |

- **No key is used twice in either dialog.**
- **An Alt+key on a check box** both focuses it and toggles it.
- **Cancel has no key;** Esc closes both dialogs.
- **Not measured:** the dialogs with a selection in the code window, and Alt+key on a disabled button.

### The References dialog's title (2026-11-16, #1468, IDE, SP6)

| Project | Its Name | Title |
|---|---|---|
| a new Standard EXE, never saved | Project1 | `References - Project1` |
| the same, Name changed in the Properties window, still unsaved | Renamed | `References - Renamed` |
| the same project, saved as `Other.vbp` | Renamed | `References - Other.vbp` |

**The rule:** the project file's name, with its extension, once the project has been saved, and the project's Name
until then. The `Other.vbp` row is the one that tells this apart from "Name + .vbp".

- **The main window** shows the Name both ways: "Renamed - Microsoft Visual Basic [design]".
- **OLE Automation's details** read `Language: Standard`.
- **Not measured:** the Language line for a library registered under a non-English LCID. None is registered on this
  VM: every `TypeLib\{guid}\<version>\<lcid>` key under HKCR and the WOW6432Node classes is 0, 9 or 409. A registry
  stand-in wouldn't tell whether VB6 reads the key or the library's own `LIBATTR.lcid`.

### The Menu Editor: its blank item, Next and the Shortcut list (2026-11-16, #4141, design-time, IDE, SP6)

Tools → Menu Editor (Ctrl+E) on a new form with no menus. Indent shows in the item list as four U+00B7 (middle dot)
per level, written `····` here.

- **Empty:**
  - the list holds one blank row, and it's selected;
  - the Caption box has the focus;
  - Enabled and Visible are checked, and Checked and WindowList aren't;
  - all four arrow buttons are enabled.
- **Typing a Caption** makes that row an item, and a new blank row appears below it straight away.

| Step | Rows afterwards | Selected |
|---|---|---|
| typed `&File` | `&File`, `''` | 0 |
| Next | the same: no row added | 1, the blank row |
| → (Alt+R) | `&File`, `····` | 1; the focus moves to the → button |
| Alt+P, typed `&Open` | `&File`, `····&Open`, `''` | 1 |
| Next | `&File`, `····&Open`, `····` | 2: **the blank row takes the indent of the item Next left** |
| typed `&Save`, then Next | …, `····&Save`, `····` | 3 |
| Next, on that blank row | unchanged | **0**: it wraps to the first item, and no row is added |

So the editor keeps exactly one blank row after the last item. Next moves onto it and gives it the indent of the item
it came from.

- **The Shortcut list** has 76 entries:
  - `(None)` first, which a new item selects;
  - `Ctrl+A` to `Ctrl+Z`;
  - `F1` to `F9`, `F11` and `F12`, with **no F10**;
  - the same eleven with `Ctrl+`, then with `Shift+`, then with `Shift+Ctrl+`;
  - last, `Ctrl+Ins`, `Shift+Ins`, `Del`, `Shift+Del` and `Alt+Bksp`.
- **NegotiatePosition** holds `0 - None`, `1 - Left`, `2 - Middle` and `3 - Right`.
- **Esc does nothing:** the dialog stays open. Its Cancel button's control ID is 4522, not IDCANCEL (2), which is
  likely why (reasoned, not checked).
- **Alt+F4 closes it and discards the edits, as Cancel does.** Reopened, it was empty again.

### The Menu Editor's access keys and Tab order (2026-11-16, #4142, design-time, IDE, SP6)

The Tab order comes from 21 presses of Tab from the Caption box. Each label's key was checked with Alt+key.

| Tab order | Control | Caption | Key |
|---|---|---|---|
| 1 | edit | `Ca&ption:` | P focuses it |
| 2 | edit | `Na&me:` | M |
| 3 | edit | `Inde&x:` | X |
| 4 | combo | `&Shortcut:` | S |
| 5 | edit | `&HelpContextID:` | H |
| 6 | combo | `Neg&otiatePosition:` | O |
| 7 to 10 | check boxes | `&Checked`, `&Enabled`, `&Visible`, `&WindowList` | C, E, V, W |
| 11 to 14 | ← → ↑ ↓ (owner-drawn) | `&L`, `&R`, `&U`, `&B` | L, R, U, **B**, since T is Delete's |
| 15 to 17 | buttons | `&Next` (default), `&Insert`, `Dele&te` | N, I, T |
| 18 | the item list | | |
| 19, 20 | buttons | `OK`, `Cancel` | none |
| 21 | back to Caption | | |

- **Every control is a tab stop except the six labels,** including the arrow buttons and the item list.
- **No key is used twice.**

### Which window an IDE dialog belongs to while a program runs (2026-11-16, #1297, IDE, SP6)

**The setup:** a Standard EXE whose one form starts a Timer that raises `Error 5`, run with F5 so the form is shown
and active. The IDE was maximised.

- **A run-time error with the form active.**
  - The error dialog ("Run-time error '5': …", with Continue disabled and Debug as the default) is **owned by the
    running form**. It's the foreground, and on top.
  - **The IDE isn't brought forward.** Top to bottom: the dialog, the form, the program's hidden `ThunderMain`, then the
    IDE's windows.
  - While the dialog is up, the form, `ThunderMain`, the IDE's main window and its owner are all **disabled**.
- **Debug:** the IDE comes forward in [break] and is the foreground. The form stays shown behind it, and enabled.
- **Ctrl+Break with the form active:** the IDE comes forward in [break], with no dialog. The form stays shown behind
  it.
- **F5 (Continue) from break:** the form is the foreground again.
- **Ctrl+F and Ctrl+G with the form active did nothing.** No dialog opened, the IDE stayed behind, and the form kept
  the foreground. Of the keys tried, only Ctrl+Break reached the IDE from the running form.
- **Not measured:**
  - the IDE not maximised;
  - a modal form shown by the program;
  - the error raised while the IDE was active;
  - other shortcuts.

**What these overturn.** Each of the six issues had HexIDE's behaviour and an expectation "as remembered", with no
measurement. Two of the expectations held: Tab in TabIndex order inside the form (#3399), and VB6 using each access
key once (#2738). The rest is new rather than overturned:
- a saved project titles References by its file, an unsaved one by its Name;
- the Menu Editor keeps one trailing blank row, and Next wraps from it;
- Esc does nothing in the Menu Editor;
- a run-time error's dialog belongs to the running form, not the IDE.
