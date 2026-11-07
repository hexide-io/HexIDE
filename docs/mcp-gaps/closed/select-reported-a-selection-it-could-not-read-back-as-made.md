# `select` reported a selection it could not read back as made (#1259)

`select` reads the selection back before reporting it (#1182), but the read fell back to *selected* when the provider's
`IsSelected` threw. A throwing peer was replied to as `selected 'X'`, and `double_click` counted its first click as a
selection, though nothing had been read: a fail-open default in the step that exists to verify.

> **Fixed.** `IsSelected` is read as selected, not selected, or unread. An unread row its list still holds is read
> from the list's own mark on it, which is all Avalonia's peer reads, so a list that puts the selection back, or picks
> another row of the same value, is refused as before, and a row already selected is taken. A row its list has let go
> is taken if the list announced it, as before. With neither, the reply says select was sent but whether it took is not
> known, and says so of an item with no list rather than blaming one; `double_click` says it is not known whether it
> selected first, rather than that it did not. No provider in the tree throws there; HexIDE implements no
> `ISelectionItemProvider` of its own. Guarded by `SelectUnreadTests`, with a substitute provider that throws over a
> real `ListBox`. The review listed the driver's other reads that report success unread, which #1280 holds.
