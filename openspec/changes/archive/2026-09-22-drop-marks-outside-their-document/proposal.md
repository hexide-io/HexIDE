# Drop marks outside their document when the sidecar loads

## Why

A sidecar can hold a bookmark or a breakpoint on a line its document does not have. Loading one restored the
mark as it was: drawn nowhere, never hit, still reported by `get_breakpoints` and `get_bookmarks`, and written
back on every save. Measured before hexide-io/HexIDE#570 on a two-line module: marks on `-1, 0, 99` survived a
relaunch unchanged (hexide-io/HexIDE#574).

hexide-io/HexIDE#570 stopped `set_breakpoints` and `set_bookmarks` from writing such lines. It did nothing
about sidecars already written that way, or about the other two ways one arises: a document shortened outside
HexIDE while its sidecar keeps the old lines, and a sidecar edited by hand.

## What changes

- When the sidecar is applied, each document's marks are checked against the lines it has, counted as the
  editor numbers them: the code without its Attribute header, bookmarks from 0, breakpoints from 1.
- A mark outside that range is dropped, and a warning in the log names the document, the lines dropped and
  the range that was valid.
- The next save writes the sidecar without them.

## Deliberately not in this change

**Moving a mark instead of dropping it.** Clamping a breakpoint on line 99 of a ten-line file to line 10 would
put it on a line nobody chose. With no record of what the line was, there is nothing to move it by.
