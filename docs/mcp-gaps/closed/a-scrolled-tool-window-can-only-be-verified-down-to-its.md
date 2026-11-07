# A scrolled tool window can only be verified down to its first screenful — **CLOSED** (#361, 2026-09-22)

> **Fixed, both halves, each measured on the running IDE.** `interact` now has a `scroll` action (`value`
> = `up`/`down`/`left`/`right` for a page, `line_up`/`line_down`/`line_left`/`line_right`, `home`/`end`).
> It moves the target, or the nearest control containing it that can scroll along the requested axis, so a
> caller can aim at the content it wants more of rather than at a `ScrollViewer` that is often a template
> part. Driven against the Properties list: `scroll down` aimed at a property *row* paged the list to
> 28.8%, and the snapshot showed rows below the fold. The reply says where the scroller now is, and a
> scroll that cannot move (`nothing to scroll vertically`, `already at that end`) fails rather than
> reporting a success that changed nothing.
>
> **The "cannot read the tab's content" half was never real, and neither was "cannot scroll" (#362).**
> The entry's attempt ran `dump_visual_tree` with its defaults, and `interactiveOnly: true` filters out
> plain text. With `interactiveOnly: false, maxDepth: 14` the same document returned every field of every
> server card; `inspect_element` on one card returned its whole `LanguageServerRowViewModel`, including a
> card below the fold; and `interact invoke` on the tab's own `PART_PageDownButton` already scrolled it,
> moving that card from y = 870 to 607. So before #361 scrolling was possible but roundabout, and
> reading was possible all along. What #361 added is a direct `scroll` action.
>
> An earlier version of this note said the reading half "had already closed" before #361. That was wrong
> in the same way as the entry: it read a working result as a fix, when nothing had been broken. The entry
> below is kept as written, because the mistake in it is the useful part.
>
> **What the entry did not notice.** `dump_visual_tree` was already *advertising* a `scroll` token, on 42
> nodes, while `interact` had no such action, so the capability this entry asked for was being promised
> and refused at the same time. Those phantom tokens also kept 40 otherwise-inert nodes in the default
> tree. With the action added and the token reported only where something would actually scroll, the
> default tree of a fresh Standard EXE project went from 256 nodes to 198.


**Symptom.** The Language Servers window (#259) lists every attached server, one card each, so its content
is routinely taller than the pane. `take_snapshot` captures what is painted, and there is no way to scroll
the content, so every server below the fold is unverifiable. Hiding a bottom-docked tool via
`set_tool_window_visible` buys one more card and no more.

**What was tried.** `press_key` with `End`/`PageDown` needs a `target` path, and the only addressable node
in that region is the `TabItem` — pressing a key there does not reach the `ScrollViewer` inside the tab's
content. `dump_visual_tree` returns the tab chrome (the `TabItem`, its close `Button`, its header `Text`)
but not the realised card content beneath it, so the values could not be asserted structurally either.
Two shapes of the same limit: the content of a document tab is neither drivable nor readable.

**Consequence.** Verification stopped at "the first two groups render correctly, with real servers and the
right fields". The case that most wanted checking — a server further down the list whose row shows
`Running` with nothing advertised — was covered by a view-model test instead. That is a reasonable place
for it, but it means the *rendering* of the most diagnostic row in the window is unverified, which is
exactly the substitution the visual-verification rule exists to prevent.

**Workaround used.** Assert the projection in `LanguageServersToolViewModelTests` and snapshot only what
fits. Note this is weaker than it sounds: a binding typo renders an empty row and passes every view-model
test.

**Suggested fix, cheapest first.** A `scroll` action on `interact` (`value` = `up`/`down`/`home`/`end`,
resolving to the nearest ancestor `ScrollViewer`) would close it for every scrolled surface at once, not
just this one — the Object Browser, the Translation Editor and the Locals tree have the same shape. Failing
that, `take_snapshot` could accept an optional element `path` and capture that element at its full desired
size rather than clipped to the viewport, which would also make long content diffable.
