## In-box controls: the CommonDialog (2026-10-27, #1084)

This extends "The CommonDialog control" (2026-10-17), which measured the dialogs the Notepad demo uses, as p-code.
The two agree where they overlap.

**The probes.** VB6 SP6 (6.00.9782), with COMDLG32.OCX 6.01.9782.
- **The control:** its form line is
  `Object = "{F9043C88-F6F2-101A-A3C9-08002B2F49FB}#1.2#0"; "COMDLG32.OCX"`, and its block is
  `Begin MSComDlg.CommonDialog`.
- **The library record:** the oracle VM's type-library dump already committed as
  `IDE/HexIDE.Runtime/Interpreter/Data/comdlg32-constants.json` identifies the library as `MSComDlg`, with the
  description `Microsoft Common Dialog Control 6.0`. Its 102 constants and this metadata came from
  `scripts/dump-typelib-constants.ps1` against COMDLG32.OCX (#1148); these are existing dump results, not a new probe.
- **The object-model program:** a Sub Main showing a form holding a CommonDialog. It drives the control late-bound
  under `On Error Resume Next`, logging each value with its `TypeName`, or the error.
- **The dialogs program:** the same form, where each step opens a dialog and logs the properties after it returns.
  - A driver on the console answered each dialog with real keystrokes, or with a click, and marked each action in the
    log.
  - The files were in `C:\hexoracle-cdfiles`: a.txt, b.txt and c.bas.
  - The guest's default printer was Microsoft Print to PDF.
- **The builds:** `/make` as native code and as p-code, both run on the guest's console. The two logged the same lines,
  apart from the EXE's own folder and the printer DC's handle.
- **The dialogs are Windows 10's own:**
  - **ShowOpen and ShowSave showed the Vista-style file dialog,** with an address bar, Organize and New folder, and a
    split Open button, with Flags 0 (screenshot).
  - **cdlOFNAllowMultiselect without cdlOFNExplorer showed the old dialog** of Windows 3.1's style.
  - What HexIDE draws for these is its own business. What the program sees when the dialog returns is what follows.

### The object model

- **Its members,** from the type library:
  - Action, FileName, FileTitle, DialogTitle, Filter, FilterIndex, DefaultExt, InitDir, MaxFileSize, Flags and
    CancelError;
  - Color;
  - FontName, FontSize, FontBold, FontItalic, FontStrikeThru and FontUnderLine;
  - FromPage, ToPage, Min, Max, Copies, PrinterDefault, Orientation and hDC;
  - HelpFile, HelpCommand, HelpKey and HelpContext;
  - the methods ShowOpen, ShowSave, ShowColor, ShowFont, ShowPrinter, ShowHelp and AboutBox.
- **It has no events.**
- **`TypeName`** is `CommonDialog`.
- **Action is write-only, and it is the default member.**
  - Reading Action is **394**, "Get not supported (write-only property)".
  - So `v = CommonDialog1` is **394**, "Property is write-only".
- **The extender has Name, Tag, Parent and Object:**
  - **Reading Left or Top at run time is 393,** "Get not supported at runtime". Setting them is **382**.
  - Index is 343.
  - Visible, Width, Height, Enabled and hWnd are **438**: the control has none of them.
- **The constants:**
  - Flags for ShowOpen and ShowSave:
    - cdlOFNReadOnly 1, cdlOFNOverwritePrompt 2, cdlOFNHideReadOnly 4, cdlOFNNoChangeDir 8, cdlOFNHelpButton 16;
    - cdlOFNNoValidate 256, cdlOFNAllowMultiselect 512, cdlOFNExtensionDifferent 1024;
    - cdlOFNPathMustExist 2048, cdlOFNFileMustExist 4096, cdlOFNCreatePrompt 8192, cdlOFNShareAware 16384;
    - cdlOFNNoReadOnlyReturn 32768, cdlOFNNoLongNames 262144, cdlOFNExplorer 524288;
    - cdlOFNNoDereferenceLinks 1048576, cdlOFNLongNames 2097152.
  - Flags for ShowColor: cdlCCRGBInit 1, cdlCCFullOpen 2, cdlCCPreventFullOpen 4, cdlCCHelpButton 8.
  - Flags for ShowFont: cdlCFScreenFonts 1, cdlCFPrinterFonts 2, cdlCFBoth 3, cdlCFEffects 256, and the rest of the
    cdlCF family.
  - Errors: cdlCancel 32755, cdlNoFonts 24574, cdlBufferTooSmall 20476, and others.

### Defaults

| Read | Logged |
|---|---|
| CancelError | False |
| FileName, FileTitle, Filter, DefaultExt, DialogTitle, InitDir | empty Strings |
| FilterIndex | 0, an Integer |
| Flags | 0, a Long |
| MaxFileSize | **260**, an Integer |
| Color | 0 |
| FontName | **empty** |
| FontSize | **8**, a Single |
| FontBold, FontItalic, FontStrikeThru, FontUnderLine | False |
| Copies | 1, an Integer |
| FromPage, ToPage, Min, Max | 0, Integers |
| Orientation | 1, portrait |
| PrinterDefault | True |
| hDC | a nonzero handle, before any dialog |
| HelpCommand, HelpContext | 0 |
| HelpFile, HelpKey | empty |

**A colour read late-bound comes back as an unsigned Long,** VarType 19, as the ListView's and TreeView's do. Color
`&H80000005` read back as `2147483653`.

### What each property takes

| Assignment | Logged |
|---|---|
| Action 0, 7, 8, -1 | **380**. 1 to 5 open the dialog of ShowOpen to ShowPrinter, and 6 is ShowHelp: see "Action 1 to 6" |
| Flags | any Long, -1 and `&H7FFFFFFF` too |
| FilterIndex -1 | **380**. 0 to 32767 are taken, 99 too |
| MaxFileSize 0, -1 | **380** |
| MaxFileSize 1 to 32767 | taken |
| MaxFileSize 32768 | **6**, overflow |
| FontSize | anything: -1, 0, 7.5, 100000 |
| FontName set to a face that doesn't exist | **ignored**. The name before it was kept |
| FontName `"arial"` | kept as typed: `arial` |
| Orientation 1, 2 | taken. 0, -1 and 3 are **380** |
| Copies 0, -1 | **380** |
| FromPage -1 | **380** |
| Min 10 with Max 2 | both taken. Nothing compares them at assignment |
| Color -1 | **6**, overflow. `&H80000005` and `&H1000000` are taken |
| HelpCommand | any Integer |
| Filter | any string at assignment: `""`, `a`, `a|`, `|`, and `a|b|c` |
| FileName of 300 characters | taken, with MaxFileSize 260 |
| `FileTitle =`, `hDC =` | **383** |
| DefaultExt `.TXT`, `toolong` | taken as given |
| InitDir naming a folder that doesn't exist | taken |
| CancelError 2, PrinterDefault 0 | True, False |

### ShowOpen and ShowSave

**A file chosen.** With Filter `Text (*.txt)|*.txt|All (*.*)|*.*` and `C:\hexoracle-cdfiles\a.txt` typed:
- FileName is the full path, and FileTitle `a.txt`.
- **Flags comes back with cdlOFNExtensionDifferent, `&H400`,** set because DefaultExt was empty and the file has an
  extension.
- **The current directory moved to the file's folder.** With cdlOFNNoChangeDir it stayed where it was, and Flags came
  back `&H408`.

**Cancelling:**
- **With CancelError False, Cancel raises nothing** and leaves FileName and FileTitle as they were.
- **With CancelError True, Cancel raises 32755,** "Cancel was selected.", and leaves them as they were too.
- **Flags is written back even on Cancel.** Set to cdlOFNFileMustExist, `&H1000`, it came back from a cancelled dialog
  as **`&H1800`**, with cdlOFNPathMustExist added.

**FileTitle keeps the last file chosen.** Setting FileName to `""` doesn't clear it, and a cancelled dialog leaves it.

| Case | FileName | FileTitle | Flags |
|---|---|---|---|
| `nosuch.txt` typed, no FileMustExist | the path to it. No error, though it doesn't exist | `nosuch.txt` | `&H400` |
| `nosuch.txt` typed, with FileMustExist | the dialog showed a message box titled "Open" and stayed open | | |
| DefaultExt `txt`, `c` typed | **`...\c.txt`** | `c.txt` | **`&H0`** |
| DefaultExt `txt`, `c.bas` typed | `...\c.bas` | `c.bas` | **`&H400`**, the extension differing |
| ShowSave, no DefaultExt, `e` typed | **`...\e.txt`**, the current filter's extension added | `e.txt` | `&H400` |
| ShowSave, DefaultExt `txt`, `d` typed | `...\d.txt` | `d.txt` | `&H0` |
| FileName `b.txt` set beforehand, Enter | `...\b.txt` | `b.txt` | `&H400` |
| MaxFileSize 10, `a.txt` chosen | **error 20476**, "The FileName buffer is too small to store the selected file name(s). (Increase MaxFileSize)" | | |

**The filters:**
- **FilterIndex comes back as the filter chosen.** Choosing the second filter in the dialog returned 2.
- **With no Filter, FilterIndex came back 0,** though it was set to 1.
- **FilterIndex 9, with two filters, came back 2.**
- A Filter of `Odd|` opened the dialog and returned FilterIndex 1.

**Choosing several files:**

| Flags | Typed | FileName | FileTitle |
|---|---|---|---|
| AllowMultiselect and Explorer, `&H80200` | `"a.txt" "b.txt"` | **`C:\hexoracle-cdfiles` Chr(0) `a.txt` Chr(0) `b.txt`**: the folder with no trailing backslash, then each name, separated by NULs | **empty** |
| the same | `a.txt` alone | `C:\hexoracle-cdfiles\a.txt`, a plain path, with **Flags `&H80600`** | `a.txt` |
| AllowMultiselect alone, `&H200`, the old dialog | `a.txt b.txt` | **`C:\hexoracle-cdfiles\ a.txt b.txt`**: the folder with a trailing backslash, then the names, separated by spaces | empty |

After the old dialog, CurDir read **`C:\HEXORACLE-CDFILES`**, in capitals.

**Other cases:**
- **Open as read-only,** chosen from the Open button's split menu, returned **cdlOFNReadOnly in Flags, `&H401`**. With
  cdlOFNHideReadOnly the Open button had no menu.
- **DialogTitle** becomes the dialog's caption. Without it the captions are "Open" and "Save As".
- **`Action = 1`** opens the Open dialog, as ShowOpen does.
- **OverwritePrompt:**
  - ShowSave with cdlOFNOverwritePrompt, given an existing file, showed a "Confirm Save As" message box.
  - **Yes** returned the file, with Flags `&H402`.
  - **No** went back to the dialog, which was then cancelled.
- **The folder the dialog opens in is not always InitDir.** In the first run, the Vista-style dialog opened in a folder
  that earlier programs on the guest had used, not in InitDir. The old dialog opened in InitDir. Which folder a
  file dialog starts in depends on the machine's history, so these probes typed full paths.

### ShowColor

| Case | Color after | Flags after |
|---|---|---|
| Color vbRed, Flags 0, OK | **0**: without cdlCCRGBInit the dialog starts at black | 0 |
| Color vbRed, cdlCCRGBInit, OK | 255 | 1 |
| Color vbBlue, cdlCCRGBInit, Cancel, CancelError True | **32755**, and Color left at vbBlue | 1 |

### ShowFont

- **With Flags 0 the Font dialog opened.** On this guest there was no error 24574, "No fonts exist", without
  cdlCFScreenFonts.
- **OK returns the choice:** with cdlCFScreenFonts, Arial, 12 and Bold set beforehand, OK returned Arial, 12, Bold True,
  Italic False. Color was untouched.
- **cdlCFEffects returns Underline, StrikeThru and Color too.** With it, FontUnderLine True and Color vbRed set came back
  Underline True and Color 255. The FontName set to a face that doesn't exist had been ignored, so the dialog showed the
  last face, Arial, and returned it. Flags came back `&H101`.
- **Cancel with CancelError True raises 32755.**

### ShowPrinter

- **OK with Flags 0:** Copies 1, FromPage, ToPage, Min and Max 0, Orientation 1, Flags 0. **hDC read 0.**
- **OK with cdlPDReturnDC, `&H100`:** hDC was a printer DC's handle, and Flags `&H100`.
- **Copies 3 set beforehand came back as 1** from Microsoft Print to PDF.
- **Cancel with CancelError True raises 32755.**
- PrinterDefault was False throughout, so the guest's default printer was not changed.

### Action 1 to 6

**The probe:** one program setting `Action` to 2, 3, 4, 5 and 6 in turn, run on the guest's console, native and p-code.
A driver answered each dialog with real keys, as for the ShowX rows above. The two builds logged the same lines.

**Each value is the method of the same number, and writes back what that method does.**

| Statement | Dialog | Written back |
|---|---|---|
| `Action = 1` | Open | as ShowOpen, above |
| `Action = 2`, DefaultExt `txt`, typed `C:\hexoracle-cdfiles\saved2` | **Save As** | FileName `C:\hexoracle-cdfiles\saved2.txt`, FileTitle `saved2.txt`, Flags 0, and CurDir moved to the folder |
| the same, cancelled, CancelError True | Save As | **32755**. FileName stayed empty, as set before the call, and FileTitle stayed `saved2.txt` from the call before |
| `Action = 3`, cdlCCRGBInit, Color vbBlue, OK | **Color** | Color 16711680, Flags 1 |
| `Action = 4`, cdlCFScreenFonts, Arial 12 bold, OK | **Font** | FontName Arial, FontSize 12, Bold True, Italic False, Flags 1 |
| `Action = 5`, Flags 0, OK | **Print** | Copies 1, FromPage, ToPage, Min and Max 0, Orientation 1, Flags 0, hDC 0 |
| the same, cancelled, CancelError True | Print | **32755**, the values unchanged |
| `Action = 6` | **none of the program's own** | no error, for each case below |

**Action 6 is ShowHelp, and on Windows 10 it hands off to the system.**
- **Every call returned at once with no error, ShowHelp's own included:**
  - HelpFile empty with HelpCommand 0;
  - HelpFile empty with HelpCommand 3, cdlHelpContents;
  - HelpFile a file that doesn't exist, with HelpCommand 3;
  - then HelpCommand 2, cdlHelpQuit;
  - and `ShowHelp` itself with the missing file and HelpCommand 3.
- **Nothing opened in the program.** Instead, Windows started `HelpPane.exe` in the console session. That started
  `OpenWith.exe`, which showed "You'll need a new app to open this ms-contact-support link".
  - It happened in the native run and again in the p-code run.
  - Left on screen, that box took the keystrokes meant for the next program's Save As dialog.
- **Which call started it isn't separated:** the five calls ran within a quarter of a second. It is Windows 10's
  replacement for the old Help engine answering, not anything the control does itself. On a Windows that still has
  WinHelp, the result would be different.
- After the calls, Flags, HelpFile and HelpCommand read back as they were set.

### The saved files

**The programs.**
- **The save:** a form of three CommonDialogs, written by hand, opened in VB6's IDE, and saved there once the form had
  been dirtied with a comment.
  - The first had every property off its default, its lines in reverse order.
  - The second had only its position and `_Version`.
  - The third had its defaults written out.
  - A second save of the same form also had a fourth CommonDialog double-clicked from the toolbox.
- **The run-time read:** an EXE read every property back from VB6's save, natively and as p-code.
- **The corpus:** VB6's save is in `corpus/inbox-controls/saved/commondialog/`.

**Double-clicked onto a 6000×4500-twip form,** VB6 writes:

```
   Begin MSComDlg.CommonDialog CommonDialog4 
      Left            =   2760
      Top             =   2010
      _ExtentX        =   847
      _ExtentY        =   847
      _Version        =   393216
   End
```

- The control is centred: the icon is 480 twips square, 847 HIMETRIC.
- **The double-clicked control was written first,** ahead of the three already on the form.

**The order VB6 writes the lines in,** whatever order they were read in: Left and Top; `_ExtentX`, `_ExtentY` and
`_Version`; then

```
CancelError, Color, Copies, DefaultExt, DialogTitle, FileName, Filter, FilterIndex, Flags,
FontBold, FontItalic, FontName, FontSize, FontStrikeThru, FontUnderLine, FromPage,
HelpCommand, HelpContext, HelpFile, HelpKey, InitDir, Max, Min, MaxFileSize,
PrinterDefault, ToPage, Orientation
```

- **The order is alphabetical, apart from three lines:**
  - MaxFileSize comes after Min;
  - ToPage comes after PrinterDefault;
  - Orientation comes last.
- **Every property is saved under its own name, and only when off its default.** The third CommonDialog, with FontSize
  8, MaxFileSize 260, Copies 1, Orientation 1, PrinterDefault True, CancelError False and Flags 0 written out, was saved
  as the minimal block.
- **There is no Font block.** The font is the six Font lines.
- **A block missing a line loads the defaults** listed under Defaults: MaxFileSize 260, FontSize 8, Copies 1,
  Orientation 1, PrinterDefault True. The minimal block read back exactly as the third did.
- **Read back at run time,** every value written in the first came back as written: Flags 524800, FilterIndex 2,
  MaxFileSize 1024, FontName Arial, and the rest.

### In HexIDE

ShowOpen, ShowSave and ShowFont run through the host (#1148). The scalar object-model slice of #1084
adds the measured defaults and assignment checks; initial hDC reads remain refused because no native printer DC is owned.
The control now loads through the MSComDlg library record and its component class, with own file properties carried
verbatim. Color reads preserve VarType 19 and the unsigned value; assigning -1 overflows. Unmeasured unsigned numeric
conversions remain refused. Single-file Open/Save now add PathMustExist even on Cancel, set ExtensionDifferent for an extension differing from
DefaultExt (including an empty one), complete DefaultExt or a simple selected Save filter extension, and raise 20476
when Open returns a path exceeding MaxFileSize. An unavailable selected filter or an unmeasured complex filter completion
is refused. Save buffer errors, exact buffer-fit boundaries, extension casing and resetting an existing ExtensionDifferent bit remain
unmeasured; their previous behavior is unchanged. Multi-select now passes every picked path through both hosts and formats the measured same-folder results above.
Cross-folder selections and old-format paths with spaces are refused because their encoding is unmeasured.
Action 1–6 now dispatches to the corresponding Show methods, awaiting their results; integral writes 0, 7, 8 and -1 raise 380. Non-integral assignment conversions remain refused. ShowHelp returns for the measured commands 0, 2 and 3; other commands are refused. ShowColor now uses a managed RGB chooser for Flags 0/1 (#2299). ShowPrinter uses a managed dialog for Flags 0, default page range/portrait and Copies 1 or 3, returning Copies 1, Orientation 1 and hDC 0 on acceptance. Both honour CancelError, including through Action 3/5. Other configurations, native DCs and actual printing remain refused or unavailable.
The old native dialog appearance, uppercase CurDir side effect and font-effect writeback remain outstanding. The measured CommonDialog save now round-trips byte for byte in the saved corpus lane;
explicit own-property defaults are omitted in the measured order, with six separate font lines and no Font block.
ShowHelp does not start HelpPane.exe as Windows 10 does: the supervisor directed HexIDE to return without launching it (#1084).
