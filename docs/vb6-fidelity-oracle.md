# VB6 fidelity oracle

The oracle is now [indexed by subject](vb6-fidelity-oracle/README.md). This redirect preserves
references in immutable archived design records; new references should link directly to a subject file.

## A control's Container and Parent (#2064)

Provenance: the measurements supplied in hexide-io/HexIDE#2064, from the #1556 probe
("Controls.Add and Controls.Remove"), native and p-code agreeing. `c.Container.Name` names the
Frame or PictureBox holding the control, or the form when it is on the form. This also held after
`Controls.Add` into `Picture1`, `Frame1`, or a Frame or PictureBox itself added at run time.
`c.Parent.Name` names the form. Both properties return objects.

The Timer and Data control Container reads gave errors; the supplied measurement does not explain
why or give their error numbers. `Set c.Container = Frame1` was not measured. No error code or
assignment behaviour is inferred from these results.
