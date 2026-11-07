## VB_Ext_KEY on a form survives VB6's save (2026-10-26, #1117, design-time)

**Why.** #1117 needs somewhere in a `.frm` to keep a control's Anchor and Dock, which VB6 has no property for, without
VB6 losing them when it saves the form. `VB_Ext_KEY` is the attribute VB6's Class Builder add-in writes to carry its own
settings, `Attribute VB_Ext_KEY = "SavedWithClassBuilder6" ,"Yes"`, so it was the candidate to measure.

**The probe.** A hand-written `Form2.frm` with no controls carried two keys VB6 has never seen, after its `VB_Exposed`
line:

```
Attribute VB_Name = "Form2"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False
Attribute VB_Ext_KEY = "HexIDE.Anchor.txtBody" ,"Top, Bottom"
Attribute VB_Ext_KEY = "HexIDE.Dock.lstLog" ,"Fill"
```

VB6 SP6's IDE opened the project on the oracle VM's console. Form2's Caption was changed through the Properties window,
and the form saved with Ctrl+S.

**The result.** Both lines came back verbatim, in the same place: after `VB_Exposed`, in their order, with the space
before the comma VB6's Class Builder writes. VB6 raised no message on opening the project or saving the form. Like every
`Attribute` line, neither shows in VB6's code window.

**What it settles.** Jen chose `VB_Ext_KEY` for Anchor and Dock on this result (#1117), over the comment annotations
proposed first. A key is `HexIDE.Anchor.<control>` or `HexIDE.Dock.<control>`.

**Not measured:**
- a key on a member, `Attribute <member>.VB_Ext_KEY`;
- a value longer than a line VB6 otherwise writes;
- one key given twice;
- a UserControl's `.ctl`;
- whether `/make` builds such a form, which it must if Make with VB6 is to keep working.
