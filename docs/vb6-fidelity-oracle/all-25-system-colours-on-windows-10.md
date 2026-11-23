## All 25 system colours on Windows 10 (2026-11-23, #5410, SP6)

**vb6.exe paints every system colour, index 0 to 24, exactly as `GetSysColor` answers for it.** This extends "Seven system colours" (#908) to the whole range. Eight of the 25 differ from the values HexIDE's two palettes held when measured. Highlight, for one, is `0078D7`, where HexIDE had `000080`.

**The probe** is #908's, widened:
- **The form:** a pixel-mode form holding 25 Labels with no caption, one for each `&H80000000` to `&H80000018` as its BackColor.
- **The run:** built with `/make` as p-code on VB6.EXE 6.00.9782 (SP6) with MSVBVM60 6.00.9848. Run in the Win10-C clone's console session through its agent, since session 0 paints nothing to read.
- **The read:** 1.5 seconds after load, a Timer logged three values for each Label: the BackColor read back, `Me.Point` at its centre, and `GetSysColor` for the low byte, through a `Declare`.
- **The guest:** Windows 10 Enterprise LTSC 2021, default colours, 100% scaling.

Values are written `RRGGBB`, not as a COLORREF's `&H00BBGGRR`. **Every BackColor read back as set, and every `Point` equalled `GetSysColor`.** So the Point column is the only one shown. "HexIDE" is the value in `IDE/HexIDE/Themes/Classic.axaml` and `IDE/HexIDE.Runtime/BuiltinControls/SystemColors.axaml`, which agreed on all 25.

| Index | VB6 name | Point | HexIDE |
|---|---|---|---|
| 0 | Scroll Bars | `C8C8C8` | same |
| 1 | Desktop | `000000` | same |
| 2 | Active Title Bar | `99B4D1` | same |
| 3 | Inactive Title Bar | **`BFCDDB`** | `ABABAB` |
| 4 | Menu Bar | `F0F0F0` | same |
| 5 | Window Background | `FFFFFF` | same |
| 6 | Window Frame | `646464` | same |
| 7 | Menu Text | `000000` | same |
| 8 | Window Text | `000000` | same |
| 9 | Active Title Bar Text | **`000000`** | `FFFFFF` |
| 10 | Active Border | `B4B4B4` | same |
| 11 | Inactive Border | `F4F7FC` | same |
| 12 | Application Workspace | `ABABAB` | same |
| 13 | Highlight | **`0078D7`** | `000080` |
| 14 | Highlight Text | `FFFFFF` | same |
| 15 | Button Face | `F0F0F0` | same |
| 16 | Button Shadow | **`A0A0A0`** | `808080` |
| 17 | Disabled Text | **`6D6D6D`** | `808080` |
| 18 | Button Text | `000000` | same |
| 19 | Inactive Title Bar Text | **`000000`** | `666666` |
| 20 | Button Highlight | `FFFFFF` | same |
| 21 | Button Dark Shadow | **`696969`** | `404040` |
| 22 | Button Light Shadow | **`E3E3E3`** | `FFFFFF` |
| 23 | ToolTip Text | `000000` | same |
| 24 | ToolTip | `FFFFE1` | same |

**What it shows:**
- **#908's rule holds for every index.** A system colour is whatever `GetSysColor` says for its low byte.
- **Indices 16, 21 and 22 confirm by a direct read** the values that #918 and "A Label's border" took from 3-D edges: `A0A0A0`, `696969` and `E3E3E3`.
- **Eight indices differ from HexIDE.** Where its values came from is not recorded beside them. Highlight `0078D7` is the one most visible in a running program: a ListBox fills its selected items with it ("A ListBox's selected and focused items, drawn", #5473).
- **Index 9 is black,** where HexIDE had white, and so is index 19, where it had `666666`.

**The obvious guess,** that Button Highlight (20) and Button Light Shadow (22) are both white, holds only for 20. That matters for keys that map them, because the two differ by `1C1C1C` on every 3-D edge.

**Not measured:**
- another Windows version, a user's own colour scheme, or high contrast;
- scaling other than 100%;
- what VB6's IDE draws for these in its own chrome.

#806 and #1137 own other platforms' values.
