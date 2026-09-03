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

### A designer-file token was eating statement separators (2026-09-02)

Not a VB6 fact but a HexIDE one, recorded here because the corpus is what found it and because it is the
best example so far of why grouping failures by SYMPTOM misleads.

`FRX_OFFSET` matches a companion-binary offset in a designer file — `Picture = "Form1.frx":0000`. It was
written `COLON [0-9A-F]+` and, being an ordinary lexer rule, was live in ordinary code. A-F are hex
digits, so:

| source | lexed as |
|---|---|
| `Debug.Print "A":Debug.Print "B"` | `:D` became an OFFSET, swallowing the separator |
| `a = 1:b = 2` | `:b` likewise |
| `Skip:Debug.Print` | `:D` likewise |

Nine corpus cases, spread across three different areas and filed under three different apparent causes —
labels, separators, control flow — were all this one token. Narrowing it to the shape VB6 actually writes
(a leading digit and at least four hex digits, which zero-padding guarantees) fixed all nine at once.

**It was predicted before it was measured.** The agent auditing the corpus read the lexer and flagged
`FRX_OFFSET` as a hazard, noting that every existing tight-colon case happened to put a non-hex letter
after the colon and so missed it. It then wrote the cases that caught it.

### A continuation may fall inside a multi-word keyword (2026-09-02)

`End _ Sub` is legal, and so is every other multi-word keyword split the same way. Ten new corpus cases
(`corpus/conformance/keyword-splitting.json`), all compiled by `vb6.exe`:

| probe | verdict |
|---|---|
| `For _` ⏎ `Each c In Array(1, 2)` | legal |
| `On Error Resume _` ⏎ `Next` | legal |
| `Select _` ⏎ `Case n` | legal |
| `Declare Function … Lib _` ⏎ `"kernel32"` | legal |
| `Declare Function … Lib"kernel32"` — **no separator at all** | **legal** |
| `… Alias _` ⏎ `"GetTickCount"` | legal |
| `Option Base _` ⏎ `1` | legal |
| `End _` ⏎ `  _` ⏎ `Sub` — two continuations in a row | legal |
| `If i = 2 Then Exit _` ⏎ `For` — split keyword inside a single-line If | legal |
| `End _` ⏎ `'a comment` ⏎ `Sub` | **illegal** |

Ten predictions, zero disagreements — the first clean sweep this corpus has had, and worth noting only
because the two cases marked `unsure` were the ones that mattered.

**`Lib"kernel32"` compiles.** That was the genuinely open question, and the answer decided a grammar
change. HexIDE spells the multi-word keywords as single lexer tokens joined by a literal space, which is
right — `End` and `Sub` mean something else apart than together — but three of them (`Select Case`,
`For Each`, `Resume Next`) are assembled by the *parser* from two tokens with a mandatory `WS` between,
and a continuation is hidden from the parser by the time it looks. For those three, making the `WS`
optional is provably not a widening: `SelectCase` lexes as one `IDENTIFIER`, so the parser can only ever
see `SELECT` and `CASE` adjacent if something hidden separated them.

That argument does not reach `Lib "x"`, because a keyword and a string literal *can* abut with nothing
between them — which is exactly why it was measured instead of reasoned about. The measurement came back
better than the argument would have: VB6 accepts the abutting form outright, so `WS?` there is not a
tolerated widening but a faithful one.

**The illegal case is the useful one.** A comment is hidden from the parser on the same channel as a
continuation, so a grammar that admitted "anything hidden may separate the two words" would have admitted
`End _` ⏎ `'comment` ⏎ `Sub` as well. VB6 rejects it — a comment ends the statement, continuation or not.
The separator has to admit continuations and whitespace specifically, not hidden tokens generally.

**Incidental:** `Rem` followed by a **tab** is a comment. `COMMENT` shared the same single-space spelling
and was widened with the keywords.

Corpus false rejections: 21 → 14, clearing this group entirely. What remains is four causes —
`REM-FORM`, `STRING-CONTINUATION`, `LABEL`, `OTHER`.

### `Rem` takes no separator, and the documentation is wrong about it (2026-09-02)

The reference says "Rem followed by a space". It is not true. 21 new corpus cases
(`corpus/conformance/rem-forms.json`), compiled by `vb6.exe`, plus a behavioural round
through the value oracle for the cases where compiling proves nothing.

**The rule, as measured:** *`Rem`, standing as a whole word, begins a comment that runs to the end of the
physical line. No separator is required. A trailing `_` extends it onto the next line. It is reserved, and
is never an identifier anywhere.*

| probe | verdict |
|---|---|
| `Rem` alone on a line | legal |
| `Rem:` alone on a line | legal |
| `Rem=1` | legal |
| `Rem'not really nested` | legal |
| `Rem"text"` | legal |
| `Rem` + tab + EOL | legal |
| `Dim RemX As Long` / `RemX = 5` | legal — an ordinary identifier |
| `Dim Rem1 As Long` | legal |
| `Dim Rem As Long` | **illegal** — Syntax error |
| `Sub Rem()` | **illegal** — Expected: identifier |
| `Private Type TRec` / `Rem As Long` / `End Type` | **illegal** — *User-defined type without members* |
| `Private Enum EKind` / `Rem` / `End Enum` | **illegal** — *Enum without members* |
| `Private Sub Take(ByVal Rem As Long)` | **illegal** — Expected: identifier |
| `If True Then Rem a remark` | **illegal** — Syntax error |
| `If True Then s = "then" Else Rem nothing` | legal |
| `If True Then s = "then" Else` — bare trailing Else | legal |
| `Rem a remark _` above `End Sub` | **illegal** — *Expected End Sub* |

**The two error messages are the interesting rows.** *"User-defined type without members"* and *"Enum
without members"* do not say "Rem is reserved" — they say the `Rem As Long` line **was not there**. That is
the proof that `Rem` is a comment even in a declaration position, and it is stronger than the plain
syntax errors, which could have meant several things.

**It also caught a wrong reading of my own, immediately.** The first version of the enum probe listed `Rem`
*alongside* a second member and compiled — which I took as "Rem may name an enum member". Exactly
backwards: it compiled because the `Rem` line vanished and the *other* member carried the enum. Rewriting
it with `Rem` as the only member turned a false "legal" into a decisive "illegal". A case that appears to
prove something can prove its opposite, and the way to tell is to remove everything else from the probe.

**Behaviour, from the value oracle** — a legality verdict cannot answer these, because the line compiles
either way and the two readings differ in what runs. Each probe builds a string from the statements that
actually executed:

| probe | measured | meaning |
|---|---|---|
| `Rem: s = s & "B"` | `A` | B does **not** run — the colon is comment text |
| `s = "A": Rem a remark: s = s & "B"` | `A` | the remark eats the rest of the line, colons included |
| `Rem a remark _` ⏎ `s = s & "B"` ⏎ `s = s & "C"` | `AC` | the continuation **swallows the next line** |
| `GoTo 10` … `10 Rem arrived` ⏎ `s = s & "B"` | `AB` | a line number carrying only a remark is still a target |
| `If False Then s = s & "T" Else Rem nothing` | `AB` | an Else branch may be nothing but a comment |
| `Rem=1`, `Rem'q`, bare `Rem`, `Rem:` | all skipped | comments, every one |

`Rem: s = s & "B"` is the one with teeth. HexIDE lexed it as `REM`, `COLON`, and a live statement, so a
parser that accepted the line would have **executed a statement VB6 treats as a remark** — no error and
nothing to debug from. Same harm shape as the single-line-If tail, reached from a different direction.

**`Then Rem` is illegal and `Else Rem` is legal.** Measured, and I have no rule I would defend for it. A
comment is invisible to the parser in both positions, so whatever distinguishes them is not syntactic. A
bare trailing `Else` with nothing after it is also legal, which at least makes the Else half consistent
with itself. Recorded as measured-but-unexplained rather than tidied into a story.

**What this cost, and what it bought.** Fixing it needed three things in the lexer, each of which is a way
to get it wrong:

1. A guard on what follows `Rem`. Without it `RemX = 5` becomes a comment and the assignment is **deleted** —
   a wrong value, which the guardrail refuses outright.
2. **No leading `WS?`.** With one, the rule starts a character early, at the space in `Dim RemX`, and
   matches `" Rem"` — four characters, which beats the one-character `WS` token and produces the same
   deleted assignment from the other side. The guard cannot help: it governs how far a match runs, not
   where it begins.
3. **Position ahead of every keyword.** ANTLR settles an equal-length match by declaration order, and a
   bare `Rem` is matched at exactly three characters by the comment rule, the `REM` keyword and
   `IDENTIFIER` alike.

Two of those were found by the corpus rather than by reasoning, and both would have shipped silently.

### The other direction was never gated (2026-09-02)

Not a VB6 fact but a process one, recorded because it is the second time this corpus has revealed a gate
that was quietly guarding less than it appeared to.

`CorpusConformanceTests.Compare()` has always returned **both** lists — code VB6 accepts that HexIDE
rejects, and code VB6 rejects that HexIDE accepts. Only the first was ever asserted. The second was
computed, returned, and consumed by nothing.

That is tolerable while every change narrows the grammar, and dangerous the moment one widens it — which
the `Rem` work does, and whose central hazard is precisely over-reach. Gated now: **43 false acceptances**,
grouped by cause into six defects. `UNDERSCORE-IDENTIFIER` is 16 of them, all one lexer character:
`LETTER` includes `_`, so a lone `_` is an identifier here and never is in VB6, and `x = 1 +_` completes as
arithmetic against a variable named `_` instead of failing.

That fix was tried in this change and **reverted**. `_` alone on a line is legal VB6, and `NEWLINE`
(`WS? '\r'? '\n' WS?`) has already eaten the space that `LINE_CONTINUATION` (`[ \t]+ '_' …`) needs to
recognise it — so removing the underscore from the identifier start refuses two legal cases to fix one
illegal. One false acceptance bought for two false rejections is the wrong direction, and the honest
record of it is worth more than a fix that traded badly.

### Two asymmetries that were sitting in the corpus unread (2026-09-02)

Both of these were measured months ago and recorded as pass/fail verdicts. Neither had ever been turned
into a *rule*, which is the difference between a corpus and an oracle — and both were found only when the
false-acceptance list was grouped by cause and someone had to say what each group actually was.

**A continuation line of nothing but an underscore is legal if and only if it is indented.**

| probe | verdict |
|---|---|
| `x = 1 + _` ⏎ `_` ⏎ `2` — the lone underscore in column 1 | **illegal** |
| `x = 1 + _` ⏎ `  _` ⏎ `2` — the same file, indented two spaces | **legal** |

Two files differing by two space characters. So the whitespace before the underscore is *literally
required*, not merely how the documentation phrases it — and a continuation is genuinely
whitespace-then-underscore-then-end-of-line, with no exemption at the start of a line.

This is why the obvious repair for HexIDE's biggest remaining divergence does not work. `LETTER` includes
`_`, so a lone `_` lexes as an identifier and `x = 1 +_` completes as arithmetic instead of failing —
16 of the 43 false acceptances, one character. But removing `_` from the identifier start alone breaks the
indented form, because `NEWLINE` (`WS? '\r'? '\n' WS?`) has already eaten the space that
`LINE_CONTINUATION` (`[ \t]+ '_' …`) needs to recognise it. The two rules are arguing over the same
whitespace, and until that is settled the fix trades one false acceptance for two false rejections.

**A continuation is honoured in every declaration block except an Enum body.**

| probe | verdict |
|---|---|
| `Private Type TPoint` / `X _` ⏎ `As Long` / `End Type` | legal |
| `Private Declare Function GetTickCount _` ⏎ `Lib "kernel32" _` ⏎ `() As Long` | legal |
| `Attribute VB_Name = _` ⏎ `"Mod1"` | legal |
| `#Const DBG = _` ⏎ `1` | legal |
| `Private Enum EColor` / `Red = _` ⏎ `3` / `End Enum` | **illegal** — *Invalid inside Enum* |

The obvious guess is that continuations are lexical and therefore work everywhere. They do not, in exactly
one declaration block, and the error message is specific enough (*"Invalid inside Enum"*, anchored on the
member line) that it is clearly a rule rather than an accident. The inverse holds too: an Enum body accepts
colon separators, and a Type body does not.

**Recorded, and deliberately not implemented.** Matching it means a lexer mode pushed on `Enum` and popped
on `End Enum` — context-sensitive lexing driven by parser state, for one measured shape. Guessing the
rule's scope wide would turn a missing error into a false rejection of a whole module, which is the trade
this project refuses. What is unmeasured is the scope: whether the restriction covers the whole Enum body
or only a member's value expression, and whether the header line is affected. Two probes would settle it.

### VB6 has no bracket-escaped identifiers (2026-09-02)

| probe | verdict |
|---|---|
| `Dim [q] As Long` / `[q] = 5` | **illegal** — Syntax error, line 1 |
| `Dim [Print] As Long` | **illegal** — Syntax error, line 1 |
| `Dim [Rem] As Long` | **illegal** — Syntax error, line 1 |

`[name]` is a **VBA / VB.NET** feature. VB6 predates it and does not have it, so there is no escape hatch
for a name that collides with a keyword — the name is simply unavailable.

**How this was found is the point, and it is a caution about confident review.** The `Rem` change made
`Dim [Rem] As Long` stop parsing: the comment rule fires inside the brackets and eats the closing one.
Two independent reviewers, working separately, reported that as a **regression to fix before shipping** —
one calling `[…]` *"the documented escape hatch for a name that collides with a keyword"*, and both
correctly observing that HexIDE had accepted it the week before. The reasoning was sound and the
conclusion was wrong, because the premise was a VBA fact wearing VB6 clothes.

Measuring it inverted the finding entirely. `[Rem]` no longer parsing is not a regression: it is one case
that accidentally moved *towards* VB6. The grammar's bracket alternative in `ambiguousIdentifier` is an
over-acceptance in every other case, and dropping it retires three corpus rows rather than costing any.

This is the same failure mode the oracle file was created for, and it is worth naming because it recurs:
**a rule remembered from a neighbouring product, asserted confidently, and wrong here.** It has now
happened with the VBA documentation (`Rem` requires a space — it does not), with a reviewer's mental model
(`[…]` escapes keywords — not in VB6), and, in this same change, with my own reading of an enum probe.
The defence is not better reviewers. It is that nothing counts until `vb6.exe` has been asked.

### Line labels: a property of the line, not a statement (2026-09-03)

Ten new corpus cases, and the answer to two of them decided the grammar's shape.

| probe | verdict |
|---|---|
| `10 Skip: Debug.Print "A"` — a line number AND a named label | **legal** |
| `Skip: 10 Debug.Print "A"` — the same pair, other order | **illegal** — Syntax error |
| `Skip<tab>: Debug.Print "A"` | legal |
| `First:` ⏎ `Second:` ⏎ `stmt` — two labels on consecutive lines | legal, and BOTH reach `stmt` |
| `Cont: Next i` — a label before a loop terminator | legal |
| `Main:` inside `Sub Main` — a label named for its own procedure | legal |
| `Chk: If True Then` … `End If` | legal |
| `z = 1:: Here: z = 2` | **illegal** — *Sub or Function not defined* |
| `Orphan:` at module level between two procedures | **illegal** — *Only comments may appear after End Sub…* |

So the head of a line is an **ordered sequence**, number then name, not a set. And a label is
procedure-scoped: outside one there is nowhere to jump from and VB6 says so.

**The most useful row is the illegal one that is not a syntax error.** `z = 1:: Here: z = 2` comes back
*"Sub or Function not defined"* — VB6 is not refusing the shape, it is **reading `Here` as a call**, which
is exactly what HexIDE's grammar does with it. The two parses agree and only the timing of the complaint
differs. That single message settled a design question that reasoning had not: a line head reachable after
a colon would not merely over-accept, it would invent a jump target the program does not have. Six corpus
rows previously filed as a label defect are really this, and they are permanent — the residue is name
binding.

**What HexIDE was getting wrong, and why no gate saw it.** `lineLabel` was an alternative of `blockStmt`
while the numeric line number was already a prefix on the block. Because the block's separator admits a
colon, taking the label alternative would have consumed the colon the block needed next — so ANTLR never
took it, and `Skip: Debug.Print "x"` parsed as a bare call to `Skip` followed by a separator.

**Ten of fourteen measured-legal label forms were dead.** Only a label standing ALONE on its line worked,
because only that one has nothing to compete with:

| form | before |
|---|---|
| `Skip: Debug.Print "x"` | label lost |
| `Later : stmt` (space before the colon) | label lost |
| `Skip:: stmt` | label lost |
| `Skip:Debug.Print` (no space after) | label lost |
| an indented label sharing its line | label lost |
| `Retry: i = i + 1 : If i < 2 Then GoTo Retry` | label lost |
| `Handler: Debug.Print "handled"` on an On Error target | label lost |
| a label whose name collides with a variable | label lost |
| `Skip _` ⏎ `: stmt` | label lost |
| `Skip: _` ⏎ `stmt` | label lost |
| `Skip:` alone on its line | worked |
| `Fin:` alone before `End Sub` | worked |

**None of this was visible to the conformance corpus**, and that is the lesson worth keeping. A lost label
PARSES — the name simply becomes a call — so a gate that asks "does this module load" reports green on
every row above. The failure surfaces later, somewhere else in the procedure, as `Label not defined` from
a `GoTo` that looks correct. An entire class of defect sat under a passing gate because the gate was
asking the only question the corpus could answer.

The fix is a line head reachable only after a separator containing a real line break. Two false rejections
fell out of it for free: `Error:` and `Name:` as label names, which the statement rules for `Error` and
`Name` had been shadowing.

### The underscore, and two rules arguing over one space (2026-09-03)

Not a new measurement — the facts were already recorded. This is the entry for what it took to *act* on
them, because the obvious repair was wrong and the reason is worth keeping.

The two facts, from earlier rounds:

| probe | verdict |
|---|---|
| `x = 1 + _` ⏎ `_` ⏎ `2` — a lone underscore in column one | **illegal** |
| `x = 1 + _` ⏎ `  _` ⏎ `2` — the same file, indented two spaces | **legal** |

So the whitespace before a continuation's underscore is *literally* required, and `_` is never a name.
HexIDE's `fragment LETTER` contained the underscore, making a lone `_` an identifier — which is what let
`x = 1 +_` complete as an addition against a variable named `_` instead of failing. Thirteen corpus rows.

**Why the one-character fix could not be taken alone.** `NEWLINE` was `WS? '\r'? '\n' WS?`, and that
trailing `WS?` eats the *next* line's indentation. So by the time the lexer reaches a line whose whole
content is ` _`, the space is already gone and `LINE_CONTINUATION` (`[ \t]+ '_' …`) cannot match. With the
underscore still an identifier that line lexed anyway, wrongly but harmlessly; take the underscore away
and it stops lexing at all. Two rules arguing over the same space, and each fix regressing the other.

**The obvious repair is wrong, and expensively so.** Giving `NEWLINE` its trailing whitespace back —
recommended by the analysis, and the first thing anyone would try — produced **151 test failures in one
run**. Every rule written since assumes a newline swallows the following indentation, so the whitespace
reappears as a token in a hundred places that never expected one. Worth recording as a measurement of the
grammar rather than of VB6: a lexer rule this old has a blast radius, and *"just make it stop doing that"*
is not available.

**What works is to ask what the line means.** A continuation joins its line to the next; joining an *empty*
line to the next yields the next line. So a line whose entire content is ` _` is not a line at all — it is
part of the boundary. Absorbing it into `NEWLINE` costs nothing elsewhere, because the token that comes out
is the one every existing rule already expects:

```
NEWLINE : WS? '\r'? '\n' (WS '_' [ \t]* ('\r'? '\n')?)* WS? ;
```

The mandatory `WS` inside the group is the whole discriminator — it is what keeps a column-one `_`
unmatched, and therefore refused, exactly as VB6 refuses it. Two space characters, load-bearing.

Thirteen false acceptances retired, no regressions. The general lesson is the one this file keeps
recording from a new direction: the fix that follows from the diagnosis is not always the fix that fits the
code, and the cheapest way to find out is to try it and count.

## Enums (2026-09-03)

Thirty-odd probes. Enums are everywhere in VB6's object model, and almost nothing about them was written
down.

### A member's value is a constant EXPRESSION, not a literal

| probe | value |
|---|---|
| `&HFF` | 255 |
| **`&H80000005`** | **−2147483643** — the high bit makes it a negative Long |
| the member after it, with no value | −2147483642 |
| `&O17` | 15 |
| `-3`, then the member after it | −3, then −2 |
| `0` explicitly, then the member after it | 0, then 1 |
| `xFirst + 1` where `xFirst = 5` | 6 |
| `KBase` where `Public Const KBase As Long = 100` | 100 |
| `2 ^ 3` | 8 |
| first member with no value | 0 |

So hex, octal, negatives, references to earlier members, references to a `Const`, and arbitrary arithmetic
are all ordinary. Every member is a **`Long`** — `TypeName` says `Long` and `VarType` says 3, never the
enum's own name.

**Issue #176 understates its own bug.** It says members "must be decimal literals"; that is the symptom.
The rule is that a member takes a constant expression, and a wider literal parser would not have been the
fix.

### Evaluation is a single forward walk — measured, not assumed

| probe | verdict |
|---|---|
| a member referencing a **later** member | **illegal** — *Constant expression required* |
| a member referencing a **later** `Const` | **illegal** — same |
| a member referencing an **earlier** enum, qualified | legal |
| a member referencing an earlier member of its **own** enum, qualified | legal |

This matters more than it looks. CLAUDE.md prescribes lazy-memoised evaluation for `Const` precisely
because `Const` is order-independent, and the obvious move was to reach for the same pattern here. VB6 says
no: source order **is** the rule, so a plain forward walk is not a shortcut, it is the specification. The
pre-pass stays pure collection and a forward reference simply is not found — the right answer for the right
reason.

### `/` is real division, then rounding half to EVEN

The one that bites, and the one that caught this session's own implementation.

| probe | value |
|---|---|
| `10 / 5` | 2 |
| `7 / 2` | **4** |
| `5 / 2` | **2** |
| `-7 / 2` | **−4** |
| `10 / 3` | 3 |
| `10 \ 4` | 2 |
| `-7 \ 2` | −3 |

A member is a Long, so the expression is evaluated and then **coerced**, and VB6 coerces by rounding half
to even. Folding in integers instead gives 3 for `7 / 2` and −3 for `-7 / 2` — both plausible, both wrong,
and neither announces itself. HexIDE shipped exactly that for two hours; it was caught because one lexer
token covers `/` and `\` and the runtime tells them apart by the token's text, which the constant folder
did not.

### Addressing: `[[Lib.][Enum.]]Value`, both qualifiers independently optional

| probe | verdict |
|---|---|
| `vbAlignBottom` | legal |
| `AlignConstants.vbAlignBottom` | legal |
| `VBRUN.AlignConstants.vbAlignBottom` | legal |
| **`VBRUN.vbAlignBottom`** — skipping the enum level | **legal** |
| `VBA.VbMsgBoxStyle.vbOKOnly` | legal — holds for the VBA library too |
| `Module1.pTwo` — a module qualifying a bare member | legal |
| `Module1.EPlain.pTwo` — module, enum, member | legal |
| `EPlain.EPlain.pTwo` | **illegal** — *Method or data member not found* |
| `VBRUN.EPlain.pTwo` — a user enum via a library qualifier | **illegal** — same |

All the legal forms resolve to the same value. The first slot takes a **module or a type library**, and the
middle slot is optional in both cases — so the notation is really `[[Lib.][Enum.]]Value`. It is not a
free-form namespace: a fourth level, and a user enum reached through a library, are both refused.

### A member is a CONSTANT, and an enum-typed variable is an open Long

| probe | verdict |
|---|---|
| `pTwo = 5` | **illegal** — *Assignment to constant not permitted* |
| `Dim x As EPlain` then `TypeName(x)` | `Long` — the enum name is not retained |
| `Dim x As EPlain` then `x = 999` | legal, prints 999 |
| `TypeName(EPlain)` | **illegal** — *Expected variable or procedure, not enum type* |
| an `Enum` declared inside a procedure | **illegal** — *Invalid inside procedure* |

So enums are named Longs, not closed sets: a value no member declares is accepted without complaint.
HexIDE was hoisting members as ordinary variables, so `pTwo = 5` **succeeded** — a program could overwrite
`vbRed` and nothing anywhere would say so. That row belonged in the *silently wrong* tier that
`MISSING_LANGUAGE.md` reports as "0 names", and which the file itself flags as a floor rather than a
ceiling.

### Two enums may share a member name — until you use it

| probe | verdict |
|---|---|
| two enums both declaring `shared_` | legal to DECLARE |
| `EOne.shared_` / `ETwo.shared_` | 11 / 22 |
| bare `shared_` | **illegal** — *Ambiguous name detected* |

The declaration is fine and the *use* is the error, which is a shape worth remembering: the collision is
diagnosed where it is unresolvable, not where it is created. HexIDE silently returns the last one loaded.

### Which keywords may name a label — measured word by word, because it is not derivable

Thirty-three probes, prompted by a line-label defect and by a maintainer's recollection about `New`.

**Usable as a name:** `Beep` `Reset` `Error` `Name` `Width` `Load` `Kill` `Time` `Mid`
**Reserved:** `End` `Close` `Randomize` `Resume` `Loop` `Next` `Wend` `Case` `Print` `Get` `Put` `Write`
`Input` `Open` `Seek` `Lock` `Unlock` `Set` `Let` `Call` `Date` `Stop` `Return` `Else` `New` `Nothing`

`Reset:` is a label and `Randomize:` is the Randomize statement. `Beep:` is a label and `Stop:` is a syntax
error. Each pair is a keyword whose statement form is complete on its own, so **no structural property
separates them** — which is why the grammar now carries a list and not a rule.

`New` and `Nothing` came from a recollection that `New` is illegal as a `Sub` name, recorded as a
hypothesis and then measured: both refused with *Expected: identifier*. Worth pinning rather than filing as
incidental, because the illegality is load-bearing for anyone extending the language — a dialect that wants
`New` to name a constructor can only do so cleanly because VB6 leaves the name unavailable.

## Module scope for Types and Enums (2026-09-03)

Issue #180 is a cross-module question, and until now the legality harness compiled **one** module — so the
thing the issue is written about was unmeasurable, and the only alternative was to guess. The harness now
takes a list of extra modules, written as `Module2.bas`, `Module3.bas` beside `Module1` and named in a
per-case `.vbp`. A list rather than one extra, because the decisive case needs three: a user and two
exporters, so nothing local can disambiguate.

### The base rule is the one already written for procedures

| probe | verdict |
|---|---|
| two modules, both `Private Type Point` | legal — unrelated types |
| two modules, both `Public Type Point` | legal |
| local `Private` beside a foreign `Public` | legal — local wins |
| a foreign `Private` enum's members, bare | **illegal** — *Variable not defined* |
| a foreign `Private` Type, bare or qualified | **illegal** — *User-defined type not defined* |
| two foreign `Public` Types, nothing local | **illegal** — *Ambiguous name detected* |

Own module first at any visibility, then other modules' `Public` only, 2+ is ambiguous. That is
`TryResolveProcedure` verbatim — so #180's own diagnosis was right: the algorithm was written and two
declaration kinds were not using it. **Two `Private`s in different modules do NOT clash**, which is why a
collision check that ignored visibility would have refused a legal program.

### Then they diverge, and the difference is not guessable

| type position | module-qualified | project-qualified | project + module |
|---|---|---|---|
| **UDT** `Point` | **legal** | legal | legal |
| **Enum** `MyPublicEnum` | **illegal** | legal | **illegal** |

Value position accepts everything, to four levels: `Project1.Module1.MyEnum.Foo` and a bare `Project1.Foo`
both compile.

**The reading that fits all of it: a UDT's type identity is MODULE-scoped, an Enum's is PROJECT-scoped.**
So `Module.Point` names a real thing and `Module.MyEnum` does not — the module is no part of an enum's
identity *as a type*. Its **members** are still hoisted into the module's namespace, which is why the
module qualifies them as *values* though not as a type.

That also explains a result recorded earlier without an account of it: two modules may each own a `Public
Type` of one name (each belongs to its module — ambiguity only at an unqualified use), and may **not** both
export a `Public Enum` of one name (they collide in the single project namespace — refused at the
declaration, reported at line 0 with no use involved). One rule, two consequences. *The mechanism is
inferred; the twelve verdicts it accounts for are not.*

### Disambiguation, and what a prefix does not buy

| probe | verdict |
|---|---|
| `Dim p As Module2.Point`, two exporters | legal — the prefix resolves it |
| `Module2.EKind.kA`, two exporting the same `Public Enum` | **illegal** — the prefix does not help |
| `Module2.kA`, a member colliding across two enums | legal |
| `Dim p As Module2.Point` beside a **local** `Point` | legal, and reads the FOREIGN member |
| a foreign `Private` Type, qualified | **illegal** |

So the module prefix is a genuine **override**, not a tie-break — and it never defeats `Private`.

### Two harness lessons, each of which produced a wrong answer first

- **`Option Explicit` is mandatory in the probing module.** Without it an unresolvable name is an
  implicitly-declared Variant and the module compiles, so *"not found"* and *"found unambiguously"* are
  indistinguishable. The first run reported a `Private` enum's members as visible across modules — a
  confident wrong fact, of the same family as the duplicate `Sub Main` incident recorded above.
- **A probe that calls `TypeName(p)` on a UDT fails on an unrelated rule** — a UDT cannot be passed to a
  late-bound function — and so measures its own mistake. Reading a field keeps the case about scoping.

## What the corpus PRINTS, not just whether it parses (2026-09-03)

The conformance corpus has been measured against `vb6.exe` for legality since it was built, and every one of
its five gate assertions bottomed out in a single predicate: *does this parse*. That is one bit per case. It
caught real defects and drove false rejections from 21 to 9 — and it was structurally unable to see four
defects that shipped in one session, all of which parse correctly and then do the wrong thing: an `Else:`
that swallowed its own branch, a line label that ate the statement beside it, `Dim p As Module2.Point`, and
the whole of module scope.

**The corpus was never the gap.** 296 of its 300 legal cases already called `Debug.Print`; 640 prints in all.
Every one of the four missed defects already had cases that printed the value that was wrong — 5 of 5 for the
`Else:` family, 69 of 69 for labels, 15 of 25 for module scope. The cases were sitting there. Nothing was
asking them what they produced.

### Method

`Debug.Print` is **inert in a compiled exe** — there is no Immediate window to receive it — which is why
this went unnoticed for so long. But a compiled exe can `Print #` to a file, which `vb6-oracle.ps1` already
relied on. `scripts/vb6-legality.ps1 -CaptureOutput` therefore builds a *second* module per case with
`Debug.Print` rewritten to a helper that appends `TypeName(v) & vbTab & CStr(v)`, compiles it, runs it, and
records what the file received. Open/append/close per call, so no startup or shutdown hook is needed and a
case defining its own `Sub Main` needs no special handling.

The rewrite changes the program, and some cases are *about* the construct being rewritten, so the probe's
legality is compared with the original's; a disagreement is recorded as `rewrite-broke-case` and **no
behaviour is kept**. Five cases hit that, all of them about unterminated or continued string literals —
exactly where a `Debug.Print` rewrite would be expected to interfere.

### Results (502 cases)

| | |
|---|---|
| legal / illegal | 356 / 146 |
| ran cleanly, output recorded | **331** |
| hung (killed on timeout) | **0** |
| non-zero exit | 0 |
| rewrite broke the case | 5 |
| no print to observe | 32 |

**Zero hangs was not the expected result.** The legality oracle compiles and never runs, deliberately,
because an unhandled VB6 runtime error in a compiled exe raises a modal and waits forever. That risk is real
but did not materialise once across 331 executions of this corpus — worth knowing before anyone else decides
running is too dangerous to attempt.

Types actually observed across 424 printed lines: `String` 304, `Long` 102, `Integer` 10, `Boolean` 5,
`Empty` 2, `Double` 1. The type is recorded alongside the value on purpose — a gate diffing rendered text
alone passes an Integer where VB6 gives a Long, which is precisely where a wrong value hides.

**Thirteen legal cases contain a `Debug.Print` and print nothing.** `second-name-colon-runs-as-call`,
`end-sub-after-colon`, `label-immediately-before-end-sub`, `colon-only-line-at-module-level` and others.
"VB6 prints nothing here" is a real expectation, and an interpreter that prints *something* at one of them is
diverging in a direction no parse check can detect.

### What it found immediately

216 of the captured cases are statement-scope and run under the interpreter today. **21 diverge.** All 21
parse cleanly, so the existing gate reported every one of them as conformant.

The largest is a single cause with seven cases: **a `Select Case` whose parts are colon-joined, split by a
line continuation, or separated by a blank line selects the wrong branch, or none at all.** Silently, with no
error raised:

| case | vb6.exe | HexIDE |
|---|---|---|
| `select-case-entirely-on-one-line` | `A` | `B` |
| `case-body-colon-joined` | `A`, `B` | `C` |
| `sep-whole-select-one-line` | `ONE` | *(nothing)* |
| `blank-line-before-first-case` | `two` | *(nothing)* |

Two further silent wrong values, unrelated to each other:

- **A line continuation inside an identifier does not join it.** `continuation-illegal/split-identifier`
  prints `Long 0` under VB6 — the halves are two separate names, so the read is of an undeclared Variant.
  HexIDE joins them, reads the real variable, and prints `Long 5`. Accepting more than VB6 would be
  tolerable; returning a different number is not.
- **`""` inside a string literal is not collapsed.** VB6 prints `he said "hi" _`; HexIDE prints
  `he said ""hi"" _`.

### What this overturned

The belief that a corpus measured against the real compiler *is* a conformance gate. It is only a gate for
the question it asks. Ours asked the cheapest one available — and because false rejections are the most
visible failure and parsing is where they live, nobody noticed that "wrong value", which CLAUDE.md ranks as
the one thing never acceptable, had no gate at all. The corpus had carried the evidence the whole time.

**A gap this turned up on its own:** the interpreter has **no `Sub Main` startup object**.
`ProjectSerializer` reads `Startup="Sub Main"` from the .vbp, and `BasicInterpreter` still describes it as
"a future `Sub Main`". So a Standard EXE that starts at `Sub Main` — the default for a code-only project —
does not run. It is also why 82 measured cases cannot be gated yet: their entry point is never called.

## `Select Case` layout — colons, blank lines, and continuations (2026-09-03)

The behavioural gate's first run found a seven-case cluster where a `Select Case` that is colon-joined,
split by a line continuation, or preceded by a blank line **selects the wrong branch or none at all**,
silently. Those seven were simply the cases that happened to exist, which is no basis for designing a fix,
so the layout space around them was measured: 18 further cases, 17 legal, all executed and their output
recorded.

### A colon is an alternative line break, after every case form

| written | VB6 |
|---|---|
| `Case 1: Debug.Print "ONE"` | prints `ONE` |
| `Case 1:` then the body on the next line | prints `ONE` — **the colon needs no statement after it** |
| `Case Else: Debug.Print "OTHER"` | prints `OTHER` |
| `Case 1, 2, 3: Debug.Print "IN LIST"` | prints `IN LIST` |
| `Case Is > 1: Debug.Print "BIG"` | prints `BIG` |
| `Case 1 To 5: Debug.Print "IN RANGE"` | prints `IN RANGE` |
| `Select Case x:` then `Case 1` on the next line | prints `ONE` |
| `Debug.Print "ONE": End Select` | prints `ONE` |
| the whole construct on one line, colon-separated | prints `ONE` |

So the colon is accepted after the selector, after every one of the three case-condition forms, and before
the block terminator. There is no form of `Select Case` in which a colon may not stand where a line break
may.

### What may sit between the selector and the first `Case`

| between `Select Case x` and `Case 1` | VB6 |
|---|---|
| one blank line | legal |
| several blank lines | legal |
| a comment line | legal |
| **an executable statement** | **illegal** — the only illegal case of the 18 |

That last row is the boundary, and it is the useful one: whatever rule admits blank lines and comments must
still refuse a statement. A fix that simply lets anything appear there would be wrong in a way none of the
other 17 cases would catch.

### The case that catches a bad fix

**An empty matching arm does not fall through.**

```vb
x = 1
Select Case x
Case 1
Case 2
    Debug.Print "TWO"
End Select
Debug.Print "AFTER"
```

VB6 prints `AFTER` and nothing else. This is recorded deliberately as a trap: the obvious way to fix the
wrong-branch bug is to make a case body greedier, and a greedy body swallows the following `Case 2` clause
as though it were a statement in arm 1 — which would print `TWO` here. Nothing else in the corpus
distinguishes the correct fix from that one. Likewise `select-case-on-one-line-no-match` prints only
`AFTER`, separating "the construct was skipped entirely" from "the construct ran and chose correctly",
which a matching one-line case cannot.

### Continuations: each half of the keyword pair works alone

`Select _ ⏎ Case n` with `End Select` intact prints normally; `End _ ⏎ Select` with the opener intact prints
normally. Both halves are independently fine, so neither is uniquely responsible for the two corpus cases
that vary both at once.

### The layout was a red herring — and so was my reading of it

12 of the 18 diverged, all in the "prints nothing" direction, but not uniformly: `Case Is > 1:` and
`Case 1 To 5:` worked, while `Case 1:` and `Case 1, 2, 3:` did not. I recorded here, wrongly, that this
pointed at how far the condition expression extends before the colon. **It has nothing to do with the colon,
the blank line, the continuation, or any other layout.** The parse trees for all seven original cases and all
18 probes are correct; the grammar was never at fault.

The real cause is one line of the interpreter, and the asymmetry is its fingerprint. `Select Case` compared
the selector to each arm with `Vb6Value.Equals`, which is `Type == other.Type && Equals(Value, other.Value)`
— **type-identical**. Every case in the cluster declared `Dim n As Long` and wrote a bare literal `1`, which
is an **Integer**. `Long(1).Equals(Integer(1))` is false, so no arm ever matched, and that single fact
produces both symptom classes: with no `Case Else` nothing prints; with one, control falls through to it,
which merely *looks* like "the wrong branch was chosen".

The two forms that worked are the two that never used `Equals`: `Case Is <` / `<=` / `>` / `>=` and
`Case a To b` go through `Vb6Value.TryCompareTo`, whose cross-type numeric path was added — the comment says
so — for exactly this. Plain `Case v`, `Case Is =` and `Case Is <>` were the three that did not.

Why ~1300 tests never caught it: `StatementTests` selects on a bare literal so both sides are Integer,
`WideningTests` uses `To` ranges, and `SplitKeywordTests.AContinuationMaySplitSelectCase` is one of these
corpus cases character-for-character except that it writes `Dim n` — a Variant holding an Integer — instead
of `Dim n As Long`. One keyword away from the failure, and green.

**The lesson worth keeping:** every layout probe in this section was answered correctly by VB6 and every one
of them was measuring the wrong thing. A cluster of failures that share a visible surface feature is not
evidence that the feature is the cause — here the shared feature was in the *test corpus'* house style
(`As Long` selectors), not in the construct.

## `Select Case` matching — the coercion rule (2026-09-03)

Having found that `Select Case` matching was the real defect, the rule itself had to be measured before it
could be widened. 30 cases; the result is a single rule with one genuinely surprising consequence.

> **VB6 coerces the CASE EXPRESSION to the SELECTOR's runtime type, then compares.**

It does *not* compare numerically, and it is not symmetric. Two measurements settle it, and they disagree
with each other under any numeric reading:

| | VB6 |
|---|---|
| `Dim s As String : s = "1.0"` … `Case 1` | **ELSE** — `CStr(1)` is `"1"`, which is not `"1.0"` |
| `Dim s As String : s = "01"` … `Case 1` | **ELSE** — same reason |
| `Dim n As Long : n = 1` … `Case "1.0"` | **MATCH** — `CLng("1.0")` is 1 |
| `Dim n As Long : n = 2` … `Case 1.7` | **MATCH** — `CLng(1.7)` is 2 |

That last row is the one that pins the rule inside the numeric family: a numeric comparison of 2 against 1.7
is not equal, and VB6 matches anyway.

Everything else follows from it and was confirmed rather than assumed: a `Long` selector matches a bare
Integer literal; an `Integer` selector matches `1&`; `Double`, `Currency`, `Date` and `Boolean` selectors all
participate (`True` matches `Case -1`, a Date of serial 2 matches `Case 2`); an unassigned Variant matches
`Case 0`; the first of two matching arms wins; and a selector that is an *expression* is judged on its
runtime type, not its declared one.

### A failed coercion RAISES — it is not a non-match

| | VB6 |
|---|---|
| `Dim n As Integer` … `Case 40000` | **Err 6** (Overflow) — `CInt(40000)` cannot succeed |
| `Dim n As Long` … `Case "abc"` | **Err 13** (Type mismatch) |
| `Dim s As String` … `Case 40000` | **Err 0**, prints `ELSE` — `CStr` cannot fail, so this direction never raises |

Both of the raising cases were first observed as **`hung`** by the capture harness: a compiled exe with an
unhandled runtime error puts up a modal and waits, and the harness killed it. For programs this small,
"hung" *is* "raised". `On Error Resume Next` then converted the modal into an observable `Err.Number`.

A caveat recorded because it is easy to misread as a rule: under `On Error Resume Next` both raising cases
also print `MATCH`, because VB6 resumes **into the matched arm's body**. That is a resume-point behaviour,
not evidence that the arm matched — the unhandled form of the same program raises and stops.

### The rule is already implemented — under another name

`VbNumeric.CoerceOnStore` is this rule exactly, and was written and oracle-verified for coercion **on store**
(`Dim i As Integer : i = "12"`). String target takes anything and never fails; a numeric target parses
strings with Err 13 on garbage, rounds half-to-even, and range-checks with Err 6. Every measurement above
falls out of it. **VB6's coercion-on-store and its `Select Case` comparison are one rule**, which is why the
fix reuses it rather than restating it.

### The trap: `=` is a DIFFERENT rule

| | VB6 |
|---|---|
| `Select Case "1.0"` … `Case 1` | **does not match** |
| `If "1.0" = 1 Then` | **True** |
| `If "1" = 1 Then` | **True** |

The obvious tidy-up — give `Select Case` and `=` one shared comparison helper — is therefore wrong, and it
was the first thing suggested. The `=` operator compares numerically after coercing a String operand to a
number; `Select Case` coerces toward the selector. They agree on `"1"` and disagree on `"1.0"`, so a single
probe would have "confirmed" the wrong rule.

This also exposed a **separate defect**: HexIDE raises Type mismatch for `"1" = 1`, because
`ExpressionExecutor.GetTwoValuesSameTypesOrNull` has no String/numeric path and falls through to its throw.
VB6 says True. Filed separately — it is not a `Select Case` bug and must not be fixed by the same code.

## In-box constants: the library qualifier selects (2026-09-03)

The full inventory of VB6's in-box constants — 728 across 77 enums and 2 constant modules in the four
libraries a Standard EXE references by default — is in
[`vb6-inbox-constants.md`](vb6-inbox-constants.md), read out of the real type libraries by
`scripts/dump-typelib-constants.ps1`. The behavioural facts that inventory established are here.

### `VBRUN.vbCancel` is 0 and `VBA.vbCancel` is 2

Exactly one name of the 728 is declared twice with two different values, and each qualified form resolves
to its own library's:

| written | VB6 |
|---|---|
| `vbCancel` | **2** |
| `VBA.vbCancel` | **2** |
| `VBRUN.vbCancel` | **0** |
| `VBRUN.DragConstants.vbCancel` | **0** |

`vbCancel` is `2` in `VBA.VbMsgBoxResult` and `0` in `VBRUN.DragConstants`; the bare form takes VBA's, so
default reference order gives VBA precedence over VBRUN.

### The reference order, and what of it is actually measured

VB6 lists the four in Project → References as **VBA, VBRUN, VB, stdole**, and an unqualified name resolves
through them in that order, first match winning. `VBA`, `VBRUN` and `VB` are implicit, irremovable and
fixed in that sequence — they never appear as `Reference=` lines in a `.vbp`, which is corroborated by the
shipped VB6 template projects, where the only `Reference=` entries are removable ones such as stdole.
`stdole` is an ordinary listed reference: it can be removed or reordered, but not moved ahead of the fixed
three, so it is always last of the four.

**Only the VBA-before-VBRUN step is observable.** It comes from `vbCancel`, the one ambiguous name. `VB`
declares no constants at all and `stdole` shares no name with the other three, so the tail of the order
has no measurable consequence today.

Recorded as a limit rather than tidied away, because the first version of this got it wrong in a way the
oracle's own rules warn about: the order was hand-written into the generator as *VBA, VBRUN, stdole, VB*
and then asserted in a test and stated here as though it had been measured — an invented rule laundered
into a fact. No behaviour changed when it was corrected, which is exactly why nothing caught it. **Supplied
by the user from the References dialog**; the implementation now reads the order from an explicit
`referenceOrder` field in the inventory instead of inferring it from key order, so it is data that can be
checked rather than a sequence someone typed.

**This is what makes a flat name→value table wrong rather than merely incomplete**, and it is why the flat
table was replaced by `VB6InBoxLibraries`. Before that, HexIDE treated both the library and the enum
qualifier as *transparent* — looked past them and resolved the bare name — so it answered `2` for all four
rows and was wrong on two of them. A wrong value, which CLAUDE.md ranks as never acceptable, and invisible
because nothing exercised `VBRUN.vbCancel`: every corpus case covering library-qualified addressing was
module-scope, so all of them wrap in a `Sub Main` the interpreter cannot run.

The control: `vbNormal` is the only other duplicated name, and it is `0` in both `VBA.VbFileAttribute` and
`VBRUN.FormWindowStateConstants`. A *consistent* duplicate is harmless, which is what attributes the
`vbCancel` result to the disagreement rather than to duplication as such.

### Both qualifier levels are real scopes, and a mismatch is REFUSED

The other half of "the qualifier selects": a qualifier that does not declare the name is an error, not a
level to step over. All three of these are **illegal** — "Method or data member not found":

| written | why |
|---|---|
| `VBRUN.VbMsgBoxResult.vbCancel` | `VbMsgBoxResult` is VBA's enum, not VBRUN's |
| `stdole.vbCancel` | stdole declares neither |
| `DragConstants.vbYes` | `vbYes` belongs to `VbMsgBoxResult` |
| `VBA.vbKeyA` | `vbKeyA` is declared by `VBRUN.KeyCodeConstants` and by nothing else |
| `VB.vbKeyA` | VB declares nothing at all |

`VBRUN.vbKeyA` is 65, and so is a bare `vbKeyA`.

**`VBA.vbKeyA` being illegal overturned a passing interpreter test.** `MultiModuleTests
.LibraryQualifier_Constant` asserted `VBA.vbKeyA` = 65 and had always passed — because the library
qualifier was resolved transparently, so it was pinning a false acceptance rather than a fact. That is the
third test in this line of work found to encode the implementation instead of the compiler; a unit test
cannot catch a false acceptance it was written from.

### The container qualifier selects on its own

`VbMsgBoxResult.vbCancel` is 2 and `DragConstants.vbCancel` is 0 — no library needed. All 79 container
names happen to be unique across the four libraries, which is what makes the unqualified form
unambiguous, and is asserted by a test rather than assumed so a regenerated inventory cannot break it
silently.

A constant **module** qualifies its members exactly as an enum does: `Constants.vbCrLf` and
`VBA.Constants.vbCrLf` are both legal.

### The project's own declarations win

| written | VB6 |
|---|---|
| `Private Enum VbMsgBoxResult` with `vbCancel = 42`, then bare `vbCancel` | **42** |
| `Private Const vbCancel = 7`, then bare `vbCancel` | **7**, and as an **Integer** |

So the search order is: the project's Enums and Consts, then the libraries in reference order. Note the
`Const` case also changes the *type* — the user's constant is typed by its own literal, not by the library
member it shadows.

Assignment is refused as it is for a user enum member: `vbCancel = 5` is "Assignment to constant not
permitted".

### An in-box enum is a type

`Dim x As VbMsgBoxResult` compiles and runs (measured: assigning `vbCancel` to it and printing gives `2`),
and so does `Dim x As VBA.VbMsgBoxResult` — **the library may qualify a type name**. That is an asymmetry
worth noting against user enums, where `Dim p As Module2.MyEnum` is *illegal* because an Enum's identity
is project-scoped: a library is not a module.

`Dim x As Constants` is illegal — "Automation type not supported in Visual Basic". An enum name is a type
*and* a qualifier; a constant module's name is only a qualifier.

An in-box-enum-typed variable reports `TypeName` "Long" and accepts a value outside the enum (`x = 999`
gives 999), both exactly as a user enum does — so the two share one representation.

### `vbUseCompareOption` does not exist — the documentation is wrong

`vbUseCompareOption` is widely documented as a member of `VbCompareMethod` with value `-1`. The type
library declares **three** members and not that one: `vbBinaryCompare` 0, `vbTextCompare` 1,
`vbDatabaseCompare` 2. And the compiler agrees with the type library:

| written | VB6 |
|---|---|
| `VbCompareMethod.vbUseCompareOption` | **compile error** — "Method or data member not found" |
| `VbCompareMethod.vbBinaryCompare` / `vbTextCompare` / `vbDatabaseCompare` | 0 / 1 / 2 |
| `Debug.Print vbUseCompareOption` | "compiles", prints **Empty** |

That last row is a trap, and the reason the qualified probe is the decisive one: with no `Option
Explicit` the bare name is simply an undeclared Variant, so it evaluates to `Empty` and the program
builds. Read alone it looks like confirmation that the constant exists with an empty value. This is the
same harness artefact that earlier produced a confident wrong fact about `Private` enum visibility —
an unresolvable name and a resolvable one are indistinguishable without `Option Explicit`.

Consequence for the coverage inventory: refusing `vbUseCompareOption` is **faithful**, not a gap. It had
been recorded in `MISSING_LANGUAGE.md` as **Dies** on the reasoning that the name was missing from
`builtInConstants` — true, and irrelevant, because it is missing from VB6 too. (Raised by the user from a
Stack Overflow thread reporting the same discrepancy, then settled here against the compiler.)

### The stdole constants are bare-resolvable despite their generic names

stdole contributes seven members that are not `vb`-prefixed: `Default`, `Color`, `Gray`, `Checked`,
`Unchecked`, `Monochrome`, `VgaColor`. Measured, they resolve **bare** — `Default` is 0, `Color` is 4,
`Checked` is 1 — and still do under `Option Explicit`. So including them is faithful rather than reckless,
and a user declaration of the same name shadows them by the rule above. This was worth measuring before
implementing: adding `Default` and `Color` to the bare namespace on a guess would have been a plausible
way to break ordinary code.

### Where the constants actually live, which the obvious guess gets wrong

- **`VB` — "Visual Basic objects and procedures" — declares NO constants at all.** The control constants
  (`AlignConstants`, `BorderStyleConstants`, `ScaleModeConstants`, `MousePointerConstants`) are **not**
  there. All 590 of them are in **`VBRUN`**, which holds 81% of the total. I asserted the opposite before
  measuring, against a correct comment already in `ExpressionExecutor.cs`.
- **`VBRUN` is not a file.** It shares `MSVBVM60.DLL` with `VBA` and is reachable only as typelib resource
  `MSVBVM60.DLL\3` (`VBA` is index 1). A tool that enumerates library *files* finds `VBA` and silently
  misses VBRUN entirely.

### `VBA.Constants` — the string-valued constants

`vbTab` = `Chr(9)`, `vbCr` = `Chr(13)`, `vbLf` = `Chr(10)`, `vbCrLf` = `vbNewLine` = CRLF, `vbBack` =
`Chr(8)`, `vbVerticalTab` = `Chr(11)`, `vbFormFeed` = `Chr(12)`, `vbNullChar` = `Chr(0)`, `vbNullString`
= zero-length, `vbObjectError` = `-2147221504`.

Confirmed twice by independent means: read from the type library, and separately from a compiled program
reporting `Asc`/`Len` of each. The cross-check earned its keep — the first generator run passed these
through a tab-separated file and reported `vbTab` as the **empty string**, with identical row counts and
every numeric constant unaffected. A measurement that has been through a lossy transport looks exactly
like a correct one.

Seven of these — `vbTab`, `vbBack`, `vbFormFeed`, `vbNewLine`, `vbNullChar`, `vbNullString`,
`vbVerticalTab` — are absent from HexIDE altogether; `vbTab` and `vbNullString` are already recorded as
**Dies** in `MISSING_LANGUAGE.md`.

## String literals: `""` is the only escape (2026-09-04)

| written | VB6 | `Len` |
|---|---|---|
| `"he said ""hi"""` | `he said "hi"` | 12 |
| `""""` | one quote (`Asc` 34) | 1 |
| `""` | **empty** | 0 |
| `""""""` | two quotes | 2 |
| `"""a"""` | `"a"` | 3 |
| `"a""b"` | `a"b` | 3 |
| `"a""" & """b"` | `a""b` — two REAL adjacent quotes | 4 |
| `"""" = Chr(34)` | True | |

`""` inside the delimiters is one quote, and it is the only escape VB6's string literals have.

**The order is what the degenerate cases pin.** The delimiters must come off *before* the unescape:
applied to the raw token text, `""` — the empty literal — would become a single quote. `Len("")` is 0 and
`Len("""")` is 1, and those two are indistinguishable until the outer pair is gone. Nothing shorter than
these two cases can establish that, which is why both are recorded rather than just the readable one.

### What this overturned

HexIDE stripped the delimiters with `Substring(1, len - 2)` and did nothing else, so **no string literal
was ever unescaped, anywhere**. `MsgBox "He said ""hello"""` displayed the doubles. A wrong value on
entirely ordinary code.

It had been recorded as a known divergence named `STRING-ESCAPE` against **one** corpus case, whose area
was line continuations — so it read as a continuation quirk. It was nothing of the sort. The lesson is the
same one the `Select Case` cluster taught in the other direction: **the corpus area a defect surfaces in
is not evidence about its cause.** There is exactly one place in the interpreter that reads a
`STRINGLITERAL`, so there was exactly one place it was wrong, and it was reachable from every string in
every program.

### A second, independent instance — in serialization

The same rule is applied inconsistently across three sites that unquote a VB6 quoted value:

| site | unescapes `""`? |
|---|---|
| `ProjectDeserializer` (`.vbp`) | **yes** |
| `ExpressionExecutor` (literals) | no → fixed |
| `VbFrmFormatDeserializer` (`.frm`) | **no** |

The `.frm` *writer* does not escape either (`Sink.WriteLine($"{field}=   \"{value}\"")`), so HexIDE
round-trips its own files consistently while disagreeing with VB6 in both directions: a real `.frm`
carrying `Caption = "He said ""hi"""` reads back with the doubles intact, and a caption containing a quote
is written as output VB6 cannot load. Filed separately — it belongs to `serialization-outcomes.md` and
needs its own measurement of what VB6 actually writes.

## `Sub Main` as a startup object (2026-09-04, #210)

| case | VB6 |
|---|---|
| `Sub Main` in the only module | runs |
| `Sub Main` in Module2, none in Module1 | **runs** — the lookup is project-wide |
| `Private Sub Main` in the startup module | **runs** |
| **`Private Sub Main` in a FOREIGN module** | **runs** — visibility is ignored entirely |
| Two modules both declaring `Main` | **illegal** — "Ambiguous name detected: Main" |
| One `Private` + one `Public` `Main`, different modules | **illegal** — same, still ambiguous |
| `Sub Main(ByVal n As Long)` | **illegal** — "Must have startup form or Sub Main()" |
| `Sub Main(Optional ByVal n As Long = 7)` | **illegal** — same |
| `Function Main() As Long` | **illegal** — same |
| No `Main` declared anywhere | **illegal** — same, at compile time |
| An executable statement outside any procedure | **illegal** — "Invalid outside procedure" |

**The startup lookup is not ordinary procedure resolution**, and the two rows that establish it are the
ones that had to be measured rather than reasoned:

- **A `Private Sub Main` in a foreign module is a valid startup.** Ordinary resolution sees another
  module's `Public` only, so delegating to it would have refused a project VB6 runs. A `Private` Main in
  the *primary* module proves nothing — own-module visibility explains that anyway — which is why the
  foreign-module case is the decisive one.
- **`Private` + `Public` in two modules is still ambiguous.** Ordinary resolution would not call that a
  clash at all, since a `Private` procedure is module-local. The startup search does, because it is
  choosing between candidates rather than resolving a reference.

And the parameter rule is "declares none", not "callable with none": an **all-`Optional`** parameter list
still disqualifies. One probe of the required-argument form alone would have suggested the weaker rule.

### VB6 has no top-level statements

`Debug.Print "x"` outside any procedure is **"Invalid outside procedure"**. So HexIDE's execution of
top-level statements — the entry point every statement-scope corpus case uses, and what the test fixture's
`Run()` is built on — is a deliberate **extension**, not a VB6 feature. Worth stating plainly: after a few
hundred corpus cases that depend on it, it reads like one. It costs nothing on real VB6 source, which
cannot contain such a statement, so no valid program is affected by accepting it. Recorded in the parse
gate as `TOP-LEVEL-STATEMENTS-ARE-AN-EXTENSION` rather than as a missing diagnostic.

### Two harness defects this exposed, both of the same kind

The first measurement of these rules produced **three wrong answers**, and the cause was the harness rule
whose own comment warns about exactly this: it appends a `Sub Main` when the case declares none, because
the .vbp names one as the startup.

1. It scanned **only the primary source** for an existing `Main`. A case whose `Main` lives in Module2 got
   a second one appended to Module1, so *"is the startup found project-wide"* came back
   illegal-because-ambiguous — an answer about the harness, not VB6.
2. It matched `Sub +Main` only, so a `Function Main` did not count as already-declared and collided with
   an appended `Sub Main`.
3. And the question *"what happens when no `Main` exists"* was **unaskable**, because the harness
   guaranteed one did. It now takes a `noAutoMain` opt-out.

Then fixing it appeared not to work, because the same decision was **derived twice** — once for the
legality module and again, differently, inside the behaviour-capture assembly. The legality verdict came
out right while the probe still failed as ambiguous. The decision is now computed once and reused.

The lesson is not "check the harness" but something narrower: **a harness that must synthesise part of the
program can only be trusted for questions that do not touch the synthesised part.** Every one of these
three questions was about `Sub Main`, which is precisely what the harness was inventing.

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

## Unsuffixed float literals, and comparing Double with Single (2026-09-03)

Two measurements, recorded together because the second was found while confirming the first. Harness:
`scripts/vb6-legality.ps1 -CaptureOutput`, four batches (33/19/13/9 cases), **two** known-illegal guard cases
per batch — one placed second, one last — with all four batches reporting exactly `illegal 2`.

### An unsuffixed floating-point literal is a Double. Unconditionally.

The oracle had **no row for this at all**: every float-typed row above uses a *suffixed* literal (`1.5#`,
`42!`), so the unsuffixed case had never been measured. HexIDE assumed Double on the strength of a source
comment (`ExpressionExecutor.cs:333`). The assumption is correct — this row makes it a measurement.

| probe | type |
|---|---|
| `TypeName(1.5)` / `TypeName(0.1)` / `TypeName(100.0)` | Double |
| `TypeName(.5)` — leading-dot form | Double |
| `TypeName(1E10)` — exponent, no decimal point | Double |
| `TypeName(1.5E10)` / `TypeName(1E-5)` | Double |
| `TypeName(3.402823E38)` — exactly Single's maximum | Double |
| `TypeName(1.7E308)` / `TypeName(1E39)` | Double — and both **compile**, which they could not if the literal were Single |
| `TypeName(-1.5)` | Double |
| `TypeName(1.5 + 1.5)` / `TypeName(1.5 / 2)` | Double |
| `Dim v: v = 1.5: TypeName(v)` | Double |
| `TypeName(1.5!)` / `TypeName(1.5#)` — positive controls | Single / Double |

It does not depend on magnitude, on the presence of a decimal point, on significant-digit count (18 digits is
still Double, silently truncated to Double precision), on unary minus, or on assignment into a Variant.

**Why the type name alone was not trusted.** `TypeName` cannot distinguish *parsed as Double* from *parsed as
Single, then widened*, so three independent probes ran alongside it:

| probe | result | what it proves |
|---|---|---|
| `Debug.Print 1.2345678901234 * 1` | `1.2345678901234` | 14 significant digits survive; a Single would have collapsed to ~7 |
| `Debug.Print 0.1 + 0.2 = 0.3` | **False** | the Double answer — Single rounding would have hidden the discrepancy |
| bare `Debug.Print 1.5` through a `ByVal Variant` helper | type `Double` | agrees without the program ever naming `TypeName`, and correctly reports `Single` for `1.5!` |

The legality row is the cleanest of the four, because it does not depend on `TypeName` at all: `1.7E308` and
`1E39` are compile-time overflows if the literal is a Single, and they compile.

### Comparing a Double with a Single happens at SINGLE precision

Found while confirming the above: `0.1 = CSng(0.1)` returns **True**, where a Double reading demands False.
That is not a defect in literal typing, and it is not a tolerance — it is a separate rule.

> **A `Double`-vs-`Single` comparison rounds the Double operand to Single first, then compares exactly.**
> `d = s` behaves as `CSng(d) = s`.

Exact rounding, not a tolerance, and one row settles which:

| probe | result |
|---|---|
| `Dim d As Double, s As Single: d = 0.1: s = 0.1: (d = s)` | True |
| `d = 0.100000006: s = 0.1: (d = s)` | **False** — the operands are only `4.5E-09` apart, inside a Single ulp at that magnitude |
| `CDbl(CSng(0.1))` | `0.100000001490116` |
| `CDbl(CSng(0.100000006))` | `0.100000008940697` — a *different* Single, which is exactly why the row above is False |
| `(0.1 = 0.1!)` — no conversion function anywhere in the expression | True |
| `(d = CDbl(s))` — an explicit `CDbl` on the Single operand | **False** — widening it by hand defeats the rule |

Measured scope rather than assumed: it holds for `=`, `<`, `>`, `>=` and `<>`, in both operand orders, for
literals and for declared variables alike, and on the **Variant** path — which is the shape HexIDE's
interpreter actually evaluates. It is *not* a general "compare at the narrower type" rule: `Double` vs
`Integer` and `Double` vs `Long` compare as expected (`1.4 = 1` is False).

**The asymmetry is the part worth remembering.** Arithmetic does the opposite:
`TypeName(0.1 - CSng(0.1))` is **Double** and retains the full `-1.49011611383365E-09` — folded and at run
time identically. **VB6 widens for arithmetic and narrows for comparison.**

**Recorded as resisting explanation.** No mechanism is offered here for that asymmetry, only a model that fits
all 20 relevant rows. Per this file's standing rule it is written down as measured rather than tidied into a
plausible rule — an honest "measured, no rule I would defend" beats a laundered generalisation, which has
already cost this project one silent bug.

Constant folding is **exonerated rather than implicated**: the folded and run-time forms agree to the digit,
and the effect reproduces with no conversion function present in the expression at all.

### Double vs Currency is the ladder, not a second anomaly

`TypeName(0.100001 - CCur(0.1))` is **Currency**, and `Double`-vs-`Currency` comparison likewise happens at
Currency's 4-decimal scale (`0.10001 = CCur(0.1)` True, `0.1001 = CCur(0.1)` False). This is **not** a second
anomaly — it is precisely the ladder already recorded under *Arithmetic result types* above, in which Currency
and Decimal sit **above** Double. Comparison simply follows the coercion.

It is recorded next to the Single case only to keep the two apart: with Currency, arithmetic and comparison
agree with each other. With Single, they do not.

### Consequences for the interpreter

`ExpressionExecutor.GetTwoValuesSameTypesOrNull` uses **one** widening ladder for both arithmetic and
comparison, and its `NumericRank` ladder is `Byte < Integer < Long < Single < Currency < Decimal < Double`.
Two divergences follow, both in the wrong-value class:

- **Comparison never narrows.** `0.1 = CSng(0.1)` is True in VB6 and False in HexIDE.
- **`NumericRank` contradicts the ladder recorded in this very file.** The measured ladder puts Currency and
  Decimal *above* Double; the code puts Double at the top. So `Double + Currency` yields Currency in VB6 and
  Double in HexIDE, and likewise for Decimal.

The second is the more uncomfortable of the two: it was never an unmeasured assumption. The correct ladder has
been sitting in *Arithmetic result types* since the first oracle pass, and the code simply does not implement
it. A measured fact that no test asserts is only marginally better than an unmeasured one.
