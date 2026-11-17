## Selected on a MultiSelect ListBox scrolls the item into view (2026-11-16, #4229, SP6 native and p-code)

**The question.** Setting `Selected(i) = True` from code on a single-select ListBox brings the item into view. Does it on
a MultiSelect list, Simple or Extended, where `a-listboxs-scroll-event.md` left MultiSelect unmeasured? #4229's change
needed the answer: HexIDE had scrolled there only through Avalonia's own scroll to the selection, which the change turns
off.

**The probe.**
- **The form:** three ListBoxes, each 1200 by 1425 twips, with 20 items, `item0` to `item19`. List0 is single-select
  (MultiSelect 0), List1 Simple (1) and List2 Extended (2). It's the form of the #3986 MultiSelect probe.
- **The build:** `/make` as native code and as p-code, VB6 SP6.
- **The run:** the form is loaded and filled, then shown with `DoEvents`. Each row sets the same thing on all three
  lists, then reads `TopIndex`, `ListIndex`, `SelCount` and the count of Scroll events so far.
- **The result:** both builds gave the same rows.

| Step, from the state the row above left | List0 (single) | List1 (Simple) | List2 (Extended) |
|---|---|---|---|
| shown | Top 0, ListIndex -1 | Top 0, ListIndex 0 | Top 0, ListIndex 0 |
| `Selected(15) = True`, below the view | **Top 9**, ListIndex 15, no Scroll | **Top 9**, ListIndex 15, SelCount 1, no Scroll | **Top 9**, ListIndex 15, SelCount 1, no Scroll |
| `TopIndex = 13` | Top 13, one Scroll | the same | the same |
| `Selected(2) = True`, above the view | **Top 2**, ListIndex 2, no Scroll | **Top 2**, ListIndex 2, SelCount 2, no Scroll | **Top 2**, ListIndex 2, SelCount 2, no Scroll |

**Readings:**
- **`Selected(i) = True` scrolls a MultiSelect list exactly as it does a single-select one.** The view moves the least it
  can to show the item: to the bottom row from below (`TopIndex` 9 shows rows 9 to 15) and to the top row from above.
- **`ListIndex` follows the item on a MultiSelect list too,** and the earlier selection stays: `SelCount` goes to 2.
- **No Scroll event is raised.** That matches the single-select rows in `a-listboxs-scroll-event.md`.

**Not measured:**
- `Selected(i) = False` on an item out of view;
- a list that isn't shown yet when `Selected` is set.
