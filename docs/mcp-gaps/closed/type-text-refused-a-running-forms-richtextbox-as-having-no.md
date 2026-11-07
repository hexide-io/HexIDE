# `type_text` refused a running form's RichTextBox as having no text surface — **CLOSED** (#2460, 2026-10-28)

**Symptom (2026-10-28, hexide-frm, live-checking #2456's RichTextBox).** `type_text(target: "Window/None[RichTextBox1]",
text: "x", mode: "keyboard", window: "Form1")` answered `'VBRichTextBox' has no text surface
(TextEditor/TextArea/TextBox) to type into`. The RichTextBox is a `Control` HexIDE draws itself, and it takes typing in
a `TextInput` handler of its own. The refusal judged the target by its class, not by whether it takes text, so
typing, KeyPress and Change order, MaxLength and protected text could not be driven at all. Every in-box control
drawn the same way would have been unreachable in the same way.

**Fixed (#2460).** In keyboard mode a control that takes focus, with no text box under it, is sent the keys: KeyDown,
TextInput and KeyUp, as a person's keyboard sends them. What it did with them is read from what came back. A control
that handled the TextInput of none of the keys that type a character is reported as taking no typing, and the reply
says their KeyDown and KeyUp were raised. A control that cannot take focus is still refused, now saying why. Document
mode still needs a text box, and its refusal names keyboard mode. **A dropdown that is not editable behaves differently
in keyboard mode:** it takes focus, so it is now sent the keys, and its own text search selects by them, as a person's
typing does. A caller that learned "expand, then select" from the old refusal now sees the dropdown take the keys
instead; document mode still refuses it that way. Refusing typing a person can do was the defect.
Guarded by `TypeIntoOwnInputTests`, which type into a stand-in control that handles its own input.

**Live (2026-10-28, Windows, headless `claude -p` through the hexide tools)**, on #2456's branch with this change
applied. The probe form held a RichTextBox with `MaxLength = 5`, whose KeyPress and Change appended to a label:

```
type_text {"target":"Window/None[RichTextBox1]","text":"abcdefg","mode":"keyboard","window":"Form1"}
→ {"success":true,"mechanism":"keyboard","detail":"typed 7 key(s) into VBRichTextBox as a person types them"}
inspect_element {"target":"Window/Text[Label1]","window":"Form1"}
→ name: "P97 C[a] P98 C[ab] P99 C[abc] P100 C[abcd] P101 C[abcde] P102 P103 "
type_text {"target":"Window/None[RichTextBox1]","text":"x","window":"Form1"}
→ 'VBRichTextBox' has no text surface (TextEditor/TextArea/TextBox) to type into; mode "keyboard" sends it keys as a
  person types, for a control that takes typing itself
```

Every key ran KeyPress and every insert ran Change, until MaxLength stopped the inserts after the fifth; whether that
matches VB6's RichTextBox is #2456's to say, not this tool's.
