### Fixed

- **ListBox Page Up follows VB6** (#3986). On a single-select list it moves the selection a page up, the rows shown less one, from wherever the selection is, as Page Down already moved it down, raising Click and then Scroll when the view moves. Before, Avalonia scrolled the view and left the selection. Selected and Text assignments now bring the item into view before Click, without Scroll.
