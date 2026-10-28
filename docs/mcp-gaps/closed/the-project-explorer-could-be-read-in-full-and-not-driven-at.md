# The Project Explorer could be read in full and not driven at all — **CLOSED** (2026-09-14)

> **Fixed in the same change that found it.** Two independent holes that only bite together, and the
> reason a whole navigation surface was unreachable.
>
> - **Tree nodes advertised no selection.** Every `TreeViewItem` reported `providers: ["scroll"]` — no
>   `selectionItem`, no `invoke` — so `interact(<node>, "select")` failed with *"element does not support
>   'select'"*. `DescribeProviders` now advertises `selectionItem` for a `TreeViewItem`, and `DoSelect`
>   sets `IsSelected`, which the owning `TreeView` reflects into `SelectedItem` — the property a view
>   model is actually bound to. Exactly the shape of the `DataGridRow` fix directly above it.
> - **Nothing could double-click.** UI Automation has no pattern for a double-click — it is a gesture, not
>   a control contract — so `interact` had invoke / select / set_value / toggle / expand / collapse and no
>   way to express the one that opens a document. Added as its own verb, `double_click`, which raises
>   `DoubleTapped` on the target and lets it bubble to whichever container handles it. It **selects the
>   target first**, because a real double-click does, and a verb that skipped that would fire the handler
>   against whatever was selected before — the wrong document, silently. The reply says whether the
>   selection moved.

**The measurement worth keeping.** The two gaps were invisible separately and fatal together, and the
reflection fallback did not cover either: `invoke_command` needs an `ICommand` and the Project Explorer's
`OpenSelected()` is a plain method (reasonably — it is a gesture handler, not a bindable command), while
`set_property` coerces from a string and `SelectedItem` is an `Object` holding a live node no string can
express. So the surface read perfectly through `dump_visual_tree` and refused every attempt to act on it,
which is the worst shape a tool can have: a caller sees a tree it appears able to address and finds out
only on the failing call.

**How long it hid, and behind what.** `open_file` already carried a branch routing carried files straight
to `EditorService`, with a comment naming all three halves of this — no double-click, no selection
provider, `OpenSelected` not a command — as the reason it existed. That workaround made one editor type
reachable and left the root cause in place, so the next feature to depend on the gesture (the read-only
project document) hit the same wall. A bypass that resolves one symptom is worth writing down as a gap
even when it unblocks the task, which is what that comment did and why this was quick to place.
