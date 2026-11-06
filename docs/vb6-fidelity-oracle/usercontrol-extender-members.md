# A UserControl's extender members at run time

Provenance: [#1631, issuecomment-6425849231](https://github.com/hexide-io/HexIDE/issues/1631#issuecomment-6425849231),
measured with vb6.exe SP6, native and p-code agreeing. The probe placed eleven private UserControls on a
Standard EXE form, each changing one `.ctl` setting. Handwritten property lines loaded without errors and
took effect. This records the member-availability portion used by part A; focus, keys, rendering and
lifecycle are separate work.

| Setting | Measured extender surface |
|---|---|
| `CanGetFocus = False` | No TabIndex, TabStop, SetFocus, CausesValidation or HelpContextID. |
| `InvisibleAtRuntime = True` | Only Name, Index, Parent and Object remain. Reading Left or Top raises 393, "Get not supported at run time". Visible, Width, Height, Container, TabIndex, TabStop and SetFocus are missing (438 late-bound). |
| Every instance | Enabled, hWnd and Data* are missing (438). Setting DataBindingBehavior to 1 alone does not add DataField. |
| Windowless | The measured control can take focus; its own `UserControl.hWnd` reads 0 without error. This is distinct from the extender's hWnd above. |

An early-bound absent member is VB6's compile error "Method or data member not found"; a late-bound one
raises 438. The supervisor's 2026-10-26 error decision prescribes HexIDE's existing
`VBMethodOrDataMemberNotFoundException` path (error 461's compile-error translation), rather than inventing
a new number.

The supervisor's 2026-10-26 **handle decision** additionally prescribes the surface HexIDE implements for
this task: windowed UserControls have an explicitly unimplemented hWnd member; windowless ones have no
hWnd member. HexIDE has no native window per control and must never manufacture a handle value. This is
a product decision, not a further VB6 measurement, and supersedes the measured zero for this implementation.

The issue records defaults CanGetFocus True, InvisibleAtRuntime False and Windowless False. Their designer
rows and save behaviour are outside part A. Existing Name/Index semantics are recorded in
[A control's Name and Index, at run time](a-controls-name-and-index-at-run-time.md).
