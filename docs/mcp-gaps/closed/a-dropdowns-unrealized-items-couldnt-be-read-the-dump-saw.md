# A dropdown's unrealized items couldn't be read: the dump saw ten of Options' 31 languages — **CLOSED** (#2450, 2026-10-28)

**Symptom (2026-10-28, hexide-mcp, a first-contact check of #2444).** The Language dropdown on Options' Language page
holds 31 items (`inspect_element` gave `itemCount` 31), but only the first ten had rows. `dump_visual_tree` with
`interactiveOnly: false`, after an `expand`, listed Arabic through Finnish and stopped, and `match: "fr"` found
nothing. `select` with `value: "French"` worked, by scrolling the list until French had a row, but nothing before
that told the caller French was there or how it was spelled. The headless session reported: "I couldn't see that
French exists beforehand."

**Fixed (#2450).** `inspect_element` on a list, an open dropdown or a tab control gives `items`: each item's text as
`select` matches it. A row that doesn't exist is brought into view to be read, as `select` does, and the list is put
back where it was before anything is drawn. Items that don't show are left out, as `select` passes over them, and at
most 200 are listed; `itemsNote` says what was left out. A closed dropdown has no rows to bring into view, and opening
it is a person's action, not a read, so it gives no `items` and `itemsNote` says to expand it. Guarded by
`InspectItemTextsTests`.

**Live (2026-10-28, Windows, a headless `claude -p` told to work from the descriptions alone).** Asked to find
every language the dropdown offers without selecting one, it inspected the closed dropdown and read `itemCount` 31
and the `itemsNote` telling it to expand. It expanded, inspected again, and listed all 31, from "System default"
to "Vietnamese", then collapsed it and cancelled the dialog. What it still tripped on was the two dropdowns having
no accessible name, which is #2449.
