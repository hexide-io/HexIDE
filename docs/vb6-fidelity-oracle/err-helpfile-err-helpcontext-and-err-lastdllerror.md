## Err.HelpFile, Err.HelpContext and Err.LastDllError (2026-10-28, #2527)

Measured on vb6.exe SP6, every row built native and as p-code, which logged the same lines. The oracle VM is an en-GB machine: `Format(1234.5, "Currency")` is `£1,234.50` and a date reads `28/10/2026`.

**What the rows say:**
- Fresh, `HelpFile` is `""` and `HelpContext` 0, a Long. `Err.Clear` puts both back.
- `Err.Raise` sets both from its arguments. **When `HelpContext` is omitted, an error VB6 itself knows gets 1000000 plus
  its number** (5 gives 1000005, and 11 and 6 raised by division and overflow give 1000011 and 1000006). An
  application-defined number (1000) gets 1000095. `HelpFile` stays `""` when omitted, and `App.HelpFile` is `""` here.
- Both can be written. `HelpContext` takes a numeric String and 70000; it is a Long.
- **`LastDllError` is 0, a Long, and read-only**: `Err.LastDllError = 3` is VB6's compile-time "Can't assign to read-only
  property". VB6 sets it after each call to a `Declare`d function. HexIDE skips `Declare` (MISSING_LANGUAGE's `Declare` row).

| Read | Logged |
|---|---|
| `fresh` | `HelpFile [] HelpContext 0 Long LastDllError 0 Long` |
| `Raise 5, src, desc, help.hlp, 77` | `HelpFile [help.hlp] HelpContext 77` |
| `after Clear` | `HelpFile [] HelpContext 0` |
| `Raise 5` | `HelpFile [] HelpContext 1000005 Description [Invalid procedure call or argument]` |
| `Raise 1000` | `HelpFile [] HelpContext 1000095 Description [Application-defined or object-defined error]` |
| `1 / 0` | `Number 11 HelpFile [] HelpContext 1000011` |
| `overflow` | `Number 6 HelpFile [] HelpContext 1000006` |
| `set HelpFile and HelpContext` | `err 0 [mine.chm] 12` |
| `HelpContext = "13"` | `err 0 13 Long` |
| `HelpContext = 70000` | `err 0 70000` |
| `GetFileAttributes(missing)` | `-1 LastDllError 3 Number 0` |
| `GetFileAttributes(C:\Windows)` | `16 LastDllError 0` |
| `after Clear` | `LastDllError 0` |
| `Raise 5, , , h.hlp` | `HelpFile [h.hlp] HelpContext 1000005` |
| `Raise 5, , , , 9` | `HelpFile [] HelpContext 9` |
| `App.HelpFile []` |  |
