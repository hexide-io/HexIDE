# A MenuFlyout on a toolbar Button opened blind, and its items were invisible — **CLOSED** (#343, 2026-09-08)

> **Fixed — and the entry was wrong about why, in the direction that sends the next reader looking for the
> wrong fix.** Corrections first, because they were measured:
>
> - *"`interact invoke` ... does nothing visible — it fires the button's own invoke, which is not what opens
>   a flyout."* **False.** `Button.OnClick` opens the flyout; invoke has always opened it. What failed was
>   *seeing* the result.
> - *"`press_key Space` ... does not open it either"* and the heading *"cannot be opened"* — false for the
>   same reason.
>
> The single real cause: a `ContextMenu` and a `Button.Flyout` are attached with
> `ISetLogicalParent.SetParent`, so their `Popup` is never a **visual** child of anything and a visual walk
> cannot reach it however deep it goes. A menu-bar dropdown's popup *is* a visual child, which is why menus
> looked like they worked and these did not.
>
> `AttachedPopupOf` now finds a popup from its owner by all three routes — `Button.Flyout`,
> `FlyoutBase.GetAttachedFlyout`, and `Control.ContextFlyout` — and feeds it to the existing popup walk, so
> the items appear as children of the owner and their paths round-trip. `SnapshotComposer` shares the same
> definition, so a popup you can address is one you can also see. A control owning either now advertises
> `expandCollapse`, and `expand`/`collapse` open and shut it: the verb existing while nothing said so is how
> these eight commands came to be recorded as unreachable.
>
> Measured live, all the way through:
>
> ```
> interact invoke  Button[Standard.AddForm]        → success
> dump_visual_tree root=<that button>
>   → Pane/MenuItem[Form], MenuItem[MDI Form] (isEnabled:false), MenuItem[Module], … MenuItem[Add File...]
> interact invoke  …/Pane/MenuItem[Module]         → success
> get_project_info                                 → modules: ["Module1"]
> take_snapshot                                    → the flyout is in the picture
> ```
>
> Not merely observable: driven end-to-end. And `MDI Form` reporting `isEnabled:false` answers the
> Consequence's "not even *observed* to be enabled or disabled".


**Symptom.** The Add Item toolbar button (`Standard.AddForm`) carries a `MenuFlyout` holding eight
Add commands. None of it is reachable:

- `interact invoke` on the button reports success and does nothing visible — it fires the button's own
  invoke, which is not what opens a flyout.
- `interact expand` fails: the button advertises only an `invoke` provider, no `expandCollapse`.
- `press_key Space` on the button reports success and does not open it either.
- `dump_visual_tree(root=<button>)` returns the button with `children: []`, both with
  `interactiveOnly: false` and at full depth — the flyout's items are simply not in that subtree.
- A synthetic Win32 click at the button's rect (DPI-corrected, see below) did not open it either.

**Consequence.** Eight toolbar commands cannot be verified through MCP at all — not driven, and not even
*observed* to be enabled or disabled. Note the contrast that makes this easy to misdiagnose: menu-bar
dropdowns work fine (`interact expand` on `MenuItem[Project]` opens it, and its items appear in the tree and
in `take_snapshot`), so the obvious inference is that "menus work" — they do, but only the menu bar's.

**Workaround.** Verify the command, not the menu item. `inspect_element` on the button lists the
DataContext's members, so the presence of the bound command (e.g. `AddFileCommand`) is confirmable there,
and the same command can be driven end-to-end through its menu-bar twin where one exists. Say explicitly
that the toolbar entry was verified structurally rather than driven.

**Suggested fix.** Give a control that owns a `FlyoutBase` an `expandCollapse` provider, so `interact
expand` opens it and the popup's contents then become dumpable; failing that, an `open_flyout(target)`
action. Whichever route, the flyout's items need to reach `dump_visual_tree` — a popup that opens but
cannot be walked only moves the problem.
