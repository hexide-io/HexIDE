# VB6 fidelity oracle — behaviour verified against `vb6.exe`

HexIDE's in-box interpreter aims for **runtime-execution fidelity**: it reproduces VB6's *observable* behaviour
without depending on `MSVBVM60.DLL` (see the CST-not-AST boundary in `CLAUDE.md`). The only trustworthy source
of truth for "what does VB6 actually do here" is **the real compiler**. This document records the facts pinned
against real `vb6.exe` during the interpreter type-system build-out (interpreter-core Phase 2), plus the
**harness** for running the oracle cleanly — future phases (intrinsics: `CInt`/`CLng`/`CDate`/`Format`/`Rnd`;
date functions; graphics) should extend it the same way.

> **Why this matters:** the oracle repeatedly overturned "documented" assumptions. Building from memory / the
> language reference alone would have shipped several wrong behaviours (see *Assumptions overturned* below).

---

## The oracle

- **Binary:** `C:\Program Files (x86)\Microsoft Visual Studio\VB98\VB6.EXE` (the `Vb6ToolchainService`
  auto-discovers it; the `VB6_EXE` env var overrides). Windows + a VB6 install required — this is a dev-time
  fidelity check, not part of the shipped product. The install does **not** have to be on your own machine:
  see [the scripted harness](#the-scripted-harness--scriptsvb6-oracleps1), which drives a copy in a Hyper-V
  guest just as happily.
- **Method:** compile a tiny `Sub Main` console-less program with `VB6.EXE /make`, run the produced `.exe`, and
  read back a file it wrote with `TypeName(expr)` + the value for each case.

### The scripted harness — `scripts/vb6-oracle.ps1`

Everything below this section is what the script automates. **Prefer the script**; keep reading only if you
need to do something it does not cover, or to understand why it does what it does.

```powershell
.\scripts\vb6-oracle.ps1 'CByte(100) + CByte(100)', 'CByte(200) + CByte(100)', '5 / 2'

Expression              Value Type    Err
----------              ----- ----    ---
CByte(100) + CByte(100) 200   Byte
CByte(200) + CByte(100)                 6
5 / 2                   2.5   Double
```

You give it expressions; it compiles them into a `Sub Main`, runs the real `vb6.exe`, and reports
`value | TypeName` — or `ERR<n>` where VB6 raised. `-Path probes.txt` reads one expression per line
(`'` comments and blanks skipped, `label: expression` to name a row). Objects come back on the pipeline,
so `| Format-Table`, `| Export-Csv`, `| Where-Object Err` all work.

**Two ways to reach VB6.** `-Local` uses an install on this machine. With no switch it opens a
**PowerShell Direct** session to a Hyper-V guest (`-VMName`, default `Win10`) — which is how the
maintainers run it, since VB6 is a 1998 Windows-only installer that few people want on their daily
driver. PowerShell Direct runs over the VMBus, so the guest needs **no network at all** — an isolated
switch with no IP route is fine.

Guest side, once:

```powershell
# in the guest, as an existing admin
$pw = Read-Host "password" -AsSecureString
New-LocalUser -Name hexoracle -Password $pw -PasswordNeverExpires
Add-LocalGroupMember -Group Administrators -Member hexoracle   # PowerShell Direct admits admins only
```

Host side, once:

```powershell
New-Item -ItemType Directory -Force "$env:USERPROFILE\.hexide" | Out-Null
Get-Credential | Export-Clixml "$env:USERPROFILE\.hexide\win10.cred"
```

`Export-Clixml` encrypts under DPAPI, so the file is readable only by that Windows account on that
machine — the password never lands in a script, a command line or a shell history. Override the location
with `-CredentialPath`.

**Getting the corpus out of the guest.** The round-trip tests need VB6's `VB98\Template` tree, and
`Copy-VMFile` only goes host→guest. Pull it the other way through the same session:

```powershell
$s = New-PSSession -VMName Win10 -Credential (Import-Clixml "$env:USERPROFILE\.hexide\win10.cred")
Copy-Item -FromSession $s -Recurse `
  -Path 'C:\Program Files (x86)\Microsoft Visual Studio\VB98\Template' `
  -Destination "$env:USERPROFILE\.hexide\vb6-template"
Remove-PSSession $s
```

Then set `HEXIDE_ROUNDTRIP_CORPUS` to that path. **This matters more than it looks:** without it
`SerializationCorpusTests` falls back to `demo/`, whose files are HexIDE-authored and filtered out by
`IsHexIdeAuthored`, so the gate passes **vacuously** — a serialization regression goes green. The corpus
is Microsoft's redistributable-restricted content and must stay out of the repository.

> **Sanity-check the harness before trusting a new answer.** Run a handful of rows that are already in
> the tables below — `CByte(100) + CByte(100)` → `200 | Byte` and `CByte(200) + CByte(100)` → `ERR6` are
> a good pair, because the second proves `Byte + Byte` really does stay `Byte`. If those reproduce, the
> loop is sound and a surprising new result is VB6 being surprising rather than the harness lying.

### Harness (reusable recipe) — the manual version

What `vb6-oracle.ps1` does internally, kept because the reasons are worth knowing when a probe misbehaves.

Two lessons learned the hard way:

1. **Use absolute paths + `CreateNoWindow`.** A relative `.vbp` path (or launching via a shell) makes `VB6.EXE`
   pop its GUI instead of doing a headless `/make`. Drive it through `System.Diagnostics.ProcessStartInfo`
   with `UseShellExecute=false`, `CreateNoWindow=true`, and an **absolute** `.vbp` path + `/out <errlog>`.
2. **Wrap every probe in `On Error Resume Next`.** A runtime error inside the probe (e.g. an overflow) pops a
   **modal error dialog on the user's screen** and blocks the run until it's dismissed. Capture `Err.Number`
   per case instead, so overflow is *data* (`ERR6`), not a modal. (This is exactly how the `Byte+Byte`
   overflow was found — the first, un-guarded probe hung on a `Runtime Error 6` modal.)
3. **The writer sub needs its own guard, and must print on every path.** `On Error Resume Next` is
   **per-procedure**, so `Main`'s guard does not extend into the helper that formats the row. `CStr(Null)`
   raises 94 and `CStr(<object>)` raises 438 — both perfectly good probe results — and an unguarded helper
   simply returns without printing. The case then **vanishes from the output**, which reads like a case you
   never ran rather than one that went wrong. Test `Null + 1` against any new harness: it should come back
   `Null | Null`, not disappear. Count the rows you get against the cases you sent.

`Module1.bas` shape:

```vb
Attribute VB_Name = "Module1"
Sub WT(f As Integer, label As String, v As Variant, en As Long)
    If en <> 0 Then Print #f, label & "=ERR" & en Else Print #f, label & "=" & v & "|" & TypeName(v)
End Sub
Sub Main()
    Dim f As Integer, v As Variant
    f = FreeFile
    Open "C:\...\out.txt" For Output As #f
    On Error Resume Next
    Err.Clear: v = 5.5 Mod 2: WT f, "mod", v, Err.Number   ' one line per case
    Close #f
End Sub
```

`verify.vbp` shape: `Type=Exe`, `Module=Module1; Module1.bas`, `Startup="Sub Main"`, `ExeName32="verify.exe"`.

Driver (PowerShell): start `VB6.EXE /make "<abs>\verify.vbp" /out "<abs>\err.log"` with `CreateNoWindow`, wait +
kill-on-timeout, then `Start-Process verify.exe -WindowStyle Hidden -PassThru -Wait`, then read `out.txt`.

**Probes that use a class (`Class_Initialize`/`Terminate`, objects) — three gotchas** (learned building the
Phase-4.2 refcount oracle):
1. The `.vbp` **must** carry the OLE Automation reference line, or the compiler reports *"User-defined type not
   defined"* for your own class: `Reference=*\G{00020430-0000-0000-C000-000000000046}#2.0#0##OLE Automation`.
   Copy a known-good `.vbp`/`.cls` header from `demo/*/` rather than hand-authoring the class-header bytes.
2. VB6 needs **CRLF + ASCII** source. The `Write` tool emits LF; convert before `/make`
   (`($c -replace "\`r\`n","\`n") -replace "\`n","\`r\`n"` then `WriteAllText(..., ASCII)`), or the module fails to
   load (same misleading *"User-defined type not defined"*).
3. Launching `vb6.exe` trips the shell sandbox — run the make/run PowerShell with `dangerouslyDisableSandbox:
   true` (it only writes to the scratch dir; this is the authorized dev-time toolchain). Avoid `Remove-Item` near
   the `C:\Program Files (x86)\…` path in the same command — the path-guard blocks it; let `/make` overwrite and
   `For Output` truncate instead.

---

## Verified findings

All rows below are **actual `vb6.exe` output** (`value | TypeName`). These are the source of the interpreter's
test expectations (`VbNumeric`, `OperatorTests`, `ArithmeticFidelityTests`, `DateCurrencyVariantTests`,
`LiteralTests`).

### Arithmetic result types — `+ − *`

Ladder (widest wins): **Byte < Integer < Long < Single < Double < Currency < Decimal**. Boolean and Empty
behave as Integer. `Byte` and `Boolean` promote to Integer *only when the other operand forces it* — `Byte+Byte`
stays `Byte`. A result outside the result-type's range is `Err 6` — there is no overflow auto-promotion.

> **That last sentence is about DECLARED types only.** Every row below was measured through declared
> variables, and the qualifier turns out to be load-bearing: with **Variant** operands the same expressions
> do not raise at all, they widen (Integer → Long → Double). See *Variant arithmetic promotes on overflow*
> further down, which is the common path, since most VB6 code does not declare types.

| expression | result | type |
|---|---|---|
| `CByte(100) + CByte(100)` | 200 | **Byte** |
| `CByte(200) + CByte(100)` | — | **Err 6 (overflow)** — proves Byte+Byte → Byte |
| `CByte(100) + 5` | 105 | Integer |
| `True + CByte(100)` | 99 | Integer (Boolean → Integer; `True` = −1) |
| `5 + 5` | 10 | Integer |
| `30000 + 30000` (via Integer vars) | — | **Err 6** |
| `5 + 3&` | 8 | Long |
| `3& + 3&` | 6 | Long |
| `5 + 2!` | 7 | Single |
| `2! + 2!` | 4 | Single |
| `5 + 2#` | 7 | Double |
| `5 * 5` | 25 | Integer |
| `30000 * 30000` (via vars) | — | **Err 6** |

### Division `/` (real division)

Result is **Single iff the widest operand is Single** (both operands in {Byte, Integer, Single}); otherwise
**Double**. Notably **int/int → Double** and **Long/anything → Double**.

| expression | result | type |
|---|---|---|
| `5 / 2` | 2.5 | **Double** (not Single!) |
| `5 / 2!` | 2.5 | Single |
| `5 / 2#` | 2.5 | Double |
| `5.5! / 2.5!` | 2.2 | Single |
| `40000 / 2` (Long/Int) | 20000 | Double |
| `CLng(5) / CLng(2)` | 2.5 | Double |
| `10@ / 4` | 2.5 | Double |

### Integer division `\` and `Mod`

Operands are **banker's-rounded to an integer first**, then the integer op. Result is **Integer only when both
operands are Byte/Integer/Boolean; otherwise Long**. `\`/`Mod`/`/` by zero → **`Err 11` (Division by zero)**.

| expression | result | type |
|---|---|---|
| `5 Mod 2` | 1 | Integer |
| `5.5 Mod 2` | 0 | **Long** (5.5 → 6; 6 Mod 2) |
| `5.5! Mod 2` | 0 | Long |
| `-5.5 Mod 2` | 0 | Long |
| `7 \ 2` | 3 | Integer |
| `7.6 \ 2` | 4 | **Long** (7.6 → 8) |
| `7.5 \ 2` | 4 | Long (7.5 → 8, half-to-even) |

### `^`, unary `−`, `CInt` rounding

| expression | result | type / note |
|---|---|---|
| `2 ^ 10` | 1024 | Double (always) |
| `-iv` where `iv` is Integer −32768 | −32768 | **Integer, no error** — two's-complement wrap (we treat as overflow; documented divergence) |
| `CInt(2.5), CInt(3.5), CInt(-2.5), CInt(0.5)` | 2, 4, −2, 0 | banker's (round-half-to-even) |

### Date

Serial epoch is **1899-12-30** (`CDbl(#1/1/2000#)` = **36526**).

| expression | result | type |
|---|---|---|
| `#1/1/2000#` | 2000-01-01 | Date |
| `#1/1/2000# + 31` | 2000-02-01 | Date |
| `#1/1/2000# - 1` | 1999-12-31 | Date |
| `#1/10/2000# - #1/1/2000#` | 9 | **Double** (day difference) |
| `#1/1/2001# > #1/1/2000#` | True | Boolean |

**Runtime error codes** (bug-hunt error-fidelity fixes — the interpreter must raise these **trappable** errors, not
raw .NET exceptions). Pinned via the `On Error Resume Next` + `Err.Number` harness:

| operation | `Err.Number` | note |
|---|---|---|
| `ReDim a(-2)` · `ReDim a(2 To 0)` · `ReDim a(5 To 1)` | **9** | inverted/negative bounds → Subscript out of range |
| `ReDim a(0 To 0)` | 0 (ok) | a size-1 array — the empty case `(0,-1)` is also valid |
| `UBound(a, 2)` on a rank-1 array · `LBound(a, 0)` | **9** | dimension < 1 or > rank |
| `CDate(1E30)` · `CDate(2958466)` · `CDate(-657435)` | **6** | serial outside Date range → **Overflow** (NB **not** 13) |
| `CDate(0)` | 0 (ok) | serial 0 = 1899-12-30 00:00 |

### Currency (`@`)

**Currency dominates Double** in `+ − *` (the biggest surprise). Fixed 4 dp, banker's rounding, hardware range
→ `Err 6`. `/` still drops to Double.

| expression | result | type |
|---|---|---|
| `10@ + 5` | 15 | Currency |
| `10@ + 20@` | 30 | Currency |
| `10@ + 1.5` | 11.5 | **Currency** (not Double!) |
| `10@ * 3` | 30 | Currency |
| `1.23455@` | 1.2346 | Currency (4-dp, 5→even 6) |
| `1.23445@` | 1.2344 | Currency (4-dp, 4 stays even) |
| `9e14@ + 1e14@` | — | **Err 6** |

### Variant — Empty / Null

| expression | result | type |
|---|---|---|
| `Empty & "x"` | `x` | String |
| `Empty + 5` | 5 | Integer (Empty acts as 0) |
| `Null + 1` | Null | Null (propagates) |
| `Null & "x"` | `x` | String (`&` treats Null/Empty as "") |

### Hex `&H` / Octal `&O` — unsigned bit-patterns (NOT colours)

No suffix: fits 16 bits → Integer via **16-bit two's-complement**; else fits 32 bits → Long via **32-bit
two's-complement**. `&` forces the Long reading (no 16-bit wrap); `%` forces the Integer reading. **Octal wraps
identically to hex.**

| literal | result | type |
|---|---|---|
| `&HFF` | 255 | Integer |
| `&H7FFF` | 32767 | Integer |
| `&H8000` | −32768 | Integer (16-bit wrap) |
| `&HFFFF` | −1 | Integer |
| `&H10000` | 65536 | Long |
| `&HFFFFFFFF` | −1 | Long |
| `&HFFFF&` | 65535 | Long (`&` forces Long) |
| `&H8000&` | 32768 | Long |
| `&HFFFF%` | −1 | Integer (`%` forces Integer) |
| `&O17` | 15 | Integer |
| `&O77777` | 32767 | Integer |
| `&O177777` | −1 | Integer (16-bit wrap) |
| `&O37777777777` | −1 | Long (32-bit wrap) |

VB6 colours are numeric `OLE_COLOR` (`0x00BBGGRR`, or `0x80……` = system colour). A hex value assigned to a
colour property is converted at the property boundary (`VBColor.FromOle`), not at literal time.

### Math intrinsics (interpreter-core Phase 3.3)

| expression | result | type |
|---|---|---|
| `Abs(-4)` | 4 | **Integer** (Abs preserves the operand type) |
| `Abs(-4.5)` | 4.5 | Double |
| `Int(-2.5)` | −3 | Double (**floor** toward −∞) |
| `Fix(-2.5)` | −2 | Double (**truncate** toward zero) |
| `Int(2.7)` / `Fix(2.7)` | 2 / 2 | Double |
| `Sgn(-5)` | −1 | Integer |
| `Sqr(9)` | 3 | Double |
| `Round(2.5)` / `Round(3.5)` | 2 / 4 | Double (**banker's** rounding) |
| `Round(2.567, 2)` | 2.57 | Double |
| `TypeName(Sqr(9))` | `Double` | — |

**`Rnd` — the exact 24-bit LCG (pinned).** Fresh state (no `Randomize`) is seed `&H50000`; each draw advances
`seed = (seed * &H43FD43FD + &HC39EC3) And &HFFFFFF` and returns `seed / 2^24` as a **Single**. Verified first
draws: `0.7055475, 0.533424, 0.5795186` (state after draw 1 = `11837123`, and `11837123 / 2^24 = 0.7055475`
bit-exact). `Rnd(0)` returns the last value **without advancing**; `Rnd(<0)` reseeds from the argument.
`Randomize [n]` only varies the starting seed — our reseed fold is deterministic-but-not-bit-identical to VB6's
(documented divergence; the fixed-seed sequence itself *is* bit-exact).

### Inspection intrinsics (interpreter-core Phase 3.4)

`TypeName`/`VarType` (`vbEmpty=0, vbNull=1, vbInteger=2, vbLong=3, vbSingle=4, vbDouble=5, vbCurrency=6,
vbDate=7, vbString=8, vbObject=9, vbBoolean=11, vbVariant=12, vbDecimal=14, vbByte=17`; arrays add
`vbArray=8192` and `TypeName` appends `()`): `TypeName(Array-of-Integer) = "Integer()"`, `VarType = 8194`;
a `Variant` array (`Array(1,2,3)`) → `"Variant()"`, `8204`.

`IsNumeric` is **far more permissive than expected** (all verified True unless noted):

| input | IsNumeric | note |
|---|---|---|
| `True` (Boolean) | **True** | Booleans are numeric |
| `Empty` | **True** | Empty coerces to 0 |
| a `Date` value | **False** | dates are *not* numeric |
| `Null` | False | |
| `"&HFF"`, `"&O17"` | **True** | hex/octal *strings* |
| `"1,234"` | **True** | thousands separators |
| `"(12)"` | **True** | accounting negative |
| `"12-"` | **True** | trailing sign |
| `"  12  "`, `"-12"`, `"+12"`, `"1E3"` | True | whitespace / signs / exponent |
| `"$12"` | False | currency symbol rejected |
| `"123&"`, `""`, `"abc"` | False | trailing type-char / empty / non-numeric |

`IsDate`: a `Date`, or a **string** parseable as date/time (`"1/2/2020"`, `"2020-01-02"`, `"10:30:00 AM"`) →
True; a **bare number is False** (`IsDate(40000) = False`, even though `CDate(40000)` works); `Empty`/`Null` →
False. `IsEmpty`/`IsNull`/`IsObject`/`IsArray` each report only their own subtype (`IsEmpty(0)=False`,
`IsNull(Empty)=False`, `IsObject(Nothing)=True`).

> **Approximations (documented in-code):** VB6's `IsNumeric` string grammar is locale-influenced and lenient
> about comma grouping and accounting notation; HexIDE strips a surrounding `()` and one trailing sign, removes
> group separators, and parses the remainder — the common cases match but exotic comma placements may differ.

### Array intrinsics (interpreter-core Phase 3.5)

`Array`/`Split`/`Join`/`Filter` are all **0-based** and 1-D. Verified:

| expression | result |
|---|---|
| `Array(10, 20, 30)` | Variant array, `LBound 0`, `UBound 2`, `TypeName "Variant()"`, `VarType 8204` |
| `Split("a,b,c", ",")` | **`String()`** array (not Variant), `["a","b","c"]` |
| `Split("a,,b", ",")` | `["a","","b"]` — empty middle elements **preserved** |
| `Split("")` | **empty array** — `LBound 0`, `UBound −1` |
| `Split("one two three")` | default delimiter is a **space** → `["one","two","three"]` |
| `Split("a-b-c-d", "-", 2)` | limit 2 → `["a","b-c-d"]` (remainder in the last element) |
| `Join(Array("a","b","c"), "-")` | `"a-b-c"` |
| `Join(Array("a","b","c"))` | `"a b c"` — default delimiter is a **space** |
| `Filter(Array("apple","banana","cherry"), "an")` | `["banana"]` (keep matches) |
| `Filter(…, "an", False)` | `["apple","cherry"]` (drop matches) |
| `Filter(…, "an", True, vbTextCompare)` | case-insensitive substring match |
| `Filter(Array("apple","pear"), "xyz")` | **empty array** — `LBound 0`, `UBound −1` |

> **Deferral:** `Array()`'s lower bound follows `Option Base` in VB6; HexIDE's builtin registry is static and
> has no access to the module's `Option Base`, so `Array()` is always 0-based (correct for the default; a
> documented divergence under `Option Base 1`). `Split`/`Join`/`Filter` are always 0-based regardless — no
> divergence there.

### Mid statement + Erase (2026-08-10 doc-debt sweep)

The **Mid statement** `Mid(target, start[, length]) = replacement` overwrites characters **in place** — the target's
total length **never changes**. Chars written = `min(length‖remaining, Len(replacement), Len(target) − start + 1)`.
`start` is 1-based; **out of `[1, Len(target)]` → Err 5** (Invalid procedure call).

| statement (target `s`) | result |
|---|---|
| `s="ABCDEF": Mid(s,2,3)="xy"` | `AxyDEF` — replacement shorter than length: only its 2 chars written |
| `s="ABCDEF": Mid(s,2,3)="wxyz"` | `AwxyEF` — replacement truncated to `length`=3 |
| `s="ABCDEF": Mid(s,3)="xyz"` | `ABxyzF` — no length: to end / replacement length |
| `s="ABC": Mid(s,2)="XYZ"` | `AXY` — clamps to the 2 remaining chars |
| `Mid(s,0)=…` · `Mid(s,Len(s)+1)=…` | **Err 5** |

**Erase** — a **dynamic** array is *freed* (undimensioned): afterward `UBound`/`LBound`/index → **Err 9**, and it can
be `ReDim`'d again. A **fixed** array keeps its bounds and resets every element to the type default (Integer → 0,
String → `""`). *(HexIDE residual: a fixed array of UDT/class elements isn't reset — the `VBArray` lacks the
interpreter context — scalar fixed arrays are faithful.)*

### Date/Time intrinsics (interpreter-core Phase 3.6)

Return types (verified): `Year/Month/Day/Hour/Minute/Second/Weekday/DatePart` → **Integer**;
`DateAdd/DateSerial/TimeSerial/DateValue/Now/Date` → **Date**; `DateDiff` → **Long**; `Timer` → **Single**.

| behaviour | verified |
|---|---|
| `Weekday(<Sunday>)` default | `1` (a **vbSunday** base); `Weekday(<Sunday>, vbMonday)` → `7` |
| `DateSerial(2020, 13, 1)` | `2021-01-01` (month rolls over) |
| `DateSerial(2020, 2, 30)` | `2020-03-01`; `DateSerial(2020, 3, 0)` → `2020-02-29` (day 0 = last of prev month) |
| `TimeSerial(25, 0, 0)` | `01:00:00` (hours roll over) |
| `DateAdd("m", 1, #1/31/2020#)` | `2020-02-29` — **month-end clamp** (not Mar 2) |
| `DateAdd("yyyy", 1, #2/29/2020#)` | `2021-02-28` |
| `DateAdd("yyyy", 100000, Now)` | **Err 5** (Invalid procedure call) — past the Date range |
| `TimeSerial(9999999, 0, 0)` | **Err 6** (Overflow) — arg past `Integer` / the Date range. **NB the two overflow codes DIFFER**: DateAdd → 5, TimeSerial → 6 (do not assume both are 6) |
| `DateDiff("d", #1/1/2020#, #12/31/2020#)` | `365`; `DateDiff("m", #1/15#, #3/10#)` → `2`; `DateDiff("yyyy", #12/31/2020#, #1/1/2021#)` → `1` |
| `DateDiff("h", 8:30, 9:15)` | **`1`** — DateDiff counts interval **boundaries crossed**, not the floored difference (0.75 h still crosses one hour boundary) |
| `DatePart("q", …)` / `DatePart("y", #2/1#)` / `DatePart("ww", #1/1/2020#)` / `DatePart("ww", #12/31/2020#)` | `1` / `32` / `1` / `53` (vbFirstJan1 + Sunday-start) |
| `MonthName(1[,True])` / `MonthName(12)` | `January` / `Jan` / `December` |
| `DateValue("March 15, 2020")` / `TimeValue("2:30:45 PM")` | `2020-03-15` / `14:30:45` |

### Format — numeric (interpreter-core Phase 3.7.1)

`Format`/`Format$` is being built facet-by-facet (3.7.1 numeric → 3.7.2 sections/scientific/scaling → 3.7.3
date/time → 3.7.4 string/Boolean). Numeric findings:

| call | result | note |
|---|---|---|
| `Format(2.5, "0")` | `3` | **half-away-from-zero** — NOT `CInt`'s banker's (`CInt(2.5)=2`) |
| `Format(0.5, "0")` / `Format(1.5, "0")` | `1` / `2` | " |
| `Format(2.345, "0.00")` | `2.35` | rounds the **decimal** value, not the raw double (2.3449… would floor to 2.34) |
| `Format(0.5, "#.##")` / `Format(0.5, "0.##")` | `.5` / `0.5` | `#` omits an insignificant zero; `0` forces it |
| `Format(0, "#")` / `Format(0, "0")` | `` (empty) / `0` | " |
| `Format(5, "000")` | `005` | `0` pads |
| `Format(1234.5678, "#,##0.00")` | `1,234.57` | grouping + 2 dp |
| `Format(0.5, "0%")` | `50%` | `%` scales ×100 |
| `Format(1234.5, "$#,##0.00")` | `$1,234.50` | `$` and other non-token chars are **literals** |
| `Format(1234.5678, "General Number"/"Fixed"/"Standard"/"Percent")` | `1234.5678` / `1234.57` / `1,234.57` | named numeric |

> **Rounding, pinned:** `Format` uses `MidpointRounding.AwayFromZero` on a `decimal` view of the value; this is
> the single most important divergence from the `C*` conversions (banker's). Culture supplies the decimal/group
> separators (and currency symbol for named `Currency`) — HexIDE uses `CurrentCulture` (faithful); tests stay on
> `.`/`,` masks, which agree across en-* and Invariant.

Advanced numeric (3.7.2):

| call | result | note |
|---|---|---|
| `Format(-1234.5, "#,##0.00;(#,##0.00)")` | `(1,234.50)` | the **negative section is fed the ABSOLUTE value** — it must supply its own sign/parens |
| `Format(0, "#,##0.00;(#,##0.00)")` | `0.00` | with 2 sections, zero uses the **positive** section |
| `Format(0, "0.00;(0.00);0.0000")` / `Format(0, "0;0;Empty")` | `0.0000` / `Empty` | 3rd section = zero; a placeholder-free section is a literal |
| `Format(1234.5678, "0.00E+00")` | `1.23E+03` | `E+` always prints the exponent sign |
| `Format(1234.5678, "0.00E-00")` | `1.23E03` | `E-` prints the sign only for a **negative** exponent |
| `Format(0.00012345, "0.00E+00")` | `1.23E-04` | |
| `Format(1234567, "#,##0,")` | `1,235` | a **trailing comma** (end of the integer part) scales ÷1000 each |
| `Format(1234567, "#,##0.0,")` | `1,234,567.0` | a comma **after** the decimal does NOT scale (dropped) |

**Out-of-decimal-range magnitudes** (`|x| > ~7.9e28` — beyond `System.Decimal`). VB6 does **not** error (verified):

| expression | vb6.exe | note |
|---|---|---|
| `Format(1E30)` / `Format(1E30, "General Number")` | `1E+30` | General/no-mask → **scientific** for very large/small |
| `Format(-2.5E29)` | `-2.5E+29` | " |
| `Format(1E300, "Scientific")` | `1.00E+300` | scientific named/mask → mantissa per the mask |
| `Format(1E30, "0.00E+00")` | `1.00E+30` | " |
| `Format(1E30, "0.00")` | `1000000000000000000000000000000.00` | a **fixed** mask FULLY EXPANDS (VB6's double-precision digit model) |
| `Format(1E28, "0.00")` | `10000000000000000000000000000.00` | `1E28` is *within* decimal range — the precise engine handles it |
| `Format(0.5, "0." & 20 zeros)` | `0.50000000000000000000` | a **wide fixed mask zero-pads** past the value's precision — VB6 does **not** error |
| `Format(1.5, "0." & 30 zeros)` | `1.500000000000000000000000000000` | " (30 fractional places, all-zero tail) |

> **HexIDE divergence (approximation):** the numeric Format engine is `decimal`-based, so it can't feed a magnitude
> beyond decimal range. HexIDE renders **every** numeric format of such a value in scientific notation (`G15`) —
> matching VB6 for General/Scientific, but **approximating** a fixed mask (`0.00`) as scientific rather than VB6's
> full expansion. The point of the fix (bug-hunt HIGH) was to stop `(decimal)d` throwing an **uncatchable**
> `OverflowException`; the fixed-mask expansion is a parked refinement.

Date/time (3.7.3), anchor `#3/5/2020 2:07:09 PM#` (a Thursday):

| token/mask | result | note |
|---|---|---|
| `d`/`dd`/`ddd`/`dddd` | `5` / `05` / `Thu` / `Thursday` | day |
| `m`/`mm`/`mmm`/`mmmm` | `3` / `03` / `Mar` / `March` | month |
| `yy`/`yyyy` | `20` / `2020`; `y` alone → `65` | year / **day-of-year** |
| `h`/`hh` | `14` / `14` | **24-hour** — no AM/PM token present |
| `h AM/PM` | `2 PM` | an AM/PM token switches `h` to **12-hour** |
| `AM/PM`·`am/pm`·`A/P`·`a/p` | `PM`·`pm`·`P`·`p` | |
| `hh:mm` / `hh:mm:ss` | `14:07` / `14:07:09` | **`m`/`mm` right after `h`/`hh` = MINUTE**; a separator between them doesn't reset it |
| `mm` standalone / `m/d/yy` | `03` / `3/5/20` | month when not after an hour token |
| `n`/`nn` | `7` / `07` | `n` is **always** minute |
| `q` / `w` / `ww` | `1` / `5` / `10` | quarter / weekday (vbSunday base) / week-of-year |
| `Format(0, "yyyy-mm-dd")` | `1899-12-30` | a **number under a date mask is its OLE serial** (serial 0 = 1899-12-30) |

String / Boolean / default (3.7.4):

| call | result | note |
|---|---|---|
| `Format("abc", "@@@@@")` | `  abc` | `@` = char-or-**space**, right-aligned |
| `Format("abc", "&&&&&")` | `abc` | `&` = char-or-**nothing** |
| `Format("abcde", "@@@")` | `abcde` | excess chars **overflow** — never truncated |
| `Format("abc", "!@@@@@")` | `abc  ` | `!` left-aligns |
| `Format("5551234", "(@@@) @@@-@@@@")` | `(   ) 555-1234` | literals kept; chars fill right-to-left |
| `Format("HeLLo", "<")` / `Format("hello", ">")` | `hello` / `HELLO` | `<`/`>` force case |
| `Format(True/5, "Yes/No")` / `Format(0, "Yes/No")` | `Yes` / `No` | Boolean formats: nonzero/True → first word |
| `Format(0, "True/False")` / `Format(-1, "On/Off")` | `False` / `On` | |
| `Format("abc", "0.00")` | `abc` | a **non-numeric string under a numeric mask is returned unchanged** |
| `Format(True)` / `Format(1234.5678)` (no format) | `True` / `1234.5678` | default = General Number / the string / General Date / True|False |

**Format engine complete** (3.7.1–3.7.4): numeric (named + custom, sections, scientific, scaling), date/time
(all custom tokens + named), string (`@ & < > !`), Boolean named formats, and the no-format default dispatch —
all `vb6.exe`-verified. `Format$` still awaits the `$`-type-hint dispatch (shared with the other `$`-twins).

> **Environment-dependent (not a hardcode target):** `DateSerial`'s two-digit-year window follows the OS/culture
> setting — this machine (window 1950–2049) gives `DateSerial(30,…) = 2030` and `DateSerial(75,…) = 1975`. VB6
> reads the registry two-digit-year setting; HexIDE uses .NET's `Calendar.ToFourDigitYear` (same default), so
> both track the host. Not asserted in CI. `WeekdayName`'s **default** `firstdayofweek` is likewise
> system-influenced (this machine returned `Monday` for `WeekdayName(1)`); HexIDE defaults it to the documented
> `vbSunday`, so only the explicit-`firstdayofweek` forms are asserted.

---

## Assumptions overturned by the oracle

These are the cases where building from documentation/memory would have been **wrong** — the justification for
always checking:

1. **`/` int/int is `Double`, not `Single`.** The planning pass asserted "true VB6 = Single"; the oracle showed
   Double, and the *existing* `DivisionOperator` test was already correct — the oracle **saved a wrong rewrite.**
2. **`Byte + Byte → Byte`** (overflows at 255), not → Integer. Found via a runtime-overflow modal.
3. **Currency dominates Double** in `+ − *` (`10@ + 1.5 → Currency`). The documented "Currency < Double" ladder
   is wrong for arithmetic result types.
4. **Octal `&O` two's-complement-wraps like hex** (`&O177777 → −1`). An earlier naive octal impl (magnitude
   only) was wrong and had to be unified with hex.
5. **Unary `−` of `Int16.MinValue` wraps to −32768 with no error** (a two's-complement quirk).
6. **`IsNumeric` accepts hex/octal strings, thousands separators, accounting parentheses and trailing signs**
   (`"&HFF"`, `"1,234"`, `"(12)"`, `"12-"` all True) but **rejects a currency symbol** (`"$12"` False) — much
   more permissive than a naive "does it parse as a number" would suggest.
7. **`IsNumeric(True) = True` and `IsNumeric(Empty) = True`, but `IsNumeric(<Date>) = False`** — Booleans/Empty
   are numeric; Dates are not.
8. **`Abs` preserves the operand type** (`Abs(-4) → Integer`), and **`Int` floors while `Fix` truncates**
   (`Int(-2.5) = -3`, `Fix(-2.5) = -2`) — they diverge only for negatives.
9. **A bare number is not a date to `IsDate`** (`IsDate(40000) = False`) even though `CDate(40000)` converts.
10. **`DateDiff` counts interval boundaries crossed, not the floored difference** (`DateDiff("h", 8:30, 9:15) = 1`,
    not `0`) — a naive "difference in units, truncated" would be wrong. And **`DateAdd` clamps month-ends**
    (`Jan 31 + 1 month = Feb 29`, not Mar 2).
11. **`For Each` over a multi-dimensional array is column-major — the FIRST subscript varies FASTEST.** A
    `(1..2, 1..3)` array yields `11,21,12,22,13,23`, i.e. `(1,1),(2,1),(1,2),(2,2),(1,3),(2,3)`. The
    interpreter-core spec's own note said "row-major, first subscript slowest" — **the oracle corrected the
    spec.** A naive nested flatten (row-major) would have shipped the wrong order.
12. **VB6's parser tolerates absurd expression nesting** — `/make` compiles **4096** nested parentheses
    (`x = (((…1…)))`) with no error (probed at 64→4096, all succeed); its parser is effectively unbounded here.
    HexIDE's recursive-descent parser overflows the C# stack near ~600, so it **cannot** match this — it installs a
    `ParseDepthGuard` (rule-depth 300, ~6× above any real code's ~50) that rejects deeper nesting as a clean
    "nesting too deep" compile error instead of an uncatchable crash. A **deliberate, documented divergence** on
    degenerate input (see `docs/interpreter-gaps.md`); no real VB6 program approaches it.

---

## `Class_Terminate` lifecycle (interpreter-advanced Phase 4.2)

Verified with a `Thing` class logging `Class_Initialize`/`Class_Terminate` to a file (via a module `Log` sub),
driven through `Set`/scope-exit/escape scenarios. **VB6 `Class_Terminate` is true last-reference-drop
(reference-counted)** — it fires exactly when the final reference to an instance goes away, never before:

| Scenario | `vb6.exe` result |
|---|---|
| `Set x = New T` then `Set x = Nothing` (sole owner) | `Terminate` fires **synchronously** at the `Set … = Nothing` statement |
| `Set y = New T` when `y` already holds an object | the **new** instance's `Initialize` fires **before** the old instance's `Terminate`; the old terminates at the reassignment (its last ref dropped) |
| Two+ locals holding objects, at `End Sub`/`End Function` | terminate in **declaration order** (`Dim a,b,c` → `Term a`, `Term b`, `Term c` — *not* reverse/LIFO; declaration order == assignment order in the probe) |
| Function returns a local object (`Set F = t`) | the local does **NOT** terminate at the function's scope exit — the return value holds a reference; it terminates when the **receiver** is later cleared |
| Local stored into a module-global (factory: `Set gThing = t`) | the local does **NOT** terminate at scope exit — the module global holds it; terminates when the global is cleared |
| Shared local (`Set b = a`) then `Set a = Nothing` | `Terminate` does **NOT** fire — `b` still references it; it fires when the **last** holder drops (here, `b` at `End Sub`) |
| `Set gLast = Nothing` (drops the last *external* ref) **inside a running method** | `Terminate` fires **only after the method returns**, not mid-method — a running method's **`Me` is a counted reference** |
| `New T` passed straight into a call (`Consume New T`), never stored — **`ByVal` and `ByRef`** | the temporary terminates **right after the call statement returns** — the temporary is a counted reference for the statement's duration |
| `With New T … End With` | terminates at **`End With`** — the `With` target is a counted reference for the block |

Every one of these is exactly what **reference counting** predicts (a reference is counted wherever it is held —
named storage, a call parameter, `Me` during a call, a `With` target, a statement temporary — and `Terminate`
fires when the count reaches zero). Phase 4.2 therefore implements **real slot-based reference counting** (runtime
execution machinery — in bounds; the walls are CST-only / no-compile-stage / no-extended-language-surface, none of which
refcounting touches), **not** a best-effort scan. Cycles leak (never reach zero) — faithful to VB6. The only
documented divergence is interface-pointer-granularity temporaries beyond call arguments (rare).

**Design consequence (recorded for Phase 4.2):** a best-effort scheme that fires `Terminate` on *every*
`Set … = Nothing` or scope-exit **without** tracking whether another reference survives would **wrongly**
terminate live objects in the return-escape, module-var-factory, and shared-ref cases — all common patterns, and
a false-fire (running `Terminate` then continuing to use the object) is worse than not firing. Faithful behaviour
needs the last-reference test — either a persistent refcount or a point-in-time reference scan. (Cycles leak in
VB6 too — never collecting them is itself faithful.)

## Custom events — `Event` / `RaiseEvent` / `WithEvents` (interpreter-advanced Phase 5)

Verified with a `Clock` source class (declares `Event Tick(ByRef Cancel As Boolean)` + `Event Plain()`, raises
them from `DoTick`/`DoPlain`) and a `Listener` class (`Private WithEvents src As Clock`, `src_Tick` handler, an
`Attach`/`Detach` to `Set src`), driven from `Sub Main`.

| Behaviour | `vb6.exe` result |
|---|---|
| `WithEvents` in a **standard `.bas`** module | **compile error: "Only valid in object module"** — `WithEvents` (and the event-sink pattern) is **class/form-only**. The sink is therefore always a class **instance**; the handler `{var}_{event}` is a method of that instance and runs **on it** (with `Me` = the listener instance). |
| `RaiseEvent` with **no sink** bound (`WithEvents` var is `Nothing`) | **silent no-op** — the raiser's pre/post lines bracket nothing; a `ByRef Cancel` stays unchanged. |
| `RaiseEvent` of an event with **no matching `{var}_{event}` handler** | **silent no-op** (handlers are optional; e.g. `Plain` with no `src_Plain`). |
| handler present | runs **synchronously between** the raiser's surrounding statements (blocking); a **`ByRef Cancel` written by the handler is seen by the raiser** after `RaiseEvent` returns. |
| **Multiple** `WithEvents` vars bound to one source (multicast) | **ALL fire, in *attachment* order** (first-`Set` fires first). The event's `ByRef` args are **shared across handlers** — a later handler sees an earlier one's write-back (e.g. `Cancel` left `True` by the first handler is `True` when the second runs, and to the raiser). |
| `Set src = Nothing` | unbinds **that** sink only — other sinks on the same source still fire. |
| Rebind `Set src = otherSource` | the sink **moves** — raising on the *old* source no longer reaches this handler; raising on the *new* source does. |
| Advised listener whose **own** references drop (source stays alive elsewhere) | the listener **terminates** at scope-exit — a source does **NOT** hold a strong back-reference to its listeners, so there is **no leaked cycle**. VB6 then auto-unadvises, so a later raise on the still-alive source does **not** reach the (now-dead) handler. *(Overturned the initial "strong back-ref → leak" guess — the oracle says the opposite.)* |
| `RaiseEvent Foo(SideEffect())` with **no listener** bound | the **argument expressions are still evaluated** (a side-effecting arg runs — a counter incremented to 1) — then dispatch is a no-op. VB6 evaluates `RaiseEvent` args regardless of whether any connection is advised. |
| `Set src = Nothing` where the old source raises an event in its **own `Class_Terminate`** (source held only by that `WithEvents` field) | VB6 **unadvises before releasing** — the detaching listener is disconnected first, so the source's `Class_Terminate`-time `RaiseEvent` does **not** reach it. (Unadvise-then-release ordering.) |

**`Set` slot ordering — assign the new reference BEFORE releasing the old.** Probed with a `CThing` whose
`Class_Terminate` reports what the global `g` points to across `Set g = New CThing` (twice) then `Set g = Nothing`:
result **`0;N;`**. So during the reassign, the release-triggered `Class_Terminate` of the *old* instance already sees
`g` pointing at the *new* instance (whose `Id` is still 0), and the final `Set g = Nothing`'s terminate sees `g` as
Nothing. Consequence for the interpreter: `Set` must write the slot/field/element **then** `ReleaseRef` the old
occupant (not the reverse) — otherwise a terminate reads the dying object, and a reassignment made inside that
terminate is clobbered by a late slot-write. (In the current interpreter this is defense-in-depth: a `Class_Terminate`
can't yet read an outer-scope slot, so the scenario isn't reachable — but the ordering is now correct.)

**Design consequence:** VB6 events are **multicast** with deterministic attach-order dispatch and shared ByRef
args. A "single-sink" MVP would visibly diverge whenever ≥2 listeners bind one source (only one would fire). The
sink is a **(listener-instance, WithEvents-var-name)** pair; a source keeps the set of currently-bound sinks and
dispatches to `{varName}_{eventName}` on each listener instance, synchronously, in attach order, passing the same
(ByRef-aliased) args to each.

## `End` fires no `Class_Terminate` (interpreter-debugger reset)

Verified: a module-level object held live, then `End` — the class's `Class_Terminate` does **not** run (output is
`BEFORE-END` only; no `TERMINATE-FIRED`). VB6 `End` terminates abruptly and invokes **no** `Terminate` (nor
`Unload`/`QueryUnload`). So the debugger's reset ("Stop") must unwind the walk **without** firing `Class_Terminate`
— the interpreter suppresses it via an aborting-guard on `FireTerminate` while `IDebugController.IsAborting` is set.

## Relational comparison + `ChDir`/`ChDrive` (2026-08-03 gap-audit fixes)

**String relational comparison** (`< <= > >=`), verified against `vb6.exe`:

| Case | Result | Rule |
|---|---|---|
| `"a" < "b"` | True | ordinal |
| `"B" < "a"` | **True** | **Binary/ordinal** ('B'=66 < 'a'=97) — VB6 default `Option Compare Binary`, *not* case-insensitive |
| `"10" < "9"` | **True** | **string** compare ('1' < '9') — *not* numeric (numeric would be False) |
| `"" < "a"` | True | empty string is least |
| `5 < "10"` | True | **string-vs-number → NUMERIC** (numeric string coerces): 5 < 10 |
| `"10" < 5` | False | numeric: 10 < 5 |
| `"abc" < 5` | **Err 13** | non-numeric string vs number → Type Mismatch |

Two strings → ordinal `String.Compare(..., Ordinal)`. **Landed:** the both-string case (interpreter previously
threw / mis-parsed numeric-looking strings numerically). **Still a gap:** string-vs-*number* comparison — the
interpreter throws Type Mismatch (its `GetTwoValuesSameTypes` rejects any type mismatch) where VB6 coerces a
numeric string and compares numerically. (`Option Compare Text` = case-insensitive is a separate wall.)

**`ChDir` / `ChDrive`** (the args coerce to String; oracle-verified):

| Statement | Result |
|---|---|
| `ChDir "<valid path>"` | ok |
| `ChDir "<bad path>"` / `ChDir 5` (→ `"5"`) / `ChDir ""` | **Path Not Found (76)** — all failures, incl. a coerced number |
| `ChDrive "C"` | ok |
| `ChDrive "Q"` (valid letter, no such drive) | **Device Unavailable (68)** |
| `ChDrive 5` (→ `"5"`, non-letter first char) | **Invalid Procedure Call (5)** |
| `ChDrive ""` | no-op (Err 0) |

(These were fully broken before — the string-unpack helper had no `String` branch, so both always threw. The
audit's "applies the side-effect then throws" description was inaccurate.)

## Control arrays (2026-08-10)

Verified against `vb6.exe` with a real form-based probe (`/make` an EXE, run it, `Form_Load` writes results to a
log under `On Error Resume Next`). Form has a 2-element `CommandButton` array `Command1(0)`, `Command1(1)`.

| Probe | Result | Rule |
|---|---|---|
| `Command1.Count` | `2` | the array-name is a members-bearing object; `.Count` = element count |
| `Command1.LBound` | `0` | lowest index present |
| `Command1.UBound` | `1` | highest index present (`Count = UBound − LBound + 1`) |
| `Command1(0).Index` | `0` | each element knows its own `Index` (read-only at runtime) |
| `Command1(1).Caption` | `"B"` | indexed element property read |
| `Command1(9).Caption` (read) | **Err 340** | **Control array element doesn't exist** — a missing element |
| `Command1(9).Caption = "x"` (write) | **Err 340** | same on the write side |
| `TypeName(Command1(0))` | `"CommandButton"` | an element's type is the control type, not "Object" |

Compile facts (from `/make` succeeding): `.Count`/`.LBound`/`.UBound`/`(i).Index` all **compile** — the array
name is a first-class object with those members. Not probed (textbook VB6, will pin via the interpreter's own live
test): the shared event handler is `Private Sub Command1_Click(Index As Integer)` and the fired element's `Index`
is passed as that leading argument. Harness + files were under a scratch dir (never committed).

**Runtime `Load` / `Unload`** (second form probe, same method) — the array starts with design-time elements 0, 1:

| Probe | Result | Rule |
|---|---|---|
| `Load Command1(5)` (new index) | `Err 0`, `Count`→3 | creates a new element |
| loaded `Command1(5).Caption` | `"A"` | clones the **lowest-index** element's properties (index 0 = "A") |
| loaded `Command1(5).Visible` | **`False`** | a loaded element starts **hidden** — you must set `.Visible = True` to show it |
| loaded `Command1(5).Left` | `240` | inherits position from the template (overlaps index 0 until moved) |
| loaded `Command1(5).Index` | `5` | its own index |
| `Load Command1(0)` (existing) | **`Err 360`** | Object already loaded |
| `Unload Command1(5)` (loaded) | `Err 0`, `Count`→2 | removes the loaded element |
| `Unload Command1(0)` (design-time) | **`Err 362`** | Can't unload controls created at design time |
| `Unload Command1(9)` (missing) | **`Err 340`** | Control array element doesn't exist |

So `Load` = clone the lowest-index element's props but force `Visible=False`; the three failure modes are distinct
codes (360 already-loaded, 362 can't-unload-design-time, 340 missing).


## Option buttons — `Value` is a Boolean, and the group can be empty (2026-08-22, #95)

Verified with a real form probe (`/make` an EXE, run it, `Form_Load` writes results under `On Error Resume
Next`). Form has `Option1`, `Option2`, `Option3` flat on the form — one group, no Frame — with `Option2`
carrying the designer's `Value = -1  'True`, plus a `Check1` for comparison. Each control's `Click` appends to
a log string so dispatch is observable.

| Probe | Result | Rule |
|---|---|---|
| `TypeName(Option1.Value)` | `Boolean` | **not** the check box's Integer |
| `TypeName(Check1.Value)` | `Integer` | the two controls are not the same shape |
| `Option2.Value` at load | `True` | the designer's `-1  'True` survives, and fires **no** Click |
| `Option1.Value = True` | `Err 0`, Option1 `True`, Option2 `False` | selecting one clears the group |
| ...its Click log | `C1;` | the **newly selected** one fires; the deselected sibling fires **nothing** |
| `Option2.Value = True` when already `True` | Click log empty | Click is a *transition*, not an assignment |
| `Option1.Value = False` on the selected one | `Err 0`, o1/o2/o3 all `False` | **honoured, not refused — the group is left with nothing selected**, and nothing is promoted in its place |
| ...its Click log | empty | deselection raises no event |
| `Option1.Value = False` when already `False` | `Err 0`, selection untouched | a no-op |

The two questions #95 left open both answer against the intuitive guess: a programmatic select **does** raise
`Click` (so the event cannot live in an `OnClick` override, which only a user reaches), and clearing the
selected member **is** allowed (so a group with nothing selected is a reachable state — from code only; a user
cannot click their way to it).

### Boolean coercion at the property boundary is the ordinary language rule

Probed separately on `Command1.Enabled`/`.Visible`, because nothing about it is option-button-specific:

| Probe | Result | Rule |
|---|---|---|
| `Enabled = 0` / `= 1` / `= 7` | `False` / `True` / `True` | **any non-zero** is True |
| `Enabled = 0.4` | `True` | non-zero, **not** "rounds to 0" |
| `Enabled = 100000` | `True` | no Integer range check |
| `Enabled = "False"` / `= "1"` | `False` / `True` | strings parse, as a Boolean literal or as a number |
| `Enabled = "banana"` | **Err 13**, property **unchanged** | trappable type mismatch |
| `Enabled = Empty` | `False` | Empty is zero |
| `Enabled = Null` | **Err 94** | Invalid use of Null |
| `Command1.Left = True` | `-1` (`Single`) | the reverse crossing: True widens to −1 |

These are exactly `CBool`'s rules, which the interpreter had already pinned — so `AvaloniaInteroperability`
calls `CBool` rather than growing a second opinion about them. Before #95 this whole row set threw a bare CLR
exception, not a VB6 error, for **every** boolean property on every control: `On Error Resume Next` could not
catch it.

### `Appearance` is readable AND settable at run time

| Probe | Result |
|---|---|
| `Option1.Appearance` (read) | `1` (`Integer`) |
| `Option1.Appearance = 0` | `Err 0`, reads back `0` |
| `Check1.Appearance` | same on both counts |

Worth pinning because the plausible assumption is the opposite one — several VB6 appearance-ish properties
*are* design-time only and raise Err 382/383 — and `OptionButton.Appearance` was registered alongside `Value`
in #95 on the strength of this measurement rather than on that assumption.

### `Check1.Value = True` is an error

`Err 380` (Invalid property value), and the value is left unchanged. A check box's `Value` is 0..2 and −1 is
outside it. This is the sharpest evidence that the two controls' `Value` properties cannot share a type: the
literal that *selects* an option button is *refused* by a check box.


## Out of stack space — Error 28 (2026-08-22, #80)

Verified with a `Sub Main` probe (`/make`, run, append each line to a log so nothing is lost to buffering).

| Probe | Result |
|---|---|
| `Sub A(): Call A(): End Sub`, `On Error GoTo` in the caller | **`Err 28`**, `"Out of stack space"` |
| depth reached before it raises | **258,825 frames** |
| the program afterwards | `2 + 2` = `4`, `Err.Number` = 0 — VB6 **does not terminate** |
| `Err.Raise 28` | same number and description |

**258,825 is the number that settles the design.** The fix for an interpreter is a *real stack probe*, not a
depth counter, because any counter a person would choose is two orders of magnitude below VB6's real
capacity — and wrong in the direction that breaks working programs rather than the one that catches broken
ones. HexIDE uses `RuntimeHelpers.TryEnsureSufficientExecutionStack()` at `RunProcedure`, the single sink
every user procedure, method, Property accessor and lifecycle hook flows through.

**Known divergence in depth, not in behaviour.** HexIDE's own frames are far fatter than compiled VB6's — an
async state machine plus an ANTLR visitor walk per call — so on a 1 MB Windows thread stack it reaches about
**202** frames for a bare `Call A(n + 1)` sub and **between 60 and 80** for a recursive Function. Linux
main threads get 8 MB and go several times deeper. That gap is tracked on its own; it is not a regression
from the guard, since the process was already dying at the same depth.

Two probes did **not** complete and their answers are still open:

- a recursive **Function** (`DeepFn = DeepFn()`) had not raised 28 after 120 s, where the equivalent Sub
  raises in well under a second. Whether VB6 gets much deeper in a Function or simply takes far longer is
  unmeasured;
- `On Error Resume Next` **inside** the recursive Sub ran for minutes without settling. Each frame swallows
  the error and carries on, so the unwind re-recurses; whether it terminates at all is unmeasured.

### Variant arithmetic promotes on overflow

First spotted while writing the #80 tests (an untyped `Fact(10)` should be 3628800 as a Long, and raised
Err 6 instead). Superseded by the fuller measurement in *Declared type, Variant subtype, and overflow
promotion* below, which has the whole ladder and the declared-vs-Variant rule that drives it.


## Infinity, NaN, and division by zero (2026-08-22)

VB6 has **no Infinity and no NaN**. IEEE-754 produces them; VB6 raises instead — so a result that is not a
VB6 number is always an error, never a value.

| Probe | Result | Rule |
|---|---|---|
| `1E+308 * 10` | **Err 6** | infinite magnitude → Overflow |
| `1E+308 / 0.0001` | **Err 6** | same, through `/` |
| `1E+308 ^ 2` | **Err 6** | same, through `^` |
| `Exp(1000)` | **Err 6** | same, through an intrinsic |
| `CSng(3E+38) * CSng(10)` | **Err 6** | Single overflows at its own range |
| `(-2) ^ 0.5` | **Err 5** | NaN → Invalid procedure call |
| `0 ^ -1` | **Err 5** | infinite, but a DOMAIN error, so 5 and not 6 |
| `Sqr(-1)`, `Log(0)`, `Log(-1)` | **Err 5** | domain |
| `2 ^ 10` | `1024` Double | `^` is always Double |

`0 ^ -1` is the row worth keeping: the result is infinite, so the obvious rule says Overflow, and vb6.exe
says Invalid procedure call. No inspection of the result can get it right — the distinction is in the cause.

### Division by zero depends on whether the operands were DECLARED

| Probe | Result |
|---|---|
| `a / b` with **Variant** operands (`a = 1`, `b = 0`) | **Err 6** |
| `a / b` with **declared** `Integer` or `Double` operands | **Err 11** |
| `1 / 0` (literals) | **Err 11** |
| `a \ b` by zero, declared or Variant | **Err 11** |
| `a Mod b` by zero | **Err 11** |

Real division by zero is *Overflow* on the Variant path and *Division by zero* everywhere else — the OLE
Automation `VarDiv` quirk, which maps a zero divisor to `DISP_E_OVERFLOW`. `\` and `Mod` are Err 11 either
way. HexIDE cannot express the Variant row yet: it has no notion of which operands were declared (see
*Declared variables do not retain their declared type* below), so it raises 11 for all of them.

## Declared type, Variant subtype, and overflow promotion (2026-08-22)

Three behaviours turn on the same distinction — **was this operand statically typed, or a Variant?** — and
HexIDE currently cannot tell, because a declared variable does not keep its type.

### A declared variable reports its declared type

| `Dim x As T`, `x = 3`, `TypeName(x)` | VB6 | HexIDE |
|---|---|---|
| `Integer` | `Integer` | `Integer` ✓ |
| `Long` | `Long` | **`Integer`** |
| `Double` | `Double` | **`Integer`** |
| `Single` | `Single` | **`Integer`** |
| `Byte` | `Byte` | **`Integer`** |
| `Currency` | `Currency` | **`Integer`** |
| `Boolean` / `String` | `Boolean` / `String` | ✓ |

The assignment overwrites the slot with the *literal's* subtype and the declared type is lost. `Dim` itself
is right — it seeds a correctly-typed zero — so this is purely an assignment-side loss.

### …and the declared type drives the result type

| expression | VB6 |
|---|---|
| `declInt * declLong` (30000 * 3) | `90000` **Long** — the ladder already widens; no promotion needed |
| `declInt * declDbl` (3 * 2) | `6` **Double** |
| `declByte * declInt` (100 * 3) | `300` **Integer** |
| `declByte * declByte` (200 + 200) | **Err 6** — Byte+Byte stays Byte |

`declInt * declLong` is the one that shows the cost: HexIDE raises Err 6 there, not because promotion is
missing but because it thinks the Long operand is an Integer.

### Variant arithmetic widens instead of overflowing

Byte → Integer → Long → Double, and **stops**. Currency, Decimal and Double are terminal — overflowing one
of those is Err 6 with no promotion.

| expression (Variant operands) | result | type |
|---|---|---|
| `CByte(200) + CByte(100)` | 300 | **Integer** |
| `30000 * 3` | 90000 | **Long** |
| `2147483647 + 1` | 2147483648 | **Double** |
| `2000000000 * 3` | 6000000000 | **Double** |
| `CSng(3E+38) * CSng(3)` | 9.0E+38 | **Double** |
| `CCur(9E+14) * 10` | — | **Err 6** |
| `CDec("7.9E+28") * 10` | — | **Err 6** |
| `1E+308 * 10` | — | **Err 6** |
| `variant * declInt` (30000 * 3) | 90000 | **Long** — one Variant operand is enough |
| `variant * literal` (30000 * 3) | 90000 | **Long** |
| `declInt * declInt` / literal-only | — | **Err 6** — no Variant, no widening |

Operators that widen: `+`, `-`, `*`, `\` (`CInt(-32768) \ CInt(-1)` = 32768 **Long**), unary `-`, and `Abs`.
`/` and `^` are always Double anyway. Assigning a widened result back into a declared variable range-checks
normally: `i = 30000 * 3` is Err 6, `l = 30000 * 3` is 90000.

> **This corrects the qualifier in *Arithmetic result types* above.** That table was measured entirely
> through declared variables, and its conclusion — "there is no overflow auto-promotion" — holds only there.


### Coercion-on-store — what a declared variable does to the value you put in it

| assignment | result | rule |
|---|---|---|
| `Dim s As String : s = 5` | `"5"` String | anything stringifies |
| `s = True` | `"True"` String | |
| `Dim b As Boolean : b = 5` | `True` | CBool: any non-zero |
| `b = "True"` | `True` | strings parse |
| `b = "banana"` | **Err 13** | and raise when they cannot |
| `Dim i As Integer : i = "12"` | `12` | a numeric target parses a String too |
| `i = "abc"` | **Err 13** | |
| `i = 12.6` / `i = 12.5` | `13` / **`12`** | half-to-even, not away-from-zero |
| `i = True` | `-1` | |
| `Dim b As Byte : b = 300` | **Err 6** | the declared range is enforced on store |
| `Dim x As T : x = Null` | **Err 94** | every T, String included |
| `x = Empty` | `0` / `""` | |
| `Dim l As Long : l = 3 : l = l + 1` | `4` **Long** | the declared type survives arithmetic |
| `Dim a(1 To 3) As Long : a(1) = 5` | `5` **Long** | array elements carry the element type |
| `a(2) = 30000 : a(2) * 3` | `90000` **Long** | …and widen like one |

### ByRef takes its type from the caller, because VB6 will not let it differ

`Call TakesLong(v)` with `v As Variant` and `Sub TakesLong(ByRef x As Long)` is a **compile error** —
*"ByRef argument type mismatch"*. So the two sides always agree, and a ByRef alias can inherit the caller's
declared type with nothing to convert at the boundary. (HexIDE does not reject it: compile-time argument
type checking needs a bound AST, which is the language engine's job, not HexIDE's.)


## The `Empty` literal (2026-08-22, #126)

| Probe | Result |
|---|---|
| `TypeName(Empty)` | `"Empty"` |
| `VarType(Empty)` | `0` |
| `IsEmpty(Empty)` / `IsNull(Empty)` | `True` / `False` |
| an un-assigned `Dim u` | `TypeName(u)` = `"Empty"`, `u = Empty` is `True` |
| `Empty = 0` | **True** |
| `Empty = ""` | **True** |
| `Empty = False` | **True** |
| `Empty = Null` | **Null** — neither True nor False |
| `v = 5 : v = Empty` | clears the Variant; `IsEmpty(v)` is `True` |
| `Empty + 1` | `1` Integer |
| `Empty * 2` | `0` Integer |
| `Empty & "x"` | `"x"` |
| `Dim Empty As Integer` | **Syntax error** — it is a reserved word, not a name |

Empty coerces to its partner's **zero**, whatever kind of zero that is — `0`, `""` or `False` — which is
what makes all three comparisons True at once. Against `Null` the comparison stays Null, because Null
propagates and Empty does not stop it.

That last row is why `Empty` belongs in the lexer beside `NOTHING`/`NULL` rather than in the built-in
constants table: VB6 reserves the word, so a user variable must not be able to shadow it. (In ANTLR the
token has to be named `EMPTY_`, because `ParserRuleContext` already has a static `EMPTY` — the same
collision the vendored LSP grammar already works around for `NULL_`.)

## The `App` object — and the difference between F5 and a compiled exe (2026-09-01, #136)

One probe project, three deliberately different `.vbp` keys — `Name="NameKey"`, `Title="TitleKey"`,
`ExeName32="ExeNameKey.exe"`, saved as `DesignTime.vbp` — so every answer names its own source instead of
identical strings agreeing by coincidence. Run twice: compiled with `/make`, and under **F5 in the IDE**.

| | compiled `.exe` | design time (F5) |
|---|---|---|
| `App.Title` | `TitleKey"` — the `Title=` key | `TitleKey"` — same |
| `App.EXEName` | `ExeNameKey` — the exe's filename, no extension | **`DesignTime` — the `.vbp` filename** |
| `App.Path` | the exe's folder | the project's folder |
| `App.ProductName` | follows `Title` | **empty** |
| `App.PrevInstance` | `False` | `False` |
| `App.Major` / `Minor` / `Revision` | `1` / `0` / `0` | same |
| `CompanyName`, `FileDescription`, `LegalCopyright`, `Comments` | empty when the version keys are unset | same |

**`App.EXEName` at design time is the `.vbp` filename** — not `ExeName32`, and not the project `Name`.
Both of those were present and different, and neither is what came back. This is the row worth having
measured: guessing "the project name" is the obvious answer and it is wrong.

**`App.ProductName` is empty at design time** but follows `Title` once compiled — it is read from the
version resource, which only a built exe has.

**With no `Title=` key at all**, a compiled `App.Title` falls back to the project `Name=` (measured
separately: `Name="verify"`, no `Title=` → `App.Title` = `verify`).

### A VB6 defect: `Title=` keeps its closing quote

`Title="TitleValue"` yields `App.Title` = **`TitleValue"`**. `Title=UnquotedTitle` reads back clean, so it
is the quotes: VB6's `.vbp` reader strips the leading `"` and not the trailing one. It shows up under F5
as well as compiled, so it is the project-file reader rather than the resource writer.

This is not academic — **VB6's own templates write the key quoted**: `Title="Project1"` appears verbatim in
the `VB98\Template` corpus. So real VB6 projects carry a stray `"` at the end of `App.Title`.

**HexIDE strips both quotes.** Per the Fidelity Principle in `CLAUDE.md` — reproduce VB6's intended
behaviour, not its bugs — a trailing quote in an application's title is a defect, not a design. Recorded
here so the divergence is deliberate and traceable rather than a later "bug".

### Note on method

`scripts/vb6-oracle.ps1` covers the compiled column; the design-time column cannot be automated from here.
A `MsgBox` is modal, so a probe that opens one never writes its file, and reading a caption from outside
does not work either — a PowerShell Direct session cannot see the guest's interactive desktop and
enumerates zero windows session-wide. The F5 column was produced by pushing the project into the VM with
`Copy-Item -ToSession`, having a human press F5, and reading the output file back over the same session.
That is the pattern for any future design-time question.
## `IsMissing`, and the shape of an omitted argument (2026-09-01)

`IsMissing` only means anything inside a procedure that has an `Optional` parameter, so this needed a probe
with its own procedures rather than the expression harness. No modal is involved, so it still ran headless
through `/make`.

| call | `IsMissing` | value |
|---|---|---|
| `Probe(1)` where `Optional v` | **True** | |
| `Probe(1, 2)` where `Optional v` | False | 2 |
| `Probe(1)` where `Optional v = 5` | **False** | 5 |
| `Probe(1)` where `Optional n As Integer` | **False** | 0 |
| `Probe(1, , 3)` where `Optional b, Optional c` | b **True**, c False | |
| `Probe(1)` where `a` is required | False | |

**An omitted argument is a Variant of subtype `vbError`** — `TypeName` = **`"Error"`**, `VarType` = **10**,
`IsEmpty` = False, `IsNull` = False, `IsError` = **True**. It is emphatically *not* `Empty`, and treating
it as Empty is what makes `IsMissing` impossible to express — the two are separate subtypes and VB6 tests
them separately.

**Two guesses the measurements overturned**, both of which would have shipped wrong:

- An `Optional` with a **declared type** is never missing. There is nowhere in an `Integer` to put a
  `vbError` Variant, so an omitted `Optional n As Integer` simply gets `0`.
- An `Optional` with a **default** is never missing either — the default supplies it, so `IsMissing` is
  False and the value is the default.

So `IsMissing` is True in exactly one situation: an `Optional` with neither a declared type nor a default,
left out by the caller (or left blank in the middle of the list — the last row ties this to #135, where a
blank argument keeps its position).

**Measured but not implemented:** using a missing value in a string concatenation raises **Error 13, Type
mismatch**. Recorded so the next person does not have to re-derive it; HexIDE currently lets such a value
flow rather than raising.

## A control VB6 cannot load renders as an empty box (2026-09-01, design-time)

**Verified**, by the human-in-the-loop method in *Note on method* above: a `.frm` citing an unregistered
OCX pushed into the VM, opened in `VB6.EXE`, screenshotted.

VB6 draws a control it cannot load as a **plain etched rectangle at the recorded position and size** —
nothing inside it. No diagonals corner-to-corner, no X, no type name, no control name, no icon. Just an
empty sunken frame where the control would be.

The recollection this was checked against was "an empty box with diagonal lines from each corner". That is
wrong **for VB6**, and it is the sort of detail worth having right before anyone draws one.

It may well be right about something else — VBA's editor, or a later Visual Studio — and that is the point
rather than a footnote. A crossed placeholder is a real thing someone has genuinely seen; it just is not
this product. This is the hazard `CLAUDE.md` names when it says VBA diverges from VB6 at the edges and
`vb6.exe` always wins: a memory can be perfectly accurate and still be about the wrong host. Which host it
came from was not worth chasing, because the only question that mattered — what VB6 draws — now has a
screenshot behind it.

The probe used a made-up GUID (`{A1B2C3D4-1111-2222-3333-444455556666}`, `NOSUCH.OCX`) with a real
`CommandButton` beneath it for scale; the box's width matched the button's `3015` twips, so the geometry
comes straight from the designer file's `Left`/`Top`/`Width`/`Height`.

### Why HexIDE should NOT copy it exactly

VB6's box carries meaning it does not itself supply: the developer reached it by dismissing a *"cannot load
control"* dialog seconds earlier, so an unlabelled rectangle is enough to say "the thing you were just told
about goes here".

HexIDE has no such dialog, and its box would mean something different — **not** "this OCX is missing from
this machine" but "HexIDE does not model this control", which stays true on a machine where the OCX is
correctly installed and registered. A developer following VB6 muscle memory would install the control,
reopen, see the same empty box, and reasonably conclude HexIDE is broken.

So: keep the geometry, which is VB6's intended behaviour and is exactly reproducible from the file; replace
the emptiness, which is a shortcoming rather than a design. Per the Fidelity Principle, a divergence — and
recorded here so it stays deliberate.

## Bitwise And / Or / Xor / Not — the result-type ladder (2026-09-02, #166)

Measured with `scripts/vb6-oracle.ps1`. Every row below is `vb6.exe` output, not inference — and two of them
overturned what the implementation would otherwise have assumed.

### The result type is a ladder, not a widening

| left | right | result type |
|---|---|---|
| `Boolean` | `Boolean` | **Boolean** |
| `Boolean` | `Byte` | **Integer** |
| `Boolean` | `Integer` | Integer |
| `Boolean` | `Long` | Long |
| `Byte` | `Byte` | **Byte** |
| `Byte` | `Integer` | Integer |
| `Byte` | `Long` | Long |
| `Integer` | `Integer` | Integer |
| any | `Long` / `Single` / `Double` / `Currency` / numeric `String` | **Long** |

So the rule is: **both Boolean → Boolean; both Byte → Byte; otherwise the wider of the two, floored at
Integer.** Note the two exceptions that a plain "widen to the larger type" rule gets wrong — `Byte And Byte`
stays `Byte`, and `Byte And Boolean` jumps to `Integer` rather than staying `Byte`.

A numeric `String` yields **Long**, not Integer: `"12" And 10` is `8` as a `Long`.

### `Not` keeps its operand's width — including 8-bit for Byte

| expression | value | type |
|---|---|---|
| `Not CByte(5)` | **250** | **Byte** |
| `Not 5` | -6 | Integer |
| `Not CLng(5)` | -6 | Long |
| `Not True` | False | Boolean |
| `Not CDbl(5)` | -6 | Long |
| `Not "12"` | -13 | Long |

`Not CByte(5)` = 250 is the one to notice: `Byte` complements at **8 bits** (255 - 5), it does not widen to
Integer and give -6. The existing `TryUnpack<int>` comment — *"Byte widens to Integer (VB6: Byte arithmetic
promotes to Integer)"* — is correct for **arithmetic** and wrong for **bitwise**.

### Floating operands round to Long, banker's-style

| expression | value | why |
|---|---|---|
| `CDbl(2.5) And 3` | 2 | 2.5 → **2** (to even), `2 And 3` = 2 |
| `CDbl(3.5) And 3` | 0 | 3.5 → **4** (to even), `4 And 3` = 0 |
| `CDbl(2.4) And 3` | 2 | 2.4 → 2 |
| `CDbl(-2.5) And 3` | 2 | -2.5 → -2, `-2 And 3` = 2 |
| `CSng(2.5) And 3` | 2 | same for Single |

Round-half-to-even, which is .NET's `Math.Round` default — so the conversion is free rather than something
to hand-roll.

### Empty is an Integer zero; Null is NOT implemented and is not simple

| expression | value | type |
|---|---|---|
| `Empty And 5` | 0 | Integer |
| `Empty Or 5` | 5 | Integer |
| `Not Empty` | -1 | Integer |
| `Null And 5` | **Null** | Null |
| `Null And 0` | **0** | Integer |
| `Null Or 5` | **5** | Integer |
| `Null Or 0` | **Null** | Null |

`Empty` is implemented — it reduces to an Integer zero, which is all four of its rows.

**`Null` is measured here but deliberately NOT implemented**, because the rows do not fit the rule they
look like. A per-bit three-valued logic predicts `Null Or 5` is Null (the bits where 5 is zero stay
unknown); vb6.exe returns **5**. Whatever the real rule is, it is not the obvious one, and guessing it
would be exactly the mistake this document exists to prevent. `And`/`Or`/`Xor` continue to raise a type
mismatch on Null, unchanged, and the rule is left to the Null-propagation work that owns it.

### Boolean mixed with a number stays bitwise

`True And 2` is **2** (Integer), not `True`. `True` is -1, so `-1 And 2` = 2. `And` never becomes a logical
short-circuit operator: `True And True` is `True` only because `-1 And -1` is `-1`.
## DefType — default typing by first letter (2026-09-02, #169)

Measured with `scripts/vb6-oracle.ps1 -Declarations`, which this work added: `DefType` is a declarations-
section directive, so it cannot be probed as an expression, and it **cannot be measured with
`Option Explicit` on at all** — it is a rule about undeclared variables.

| probe (under `DefInt A-M`) | result | rule |
|---|---|---|
| `TypeName(apple)` | `Integer` | first letter in range |
| `TypeName(mango)` | `Integer` | range is **inclusive at both ends** |
| `TypeName(zebra)` | `Empty` | outside the range: an ordinary Variant |
| `Dim explicitVar As String` | `String` | an explicit `Dim` **overrides** the directive |
| `kilos$ = "text"` | `String` | a **type suffix overrides** it too |
| `quantity = 2.6` | **3**, `Integer` | coerces on store, exactly like `Dim x As Integer` |
| `amount = 2.5` | **2** | half-to-even, as everywhere else in VB6 |

### Overlapping ranges are a compile error

`DefInt A-M` followed by `DefStr A-C` does **not** resolve by last-wins or first-wins. `vb6.exe` refuses
the module:

```
Compile Error in File 'Module1.bas', Line 1 : Duplicate Deftype statement
```

Discovered by a probe that was trying to measure precedence and could not compile — which is the answer.
Any letter covered by two directives is rejected before the program runs, so an interpreter never has to
decide which wins.

### Why this is a lookup and not a map

The whole implementation is a letter-range → type table consulted when an undeclared variable is created.
No relationship between symbols appears anywhere in it, which is why it sits inside the pre-pass boundary
in `CLAUDE.md` rather than against it.

## Variant arithmetic promotes; a declared type is a ceiling (2026-09-02, #122)

Re-measured because the rule as originally filed was wrong in a way that would have shipped a silent bug.

| probe | measured |
|---|---|
| `TypeName(30000)` | `Integer` — a literal is Integer |
| `a = 30000` (undeclared) | Variant, subtype `Integer` |
| `a = 30000 : b = 3 : a * b` | **`Long` / 90000** |
| `a = 30000 : b = 30000 : a + b` | **`Long` / 60000** |
| `a = 2000000000 : b = 3 : a * b` | **`Double` / 6000000000** |
| `a = 30000 : a * 3` (Variant × literal) | **`Long` / 90000** |
| `Dim a As Integer` × Variant `b` | **`Long` / 90000** — either side lifts it |
| Variant `b` × `Dim a As Integer` | **`Long` / 90000** — order irrelevant |
| **`30000 * 3` (literal × literal)** | **Err 6** |
| `Dim a As Integer` + literal `30000` | **Err 6** |
| `Dim a As Integer, b As Integer` … `a * b` | **Err 6** |
| `(a + 0) * 3`, `a` declared Integer | **Err 6** — fixedness **propagates** |
| untyped `Fact(10)` / `Fact(13)` | `3628800` **Long** / **Double** |

### The rule

> **If either operand is a Variant, the ceiling lifts and the result widens Integer → Long → Double by
> magnitude. If both operands are typed — declared *or literal* — the type is a ceiling and overflow is
> Err 6.**

Driven by **operand provenance**, not by the result. #122 originally recorded it as *"driven by the result,
not the operands"* with `30000 * 3` → 90000 at the head of its table; that row is wrong, and building to it
would have promoted `Dim i As Integer : i = i + 1` past 32767 in silence — the common shape, not a rare one.

Two consequences for any implementation:

- **A literal is fixed, not a Variant.** `Dim a As Integer` plus the literal 30000 raises Err 6.
- **Fixedness propagates through sub-expressions**, so it must ride on the *value*: `(a + 0)` keeps its
  ceiling. It cannot be looked up at the operator, because an operand may be a call result with no slot to
  interrogate — which is exactly the shape of `Fact = n * Fact(n - 1)`, the case #122 was filed from.

### And a correction to a wrong expectation of my own

`CStr(6000000000#)` is **`6000000000`**, not `6E+09`. VB6 does not switch a Double of that magnitude to
scientific notation. Recorded because a test was briefly written against the invented form.
## Member chains, including module-qualified ones (2026-09-02, #173)

| probe | measured |
|---|---|
| `p.In1.Z` (nested UDT field, unqualified) | `99` |
| `Module1.n` (module-qualified scalar) | `5` |
| `Module1.p.X` (qualified, then a field) | `7` |
| `Module1.p.In1.Z` (qualified, then **two** fields — four levels) | `42` |

No depth limit and no special case: a module qualifier is simply the first step of an ordinary chain, and
everything after it resolves exactly as it would unqualified. `Module1.p.In1.Z` is `Module1` → `p` → `In1`
→ `Z`, evaluated left to right.

Worth stating because HexIDE refused the qualified form with *"Multi-level qualified member access is not
supported"* while already folding the unqualified `p.In1.Z` correctly — the two were treated as different
problems when only the first step differs in kind (a namespace rather than a value).

## Implements, and interface-typed variables (2026-09-02, #186)

Measured with `scripts/vb6-oracle.ps1 -Classes`, added for this: the harness could only ever compile one
`.bas`, so **the entire object model was unmeasurable**. Implements, `As New`, `Set` type-enforcement,
default members and parameterized properties all need at least one class module and several need two.

| probe | measured |
|---|---|
| `Dim x As IFoo : Set x = New Bar : x.Area()` | **7** — dispatched to `IFoo_Area` |
| `TypeName(x)` where `x As IFoo` holds a `Bar` | **`Bar`** — the concrete class, not the interface |
| `TypeName(New Bar)` | `Bar` |
| `TypeOf b Is IFoo`, `b` implements it | **True** |
| `TypeOf b Is IFoo`, `b` does **not** | **False** — not an error |
| `IFoo_Draw` declared **Public** rather than Private | **accepted** — the Private convention is not enforced |
| `x.Own` where `x As IFoo` and `Own` is `Bar`'s own member | **compile error**, *"Method or data member not found"* |
| a class declaring `Implements IFoo` but omitting a member | **compile error**, see below |

### The two compile errors, verbatim

An interface-typed variable exposes **only the interface's members**. Reaching for the concrete class's own
member fails, in the module that does it:

```
Compile Error in File 'Module1.bas', Line 19 : Method or data member not found
```

And a class that claims an interface must supply all of it — reported against the *class*, naming both the
missing member and the interface:

```
Compile Error in File 'Partial.cls', Line 0 : Object module needs to implement 'Erase2' for interface 'IFoo'
```

That second message is the one HexIDE should reproduce. Per the approximation rule in `CLAUDE.md` and
`interpreter-core:40-42`, it is raised **when the class is first instantiated** rather than before the
program runs — the conformance data (both member tables) is already collected by `PrePass`, so the check
itself needs no binding. A class never instantiated is never checked, which is the accepted
error-on-a-path-never-taken divergence.

### What a class-typed slot refuses

A slot declared `As <class>` enforces that name on every `Set`. Measured with `On Error GoTo` (see the
harness note below), the error is the same in both directions and carries no interface-specific message:

| probe | measured |
|---|---|
| `Dim c As Bar : Set c = New Baz` (unrelated classes) | **Err 13**, `Type mismatch` |
| `Dim x As IFoo : Set x = New Baz` (`Baz` does not implement `IFoo`) | **Err 13**, `Type mismatch` |
| `Set v = New Bar` (`v As Variant`), then `Set x = v` (`x As IFoo`) | **accepted** |

The third row is the interesting one: routing a reference through a Variant does not launder it, and does not
break it either. The check reads the OBJECT, not the declared type of wherever the reference came from — a
Variant has no declared type to check against, and the `Set` into `x` still succeeds because `Bar` really does
implement `IFoo`.

### Harness note: `On Error Resume Next` does not trap inside a `-Declarations` function

Found while measuring the above, and it silently corrupts results, so it is worth stating plainly. A probe
function of the form

```vb
Function P()
    On Error Resume Next
    ' ...something that errors...
    P = "n=" & CStr(Err.Number)
End Function
```

returns **nothing**, and the error surfaces on the harness's own outer trap instead. A control probe that
merely does `Err.Raise 5` behaves the same way, so this is not specific to the error being measured. The same
function written with `On Error GoTo H` works correctly and returns the number and description.

**Use `On Error GoTo` in `-Declarations` probe functions.** `On Error Resume Next` remains fine in the
expression-level harness, which is where the rest of this document's error numbers came from. The cause has
not been established, so this is recorded as a measured harness behaviour rather than explained.

## Static locals (2026-09-02)

Measured before implementing anything, because the design branches on the answer and two plausible
positions were both on the table. Nothing here is implemented yet — `Static x As Long` throws, and
`Static Sub` is silently accepted with the modifier discarded, which is worse (see `interpreter-gaps.md`).

| probe | measured |
|---|---|
| `Static n As Long` inside a class module method | **compiles** — legal, not a VB6 error |
| two instances of that class, `a` bumped 3x and `b` once | **3/1** — storage is PER-INSTANCE |
| `Static Function` with an ordinary `Dim k` inside, called 3x | **3** — the modifier makes ALL locals static |
| plain `Function`, `Static n` + `Dim m`, called 3x, reported as `n * 10 + m` | **31** — n persists, m resets |

### What this overturned

The expectation going in was that VB6 might not permit `Static` in a class module at all — a reasonable
guess, since per-instance static storage is an odd thing for a language to offer and there is no analogue
in most of the family. It permits it, and it does the more expensive thing: each instance gets its own
copy. Had we implemented on the guess, a class with a `Static` counter would have shared one counter
across every instance, which is a silent wrong answer rather than a crash.

The `Static Function` row matters for a different reason: the modifier is not a shorthand, it genuinely
changes the storage class of every local in the procedure. HexIDE currently parses it and throws the
modifier away, so such a procedure runs with ordinary locals and quietly produces wrong numbers.

### What it means for the implementation

Static storage cannot hang off `ProcedureInfo`, because that is shared by every instance. For a class
module method it belongs on the `VbObject`, keyed by procedure and name, and it dies with the instance.
For a standard module the two are indistinguishable (the module is a singleton), so a per-procedure table
is correct there.

The slot model already does the hard part: `ExecutionState` holds slots by id and `ExecutionEnvironment`
binds names to them, so a static local is "look up or create the slot, bind the name to it, and do NOT add
it to `ownedSlots`" — the last part being what keeps scope-exit from releasing it and firing a premature
`Class_Terminate`. Recursion then shares the slot without any extra work, which is what VB6 does.

## Omitted optional arguments, and string operands to numeric intrinsics (2026-09-02)

Measured to check two defects an adversarial pass reported against HexIDE. Both were real; one was
described wrongly, in a way that would have produced the wrong fix.

| probe | VB6 | HexIDE |
|---|---|---|
| `Split("a b c", , 2)` | **`n=1 [a][b c]`** — 2 elements on the default space delimiter | returns the whole string as ONE element |
| `Split("a,b,c", ",", , 1)` | **`n=2 [a]`** — 3 elements, compare arg accepted | raises **Err 13** |
| `Abs("abc")` | **Err 13** | Err 13 — correct |
| `Abs("5")` | **`5`** | Err 13 — wrong |

### The omitted-argument hole

A skipped middle argument (`Split(s, , 2)`) must take the parameter's default. HexIDE materialises a blank
call-site slot as `Vb6Value.Missing`, but `VB6BuiltIns.Array.HasArg` tests `!= EmptyVariant`, so the guard
never fires: the delimiter becomes `AsStr(Missing)` = `""`, and the empty-delimiter branch returns the
input unsplit. Skipping `limit` is worse than a wrong answer — `a.Count >= 3` is satisfied by the blank
slot, `AsInt(Missing)` falls past every case, and it throws. `Filter` shares the bug.

The comment above `HasArg` asserts that a skipped optional "arrives as Empty". That is wrong about this
interpreter's own call machinery, and the wrong comment is what kept the bug alive.

### Why the `Abs` row needed the oracle

The report was "a String operand raises Err 13", offered as the defect. **VB6 raises Err 13 there too**,
so taken at face value it would have led to making `Abs("abc")` return something — a change away from
VB6, not toward it.

The real divergence is narrower and opposite in shape: VB6 accepts a *numeric* string and rejects a
non-numeric one; HexIDE rejects **both**, because `TryNumericToDouble` switches only over the numeric CLR
types plus `DateTime` and returns false for `String`. The same path is shared by `Sqr`, `Sgn`, `Sin`,
`Cos`, `Exp`, `Log`, `Tan`, `Atn`, `Hex`, `Oct`, `CDate` and `CBool`, so one fix covers all of them —
which is only visible once the boundary is stated correctly.

This is the second time a plausible-sounding defect report would have moved HexIDE *away* from VB6 if
implemented as written. Measure the claim, not just the behaviour it complains about.

### Return widths of the integer-returning intrinsics (2026-09-02)

Found while fixing #190, when a test asserted the wrong type and the oracle disagreed with both of us.
Then measured across the whole family before fixing (#193), which turned up two the original finding had
missed.

| `TypeName` in VB6 | intrinsics |
|---|---|
| **Long** | `Len`, `InStr`, `InStrRev`, `LBound`, `UBound`, `DateDiff`, **`VarType`** |
| **Integer** | `Asc`, `Sgn`, `Year`, `Month`, `Day`, `Hour`, `Minute`, `Second`, `Weekday`, **`DatePart`** |
| operand-dependent | `Int`, `Fix` — these preserve the operand's subtype, so `Int(3)` is Integer and `Int(3.5)` is Double |

**It is not one rule**, which is the whole point: a blanket "widen the integer-returning intrinsics" would
have been just as wrong as leaving them. Two pairs are worth remembering because they look like
inconsistencies somebody will later try to tidy away:

- **`DateDiff` is Long, `DatePart` is Integer.** Same family, same argument shapes, different widths.
- **`VarType` is Long**, even though every `vbXxx` code it can return fits comfortably in an Integer.

### What HexIDE had wrong, and why

None of these functions chose a type. They built their result from a C# `int`, and `Vb6Value(int)` applies
a **magnitude rule** — anything fitting Int16 is reported as an Integer. That rule is correct for an
arithmetic literal, whose type genuinely does follow its magnitude; it is wrong for a function with a
**fixed declared return type**, which is what these seven have.

Reachable with entirely ordinary arguments — `TypeName(InStr("hello", "l"))` was `Integer` — so not an
edge case, merely an invisible one until something asks for the type. It does not stay invisible: the
subtype feeds the arithmetic result-type ladder, so `Len("hi") + 1` was an Integer where VB6 gives a Long.

`Int` and `Fix` are the control. They must keep going through the magnitude rule, because for them the
operand really does decide.

### `Join(a, )` is a syntax error — only a MIDDLE argument can be omitted (2026-09-02)

Found while fixing #190, when a probe would not compile:

```
Compile Error in File 'Module1.bas', Line 10 : Syntax error
```

`Split(s, , 2)` is fine; `Join(arr, )` is not. VB6 permits an omitted argument only *between* supplied
ones, never trailing with a dangling comma. So a short argument list genuinely means "the rest were
omitted", and an interior blank is the only case an implementation has to model.

That is what makes the fix for #190 safe: `Supplied(a, i)` can test `a.Count > i` for the trailing case
and `Missing` for the interior one, without a third possibility to worry about.

### Numeric line labels (2026-09-02)

| probe | measured |
|---|---|
| `GoTo 20` … `20 s = …` … `GoTo 10` … `10 s = …` … `GoTo 99` | **`a-twenty-ten`** — labels are jump targets, not BASIC line numbers, so they need not ascend |
| handler at `50` doing `Resume 60` | **`resumed to 60`** — a numeric label is a valid Resume target |
| a numeric label and a named label in one procedure | **both work** — one label table, nothing distinguishes them at the jump |

Measured before implementing, because the alternative was assuming these behave like identifier labels.
They do — which is the useful result, since it means the whole downstream machinery (the label table, the
pc-driver, `GoTo`/`On Error GoTo`/`Resume`) is shared and only the declaration form differs.

Worth recording separately: this gap existed **only in the interpreter's grammar**. The LSP server's
already had a `lineNumber` rule, so the editor reported no syntax error on a file the interpreter could
not load. Grammar divergence between the two halves is now guarded by `GrammarParityTests`.

## Line continuations and statement separators (2026-09-02)

319 clean-room cases compiled against real vb6.exe by `scripts/vb6-legality.ps1`. **243 legal, 76
illegal; 16 predictions wrong and 60 genuine unknowns resolved.** Every case was authored from the VB6
Language Reference and the grammar, under the clean-room rule in `vb6-grammar-fixes.md` — no GPLv3 VB6
grammar or test suite was consulted. That constraint is permanent, and the provenance record lives in
that document rather than here.

### An unterminated string literal auto-closes at end of line

| probe | measured |
|---|---|
| `Debug.Print "A` | **legal** — the string closes itself at the newline |
| `s = "A` | **legal** — same in an assignment |

Not a quirk of `Debug.Print`, and not what most people expect of a language with no multi-line strings.

### A trailing `_` inside a string continues the line — but only sometimes

| probe | measured |
|---|---|
| `Debug.Print "A _` then `B"` | **legal** |
| `Debug.Print "A _` alone | **illegal** — Syntax error |
| `s = "A _` then `B"` | **illegal** — Syntax error |
| `Debug.Print "AB_` (no space before `_`) | **illegal** — the whitespace-before rule holds inside a string too |

The first row only makes sense if the `_` joins the two physical lines, giving `Debug.Print "A B"` — and
the second row supports that, because alone it swallows the following `End Sub`. **But then the third row
should be legal, and it reproducibly is not**, with or without a later use of `s`.

Recorded as measured and unexplained rather than tidied into a rule. Both halves reproduce in isolation.
Something distinguishes an output list from an assignment here, and this document would rather say so than
invent the reason.

### Colons, and where they are refused

| probe | measured |
|---|---|
| `Debug.Print 1:` (trailing colon) | legal |
| a colon-only line at module level | legal |
| `Enum` members colon-joined | legal |
| `Type` header colon first member, and members colon-joined | legal |
| `Attribute` colon `Option` | legal |
| a statement colon `End If` | legal |
| a label named `Error` | legal — a statement keyword is still usable as a label |
| **two `Declare`s colon-joined at module level** | **illegal** — "Expected: statement or end of statement" |
| **a continuation inside an `Enum` member value** | **illegal** — "Invalid inside Enum" |

The last two are the only places in the whole sweep where colons and continuations are refused in a
context that otherwise accepts them. Continuations work almost everywhere — including the canonical
multi-line Win32 `Declare` — which makes the `Enum` exception worth remembering.

### The harness lesson, which cost more than the findings

The first run reported **35** wrong predictions. Nineteen of them were the harness's fault, in two ways:

1. It appended its own `Sub Main` to every module-scope case, including the many that define one. VB6
   answers *"Ambiguous name detected: Main"*, and the case is recorded illegal for a reason with nothing
   to do with what it tested — quietly converting a whole family of declaration probes into confident
   wrong facts. The canonical multi-line `Declare` was among them.
2. The guard added to fix that silently never fired, because a shell heredoc turned the `\b` in its regex
   into a literal **backspace byte** (0x08). The pattern `Sub\s+Main<BS>` cannot match anything, and the
   character is invisible in every editor, diff and terminal rendering — it was found only by hexdumping
   the line.

**A corpus is only as good as the harness, and a harness bug looks exactly like a language discovery.**
Both symptoms presented as surprising VB6 behaviour in the direction the author half-expected, which is
the most dangerous shape an error can take. Check that a suspicious cluster shares a compiler *message*
before believing it shares a *cause*.

### The single-line If: where a colon-joined tail belongs (2026-09-02)

The question the conformance corpus was built to ask, and the one a legality oracle cannot answer — the
line compiles either way, so only running it says what it means.

| probe | measured |
|---|---|
| `If False Then A : B` | **`[]`** — NEITHER runs. The whole joined tail is the Then branch. |
| `If True Then A : B` | `[AB]` — the whole tail runs, in order |
| `If False Then A : B Else C` | `[C]` — Else binds to the If, after the entire tail |
| `If False Then A : Else C` | `[C]` — a colon may sit immediately before Else |

**The intuitive reading is wrong, and wrong in the dangerous direction.** Treating the tail as
unconditional would silently execute code the program said not to — no error, nothing to debug from. It
is the same harm shape as the silent-failure class closed in #191, arrived at from a completely different
direction.

A consequence worth stating for implementers: because the branch is a run of statements rather than one,
control leaving it must stop the rest. `If i = 2 Then Exit For : s = s & "X"` must not append.

## Extending the oracle (future phases)

Phase 3 (intrinsics) and beyond should verify, at minimum:
- Conversions: `CInt`/`CLng`/`CByte`/`CCur`/`CDec`/`CDbl`/`CSng`/`CDate` — rounding, ranges, overflow codes.
- `Rnd`/`Randomize` — the exact 24-bit LCG seed + first outputs (pin constants only after oracle confirmation).
- `Format`/`Format$` — the date/number/currency mask mini-language.
- Date library: `DateAdd`/`DateDiff`/`DatePart`/`Year`/`Month`/`Weekday`/`Now`, and how they treat the epoch.
- String funcs edge cases (`InStr` start arg, `Mid` boundaries, `Val` parsing).

Reuse the `On Error Resume Next` + `TypeName` harness above; keep the probe `.bas`/`.vbp` under a scratch dir,
never in the tree.

---

## Serialization / file-format fidelity (2026-08-11)

First use of the oracle for **file format** rather than runtime semantics. Method: author a minimal Standard
EXE, vary one aspect of the `.frm`, and run `VB6.EXE /make` headless. A `/make` must load and parse the form
to build its resource, so a load failure is a compile failure — this exercises VB6's real form parser without
driving the IDE. Harness at `scratchpad/oracle-q1` (see the recipe above: absolute paths, `CreateNoWindow`).

Every result below is from a real `/make` run, not inference.

| # | Question | Result | Consequence |
|---|---|---|---|
| Q1a | Is the **trailing space** after `Begin VB.Form Form1 ` required on load? | **No** — compiles without it | HexIDE omitting it is COSMETIC, not blocking |
| Q1b | Is the **property-name column padding** (`Caption         =`) required? | **No** — compiles unpadded | COSMETIC |
| Q1c | Both deviations together (HexIDE's actual output shape) | **Compiles** | Confirms the two are independent and neither is load-bearing |
| Q5 | Does VB6 accept a **fractional** `ClientWidth`/`ScaleWidth` (`6683.999999999999`)? | **Yes** — compiles | HexIDE's float noise is value drift, not a load failure |
| Q2a | Nested menus with `Shortcut` on a child (VB6's own shape) | **Compiles** | Control |
| Q2b | **Flattened** menus, no shortcut | **Compiles** | Flattening alone destroys structure but still loads |
| Q2c | **Flattened** menus **with** a shortcut | **FAILS TO LOAD** | See below |

### Q2c is the important one

```
Line 16: Cannot set shortcut property in menu mnuFileNew. Parent menu cannot have a shortcut key.
```

VB6 treats a top-level `Begin VB.Menu` as a *parent menu* and **rejects** a `Shortcut` on it. HexIDE flattens
nested `Begin` blocks on save, which promotes every child menu item to top level — so any form whose menus
carry shortcuts is written out in a state **VB6 refuses to open**.

This is not a corner case. Of the six menu templates VB6 ships, `File Menu.frm` (4 shortcuts) and
`Edit Menu.frm` (5) use them — the two menus essentially every VB6 application has. `Ctrl+N`/`Ctrl+O`/`Ctrl+S`
on a File menu is the canonical VB6 idiom.

**Severity: the menu-flattening defect is BLOCKING, not CORRUPTING** — it produces files the oracle cannot
load. Recorded against the round-trip epic.

### Still unanswered

Q4 (which rect VB6 honours when a file declares two that contradict each other), Q6 (`Startup=` without
quotes), Q9–Q10 (`.frx` record layouts), Q11 (`VERSION 4.00` upgrade), Q13–Q16. These need the interactive
IDE or a hex dump rather than a `/make`, so they are a separate session.

Q3 (invented outer rect on a root) stopped being a question when #104 removed the invention: HexIDE no
longer writes a rectangle the file did not declare, so what VB6 would have made of one is moot.

### Corpus-wide: VB6 accepts HexIDE's reformatted output (2026-08-19)

Q1a/Q1b/Q1c above answered the "is the formatting load-bearing" question from a hand-built fixture. This
is the same question asked of the **whole corpus**, which is what that section's own method note asks for —
a fixture only exercises what its author thought to include.

Method: round-trip all 20 `.frm` files in the Template tree through `FormDeserializer` +  `FormSerializer`,
drop each into a generated single-form `.vbp` (carrying the `Object=` references from the form's own header
and the original `.frx` beside it), and `VB6.EXE /make /out` each one.

**Result: 15 of 20 build. The other 5 fail identically when the harness is given Microsoft's ORIGINAL file.**

| Outcome | Forms |
|---|---|
| Built from HexIDE's output | About Dialog, Button ListBox, Dialog, Edit Menu, Explorer File Menu, File Menu, Form1, Help Menu, Log in Dialog, Mover ListBox, ODBC Log In, Splash Screen, Tip of the Day, View Menu, FRMDATEN |
| Failed — **and the original fails the same way** | Window Menu (`Method or data member not found` — the template calls MDI methods with no MDI parent), Options Dialog / Treeview Listview Splitter (`Must have startup form` — the form fails to load without its project), ADDIN (`User-defined type not defined`), Web Browser (`Errors during load` — unregistered OCX) |

So no form is broken by what HexIDE writes; the five failures are template forms lifted out of the project
context they were written for.

**The control run is the whole point.** Run only HexIDE's output and five failures look like five defects.
The same harness on the original files is what turns them into harness artifacts — and it costs one extra
loop. Do not report a `/make` failure against generated output without it.

**Consequence:** property order, column padding, the trailing space on the `Begin` line and the missing enum
comments are confirmed COSMETIC at corpus scale, not merely on a fixture. They keep a file from being
byte-identical; they do not keep VB6 from loading it. That is what makes the remaining round-trip work a
fidelity burndown rather than a data-loss one.

**Limit of this evidence:** `/make` proves VB6 *loads and compiles* the form. It does not prove the built
program looks the same — a form whose geometry changed still compiles. That is why #104 is a separate
finding from this one, and why Q4 stays open.

### `.frx` blob encoding is deterministic (2026-08-11)

Byte-identical `.frx` round-trip is **achievable**, not a fool's errand — worth recording because the
opposite is a plausible-sounding assumption drawn from a real neighbouring case.

Four VB6-shipped forms in four separate projects (`COPY`, `DSKSPACE`, `PATH`, `SERVERDT`) produced companion
files that are **byte-identical** (SHA-256 `2248962480f2260f…`, 1090 bytes each). The same icon encoded the
same way across four independent saves. The record header carries nothing session- or machine-dependent:

```
06 03 00 00  6c 74 00 00  fe 02 00 00  00 00 01 00  01 00 20 20 …
└ length ─┘  └ Preamble ┘  └ Size ────┘  └ raw .ico header ─────┘
             └──────── StdPicture stream, [MS-OFORMS] §2.4.5 ───────…
```

No timestamp, no checksum, no GUID.

**Correction (2026-08-19): the second field is not a "type tag".** It was labelled as one here when this
was first written, from the hex alone. `6c 74 00 00` is `0x0000746C`, and [MS-OFORMS] §2.4.5 specifies
that exactly: a `StdPicture` stream opens with a 4-byte **Preamble** that MUST be `0x0000746C`, followed
by a 4-byte **Size** giving the length of the picture bytes that follow.

The arithmetic confirms it rather than merely fitting it: the outer length is `0x306` = 774, and
8 bytes of Preamble-plus-Size plus `0x2FE` = 766 bytes of payload is 774 exactly.

So the record is **`[4-byte VB6 length][StdPicture stream]`** — VB6's own container wrapped around a
structure that is published, and that HexIDE is licensed to implement. See the section below.

**The Access precedent does not transfer.** MS Access under VSS famously showed diffs on untouched forms
every commit — but the cause was specific to Access: `SaveAsText` embedded `PrtDevMode`/`PrtDevNames`
(printer device settings snapshotted from the current default printer) plus a `Checksum` line. VB6 forms
have neither.

**Limit of this evidence:** it shows the *encoder* is deterministic for a given image. It does not prove
that re-saving an unchanged form in the VB6 IDE reproduces byte-identical output — VB6 could still reorder
blobs. That needs the interactive IDE and remains open.

**Consequence for HexIDE:** none immediately, because blob *pass-through* (keep the original bytes at the
original offsets) sidesteps encoding entirely. Matching VB6's encoder only becomes necessary if the designer
ever lets a user edit an image — the comprehension problem, deliberately deferred.

### The `.frx` payloads have a published specification — the container does not (2026-08-19)

A distinction worth keeping, because it splits the remaining `.frx` work into two very different halves.

| | Documented where | May we implement from it? |
|---|---|---|
| The `.frx` **record container** — `[4-byte length][payload]`, and the 2-byte-count variant `List`/`ItemData` use | **Nowhere.** VB6-specific, undocumented | Black-box observation only, as below |
| The **payload** of a `Picture`/`Icon` record — a `StdPicture` stream | **[MS-OFORMS] §2.4.5** | **Yes** |
| The **payload** of a persisted font — a `StdFont`/`TextProps` structure | [MS-OFORMS] §2.4.x | **Yes** |

[MS-OFORMS] is *Office Forms Binary File Formats* — the MSForms control set as persisted inside Word,
Excel, PowerPoint and VBA project storage. **It is not a specification of VB6's designer files**, and it
never mentions `.frm`, `.frx` or Visual Basic. What makes it useful anyway is that VB6 and Office Forms
both persist standard OLE types the same way, so VB6's container turns out to be wrapped around
structures Office documented.

#### Why implementing from it is clean, where the VBA SDK was not

Two independent clearances, both checked rather than assumed:

- **Copyright.** The Open Specifications IP notice grants, in terms: *"you can make copies of it in order
  to develop implementations of the technologies that are described in this documentation and can
  distribute portions of it in your implementations that use these technologies or in your documentation
  as necessary to properly document the implementation."* It also states plainly: *"Microsoft does not
  claim any trade secret rights in this documentation."*
- **Patents.** [MS-OFORMS] is a **Covered Specification** under the Microsoft Open Specification Promise,
  listed by name under *Office Binary File Formats — First Published June 30, 2008*. Microsoft
  *"irrevocably promises not to assert any Microsoft Necessary Claims against you for making, using,
  selling, offering for sale, importing or distributing any implementation to the extent it conforms to a
  Covered Specification."* [MS-CFB] is covered too, if a compound-file path is ever needed.

So this may be read, implemented, and quoted into HexIDE's own documentation. That is a different licence
from anything else consulted for this work, and the difference is deliberate: it is why the format facts
above can be written down here at all.

**Contrast, recorded so it is not re-litigated.** The VBA 6.4 SDK (`VBA6SDK`) was examined on the same day
and is **not** a usable source: its EULA grants no distribution rights, restricts documentation to internal
use with no republication, and forbids reverse engineering. It also turned out to be irrelevant — it is a
COM host-embedding API (`IActiveDesigner`, `ICodeNavigate`, the `Apc*` interfaces) with zero mentions of
`.frx`, file formats, `IPersistStream` or property bags anywhere in its headers or help file. Nothing from
it has entered this project and nothing should.

#### Qualification: that determinism finding covers INTRINSIC controls only

The four byte-identical files above are a form `Icon` and a `PictureBox.Picture` — blobs VB6 encodes itself.
It does **not** follow that every `.frx` is stable, because VB6 does not author all of a `.frx`.

An OCX persists through the COM interfaces (`IPersistStream` / `IPersistPropertyBag`) and writes its own
assets into the container's stream. `Template\Forms\Web Browser.frm:126` shows the shape — an ImageList
persisting nested property bags that cite the companion:

```
BeginProperty Images {2C247F25-8591-11D1-B16A-00C0F0283628}
   BeginProperty ListImage1 {2C247F27-8591-11D1-B16A-00C0F0283628}
      Picture         =   "Web Browser.frx":0000
```

Those bytes come from third-party control code, not from VB6. Whether they are stable across saves depends
on each control's implementation — one that serialises an internal buffer, re-renders a bitmap, or iterates
an unordered collection would churn while nothing meaningful changed. Reported from practice (MS Access
under VSS showed exactly this pattern; the Access-specific `PrtDevMode`/`Checksum` cause recorded above is a
*different* mechanism that happens to produce the same symptom). **Untested here** — a single corpus snapshot
cannot show churn.

**The permanent consequence for HexIDE, which is a boundary and not a backlog item:** HexIDE does not host
ActiveX controls, and only a control can serialise itself. HexIDE therefore can *never* correctly regenerate
OCX-persisted bytes, at any level of effort. For that data, blob **pass-through — preserve the original
bytes at their original offsets, never re-encode — is the only correct strategy that will ever exist**, not
a pragmatic shortcut.

This splits the binary layer cleanly:

| Blob source | Relationship |
|---|---|
| Intrinsic VB controls (`Picture`, `Icon`, `DragIcon`, `MouseIcon`) | HexIDE can own it; encoder verified deterministic |
| OCX property bags | Opaque bytes, permanently. Preserve, never regenerate. |

Corollary: byte-identity is the wrong assertion for an OCX-hosted form, and a read-only gate is the wrong
instinct — with pass-through such a form round-trips *perfectly*, precisely because nothing looks inside.

#### Correction (same day): "never host ActiveX" was wrong, and so was the target

Two errors in the entry above, both corrected here rather than edited away.

**1. OCX hosting is in scope.** `CLAUDE.md:170` and `docs/OUT_OF_SCOPE.md:5` are explicit: COM/OLE is *not*
excluded, it is Windows-gated and foundational to real-world VB6. What is excluded is ActiveX **Documents**
(`.dob`) and ActiveX **Designers** (plug-ins needing Win32 subclassing) — not hosting an OCX on a form.
So "HexIDE can never regenerate OCX-persisted bytes" is false as a permanent claim.

It also conflated two separable concerns. **Rendering** an OCX needs the control and is Windows-gated;
**storing** its persisted bytes is a byte format and needs nothing but the format. The persistence contract
(`IPersistStream`, `IPersistStreamInit`, `IPersistPropertyBag`) is documented in the VBA SDK. Reading and
writing those records is therefore knowable without hosting anything, on any platform.

*(Licence note if the SDK is used: the same clean-room rule as the VBA-Docs repo — facts, APIs and
semantics are not copyrightable, so learn-then-implement is fine; do not copy prose or code samples. Verify
the SDK's own licence terms before relying on it.)*

**2. The target is validity, not byte-identity.** VB6 developers using source control already treat `.frx`
churn as noise and ignore it — a control writing its own state through the COM persistence interfaces has
no obligation to emit identical bytes twice, and in practice does not. So **byte-identical `.frx`
round-trip is not a fidelity requirement and should not be asserted anywhere.**

The requirement is that the file stays **valid**: it loads, every offset the `.frm` cites resolves to a
record, and each control gets back data it can consume.

**Consequences:**

- The corpus harness's byte comparison of companion binaries is the **wrong assertion** — for every `.frx`,
  not merely OCX-hosted ones. The right invariant is the citation-resolution gate
  (`Every_companion_offset_cited_by_a_form_resolves_to_a_blob`) plus "the record a given citation resolves
  to is unchanged". Whole-file byte-identity should be dropped.
- Blob **pass-through** remains the recommended first step, but as the cheapest route to validity — not,
  as previously stated, the only strategy that could ever exist.
- Regenerating records is legitimate once the layouts are known, which is a documentation problem rather
  than a hosting problem.

#### Correction to Q2c: separators are fatal too, independently of shortcuts

The Q2 isolation above concluded "flattening alone still loads; flattening **with** a shortcut fails". That
generalised from a hand-built two-item fixture with no separator, and it is wrong.

The corpus-wide build gate (`Vb6OracleRoundTripTests`) shows menu flattening breaks VB6 for **two
independent reasons**:

```
Line 20: Cannot set shortcut property in menu mnuEditUndo. Parent menu cannot have a shortcut key.
Line 25: Parent menu mnuFileBar1 cannot be loaded as a separator.
```

A separator (`Caption = "-"`) promoted to top level is rejected exactly as a shortcut is. Three of the five
broken corpus forms — `Explorer File Menu`, `Help Menu`, `View Menu` — carry **zero** shortcuts and fail
purely on separators.

So the affected set is not "menus with shortcuts" but "menus with shortcuts **or** separators", i.e.
essentially every real menu. Recorded against #22.

**Method note worth keeping:** the hand-built experiment was decisive about the mechanism and wrong about
the scope, because a fixture only exercises what its author thought to include. The corpus gate found the
second cause on its first run. Prefer running the corpus over reasoning from a minimal repro when the
question is *how much* rather than *why*.
