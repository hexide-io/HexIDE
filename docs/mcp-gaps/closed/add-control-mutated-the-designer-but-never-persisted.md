# 11. `add_control` mutated the designer but never persisted — **CLOSED** (#344, 2026-09-09)

> **Fixed.** `add_control` now saves the form, like its sibling `set_control_property` always did. The pair
> disagreed about whether a designer edit was durable, and the one that did **not** save is the one that
> creates things — which is why nine controls could exist, be reported as added, and not survive a crash.
>
> The save runs **on the UI thread**: a save first publishes `ApplyAllUnsavedChangesEvent`, whose handler
> reads AvaloniaEdit's `Document.Text` and throws off it, leaving the previous content to be written and
> reported as success (#334, closed the same week). And a refusal is **not** reported as success (#147): an
> unfaithful form comes back `success:false`, naming the control that is in the designer but not on disk,
> because a caller told "saved" would not find out otherwise until something else read that file.
>
> Measured live: `add_control(Form1, CommandButton, 40, 40, 100, 30)` → `Command0`, and the `.frm` on disk
> carried `Begin VB.CommandButton Command0` immediately, with no further call.


**Symptom.** Nine controls were added successfully via `add_control`; the IDE then crashed and **all of
them were lost** — `frmOrders.frm` on disk still held only the bare form. `set_control_property` saves;
`add_control` does not.

**Workaround.** For anything more than a couple of controls, author the `.frm` directly and open the
project — the format is small and well understood (`Left`/`Top`/`Width`/`Height` in twips, i.e. pixels
× 15, plus `Caption`/`Text`). That is also faster than one round trip per control, and it survives a
crash. Use `add_control` for interactive exploration, not for composing a form.

**Fix consideration.** Either save after `add_control` (consistent with `set_control_property`), or add
an explicit `save_form` tool so a caller can batch adds and commit once.
