# 15. `dump_visual_tree` showed a hidden element as though it were on screen — **CLOSED** (#341, 2026-09-08)

> **Fixed.** A node carries `"isHidden": true` when it is in the tree but not on screen; the field is
> absent when it is showing. `inspect_element` reports the same on a single control.
>
> **Effective visibility, not the local flag.** A control can be `IsVisible` itself and still be invisible
> because an ancestor is collapsed, so reporting `Visual.IsVisible` would have moved the same trap up one
> level instead of closing it. `IsEffectivelyVisible` answers the question a caller is actually asking.
>
> **Null rather than false**, so the field is absent from the overwhelming majority of nodes: a dump runs to
> hundreds, and the hidden one is the exception worth spelling out.
>
> `isOffscreen` is left alone and still means what UIA means by it — clipping and scroll position. The two
> are genuinely different: a control scrolled out of view is *offscreen and visible*; a collapsed one is
> *visible to UIA and not showing*. Neither implies the other, which is why one more flag was the fix
> rather than a correction to the existing one.
>
> Verified against the case in the report below — the read-only banner on a form that is **not** read-only:
>
> ```
> "path": ".../Custom[Root]/Text[Read-only — HexIDE cannot yet reproduce this form faithfully, ...]",
> "isEnabled": true, "isOffscreen": false, "isHidden": true
> ```
>
> Its visible siblings in the same dump carry no `isHidden` at all. The entry had gone on misleading after
> it was written, too: the same banner appeared in every dump taken while investigating #334 and was read,
> again, as a form being held unsaveable.


**Symptom.** The read-only banner `TextBlock` appears in the tree for a form that is *not* read-only, with
`isOffscreen: false` and no other flag distinguishing it from a rendered element:

```
"path": ".../Custom[Root]/Text[Read-only — HexIDE cannot yet reproduce this form faithfully, ...]",
"isEnabled": true, "isOffscreen": false
```

The banner is bound to `IsReadOnly` and was collapsed. Nothing in the node says so.

**How it bit.** Verifying #152 against a brand-new UserControl. The tree said the read-only banner was
present and on screen, which would have meant a fresh, empty, perfectly reproducible `.ctl` was being held
unsaveable — a serious bug, and one entirely consistent with the change under test. It took a
`take_snapshot` to establish that nothing was rendered and the IDE was behaving correctly.

**Workaround.** Treat presence in the tree as "exists in the template", never as "visible". For anything
whose whole meaning is *whether it is showing* — banners, validation text, overlays, empty-state
placeholders — confirm with `take_snapshot`, or assert the bound view-model property via
`inspect_element` rather than reading the tree.

**Suggested fix.** Carry the real visibility on the node — `isVisible` from `Visual.IsVisible` (and ideally
`isEffectivelyVisible`, since an ancestor may be the one collapsed). `isOffscreen` is a UIA concept about
scroll position and clipping, and it does not answer this question. Without it the tree cannot be used to
assert the absence of a warning, which is exactly the assertion a fidelity gate needs.
