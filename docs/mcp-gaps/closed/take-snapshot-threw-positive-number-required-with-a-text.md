# `take_snapshot` threw "Positive number required" with a text box's flyout reopened in a dialog — **CLOSED** (#1960, 2026-10-26)

**Symptom (2026-10-25, Windows, IDE in Arabic).** With Replace open and its first box's Cut/Copy/Paste flyout reopened by a
second `interact` `expand`, `take_snapshot()` and `take_snapshot(window: 'استبدال')` failed with an unhandled
`System.ArgumentException: Positive number required (Parameter 'size')`, thrown from `RenderTargetBitmap`'s constructor
in `SnapshotComposer.Compose`. Escape closed the flyout and the next snapshot worked.

**Workaround (before the fix).** Close the popup, then take the snapshot.

**Cause (by decompiling Avalonia 12.0.4 and its Skia backend).** That message comes only from a render target whose
sides are each at least 1 and whose size in bytes, an `Int32`, has wrapped negative: past 2 GiB. A side below 1 gives a
different message. The canvas is the union of the window and every open popup in screen pixels, so a popup reported
far from the window does it: one at (-32000, -32000) throws the same exception. One whose own size is the largest
`Int32` does too. Where the reopened flyout was really reported is not established.

**Fixed (#1960).** `Compose` leaves out a popup that lies on no screen, and one that would take the image past
16,384 × 16,384 pixels' worth, reckoning the extent in longs. `take_snapshot`'s `note` names each: what it held, where
it was reported in screen pixels, and why. A popup with no area is named too. The window and every other popup are
drawn. `SnapshotComposerTests` and `SnapshotLeftOutNoteTests` pin it. A wrap that lands positive is worse than the
exception, since the canvas is then too small for what is drawn into it, and the cap covers that as well.

**What is still owed.** If a snapshot carries the note, its coordinates are what explains the original placement.

**Live (#1960, 2026-10-26).** Windows, a Debug build of the fix, from a Claude Code session's hexide tools. The report's
steps did not reproduce the failure: across six cycles of `collapse` and `expand` on Replace's first box (three in English,
three in Arabic, the dialog titled `استبدال`), 14 snapshots each drew the flyout inside the 546×291 dialog, with no note.
Where the reported flyout really was placed is not established, so the fix is pinned by the composer's tests, not by a
live reproduction.
