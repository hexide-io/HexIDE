## Ambient's LocaleID under other user locales, and DisplayName on a control-array element (2026-11-16, #4149, SP6)

**The question.** Two of a UserControl's `Ambient` members that the Ambient measurements (#4149) left open:
- What `Ambient.LocaleID` reports when the user locale isn't US English. Is it the user's locale?
- What `Ambient.DisplayName` reports for an element of a control array, such as `AmbCtl1(0)`.

**The probe.**
- **The build:** the Ambient probe's project, a form hosting a UserControl, `AmbCtl`, built with `/make` on vb6.exe SP6.
  - **The control** logs every Ambient member, with its value and type, from `ReadProperties` and `Show`.
  - **One change:** the form's `AmbCtl1` has `Index = 0`, and the form's `Load` adds `AmbCtl1(1)` with
    `Load AmbCtl1(1)` and shows it.
- **The run:** the EXE in the guest's console session.
- **The guest's locale settings,** read with PowerShell before the run:
  - the user locale (`Get-Culture`) is **en-GB, 2057**;
  - the system locale (`Get-WinSystemLocale`) is **en-US**;
  - the first user language, the UI's, is **en-US**.
- **The second run:** under a German user locale, set with `Set-Culture de-DE`, which a new process read back as
  de-DE, 1031.
- **The restore:** afterwards every value under the user's `Control Panel\International` key was restored from a
  snapshot taken first: 56 values, none differing.

| Member | User locale en-GB (2057) | User locale de-DE (1031) |
|---|---|---|
| `Ambient.LocaleID`, `AmbCtl1(0)` and `(1)`, in `ReadProperties` and `Show` | **1033 (&H409)** | **1033 (&H409)** |
| `Ambient.DisplayName`, the design-time element | **`AmbCtl1(0)`** | `AmbCtl1(0)` |
| `Ambient.DisplayName`, the element loaded at run time | **`AmbCtl1(1)`** | `AmbCtl1(1)` |

**Readings:**
- **`Ambient.LocaleID` doesn't follow the user locale.** It was 1033 under both en-GB and de-DE. So the 1033 the Ambient
  measurements recorded was taken under an en-GB user locale too, not under en-US as might be assumed.
  - **The source is unknown.** It might be the system locale, the UI language or a constant: on this guest all three
    are 1033, so no row here tells them apart.
  - **Telling them apart** needs a Windows whose system locale or display language isn't US English. That would take
    a reboot or a language pack, so it wasn't done here.
- **`Ambient.DisplayName` on a control-array element is the name with its index in parentheses,** for an element
  placed at design time and for one loaded at run time alike.

**Not measured:**
- `LocaleID` under a non-US system locale or display language. The earlier design-time and IDE rows were taken on this
  guest, so they too read 1033 under an en-GB user locale;
- `DisplayName` for an element of an array whose index isn't 0 or 1.
