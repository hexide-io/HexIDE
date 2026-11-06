# `set_control_property` could not set `Name` on anything — **CLOSED** (#494, 2026-09-22)

> **Fixed.** A property is matched by the name the Properties window shows, without regard to case or to the
> parentheses of `(Name)`, so `Name` reaches it. A rename passes the Properties window's rules on every route,
> now kept in one place (`ComponentNaming`): not empty and unique on the form, and for the form itself a valid
> VB6 name no other document of the project has. With the designer open its handler applies them and the
> tool turns the `DataValidationException` into a refusal; with it closed the tool asks them first. Live, on a
> `--newproject` form with `Command0` and `cmdOk`:
> `set_control_property {"formName":"Form1","controlName":"Command0","property":"name","value":"cmdOk"}` →
> `'Command0' was not renamed: Name must be unique in form`, with the designer open and again with it closed;
> `Form1` → `1st` refused as not a VB6 name; `Form1` → `frmMain` renamed the document.
>
> **Still open, and filed separately:** `set_value` on the Properties window row's `Edit` reports success and
> does not commit: [#625](https://github.com/hexide-io/HexIDE/issues/625), with its own entry in
> `docs/mcp-server-gaps.md`.


**Symptom.** `set_control_property(formName: "Form1", controlName: "Command1", property: "Name", value:
"Command0")` answers `Property 'Name' not found on VB.CommandButton`. The same call against the form's own
root answers `Property 'Name' not found on VB.Form`. `Name` is the first row of the Properties window and
the one property every VB6 developer sets on every control they draw.

**Measured, not inferred** (2026-09-20), while checking whether a newly added validation refusal could
escape this tool's narrow catch (`HexIdeTools.cs` catches only `FormatException` and `OverflowException`
inside its dispatcher lambda). It cannot, because the property is refused before anything is set — so the
escape is unreachable through this tool, and the guard rail nobody can reach is worth recording as such.

**Consequence.** Renaming a control or a document is not automatable through the property tool at all. The
working route is the Properties window itself: `interact` with `set_property` on the `(Name)` row's
`PropertyViewModel.Value`, which is the reflection fallback rather than a provider action, and which does
commit through the same validation the user gets. `set_value` on that row's `Edit` writes the text and does
**not** commit — the binding updates on focus loss and Enter does not stand in for it — so a caller who uses
the obvious verb sees success and no rename.
