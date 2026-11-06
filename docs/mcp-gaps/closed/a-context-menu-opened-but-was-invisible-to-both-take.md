# A context menu opened but was invisible to both `take_snapshot` and `dump_visual_tree` — **CLOSED** (#343, 2026-09-08)

> **Fixed, same cause as the MenuFlyout entry above — one parenting problem wearing two faces, exactly as
> this entry predicted.** The Project Explorer's menu turned out to be a `MenuFlyout` on
> **`TreeView.ContextFlyout`** (Tools/Projects/ProjectToolView.axaml:125) — not a `ContextMenu`, and not on
> the `TreeViewItem`. Handling only `ContextMenu` and `Button.Flyout` left an open, plainly visible menu
> reporting nothing, which reads exactly like a menu that never opened, and was twice diagnosed that way
> before the AXAML was read.
>
> **The second half was lifetime, not visibility.** A popup opened over an UNFOCUSED owner light-dismisses
> almost immediately: `press_key Apps` does open this menu — watched on screen — but it is gone before the
> next MCP call can walk it, so every later look reported nothing. `interact expand` now focuses the owner
> before opening, and the menu stays up across calls. The same trap as the transient tooltip, one layer up.
>
> Measured live:
>
> ```
> interact expand  …/Custom/Tree                   → "opened flyout on 'Tree'"
> dump_visual_tree root=…/Custom/Tree
>   → MenuItem[Properties], MenuItem[Add] (expandCollapse), MenuItem[Print]
>     plus Set as Start Up / View Object / View Code, each isHidden:true
> take_snapshot                                    → Properties / Add ▸ / Print, in the picture
> ```
>
> The visible three match a photograph the maintainer took of this menu earlier the same day, and the
> `isHidden` flags — added the day before — are what separate them from the items that are merely present.
>
> **Still transient across calls.** Snapshot it in the call straight after opening: an intervening
> `dump_visual_tree` was enough for it to dismiss before the picture was taken.


**Symptom.** A context menu can be opened — and then neither tool can see it. `take_snapshot` returns the
window with no menu in it, and `dump_visual_tree` returns the ordinary window tree with no popup root. The
menu is plainly on screen the whole time.

**This is not a missing verb, and I recorded it as one until it was pointed out.** `interact` has no
right-click action, so the first conclusion was "a context menu cannot be opened at all". It can:

```
press_key(…/Custom/Tree/Pane/TreeItem, "Apps")
  → {"success":true,"detail":"pressed Apps on Border[SelectionBorder]"}
```

…opens the Project Explorer's menu (Properties / Add ▸ / Print). The keypress works. What fails is
*seeing* it, and the two failures look identical from the tool output — which is exactly how a capture
problem gets written up as an interaction problem.

**Cause.** Both tools discover popups by walking the **owner window's visual tree** for `Popup` children —
`SnapshotComposer.CollectOpenPopupRoots` (`:105-124`) and `UiAutomationDriver` (`:643`, `:683`). That
reaches a **menu-bar** popup, because `Menu` → `MenuItem` → `Popup` really are visual children, which is
why the fix for the closed gap 12 works. A `ContextMenu` is not: it is set as a *property* on the control
that owns it, so it is only logically parented and the walk never arrives.

Predicted, as it turns out: `artifacts/design/259-language-server-visibility.md` noted that these two call
sites "cross `Popup`s found via `GetVisualChildren()` and a flyout's popup may be only logically parented".
This is that, measured.

**A second oddity, recorded rather than explained.** A keyboard-invoked context menu is placed at the
pointer, and a synthetic key press carries no pointer position — so it opened at the last real pointer
location, which was over a *different application* entirely. Whether the placement contributes to the
capture failure or is merely untidy is not established: the composer never finds the popup at all, so it
never gets as far as caring where it is.

**Workaround.** `press_key` with `Apps` opens the menu, and its items can be invoked blind through
`interact invoke_command` on the owning DataContext if the command is known. So a context-menu *action*
can be driven; its *presentation* — that the right items appear, enabled, in the right order — cannot be
asserted at all.

**Suggested fix.** Enumerate open popups from the application's own top-level list rather than by walking
the owner window's visual tree; Avalonia tracks popup roots globally, and that reaches logically-parented
popups as well as visual ones. Both call sites want the same change, and closing it would also make the
`MenuFlyout` gap above (a flyout attached to a toolbar `Button`) tractable, since that is the same
parenting problem wearing different clothes.

A `context_menu(target)` action raising `ContextRequested` is still worth adding — `Apps` depends on the
target being focusable and on Avalonia's own key handling — but it is a convenience, not the blocker.
