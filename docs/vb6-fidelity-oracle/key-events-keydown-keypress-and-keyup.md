## Key events: KeyDown, KeyPress and KeyUp (2026-10-27, #2325)

**The question.** In what order do a form and its controls raise KeyDown, KeyPress and KeyUp? What do KeyPreview, a
handler's change to KeyCode or KeyAscii, and a Default or Cancel button do to them?

**The probe:**
- **The software:** VB6 SP6 (6.00.9782), on Windows 10, in the en-GB user locale.
- **The form:** Form1 holds Text1 (TabIndex 0), List1 (1) holding four items, Command1 (2, Default True) and Command2
  (3, Cancel True). Form2 holds no control.
- **The handlers:** every KeyDown, KeyPress and KeyUp is logged with KeyCode and Shift, or KeyAscii, along with Change,
  Click and GotFocus. Some handlers change their argument:

  | Handler | Change it makes |
  |---|---|
  | `Text1_KeyPress` | `a` to `b`, and `q` to 0 |
  | `Text1_KeyDown` | KeyCode 0 for W |
  | `Form_KeyDown` | KeyCode 0 for X |
  | `Form_KeyPress` | `z` to 0, and `e` to `E` |

- **Four modes, from the command line:**
  - A, as built;
  - B, with KeyPreview True;
  - D, with neither a Default nor a Cancel button;
  - C, showing the empty Form2.
- **The input:** a driver on the guest's console typed real keys with SendKeys, with Text1 focused at the start. The
  sequence was `k`, Shift+K, `a`, `q`, `w`, `x`, `z`, `e`, Backspace, Delete, Left, F2, Ctrl+A, Alt+X, Enter and Esc. It
  continued with Tab, to List1, then Down, `k`, Enter and Esc there. Then Tab, to Command1, then Space, `k`, Enter, Esc
  and Down there.
- **The builds:** native code and p-code logged the same lines in every mode. One p-code run of mode D lost SendKeys'
  Shift and typed `k` for Shift+K; a second run matched native.

### The order, with KeyPreview False

**A printable key:** KeyDown, KeyPress, then Change, then KeyUp, all on the focused control. `k` gave
`KeyDown 75`, `KeyPress 107`, `Change` with Text `"k"`, `KeyUp 75`.

**Shift, Ctrl and Alt each raise their own KeyDown and KeyUp around the key:**

| Keys | Events |
|---|---|
| Shift+K | KeyDown 16 Shift 1, KeyDown 75 Shift 1, **KeyPress 75** (`K`), Change, KeyUp 75 Shift 1, KeyUp 16 Shift 0 |
| Ctrl+A | KeyDown 17 Shift 2, KeyDown 65 Shift 2, **KeyPress 1**, KeyUp 65 Shift 2, KeyUp 17 |
| Alt+X | KeyDown 18 Shift 4, KeyDown 88 Shift 4, KeyUp 88 Shift 4, KeyUp 18. **No KeyPress** |

- **KeyCode is the key, whatever the case:** 75 for both `k` and `K`. KeyAscii is the character.
- **The modifier's own KeyUp reads Shift 0,** once it is up.

**Keys with no character have no KeyPress:**
- Delete gave KeyDown 46 and KeyUp 46, with no Change at the end of the text.
- Left gave 37 and F2 gave 113, each a KeyDown and a KeyUp.

**Backspace does have one:** KeyDown 8, **KeyPress 8**, Change, KeyUp 8.

### KeyAscii and KeyCode, changed by the control's own handler

- **`KeyAscii = 0` in KeyPress swallows the character.** `q` raised KeyDown, KeyPress and KeyUp, with no Change and no
  `q` typed.
- **Changing KeyAscii changes what is typed.** `a` set to 98 typed `b`.
- **`KeyCode = 0` in the control's own KeyDown does not stop the key.** `w` still raised KeyPress and was typed.

### KeyPreview True: the form first

**For every key, the form's event comes before the control's, one event at a time:** Form KeyDown, control KeyDown, Form
KeyPress, control KeyPress, then Change, Form KeyUp, control KeyUp. That held for Text1, List1 and Command1.

**What the form's handler does to the control:**
- **`KeyCode = 0` in `Form_KeyDown` stops the control's KeyDown, and only that.** For `x`, Text1 got no KeyDown, but it
  still got KeyPress and KeyUp, and `x` was typed. For Alt+X, Text1 got no KeyDown for the X.
- **`KeyAscii = 0` in `Form_KeyPress` stops the control's KeyPress and the character.** `z` was not typed, and Text1 still
  got KeyDown and KeyUp.
- **Changing KeyAscii in `Form_KeyPress` changes what the control sees and types.** `e` set to 69 reached Text1 as 69,
  and `E` was typed.

### Enter, Esc, Tab and arrows: the keys a form takes for itself

| Key | With a Default and a Cancel button (A, and B with KeyPreview) | With neither (D) |
|---|---|---|
| Enter, in Text1 or List1 | **Command1 Click, and no key event anywhere**, the form's included under KeyPreview | KeyDown 13, KeyPress 13, KeyUp 13 on the control. Text1 didn't change |
| Esc, in Text1 or List1 | **Command2 Click, and no key event anywhere** | KeyDown 27, KeyPress 27, KeyUp 27 on the control |
| Enter, on Command1 | Command1 Click, with no key event | **Command1 Click, with no key event**: a focused button takes Enter as a click |
| Esc, on Command1 | Command2 Click, with no key event | KeyDown 27, KeyPress 27, KeyUp 27 on Command1 |
| Tab | the next control's GotFocus. **No key event**, even under KeyPreview | the same |
| Down, on Command1 | **the focus moves to Command2**, with no key event | the same |

**On a CommandButton:**
- **Space raises KeyDown 32, KeyPress 32 and KeyUp 32, then Click, after the KeyUp.**
- A letter raises the three key events and no Click.

**In a ListBox:**
- Down raised KeyDown 40, then **Click with ListIndex 0**, then KeyUp.
- `k` raised KeyDown, KeyPress 107, then **Click with ListIndex 2**, the item starting with k, then KeyUp.

### A form with no controls

Form2, empty, raised its own events for every key:
- **Printable keys:** KeyDown, KeyPress, KeyUp, as for `k` and Shift+K.
- **Enter, Esc and Space:** KeyDown 13, KeyPress 13, KeyUp 13; then 27, 27, 27; then 32, 32, 32.
- **Left and F2:** KeyDown and KeyUp.
- **Ctrl+A:** KeyPress 1 between the KeyDowns and the KeyUps.
- **Alt+X:** no KeyPress.
- **Tab:** KeyDown 9 and KeyPress 9, and **no KeyUp**. **That resisted explanation.**

### CheckBox, OptionButton, ComboBox and a multi-line TextBox (2026-10-27)

**The probe:**
- **The form:** Check1, Combo1 (Style 0, a dropdown combo), Combo2 (Style 2, a dropdown list), Option1 and Option2,
  Text2 (MultiLine), then Command1 (Default) and Command2 (Cancel), in that tab order.
- **The lists:** each combo held `apple`, `banana` and `cherry`.
- **The focus:** a Timer moved it with SetFocus to the control the driver named, so no key was spent on getting there.
- **The modes:** A, as built; B, with KeyPreview True; D, with neither a Default nor a Cancel button.
- **The input:** SendKeys on the guest's console.
- **The builds:** native and p-code logged the same key events in every mode. One native run of mode D read the
  driver's focus file while it was still being written, and logged an empty name for it. The key events after it
  matched.

**Under KeyPreview, the form's event still comes first for every key,** one event at a time, as for Text1 and List1:
Form KeyDown, control KeyDown, Form KeyPress, control KeyPress, then Change or Click, Form KeyUp, control KeyUp. With
Alt+Down, each of the two keys went through that pairing in turn.

**The CheckBox and the OptionButton:**

| Key | CheckBox | OptionButton, already selected |
|---|---|---|
| Space | KeyDown 32, KeyPress 32, KeyUp 32, **then Click**, with Value 1 | KeyDown 32, KeyPress 32, KeyUp 32, **no Click** |
| `k` | KeyDown, KeyPress 107, KeyUp | the same |
| Enter, Esc, with a Default and a Cancel button | **Command1 Click; Command2 Click. No key event anywhere**, the form's included under KeyPreview | the same |
| Enter, Esc, with neither (D) | KeyDown, KeyPress and KeyUp, 13 and 27 | the same |
| Down | **the focus moved to Combo1, the next control,** and Combo1 raised **KeyUp 40**, with no KeyDown | **Option2 got the focus and was selected:** GotFocus, then Click. No key event on either |
| Up, from Option2 | | Option1: GotFocus, then Click |

- **SetFocus on an OptionButton that isn't selected selects it:** GotFocus, then Click with Value True.
- **The KeyUp after Down went to the control that took the focus** when that was a ComboBox. In the earlier probe, Down
  on Command1 moved the focus to Command2, and Command2 raised no KeyUp.

**The ComboBox, both styles:**

| Key | Style 0, dropdown combo | Style 2, dropdown list |
|---|---|---|
| a letter, list closed | KeyDown, KeyPress, **Change**, KeyUp. `k` typed | KeyDown, KeyPress, **Click**, KeyUp. `b` selected banana, ListIndex 1 |
| Down, list closed | KeyDown 40, **Click with ListIndex 0**, KeyUp. Text became `apple`, **with no Change** | KeyDown 40, Click with ListIndex 2, KeyUp |
| Alt+Down | KeyDown 18, KeyDown 40 Shift 4, **DropDown**, KeyUp 40 Shift 4, KeyUp 18 | the same |
| Down, list open | KeyDown, Click with the next item, KeyUp | the same. On the last item: KeyDown and KeyUp, no Click |
| **Enter, list open** | **KeyDown 13 and KeyPress 13, and no KeyUp.** The list closed. **Command1 wasn't clicked** | the same |
| **Esc, list open** | **KeyDown 27 and KeyPress 27, and no KeyUp.** **Command2 wasn't clicked** | the same |
| Enter, Esc, list closed | Command1 Click; Command2 Click. No key event | the same |
| F4, list closed; F4 again | KeyDown 115, DropDown, KeyUp 115; then KeyDown 115 and KeyUp 115 | |

- **With neither a Default nor a Cancel button (D), Enter and Esc in an open list did raise their KeyUp,** as did Enter
  and Esc with the list closed.
- The ComboBox has no CloseUp event, so closing the list raised nothing of its own.

**The multi-line TextBox:**

| Key | With a Default and a Cancel button | With neither (D) |
|---|---|---|
| **Enter** | **Command1 Click, and no key event. No line break** | KeyDown 13, KeyPress 13, Change with `a` vbCrLf, KeyUp 13 |
| **Ctrl+Enter** | KeyDown 17 Shift 2, KeyDown 13 Shift 2, **KeyPress 10**, Change with a line break added, KeyUp 13 Shift 2, KeyUp 17 | the same |
| Esc | Command2 Click | KeyDown 27, KeyPress 27, KeyUp 27 |
| Tab | **the focus moved to Command1.** No tab was typed | the same |

**This overturned a guess recorded here earlier,** that a multi-line TextBox takes Enter rather than the Default
button. **The Default button takes it,** and Ctrl+Enter is how a user types a line break, with KeyAscii 10.

**Can KeyAscii = 0 stop Enter reaching a Default button? No.** Whenever the Default button took Enter, in Text1, List1,
Check1, Option1, Text2, or a combo with its list closed, no KeyDown and no KeyPress was raised. That held on the control
and, under KeyPreview, on the form. There is no handler in which to set KeyCode or KeyAscii to 0. The same holds for Esc
and the Cancel button. **The only thing that kept Enter from the Default button was an open dropdown list,** which
took the key itself.

**Unmeasured:**
- a form with controls but none focusable;
- a ComboBox of Style 1, Simple, and arrows in a combo's edit part with the list closed and Text typed;
- the scroll bars and the PictureBox;
- a UserControl's own key events;
- holding a key down for auto-repeat;
- the key events of a modal form shown from a key handler.
