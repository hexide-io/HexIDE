# `interact` `select` on a dropdown item leaves the dropdown open — **CLOSED** (#907, 2026-10-20)

> **Fixed.** `select` on an item in an open dropdown now closes the dropdown, as clicking the item does, and its reply
> says so: `selected 'VBA' and closed the dropdown, as a click does`. That holds for `select` on the item itself and
> for `select` with a `value` on the ComboBox. `double_click` on such an item does the same and raises no
> double-click, since a real one's second click lands on what lay beneath. The item's peer sets the selection and
> nothing else, and Avalonia's ComboBox closes its popup only from pointer, key and visibility handling. The
> workaround under "A collapsed ComboBox reports only its selected item", which said the popup closes between calls,
> is corrected: that described the IDE window losing focus to the caller, which closes a light-dismiss popup.
> Guarded by `UiAutomationDriverTests`.

**Symptom.** On the Object Browser's library box (Linux, openSUSE Leap 16 under XWayland, Debug build of main at
b15c7cf, 2026-10-13):

```
interact {"target":".../List[#0]/Pane[#0]/Custom/ComboBox","action":"expand"}
interact {"target":".../ComboBox/Pane/ComboBoxItem[VBA]","action":"select"}
→ selected 'VBA'
```

The selection changed, but the popup stayed open over the Classes list until
`interact {"target":".../ComboBox","action":"collapse"}`. Clicking the item closes it, and the reply does not say
it is still open. `select` sets the selection through the item's peer, and Avalonia's ComboBox closes its popup on
pointer, key and visibility changes, never on a selection change. The mechanism is cross-platform by code
reading; whether a caller sees the popup linger depends on whether the IDE window keeps focus. Same family as the
menu entry still open in `docs/mcp-server-gaps.md` ([#947](https://github.com/hexide-io/HexIDE/issues/947)): the
driver acts on a control without the close a real gesture performs.

**Workaround.** After `select` on a dropdown item, `collapse` the ComboBox. Do not rely on the popup closing
between calls.

**Suggested fix.** After selecting in an open dropdown, `select` closes it as a click does, and says so in its
reply. [#907](https://github.com/hexide-io/HexIDE/issues/907).
