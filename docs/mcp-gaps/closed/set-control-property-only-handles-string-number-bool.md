# 2. `set_control_property` only handles string / number / bool — **CLOSED** (#641, 2026-09-22)

> **Fixed.** `set_control_property` reads its value through `PropertyText`, which defers to the Properties
> window's own parser and adds an enum by number, by name or as the dropdown shows it (`1 - Opaque`), with
> numbers read in the invariant culture. A refusal lists what the property takes. Verified live:
> `BackStyle` `"0 - Transparent"` and `BackColor` `"&H00C0FFC0&"` were written to the `.frm` as
> `BackStyle = 0 'Transparent` and `BackColor = &H00C0FFC0&`; `"7"` and `"green"` were refused naming the
> accepted spellings.

**Symptom.** Setting an **enum** property fails — `set_control_property(Label0, "BackStyle", "1")` →
*"Property 'BackStyle' has type 'BackStyles' which is not supported by set_control_property"*. **Colour**
(`VBColor`) properties (`BackColor`/`ForeColor`) are likewise unsettable.

**How it bit.** I couldn't make a label opaque, nor set a control's colour, via the designer tool — so the
Phase-2 colour verification had to be done by *running code* (`Me.BackColor = &HC0FFC0` in `Form_Load`) and
snapshotting the result, rather than a designer property set.

**Fix.** Accept enum values (by member name or ordinal) and `VBColor` values (a hex `OLE_COLOR` string like
`"&H00FF0000&"`, or an `R,G,B` triple). Better: route the incoming string through the **same property-editor
coercion the designer's property grid uses**, so every editable property type is settable through one path.
