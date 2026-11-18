## A form's window icon: no Icon line, (None), and an icon of its own (2026-10-24, #1594, SP6)

**The question.** #1575's probe read only forms with no Icon line: VB6's default icon as the window's big icon, and no
small one. The usual account says a Fixed Dialog therefore shows no icon on its caption. HexIDE gave every running
window Avalonia's icon, big and small, whatever the file said. What does VB6 do with each kind of Icon, and what does
Windows draw?

**The probe.** VB6 SP6 (VB6.EXE 6.00.9782). One project of six forms was built by `/make`, native and p-code, and run
on the guest's console. There were two BorderStyles, 3 (Fixed Dialog) and 2 (Sizable), and three Icons for each:
- no Icon line;
- `Icon` = (None);
- a 32 × 32 icon made for the probe.

(None) is what VB98's own `Splash Screen`, `Options Dialog` and `ODBC Log In` templates save: `Icon = "….frx":0000`,
where the record is an empty picture, `08 00 00 00`, then `lt\0\0`, then a size of 0. The real icon was written in the
same framing, with its own size and the `.ico`'s bytes.

Each window was read with `WM_GETICON` for `ICON_SMALL`, `ICON_BIG` and `ICON_SMALL2`, and with the class's icons.
Its caption was captured. **Native and p-code agree on every row.**

| The form's file | `ICON_BIG` | `ICON_SMALL` | `ICON_SMALL2` | Fixed Dialog caption | Sizable caption |
|---|---|---|---|---|---|
| no Icon line | VB6's default | none | made by Windows | VB6's form icon | VB6's form icon |
| `Icon` = (None) | none | none | none | **no icon** | Windows' generic window icon |
| an icon of its own | the icon | the icon, its own handle | the small icon | the icon | the icon |

The class icons were none on every form.

**What it shows:**
- **The usual account is wrong.** A Fixed Dialog with VB6's default icon draws it on its caption on Windows 10. With
  no small icon, Windows makes one from the big icon.
- **A bare dialog caption comes from Icon = (None).** That is why VB98's dialog templates save it.
- **An icon of the form's own is both its big and small icon.** Only the default is big only.

**In HexIDE (#1594):** `FormIcon` reads the form's Icon as the window is made, from the picture's own size, since a
blob runs to the next cited offset:
- an icon of its own is the window's icon, big and small;
- (None) leaves the window without one. On Windows, the window procedure keeps no icon and reports none, since
  Avalonia would otherwise give it the executable's;
- no Icon line keeps HexIDE's mark, big only. VB6's own artwork can't ship, and the made program's runner gives its
  `form.ico`.

Checked live with F5 on the probe's six forms: each window's `WM_GETICON` answers, and its caption drawn by
`PrintWindow`, matched the table above. The default's icon is HexIDE's mark.

**Not measured:**
- An icon with several sizes, and which of them VB6 picks for the small icon.
- An Icon changed at run time, `Me.Icon = LoadPicture(…)`. HexIDE reads the Icon once, as the window is made. Measured since: "An Icon set at run time", below.


### An Icon set at run time (2026-11-18, #4603, SP6)

**The probe.** A running form set its own `Icon`, logging each result and error. p-code and native code logged the
same lines. The files:
- `ARW01UP.ICO`, VB6's own 32-by-32 sample from `Common\Graphics\Icons\Arrows`, 1078 bytes, in the classic format;
- `x.ico`, one 32-by-32, 32-bit image stored as PNG, the format later versions of Windows write;
- `x.bmp`, a 32-by-32 bitmap.

| Statement | Result |
|---|---|
| reading `Me.Icon` on a new form | TypeName Picture, Type 3, a Handle that isn't 0 |
| `Set p = LoadPicture(ARW01UP.ICO)` | ok: p.Type 3, Width 847, Height 847 (HiMetric, 32 pixels) |
| `Set Me.Icon = p` | ok. `Me.Icon Is p` is True, with the same Handle |
| `Me.Icon = LoadPicture(ARW01UP.ICO)`, without Set | ok |
| `LoadPicture(x.ico)`, the PNG-stored icon | **error 481**, Invalid picture, raised by LoadPicture itself |
| `Set Me.Icon = LoadPicture(x.bmp)` | **error 380**, Invalid property value, and the Icon keeps its Type 3 |
| `Set Me.Icon = LoadPicture()` | ok: Type 0, Handle 0 |
| `Set Me.Icon = Nothing` | ok. `Me.Icon` then reads a Picture of Type 0 and Handle 0, **not Nothing** |
| `LoadPicture(nosuch.ico)` | error 53, File not found, naming the path |
| `Set f2.Icon = …` on a `New Form2` not yet loaded | ok, and it loads the form |

- **On screen:** after `Set Me.Icon`, the form's title bar and its taskbar button both showed the new icon (console
  screenshot).
- **Every error here was trappable** under `On Error Resume Next`.

**Not measured:** what the title bar shows after `LoadPicture()` or `Nothing`, and an icon file holding several
sizes.
