## A caption's access key (2026-10-22, #920)

**The probe.** One form, built as **p-code** with `vb6.exe /make` on the Win10 guest and run in its console session.
It holds six CommandButtons, a CheckBox, an OptionButton, seven Labels each followed in the tab order by a TextBox,
two Frames, and four top-level menus with no items under them. Each caption uses its letters once across the whole
form, so a key can only reach one control. Every Click, and every TextBox's GotFocus, appends a line to a log.

A driver in the console session switched keyboard cues on (`SPI_SETKEYBOARDCUES`), so underlines draw without Alt
held, and put the setting back afterwards. It screenshotted the form, then sent each Alt+letter with `SendKeys`,
followed by Esc, writing a marker line into the log before each. Built p-code only. Captions are drawn and keys are
dispatched by the runtime and by Windows, not by compiled code, so native code has nothing to differ on.

| Caption | Control | Drawn | Underlined | Keys that reach it |
|---|---|---|---|---|
| `Tom && Jerry` | CommandButton | `Tom & Jerry` | — | none: Alt+J and Alt+T reach nothing |
| `File_Name` | CommandButton | `File_Name` | — | none: Alt+N reaches nothing |
| `Trail&` | CommandButton | `Trail` | — | none |
| `A&&&Bc` | CommandButton | `A&Bc` | B | Alt+B |
| `&One &Wide` | CommandButton | `One Wide` | **W only** | **Alt+O and Alt+W both** raise Click |
| `Hex_&Kit` | CommandButton | `Hex_Kit` | K | Alt+K |
| `Chk && Box` | CheckBox | `Chk & Box` | — | |
| `Opt_Ion` | OptionButton | `Opt_Ion` | — | none: Alt+I reaches nothing |
| `Lab && Rat` | Label | `Lab & Rat` | — | none: Alt+R reaches nothing |
| `Lab && Rat`, UseMnemonic False | Label | `Lab && Rat` | — | |
| `Zip_Code` | Label | `Zip_Code` | — | none: Alt+C reaches nothing |
| `&Quick &Yak` | Label | `Quick Yak` | **Y only** | **Alt+Q and Alt+Y both** move focus to the next TextBox |
| `Hush&` | Label | `Hush`, **with an underline after it** | the empty place after the text | none: Alt+H reaches nothing |
| `&Mute`, UseMnemonic False | Label | `&Mute` | — | none: Alt+M reaches nothing |
| `Pre_&Vex` | Label | `Pre_Vex` | V | Alt+V |
| `Fra && Me` | Frame | `Fra & Me` | — | |
| `Fra_&Xen` | Frame | `Fra_Xen` | X | Alt+X moves focus to the next control |
| `Menu && More` | menu | `Menu & More` | — | |
| `Menu_Bar` | menu | `Menu_Bar` | — | |
| `&Go &Upward` | menu | `Go Upward` | **U only** | **Alt+G only**: Alt+U reaches nothing |
| `Dot_&Edge` | menu | `Dot_Edge` | E | Alt+E |

So, read left to right:
- `&&` is one `&`.
- A single `&` is hidden and marks the character after it.
- A trailing `&` marks nothing and is not drawn.
- `_` is an ordinary character everywhere.

**What was overturned.**
- `VBCaption.AsShown` kept a trailing `&` as drawn, and a test pinned it "so a measurement that disagrees fails here". A
  button drops it.
- A Label draws something different from the button: an underline where the marked character would be, as `Hush_`.
  That reads like Windows' `DrawText` underlining a character that isn't there. HexIDE draws `Hush`, as the button does,
  and does not reproduce the underline.

**What resists one rule.** A caption that marks two characters is underlined at the **last** on every control and on
the menu bar, but the two disagree about keys:
- a CommandButton and a Label answer to **every** marked character;
- a top-level menu answers only to the **first**, and so underlines a letter its key is not.

Alt+U was sent twice, once after other keys and once as the first key of a fresh run, and reached nothing both times.
No rule here explains why the two differ; they are recorded as measured.

**A reading that was confounded and re-run.** In the first run, Alt+Y came straight after Alt+Q. Alt+Q had already
moved focus to the TextBox after `&Quick &Yak`, so Alt+Y could not raise its GotFocus, and logged nothing. A second
run sent Alt+K first, to put focus elsewhere, and then Alt+Y reached the TextBox.

**What HexIDE does with it** (#920). Avalonia's AccessText marks its key with `_` and draws `__` as one underscore,
but takes its key from the first underscore in its text whether it is doubled or not. So:
- A caption with no marked character, or with an underscore before its key, is drawn as plain text, with no key.
- Any other caption is an AccessText.
- A control takes the last marked character as its key, the one VB6 underlines; a menu takes the first, the one VB6
  answers to.

The losses, against the table:
- Alt+K on `Hex_&Kit` and Alt+V on `Pre_&Vex` do nothing.
- Alt+O on `&One &Wide` and Alt+Q on `&Quick &Yak` do nothing.
- A menu underlines its first marked letter where VB6 underlines its last.


## With Windows' keyboard cues off (2026-11-21, #919, SP6)

**The question.** The section above switched keyboard cues on before it looked. What do a CommandButton and a Label
draw with the guest's own setting, which is off?

**The probe.** Two forms, each with Command1 captioned `&OK` and Label1 captioned `Centred &label` (Alignment 2,
BorderStyle 1). Both were built with `/make` as native code, run on the guest's console (VB6.EXE 6.00.9782, SP6), and
opened in VB6's designer. Each was captured through the host's framebuffer twice: at rest, and with Alt held down
through Hyper-V's synthetic keyboard.
- **The guest's setting was off.** The `UserPreferencesMask` was `9E 1E 07 80 12 00 00 00`, whose keyboard-cues bit
  (`0x20` of the first byte) is clear. That is Windows' "Underline access keys" off, and `Keyboard Preference\On` was 0.
- **The first form was reached by keyboard.** The program was started from the Run box with Return, and the designer
  was opened with Shift+F7.
- **The second was reached by the mouse alone.** The program was started from a desktop shortcut, by right-clicking it
  and clicking Open. Its project was opened the same way, and the designer by double-clicking Form1 in the Project
  window. No key was pressed between the clicks and the first picture.
  - **The cues really were hidden for mouse input.** The shell's context menu, opened by the right-click in that same
    session, drew no underlines.

**What was drawn.**

| | Reached by | At rest | Alt held |
|---|---|---|---|
| Command1 `&OK`, run | keyboard | `O` underlined | the same |
| Label1 `Centred &label`, run | keyboard | `l` underlined | the same |
| Command1 and Label1, designer | keyboard | both underlined | the same |
| Command1 `&OK`, run | mouse alone | `O` underlined | the same |
| Label1 `Centred &label`, run | mouse alone | `l` underlined | the same |
| Command1 and Label1, designer | mouse alone | both underlined | not taken |

- **A VB6 form draws its access-key underlines whatever the cues setting and however it was reached,** in a run and in
  the designer. Holding Alt changes nothing. In the mouse-alone run, the only pixels that differed between the two
  pictures were the TextBox's caret, which blinks.
- **The CommandButton too,** though it is a real BUTTON window. Why it ignores the cues wasn't established.

**What was overturned.** #919 guessed that the two would differ: the windowless Label always underlined, and the BUTTON
window hiding it until Alt with the setting off. Both underline in every case measured.

### Not measured

- **A form shown from code after the program has started,** such as a second form, or a MsgBox.
- **A DPI other than 96.**
