## Default and Cancel buttons (2026-11-17, #4371)

**The question.** How do a CommandButton's `Default` and `Cancel` behave at run time? Can a program read and set them,
can more than one button hold them, and what do Enter and Esc do when the button that holds one is disabled or hidden?
Which button wins when a `.frm` names two? Where does the thick default border sit as the focus moves?

The key routing with a Default and a Cancel button present, and with neither, is on
[the key-events page](key-events-keydown-keypress-and-keyup.md). This page adds the property's own behaviour.

**The probe:**
- **The software:** VB6 SP6 (6.00.9782), on Windows 10, in the en-GB user locale.
- **The form:** Text1 (TabIndex 0), then Command1 "OK" (1, `Default = -1 'True` in the `.frm`), Command2 "Cancel"
  (2, `Cancel = -1 'True`) and Command3 "Three" (3, neither), and a Timer that ends the program after four seconds.
- **The log:** Text1's KeyDown, KeyPress, KeyUp and Change, and each button's Click, written to a file. `St` writes
  every button's `Default` and `Cancel`.
- **The modes**, from `Trim$(Command$)` in `Form_Load`, under `On Error Resume Next` with `Err.Number` logged:

  | Mode | What `Form_Load` does |
  |---|---|
  | read | nothing |
  | move | `Command3.Default = True`, then `Command3.Cancel = True` |
  | off | `Command1.Default = False` and `Command2.Cancel = False` |
  | disabled | `Command1.Enabled = False` and `Command2.Enabled = False` |
  | hidden | `Command1.Visible = False` and `Command2.Visible = False` |
  | both | `Command1.Cancel = True` |

- **The input:** a driver on the guest's console started the program, activated it, and typed Enter, then Esc, with
  SendKeys, Text1 holding the focus.
- **The builds:** native code and p-code logged the same lines in every mode.

**Results:**

| Mode | `Default`/`Cancel` after `Form_Load` | Enter | Esc |
|---|---|---|---|
| read | Command1 Default, Command2 Cancel | Command1 Click | Command2 Click |
| move | **Command3 Default and Cancel; Command1's Default and Command2's Cancel are now False**, err 0 | Command3 Click | Command3 Click |
| off | none, err 0 | Text1 KeyDown 13, KeyPress 13, KeyUp 13 | Text1 KeyDown 27, KeyPress 27, KeyUp 27 |
| disabled | **still Command1 Default, Command2 Cancel** | Text1 KeyDown 13, KeyPress 13, KeyUp 13 | Text1 KeyDown 27, KeyPress 27, KeyUp 27 |
| hidden | **still Command1 Default, Command2 Cancel** | Text1 KeyDown 13, KeyPress 13, KeyUp 13 | Text1 KeyDown 27, KeyPress 27, KeyUp 27 |
| both | **Command1 Default and Cancel; Command2's Cancel is now False**, err 0 | Command1 Click | Command1 Click |

So:
- **A program reads and sets both properties** with no error.
- **Each is held by at most one button.** Setting it on one button clears it on whichever held it before.
- **One button may hold both.** Enter and Esc then both click it.
- **A disabled or hidden holder keeps the property but doesn't take the key.** Enter or Esc then goes to the focused
  control as ordinary key events, exactly as on a form with neither button.

### Two buttons that both say `Default` in the `.frm`

**The probe:** a form with Text1 and two CommandButtons, CmdA (TabIndex 1) and CmdB (TabIndex 2), each with both
`Default = -1 'True` and `Cancel = -1 'True` in the `.frm`. `/make` built it with no message. Two builds of each,
native and p-code, which agreed:

| `.frm` order | After loading | Enter | Esc |
|---|---|---|---|
| CmdB, then CmdA | **CmdA** Default and Cancel, CmdB neither | CmdA Click | CmdA Click |
| CmdA, then CmdB | **CmdB** Default and Cancel, CmdA neither | CmdB Click | CmdB Click |

**The later one in the file wins.** The first row alone couldn't tell "later in the file" from "lower TabIndex" or
"first by name", since CmdA was all three; the second row swapped only the file order, and the winner followed it.
This is what the run-time exclusivity above predicts if the loader sets the properties in file order.

### The default border, as the focus moves

**The probe:** the same form, the timer disabled, a screenshot after each Tab from Text1.

| Focus | Thick dark border |
|---|---|
| Text1 | on OK, the Default button |
| OK | on OK, with the focus rectangle |
| Cancel | **on Cancel, and OK loses it** |
| Three | **on Three, and OK loses it** |

The border marks the button Enter would click: the Default button while a control that isn't a button has the focus,
and otherwise the focused button, which Enter clicks whatever `Default` says (key-events page).

**Not measured:**
- the Properties window: whether setting `Default` on a second button at design time clears it on the first, or saves
  both and leaves the load to pick the later one;
- a Default button on a frame or other container, or inside a UserControl;
- changing `Default` while a button has the focus;
- whether `Cancel` on a disabled or hidden button behaves differently from `Default` in any case other than Esc.
