# `expand` also opens a control's context menu or flyout, and no description says so

**Symptom.** (Linux, openSUSE Leap 16 under XWayland, Debug build of main at b15c7cf, 2026-10-13.)

```
dump_visual_tree {"maxDepth":3}
→ Window/Custom[MainView]/None[#0]   (the menu band's StackPanel)   providers ["expandCollapse"]
  TabItem[…]/None[PART_TabBody]      (a Border)                     providers ["expandCollapse"]
dump_visual_tree {"root":"Window/Custom/Custom","interactiveOnly":false,"maxDepth":2}   (Options > Editor > Formatting)
→ the Tab width TextBox                                             providers ["value","expandCollapse"]
```

The run read these as phantom tokens. They are not: each control owns a popup that `expand` opens, by design since
#343. The StackPanel owns the Toolbars context flyout, `PART_TabBody` Dock's document context menu, and every
TextBox the theme's Cut/Copy/Paste flyout. But `interact`'s `action` description defines expand and collapse as
"a dropdown, tree node or expander", and `dump_visual_tree` says only "expandCollapse → expand/collapse", which
is how #836 and its entry (now archived) came to say that no tool opens a context menu. `collapse` with the popup closed
answers "element does not support 'collapse'" while the token is still shown. Cross-platform by code reading.

**Workaround.** On a control with no expander of its own, read `expandCollapse` as "owns a context menu or
flyout": `expand` opens it, and its items then appear under it in `dump_visual_tree`.

**Suggested fix.** Say so in `interact`'s `action` description and, in a few words, in `dump_visual_tree`'s token
list, and have `collapse` with nothing open say there is nothing to close.
[#949](https://github.com/hexide-io/HexIDE/issues/949).

> **Fixed.** `interact`'s `action` description lists, under expand and collapse, a menu, or else the context menu or
> flyout the control owns. `dump_visual_tree`'s token list says the same of `expandCollapse`, and CLAUDE.md adds that
> the popup's items then appear under the control. Room in the two descriptions came from shorter wording of `scroll`'s
> reply and of the opening of `dump_visual_tree`'s. Tests pin the new text, so a later trim cannot drop it unseen.
>
> Every reply says what was already the case:
>
> - `collapse` with the popup closed says it was not open and there is nothing to close, as a closed menu's did,
>   rather than "element does not support 'collapse'";
> - `expand` on an open popup, menu or dropdown says it was already open;
> - `collapse` on a closed dropdown says it was already collapsed.
>
> Its review found three more problems, now fixed:
>
> - `collapse` on the code editor with the completion list open said its flyout had nothing to close, while the dump
>   showed the list under it. That is now refused, pointing to `press_key Escape`.
> - The theme's TextBox context flyout is one instance shared by every box. An open one was closed, and listed in the
>   dump, under every box. Both now go by the flyout's target.
> - `expand` prefers a ContextMenu to a flyout, which `FlyoutsOf` now records.
>
> Guarded by `LogicallyParentedPopupTests`, which cover the four routes to a popup, `CodeEditorPopupTests`,
> `MenuPopupTests` and `InteractClickDescriptionTests`.
>
> **Live (#1330, 2026-10-25).** Windows, a Debug build of main at beada72, from a Claude Code session's hexide tools, in
> Tools ▸ Options ▸ Form Designer ▸ Grid, a page with two TextBoxes:
>
> ```
> interact {"target":"Window/Custom/Custom/Edit[Grid Width:]","action":"expand","window":"Options"}
> → opened flyout on 'Grid Width:'
> dump_visual_tree {"window":"Options","root":"Window/Custom/Custom","interactiveOnly":false,"maxDepth":10}
> → MenuItem[Cut], MenuItem[Copy] (both disabled) and MenuItem[Paste] under Edit[Grid Width:], none under Edit[Grid Height:]
> expand again   → the flyout on 'Grid Width:' was already open
> collapse       → closed flyout on 'Grid Width:'
> collapse again → the flyout on 'Grid Width:' was not open; there is nothing to close
> ```
>
> With the code window's completion list open, `collapse` on its `TextEditor` was refused: "'TextEditor' has a popup
> open that collapse does not close, such as the completion list; press_key Escape closes it".
