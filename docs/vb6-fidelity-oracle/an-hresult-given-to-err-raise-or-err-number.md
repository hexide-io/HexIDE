## An HRESULT given to Err.Raise or Err.Number (2026-11-18, #4730, SP6)

**The question.** When `Err.Raise`, or an assignment to `Err.Number`, is given a negative number that is a COM
HRESULT, does `Err.Number` keep it?

**The obvious guess was yes,** and HexIDE stores the number as given. **It is not:** VB6 turns facility
`0x0A` codes, and a set of common COM and dispatch codes, into VB error numbers. Every other negative number is
kept, with "Automation error" as its description.

**The rig:**
- **Builds:** vb6.exe 6.00.9782 with MSVBVM60 6.00.9848. A Standard EXE `Sub Main` was made native and p-code with
  `/make` in session 0 of the oracle guest. The same project was also run in the IDE itself with `vb6.exe /runexit`.
- **Each row:** `Err.Clear: Err.Raise n`, under `On Error Resume Next`, then a log of `Err.Number`, its hex,
  `Err.Description` with CR and LF shown as `<CR>` and `<LF>`, `Err.HelpContext` and `Err.Source`.
- **The three runs agreed in every row** except the two HelpContexts marked below.

### Mapped to a VB error number

| Raised | `Err.Number` | `Err.Description` | HelpContext |
|---|---|---|---|
| `&H800A0005` | **5** | Invalid procedure call or argument | 1000005 |
| `&H800A01AD` | **429** | ActiveX component can't create object | 1000429 |
| `&H800A0001` | **1** | Application-defined or object-defined error | exe 1000001, **IDE 1000095** |
| `&H800A1000` | **4096** | Application-defined or object-defined error | 1000095 |
| `&H800AFFFF` | **65535** | Application-defined or object-defined error | 1000095 |
| `&H800A0000` | **0**: no error; Description, HelpContext and Source empty | | |
| `&H80040154` (class not registered), `&H800401E3`, `&H800401F3` | **429** | ActiveX component can't create object | 1000429 |
| `&H80004001` (E_NOTIMPL) | **445** | Object doesn't support this action | 1000445 |
| `&H80004002` (E_NOINTERFACE) | **430** | Class does not support Automation or does not support expected interface | 1000430 |
| `&H80004004` (E_ABORT) | **287** | Application-defined or object-defined error | exe 1000287, **IDE 1000095** |
| `&H8007000E` (E_OUTOFMEMORY) | **7** | Out of memory | 1000007 |
| `&H80070005` (E_ACCESSDENIED) | **70** | Permission denied | 1000070 |
| `&H80070057` (E_INVALIDARG) | **5** | Invalid procedure call or argument | 1000005 |
| `&H80020003` (DISP_E_MEMBERNOTFOUND), `&H80020006` (DISP_E_UNKNOWNNAME) | **438** | Object doesn't support this property or method | 1000438 |
| `&H80020004` (DISP_E_PARAMNOTFOUND) | **448** | Named argument not found | 1000448 |
| `&H80020005` (DISP_E_TYPEMISMATCH) | **13** | Type mismatch | 1000013 |
| `&H80020008` (DISP_E_BADVARTYPE) | **458** | Variable uses an Automation type not supported in Visual Basic | 1000458 |
| `&H8002000A` (DISP_E_OVERFLOW) | **6** | Overflow | 1000006 |
| `&H8002000E` (DISP_E_BADPARAMCOUNT) | **450** | Wrong number of arguments or invalid property assignment | 1000450 |
| `&H80030002` (STG_E_FILENOTFOUND) | **432** | File name or class name not found during Automation operation | 1000432 |

### Kept as given

| Raised | `Err.Number` | `Err.Description` |
|---|---|---|
| `vbObjectError` (`&H80040000`) | -2147221504 | `Automation error<LF>Invalid OLEVERB structure ` |
| `vbObjectError + 1`, `+ 5`, `+ 339`, `+ 341`, `+ 513` | the number | `Automation error<LF>` and the system's text for it, such as `Need to run the object to perform this operation ` for `+ 5` |
| `vbObjectError + 512`, `+ 1000`, `+ 65535` | the number | `Automation error` alone |
| `&H80004003` (E_POINTER) | -2147467261 | `Automation error<LF>Invalid pointer ` |
| `&H80004005` (E_FAIL) | -2147467259 | `Automation error<LF>Unspecified error ` |
| `&H8000FFFF` (E_UNEXPECTED) | -2147418113 | `Automation error<LF>Catastrophic failure ` |
| `&H80070002`, `&H80070006` | the number | `Automation error<LF>The system cannot find the file specified. `, `…The handle is invalid. ` |
| `&H80020009` (DISP_E_EXCEPTION) | -2147352567 | `Automation error<LF>Exception occurred. ` |
| `&H80010001`, `&H80090005`, `&H800B0005` | the number | `Automation error<LF>` and the system's text |
| `&H80040200`, `&H80041234`, `&H8004FFFF`, `&H8007FFFF`, `&HC0000005`, `&H8A000005`, `&H80000000` | the number | `Automation error` alone |
| `-1`, `-5`, `-32768`, `&H8000` (an Integer, -32768) | the number | `Automation error` alone |

- **HelpContext is 1000440 for every kept number,** error 440's topic ("Automation error").
- **The system's text** is the code's Windows message, its line break turned into a trailing space. No CR is left in
  it, and a single LF separates it from "Automation error". A code Windows has no message for gets "Automation error" alone.
- **`vbObjectError + 340` is `&H80040154`, so it raises 429,** not a number of the program's own.

### Positive numbers, and an assignment

| Statement | `Err.Number` | `Err.Description` |
|---|---|---|
| `Err.Raise 0` | **5** | Invalid procedure call or argument |
| `Err.Raise 65535` | 65535 | Application-defined or object-defined error |
| `Err.Raise 65536`, `Err.Raise &H7FFFFFFF` | **5** | Invalid procedure call or argument |
| `Err.Raise &H80020009, "MySrc", "My text"` | -2147352567 | My text; Source MySrc |
| `Err.Raise &H800A0005, "MySrc", "My text"` | **5** | My text; Source MySrc |
| `Err.Number = &H800A0005`, no raise | **5** | empty |

- **An assignment to `Err.Number` maps as `Err.Raise` does.**
- **A description given to `Err.Raise` replaces the mapped one,** and the number is still mapped.

**What these rows support:**
- facility `0x0A` becomes its low word;
- the twenty listed COM and dispatch codes become the VB numbers in the table;
- any other negative number is kept as given.

**What they don't:** a rule that predicts which other COM codes VB6 maps. The table is only what was raised here.

**Not measured:**
- the HRESULTs not listed;
- an HRESULT returned by a real COM call rather than raised;
- the system's text on a Windows in another language.

**HexIDE, read on main:** `VbErr.Raise` (`IDE/HexIDE.Runtime/Interpreter/VbErr.cs:76`) stores the number as given.
So `Err.Raise &H800A0005` reads back as -2146828283, not 5.
