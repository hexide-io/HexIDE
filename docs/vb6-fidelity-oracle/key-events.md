# Key events (2026-10-27, #2325)

Provenance: hexide-io/HexIDE#2325, issuecomment-6441146230, measured with real
SendKeys input on VB6 SP6 (6.00.9782), Windows 10, en-GB. The native and p-code
builds agreed in all four probe modes. The supervisor identifies the source
measurement as oracle commit 25d78930 on `docs/oracle-tier3`.

The probe placed Text1, a four-item List1, a Default Command1 and a Cancel
Command2 on Form1; Form2 was empty. Modes enabled KeyPreview, removed the
Default/Cancel settings, or showed Form2. Each keyboard event logged its
arguments; Text1 also logged Change. Handlers changed selected KeyCode and
KeyAscii arguments.

For a focused TextBox with KeyPreview off, a printable key produces KeyDown,
KeyPress, Change and KeyUp, in that order. Lowercase k and Shift+K both report
KeyCode 75; their KeyAscii values are 107 and 75 respectively. Ctrl+A reports
KeyCode 65 and KeyAscii 1. Alt+X reports KeyCode 88 without KeyPress.

Backspace reports 8 in all three key events. Delete (46), Left (37) and F2
(113) produce KeyDown and KeyUp only. Modifier keys have their own down/up
pair: Shift 16, Ctrl 17 and Alt 18. The Shift argument is the mask 1, 2 or 4
respectively while held; the modifier's own release reports mask 0.

With KeyPreview on, the form precedes the control separately for each event.
A zero KeyCode from Form_KeyDown removes only the control's KeyDown. A zero
KeyCode from the control's own handler leaves subsequent processing intact.
KeyAscii zero suppresses the character; changing it changes the typed character.
A form's zero KeyAscii also suppresses the control's KeyPress.

Enter and Esc activate Default/Cancel targets without any key event, including
under KeyPreview. With neither target, Text1 and List1 report 13/27 in down,
press and up; a single-line Text1 does not change. A focused CommandButton
consumes Enter as Click. Tab between controls has no key events. Down on a
CommandButton changes focus without key events. Space on a button produces
down, press and up before Click. In List1, Down produces Click between down
and up; a letter produces Click after KeyPress and before KeyUp.

The empty Form2 reports its own keyboard events, including Enter, Esc and
Space. Its Tab reports down and press (9), but no up; the measurement offers
no explanation for this exception.

Unmeasured: forms with controls but no focusable control; multiline TextBox
Enter arbitration; CheckBox, OptionButton, ComboBox, scroll bars and PictureBox
keyboard events; UserControl key events; auto-repeat; modal forms opened by
key handlers. These are not inferred from the measured controls.
