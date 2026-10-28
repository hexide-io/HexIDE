# A UserControl instance's saved properties

Provenance: #1631, issuecomment-6426450412 (2026-10-26), measured in the VB6 SP6 IDE on the oracle
VM console; native and p-code builds agreed. This records the file-save subset implemented here.

The saved `.frm` omitted TabIndex on the CanGetFocus=False instance. ForwardFocus=True together with
CanGetFocus=False retained TabIndex (and omitted TabStop). An InvisibleAtRuntime instance saved only
Left, Top, _ExtentX and _ExtentY: Width, Height and TabIndex were absent. The saved extents carried its
size. Windowless siblings, including a Label and the windowless UserControl, appeared after windowed
siblings, in their Controls collection order. A ControlContainer instance retained its nested child.

The same comment also measures `.ctl` spellings and refused load combinations. Those remain a separate
implementation slice; this note does not supply any unmeasured extent conversion or default.

An invisible instance without recorded extents has no measured save conversion here. HexIDE refuses
that save rather than discarding its only size or fabricating extent values. Per the #1631 supervisor
live-check correction (2026-10-27), such a hand-written form opens read-only with the missing-extents
reason; rendering for dirty checks does not refuse. This is a HexIDE save limitation, not a new VB6 measurement.

For the `.ctl` root, VB6 retained the original property spelling and spacing (apart from one comment's
spacing): AccessKeys with string `q`; Alignable, ControlContainer, DefaultCancel, ForwardFocus,
InvisibleAtRuntime and Windowless with -1; CanGetFocus and BackStyle with 0; DataBindingBehavior with 1.
The comments named True, False, Transparent and vbSimpleBound as appropriate. The spelling tests use
those ten lines from the comment directly. This preservation already worked before this slice.

## PropertyPage file properties

The same measurement (#1631, issuecomment-6426450412, VB6 SP6 IDE, native and p-code agreed) loaded
and saved a PropertyPage with `HelpContextID = 7`; the saved file retained that line. A `.pag` containing
`StandardSize = 1 'Small` reported that StandardSize was an invalid property name, and VB6's save
omitted the line. Only the resulting dimensions belong in the file. The Small and Large dimensions
were not measured because the probe could not change the Properties window row. HexIDE models
HelpContextID and omits StandardSize on save; it does not invent either preset's dimensions.
