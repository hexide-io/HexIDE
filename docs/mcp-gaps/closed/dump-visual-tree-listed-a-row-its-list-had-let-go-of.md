# dump_visual_tree listed a row its list had let go of (#1292)

A list whose rows bind their visibility to the item, as the Properties window's Categorized list does, keeps a recycled
`ListBoxItem` in the visual tree once its virtualizing panel has scrolled: no owner, no index, still holding the last
item it showed, and showing by its binding. #1256 stopped `select` matching one; the dump still listed it as a row, a
second copy of an item or a row for one out of view, and a path to it resolved.

> **Fixed.** The control view's collector, which the dump and path resolution share, and the `#AutomationId` search
> leave out a released row and everything in it, by #1256's `Released` test, now held to an items panel's own children,
> so a container written into a template or set as content is still listed. Rows declared in `Items` and scrolled out of
> view are left out with the rest, as generated rows out of view always were. Guarded by `SelectScrolledOutRowTests`:
> after a `select` scrolls and puts the list back, and the released row's item shows again, the dump lists the rows the
> list holds, each once, and neither a path nor an id reaches the released row.
>
> **Live (#1330, 2026-10-25).** Windows, a Debug build of main at beada72, from a Claude Code session's hexide tools,
> with Form1 selected and the Categorized tab showing.
>
> - **`interact` `select` with value `Width`** on `…/List[CategorizedProperties]` replied "selected 'Width' (it was
>   scrolled out of view, so the list was scrolled to it first)".
> - **The dump that followed** (`interactiveOnly` false, `maxDepth` 6) listed the four rows in view, Width, Top,
>   StartUpPosition and Left. Each appeared once, and nothing out of view was listed.
> - **The dump's order is the reverse of the screen's:** [#1966](https://github.com/hexide-io/HexIDE/issues/1966).
>   Fixed in #1966: the shared collector orders items-panel containers by their owning list's item index,
>   with non-container children afterwards. Two Linux headless regressions reproduce the reversed end-jump
>   and selected-third-row-first cases before the fix, and check item order, uniqueness and path resolution
>   afterwards (`SelectScrolledOutRowTests`). Live verification of the Properties list remains for review.
> - **Not met: a row out of view is listed.** After `scroll` `home` and a collapse of Appearance, the next dump
>   listed Width, the selected row, with `isOffscreen: true`, and so did the one after Appearance was expanded. The
>   fix says rows out of view are left out: [#1981](https://github.com/hexide-io/HexIDE/issues/1981).
> - **Collapsing the Appearance category** left its rows listed once each with `isHidden: true`, and expanding it
>   showed them once each again. That was done with its toggle, since the row itself refuses `collapse`:
>   [#1967](https://github.com/hexide-io/HexIDE/issues/1967).
