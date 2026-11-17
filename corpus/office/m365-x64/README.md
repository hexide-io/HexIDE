# Office corpus for #4330

Eleven Office files with VBA projects, made 2026-11-16 for #4330, the reader of an Office document's VBA project (#1119).

**How they were made:**
- **Office:** Microsoft 365, 64-bit, version 16.0.20527, on Windows 11 with ANSI code page 1252, except the two `cp932` files (below).
- **Building:** driven through Office's COM object model by `build.ps1` (beside this file), run at an interactive console logon, because Office's `SaveAs` refuses from a non-interactive session.

**What was scrubbed:**
- **Personal information:** Office's user name was set to `Corpus`, and every file was saved with `RemovePersonalInformation`. The author, last-saved-by and company properties read blank or `Corpus` in all eleven files, checked by `authors.py`.
- **The identity scan** covered each file's bytes, every zip entry, every OLE stream, and every VBA stream decompressed (MS-OVBA, through oletools 0.60.2).
  - It searched for the build account's user name, profile path, machine name, Microsoft account and full name, plus `$HEXIDE_IDENTITY_PATTERN`, in ANSI/UTF-8 and UTF-16LE. **0 hits.**
  - The first build of `userform.xlsm` held the profile path 3 times. A UserForm's MSForms reference names Office's `MSForms.exd` cache under `%TEMP%`. It was rebuilt with `TEMP` pointing at `C:\corpus-tmp`, so that reference reads `C:\corpus-tmp\VBE\MSForms.exd`.

**Paths:** every file was saved under `C:\corpus`, so the paths recorded inside them (the `other.xlsm` reference, the `.exd`) name only that folder.

## The files

| File | Format | VBA project | Modules | References beyond the defaults |
|---|---|---|---|---|
| `basic.xlsm` | Excel OOXML, macro-enabled | `CorpusProject` | `Module1`, `Class1`, `BigModule`, `ThisWorkbook` | Microsoft Scripting Runtime (`scrrun.dll`, a type library); `C:\corpus\other.xlsm` (project `OtherProject`) |
| `basic.xlsb` | Excel binary | The same workbook, saved again | The same | The same |
| `basic.xls` | Excel 97-2003 (BIFF8, OLE) | The same workbook, saved again | The same | The same |
| `other.xlsm` | Excel OOXML | `OtherProject` | `OtherModule` (`Twice`) | none |
| `userform.xlsm` | Excel OOXML | `FormProject` | `Module1`, `Class1`, `ThisWorkbook`, `UserForm1` (caption "Corpus form", `Label1` "A label", `CommandButton1` "OK", with a Click handler) | Microsoft Forms 2.0, added by the UserForm |
| `basic.docm` | Word OOXML, macro-enabled | `CorpusProject` | `Module1`, `Class1`, `ThisDocument` (`Document_Open`) | Word's own Normal project, as every document has |
| `basic.doc` | Word 97-2003 (OLE) | The same document, saved again | The same | The same |
| `basic.pptm` | PowerPoint OOXML, macro-enabled | `CorpusProject` | `Module1`, `Class1` | none |
| `locked.xlsm` | Excel OOXML | `LockedProject`, **locked for viewing**, password `corpus` | `Module1` (a `Public Const` and `SayHello`), `ThisWorkbook` | none |
| `cp932.xlsm` | Excel OOXML | `CodePageProject`, saved under **ANSI code page 932** | a standard module named `日本語モジュール`, holding a Japanese comment and `Public Const Greeting As String = "こんにちは"`; `ThisWorkbook` | none |
| `cp932.xls` | Excel 97-2003 (OLE) | The same workbook, saved again | The same | none |

**What each module holds:**
- **`Module1`:** `Option Explicit`, a `Public Const`, and an `#If VBA7 Then … #Else … #End If` block choosing a `Private Const`. Also `Public Sub SayHello`.
- **`Class1`:** a `Value` property with `Get` and `Let`.
- **`BigModule`:** 120 `Public Const` lines and a function, over 4 KB of source, so its compressed form spans several 4096-byte chunks.
- **`ThisWorkbook` / `ThisDocument`:** an `_Open` handler that prints.

## Still to come

- **32-bit Office:** later, once 32-bit Office is available.

`locked.xlsm` was locked by `lock.ps1`, which drives the VBA editor's Project Properties with keys, since protection has no COM call; reopened, its `VBProject.Protection` reads 1, and its `PROJECT` stream carries `CMG`, `DPB` and `GC`. `build.ps1`, `lock.ps1` and the two check scripts are kept for repeating the build. The scan needs the account's names, which are never written down. They're read on the build machine at scan time.

`cp932.xlsm` and `cp932.xls` were made by `cp932.ps1` with the build machine's system locale set to Japanese (ANSI code page 932), then set back. `cpcheck.py` confirms each holds the module's name in its `dir` stream in code page 932 (MODULENAME) and in UTF-16LE (MODULENAMEUNICODE), a module stream named in Japanese, and the string literal in code page 932 in the decompressed source. A reader that decodes with the wrong code page will mangle all three.
