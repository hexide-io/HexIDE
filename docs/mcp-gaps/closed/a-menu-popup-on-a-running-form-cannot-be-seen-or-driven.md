# 12. A menu popup on a running form cannot be seen or driven — **CLOSED** (2026-09-01)

> **Fixed for the tree and for driving; `take_snapshot` still cannot show a dropped-down menu.**
>
> The suggested fix said #61 would cover this. It did not, and could not: #61 picks between *windows*, and
> a popup never enters `lifetime.Windows`. Two separate causes, both now addressed.
>
> **Seeing.** A popup's content is not under the popup in the visual tree — it is realised in the popup's
> own root — so the walk crossed nothing and every open menu reported `"children": []`. The control-view
> collector (and the `#AutomationId` descendant search) now step through an open `Popup` into its content.
> Because `Resolve` shares that collector, the emitted paths round-trip, so `interact` and `press_key`
> reach popup items too.
>
> **Driving.** `MenuItemAutomationPeer` exposes **no providers at all** — not `invoke`, not
> `expandCollapse` — which is why every verb failed, not just `expand`. `Interact` now falls back to
> `MenuItem.Open()`/`Close()` for a submenu, and to raising `Click` for invoke (MenuItem's class handler
> for that event executes a bound `Command`, so one raise does what a real click does). `DescribeProviders`
> reports those tokens for a `MenuItem`, because an action nobody can see advertised is an action nobody
> tries — `expandCollapse` only where `HasSubMenu`, so a leaf still refuses honestly.
>
> **Separators** are no longer collapsed as structural. They match every "transparent wrapper" condition
> and are still not plumbing: a separator's *position* is the VB6-fidelity question. **They appear only
> under `interactiveOnly: false`** — the default filter drops them, since they have no providers and no
> interactive descendants.
>
> Verified live against `demo/bill-of-fare` while running: `File` and `View` open via `interact expand`;
> `Save As...` reports `isEnabled: false`; `PART_InputGestureText` carries the shortcut text; the
> `Separator` sits between `Zoom` and `Refresh`; `View ▸ Zoom ▸ Zoom In` — two popups deep — resolves and
> invokes, and the form's own label reads *"you chose: View > Zoom > Zoom In (a submenu, two levels
> deep)"*. Eight headless tests in `MenuPopupTests` pin the behaviour.
>
> **The snapshot half is closed too, in a follow-up.** `SnapshotComposer` renders the window and each open
> popup root separately and composes them at their real screen positions, so a dropped-down menu is now in
> the picture — verified live on `demo/bill-of-fare`: *Zoom ▶*, the separator as a rule, *Refresh  F5* with
> its shortcut, and the Zoom submenu open beside its parent in the right z-order.
>
> Three things that cost a build cycle each, worth knowing before touching this code:
>
> - **Render the popup's ROOT, not `Popup.Child`.** The root owns the border, padding and shadow the menu
>   is drawn with; the child alone came out short and shifted.
> - **`DrawingContext.DrawImage(image, destRect)` samples the source's *device-independent* extent.** On a
>   150% display that reads the top-left two-thirds of the piece and stretches it to fill — a correctly
>   placed, correctly sized menu box with magnified, clipped contents. The three-argument overload with an
>   explicit pixel source rect is what is wanted.
> - **The window still goes through `RenderTargetBitmap.Render`, not the context.** That path was already
>   right; routing it through `DrawImage` made the whole form `scaling` times too big.
>
> None of this is visible headlessly, because **headless hosts popups as overlays inside the window** — so
> the defect cannot occur there and a "the popup appears" test passes with the fix removed. The headless
> tests therefore cover the overlay path (the composer must not draw an already-rendered popup twice) and
> the desktop path is verified against the running IDE.


**Symptom.** Verifying that a running form's menus render correctly, `take_snapshot` shows the menu *bar*
but never a dropped-down menu, and `dump_visual_tree` reports every top-level `MenuItem` with
`"children": []`. `interact` refuses both `expand` and `invoke` on a top-level `MenuItem`
(`element does not support 'expand'`), and pressing Enter or Down on it via `press_key` highlights the item
without opening it.

The consequence is that the *inside* of a menu is unverifiable through MCP: separators, shortcut text drawn
right-aligned in the item, item enabled/checked state, and submenu nesting are all only reachable as data.

**Cause.** A menu's items are realised in a popup, which is its own top-level window. `take_snapshot`
captures the form's window, and the popup is not in it — the same reason
[#61](https://github.com/hexide-io/HexIDE/issues/61) reports runtime modal dialogs as invisible. The empty
`children` array is not a bug in the dump: sub-items genuinely do not exist in the tree until the menu is
opened.

**Workaround.** Assert the structure headlessly, where the objects the loader builds can be inspected
directly — `RuntimeMenuTests` covers the bar's contents, sub-item nesting, separators, `InputGesture` and
click dispatch that way. Then verify live only what the *form's own window* can show: the bar's captions,
and the effect of a shortcut, by having the handler write to a `Label` on the form. That combination did
verify #85 end to end, but it verified the dropdown's contents by proxy rather than by looking at them.

**Suggested fix.** Whatever fixes #61 for modal dialogs should cover this, since it is the same underlying
limitation — snapshot and tree-walk the topmost popup root rather than only the form's window. An
`interact` action that opens a menu (`open`/`expand` mapped to `MenuItem.IsSubMenuOpen`) would be the other
half, since a popup that cannot be opened cannot be captured either.
