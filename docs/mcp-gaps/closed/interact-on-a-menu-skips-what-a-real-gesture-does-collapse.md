# `interact` on a menu skips what a real gesture does: `collapse` leaves the menu bar active, `invoke` leaves the menu open — **CLOSED** (#947, 2026-10-20)

> **Fixed.** `interact` on a menu now does what a click does, in the order Avalonia 12's own menu handling uses.
> `invoke` on an item moves a checkable item's tick, raises Click, and closes the whole menu, bar included, unless
> the item stays open on click. Its reply says so: `invoked MenuItem 'Minimap' (now unticked) and closed the menu, as
> a click does`. Context menus and flyouts close the same way. `collapse` on a menu bar's header closes the bar with
> it: `closed menu 'Format' and left the menu bar`, and a `collapse` with nothing open says there is nothing to
> close. A disabled item is refused rather than clicked, with a focus hint when its command is routed. `toggle` on a
> menu item says that `invoke` clicks it and reports whether the tick moved. `invoke_menu_item` moves the tick of a
> checkable item that has no command, such as View/Minimap, where it used to refuse. Guarded by `MenuPopupTests`, and
> measured on Windows by an agent driving Format, View/Minimap and the Project Explorer's flyout.


**Symptom.** (Linux, openSUSE Leap 16 under XWayland, Debug build of main at b15c7cf, 2026-10-13.)

```
interact {"target":"Window/Custom[MainView]/None[#0]/Menu[MenuBar]/MenuItem[Format]","action":"collapse"}
→ closed menu 'Format'
```

The Format header stayed highlighted in every later snapshot until
`press_key {"target":"Window/Custom[MainView]/None[#0]/Menu[MenuBar]","key":"Escape"}`. `interact` `invoke` on the
Project menu's `MenuItem[Remove Form2]` left the Project dropdown open, still drawn over the Object Browser after
`activate_document_tab`. `collapse` closes only the submenu, not the menu bar, and `invoke` only raises Click,
where a real click also toggles a checkable item and closes the menu. So, read in code and not run, `invoke` on
the eight checkable items (the toolbar band's flyout, View > Toolbars and Minimap) changes nothing, and `toggle`
and `invoke_menu_item` refuse them. Cross-platform by code reading.

**Workaround.** `press_key` Escape on the MenuBar clears a header that `collapse` left active. What closes a
dropdown that `invoke` left open was not measured, so check a snapshot after any menu `invoke`.

**Suggested fix.** Do what Avalonia 12.1.4's own peer does: `collapse` on a top-level item closes the menu bar,
and `invoke` toggles a checkable item, raises Click, then closes the menu unless `StaysOpenOnClick`. Have
`MenuPopupTests` assert that the menu is closed after each. [#947](https://github.com/hexide-io/HexIDE/issues/947).
