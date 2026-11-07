## Seven system colours (2026-10-22, #908)

**The probe.** A pixel-mode form holding eight Labels, each with no caption and one system colour as its BackColor.
It was built as p-code and run in the oracle guest's console session, through the agent described in "Reaching the
VB6 *IDE*, not just the compiler" above. 1.5 seconds after load, a Timer logged three values for each Label:
- the BackColor, read back;
- `Me.Point` at the Label's centre;
- `GetSysColor` for the colour's index, called through a `Declare`.

The values below are written as `RRGGBB`, not as the `&H00BBGGRR` a COLORREF holds.

| Colour | BackColor | Index | Point | GetSysColor |
|---|---|---|---|---|
| Scroll Bars | `&H80000000` | 0, `COLOR_SCROLLBAR` | `C8C8C8` | `C8C8C8` |
| Active Title Bar | `&H80000002` | 2, `COLOR_ACTIVECAPTION` | `99B4D1` | `99B4D1` |
| Active Border | `&H8000000A` | 10, `COLOR_ACTIVEBORDER` | `B4B4B4` | `B4B4B4` |
| Inactive Border | `&H8000000B` | 11, `COLOR_INACTIVEBORDER` | `F4F7FC` | `F4F7FC` |
| ToolTip Text | `&H80000017` | 23, `COLOR_INFOTEXT` | `000000` | `000000` |
| ToolTip | `&H80000018` | 24, `COLOR_INFOBK` | `FFFFE1` | `FFFFE1` |
| Window Frame | `&H80000006` | 6, `COLOR_WINDOWFRAME` | `646464` | `646464` |
| Button Face, as a control | `&H8000000F` | 15, `COLOR_BTNFACE` | `F0F0F0` | `F0F0F0` |

So vb6.exe paints a system colour exactly as `GetSysColor` answers for its low byte, on this Windows 10 with its
default colours. The last two rows are controls. Window Frame's `646464` matches what #1077 measured from a Flat
label's border, by a different route. Button Face's `F0F0F0` matches the value HexIDE already had.

HexIDE had no value for the first six. A control given one painted `DeepPink`, the fallback for a resource nothing
defines, in the designer and at run time. Its theme now carries these values (`Themes/Classic.axaml`). They are
Windows 10's, so they follow #806's rule that a theme pack does not restyle the user's program. Two things are not
measured: another Windows version's colours, and a user's own colour scheme. Both are what #1137's platform skins
would own.

