# VB6 Language Surface — What HexIDE Runs

> **Scope.** Every built-in of the VB6 *language*: statements, functions, operators, keywords and
> modifiers, literals and type names, compiler directives, intrinsic constants, and the in-box objects a
> program can touch without adding a reference. **1182 names**, each with HexIDE's current support level.
>
> **Sibling docs.** This is the *positive* inventory — what runs. The *gap catalogue*, which classifies
> why something is missing (Deferred / Walled / Platform / Partial), is
> [`interpreter-gaps.md`](interpreter-gaps.md); where they disagree, that one is authoritative on
> classification and this one on coverage. Verified runtime *semantics* live in
> [`vb6-fidelity-oracle.md`](vb6-fidelity-oracle.md). Debugger divergences are in
> [`debugger-vb6-divergences.md`](debugger-vb6-divergences.md). Permanently excluded features are in
> [`OUT_OF_SCOPE.md`](OUT_OF_SCOPE.md), and the IDE surface (menus, windows, designer) is
> [`MISSING_FEATURES.md`](MISSING_FEATURES.md).
>
> **Living document.** Update a row the moment its status changes.

## How to read this

The statuses are ordered by what a user experiences when they press **F5**, because that — not semantic
fidelity — is what decides whether someone keeps the IDE open. A missing statement is a cliff; a rounding
divergence is a footnote.

| Status | What happens on F5 | Why it ranks here |
|---|---|---|
| **Won't load** | The module never opens | Nothing runs. The user cannot even look around. Worst outcome available. |
| **Dies** | Parses, then throws partway through | The program gets going and falls off a cliff. Second worst. |
| Partial | Runs, with a real restriction | Fine *if the restriction is written down* — which is what the Detail column is for. |
| No-op | Accepted and deliberately ignored | Acceptable only where ignoring it cannot mislead. See below. |
| Supported | Works for the ordinary case | |

**Not every absence is safe to demote into No-op.** `DoEvents` returning 0 without pumping messages
withholds a behaviour and lies about nothing. `Print #` as a no-op would silently not write the user's
file — the same harm shape as a silently lossy save, which
[`serialization-outcomes.md`](serialization-outcomes.md) rules out entirely. So the question for each gap
is not only *how expensive to close* but *can it be degraded honestly in the meantime*.

`Beep` is the cautionary tale in the other direction: it sat as a no-op justified by cross-platform
concerns until someone checked, and the justification was simply wrong — `Console.Beep()` is unsupported
only on platforms HexIDE does not ship to. It is now implemented. Before accepting a No-op row here,
check whether the reason still holds.

## Two things this document is not

**Nothing here is `vb6.exe`-verified.** The classification ran while the oracle VM was unreachable, so
every row is derived from reading HexIDE's own source, not from measuring real VB6. That is sound for
*does it run* — a `NotImplementedException` is a fact about our code — but it is **not** authority on
*does it behave correctly*. Where a Partial row describes a semantic difference, treat it as a lead to
verify, not a measurement. Verified behaviour only ever lives in
[`vb6-fidelity-oracle.md`](vb6-fidelity-oracle.md).

**"Supported" is an optimistic ceiling.** Every row was attacked by an adversarial pass whose only job
was to disprove it. Of the supported rows it reached, **217 were demoted to Partial** — roughly a third.
It did not reach all of them. So read Supported as *no one has yet shown this broken*, and expect the
real figure to be lower.

## At a glance

| Category | Won't load | Dies | Partial | No-op | Supported | Total |
|---|---:|---:|---:|---:|---:|---:|
| Statements | 0 | 47 | 35 | 0 | 23 | 105 |
| Operators | 0 | 3 | 18 | 0 | 11 | 32 |
| Intrinsic functions | 0 | 70 | 37 | 1 | 45 | 153 |
| Keywords and modifiers | 0 | 11 | 22 | 1 | 17 | 51 |
| Literals, types and suffixes | 3 | 7 | 17 | 0 | 23 | 50 |
| Compiler directives and options | 1 | 16 | 9 | 13 | 8 | 47 |
| In-box objects | 0 | 98 | 8 | 0 | 17 | 123 |
| Intrinsic constants | 0 | 8 | 261 | 0 | 352 | 621 |
| **Total** | **4** | **260** | **407** | **15** | **496** | **1182** |

### The single most useful number here is 4

**Only 4 of 1182 constructs fail to parse.** Every other gap — all 260 of them — parses cleanly and then
throws at run time. The grammar comprehends essentially the whole of VB6; what is missing is execution.

That matters for three reasons. It is the empirical form of the claim in `CLAUDE.md` that no VB6
construct is incomprehensible to the CST, which until now was an architectural assertion rather than a
measurement. It means a user opening real VB6 source will nearly always *see* their code, correctly
highlighted, with working navigation, even where it will not run — a far better first impression than a
file that refuses to open. And it means the remaining work is almost entirely additive: implementing a
visitor, not changing how the language is understood.

### Where the damage actually is

**Operators are nearly complete** — 3 absent of 32 — so expressions and arithmetic generally work. The
damage is concentrated in **statements** and the **in-box object model**: 45% of VB6's statements and
80% of the in-box surface (`App`, `Screen`, `Printer`, `Clipboard`, `Collection`) are unavailable.

That is the honest answer to "will my code run?" — the arithmetic will; the plumbing around it often will
not. The classic file-I/O family is absent as a block, which is the most common way a real program dies
here, usually within its first few statements.

## Won't load — 4 names

The whole list. A parse failure takes the entire module down — nothing in the file runs and the editor
cannot open it usefully — so despite how obscure these look, each one has a blast radius of a file rather
than a statement.

Two of the four are the same construct counted twice (a numeric line label, `10 Debug.Print 1`, which the
enumeration recorded under two names). So in practice there are **three** parse-level gaps in the entire
VB6 language: numeric line labels, the `D` exponent marker (`1.5D2`), and `#Const`.

Worth noting what is *not* here: `#If` parses fine and throws, and so does the named-argument operator
`:=` — both are execution gaps rather than grammar gaps, which is the cheaper kind to close.

| Name | Status | Detail | Source |
|---|---|---|---|
| `#Const` | **Won't load** | VB6.g4 has no #Const lexer token and no parser rule; the only HASH-prefixed directive tokens are MACRO_IF/MACRO_ELSEIF/MACRO_ELSE/MACRO_END_IF (VB6.g4:1479-1496). '#Const' instead lexes as… | IDE/HexIDE.Runtime/Interpreter/Grammar/VB6.g4:2062 |
| `DExponentLiteral` | **Won't load** | `DOUBLELITERAL` and `INTEGERLITERAL` accept only an `e`/`E` exponent marker, never `D`. Measured: `Debug.Print 1.5D2` -> "Compile error: mismatched input 'D2' expecting <EOF>". | IDE/HexIDE.Runtime/Interpreter/Grammar/VB6.g4:2059 |
| `Line number` | **Won't load** | Grammar-level gap - does not parse. lineLabel requires an ambiguousIdentifier (IDENTIFIER \| ambiguousKeyword), and INTEGERLITERAL is neither, so a numeric label has no production… | IDE/HexIDE.Runtime/Interpreter/Grammar/VB6.g4:789 |
| `LineNumber` | **Won't load** | at all — a grammar-level gap. `lineLabel : ambiguousIdentifier COLON` and `ambiguousIdentifier : (IDENTIFIER \| ambiguousKeyword) +` with `IDENTIFIER : LETTER LETTERORDIGIT*`, so a bare… | IDE/HexIDE.Runtime/Interpreter/Grammar/VB6.g4:789 |

## Dies mid-run — 260 names

These parse. The program starts, reaches the statement, and throws. Grouped by area below so the clusters
are visible — and they are very clustered: the classic file-I/O family is absent as a block, and so is
most of the in-box object model.

| Name | Status | Detail | Source |
|---|---|---|---|
| `! (dictionary access)` | **Dies** | throws `"dictionaryCallStmt is not supported"` — The grammar yields the pair (`dictionaryCallStmt : EXCLAMATIONMARK ambiguousIdentifier typeHint?`) but every consuming site throws. Measured on a… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:450 |
| `#` | **Dies** | and it is the one piece of this batch that fails even outside a file statement. `#1` lexes as its own token, `FILENUMBER : HASH LETTERORDIGIT+` (Grammar/VB6.g4:2063-2065), and FILENUMBER is… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:369 |
| `#Else` | **Dies** | throws. macroElseBlockStmt (VB6.g4:397-399) is part of macroIfThenElseStmt and shares its fate: measured, '#If Win32 Then ... #Else ... #End If' raises VBCompileErrorException 'Conditional… | PrePass.cs:227 |
| `#ElseIf` | **Dies** | throws. macroElseIfBlockStmt (VB6.g4:393-395) is only reachable as part of macroIfThenElseStmt, so it fails with its parent: VBCompileErrorException 'Conditional compilation (#If / #Const)… | PrePass.cs:227 |
| `#End If` | **Dies** | throws, as the terminator of macroIfThenElseStmt (VB6.g4:385-387) - same two exceptions as #If (PrePass.cs:227 at module level, StatementExecutor.cs:1045 inside a procedure). | PrePass.cs:227 |
| `#If` | **Dies** | throws `"Conditional compilation (#If / #Const) is not supported"` — throws - with two different exceptions depending on where it sits. At module level PrePass hits it first: '' (PrePass.cs:227), under the comment… | PrePass.cs:227 |
| `#n` | **Dies** | `FILENUMBER : HASH LETTERORDIGIT +` is a lexer token listed in the `literal` rule — then throws at run time, because `VisitVsLiteralCore` has no FILENUMBER branch and falls into `throw new… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:369 |
| `$` | **Dies** | There is NO literal form for `$` at all (a String literal carries no suffix), so every occurrence is an identifier or function type hint and every one throws. Measured: `Debug.Print s$` ->… | IDE/HexIDE.Runtime/Interpreter/VB6BuiltIns.Strings.cs:9 |
| `,` | **Dies** | The 14-column print-zone comma is grammar-level only: `outputList : outputList_Expression (WS? (SEMICOLON \| COMMA) WS? outputList_Expression?)* \| outputList_Expression? (WS? (SEMICOLON \|… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1105 |
| `:= (named argument)` | **Dies** | throws `"Assign is not implemented"` — `implicitCallStmt_InStmt WS? ASSIGN WS? valueStmt # vsAssign` with `ASSIGN : ':='` — then throws at run time: `public override Task<object?>… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:1014 |
| `;` | **Dies** | The suppress-newline / adjacent-item semicolon is in `outputList` (Grammar/VB6.g4) and nothing consumes it - probed `Print #1, "a"; "b"` -> NotImplementedException "Print not implemented". | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1105 |
| `Access` | **Dies** | ACCESS is a real lexer token and the clause is in the grammar - `openStmt : OPEN WS valueStmt WS FOR WS (APPEND\|BINARY\|INPUT\|OUTPUT\|RANDOM) (WS ACCESS WS (READ\|WRITE\|READ_WRITE))?… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1100 |
| `AddressOf` | **Dies** | throws `"ADDRESSOF is not implemented"` — (`ADDRESSOF WS valueStmt # vsAddressOf`) then throws at run time: `public override Task<object?> VisitVsAddressOf(VB6Parser.VsAddressOfContext… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:957 |
| `AmbientProperties` | **Dies** | Measured for `Dim a As AmbientProperties`: VBCompileErrorException: "User-defined type not defined: AmbientProperties" (StatementExecutor.cs:1545). The route to a real instance is gone too:… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `AmbientProperties.BackColor` | **Dies** | Unreachable: there is no `Ambient` and no `UserControl` global to read it from — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" and "Variable not defined… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `AmbientProperties.DisplayAsDefault` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `AmbientProperties.DisplayName` | **Dies** | Measured directly: `Debug.Print UserControl.Ambient.DisplayName` gives VBVariableNotDefinedException: "Variable not defined (UserControl)" (ExpressionExecutor.cs:434). The property name… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `AmbientProperties.Font` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `AmbientProperties.ForeColor` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `AmbientProperties.LocaleID` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `AmbientProperties.MessageReflect` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `AmbientProperties.Palette` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `AmbientProperties.RightToLeft` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `AmbientProperties.ScaleUnits` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `AmbientProperties.ShowGrabHandles` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `AmbientProperties.ShowHatching` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `AmbientProperties.SupportsMnemonics` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `AmbientProperties.TextAlign` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `AmbientProperties.UIDead` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `AmbientProperties.UserMode` | **Dies** | Measured directly: `Debug.Print Ambient.UserMode` gives VBVariableNotDefinedException: "Variable not defined (Ambient)" and `Debug.Print UserControl.Ambient.UserMode` gives "Variable not… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `App.HelpFile` | **Dies** | the name is absent from VbApp.TryGetProperty's switch, so the read falls through to ExpressionExecutor.cs:557. Measured verbatim: "Compile error: Method or data member not found (HelpFile… | VbApp.cs:123-148 |
| `App.LogEvent` | **Dies** | VbApp.Call has no dispatch at all - VbApp.cs:112-113 is: public void Call(string method, List<Vb6Value> args) => throw new VBRunTimeException(VBStandardError.MethodOrDataMemberNotFound… | VbApp.cs:112 |
| `App.LogMode` | **Dies** | the name is absent from VbApp.TryGetProperty's switch, so the read falls through to ExpressionExecutor.cs:557. Measured verbatim: "Compile error: Method or data member not found (LogMode in… | VbApp.cs:123-148 |
| `App.LogPath` | **Dies** | the name is absent from VbApp.TryGetProperty's switch, so the read falls through to ExpressionExecutor.cs:557. Measured verbatim: "Compile error: Method or data member not found (LogPath in… | VbApp.cs:123-148 |
| `App.StartLogging` | **Dies** | VbApp.Call has no dispatch at all - VbApp.cs:112-113 is: public void Call(string method, List<Vb6Value> args) => throw new VBRunTimeException(VBStandardError.MethodOrDataMemberNotFound… | VbApp.cs:112 |
| `App.StartMode` | **Dies** | the name is absent from VbApp.TryGetProperty's switch, so the read falls through to ExpressionExecutor.cs:557. Measured verbatim: "Compile error: Method or data member not found (StartMode… | VbApp.cs:123-148 |
| `App.TaskVisible` | **Dies** | the name is absent from VbApp.TryGetProperty's switch, so the read falls through to ExpressionExecutor.cs:557. Measured verbatim: "Compile error: Method or data member not found… | VbApp.cs:123-148 |
| `AppActivate` | **Dies** | throws `"AppActivate not implemented"` — Grammar rule `appActivateStmt : APPACTIVATE WS valueStmt (WS? COMMA WS? valueStmt)?` (VB6.g4:218-220); the visitor body is a single throw:… | StatementExecutor.cs:314 |
| `Append` | **Dies** | APPEND is a lexer token and an openStmt mode alternative (Grammar/VB6.g4:425-427). Probed `Open "z.txt" For Append As #1` -> NotImplementedException "Open not implemented". | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1100 |
| `AscB` | **Dies** | No registry entry: BuildRegistry (VB6BuiltIns.cs:768-783) calls RegisterStrings/Conversion/Math/Array/Inspection/DateTime/Format and adds only DoEvents; grep for "AscB" across… | ExpressionExecutor.cs:640; registry at VB6BuiltIns.cs:768 |
| `AscW` | **Dies** | No registry entry (VB6BuiltIns.cs:768; no occurrence of "AscW" anywhere under IDE/HexIDE.Runtime). Reaches `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not… | ExpressionExecutor.cs:640; registry at VB6BuiltIns.cs:768 |
| `AsyncProperty` | **Dies** | Measured for `Dim a As AsyncProperty`: VBCompileErrorException: "User-defined type not defined: AsyncProperty" (StatementExecutor.cs:1545). The name appears nowhere in the repository. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `AsyncProperty_VB5` | **Dies** | Measured for `Dim a As AsyncProperty_VB5`: VBCompileErrorException: "User-defined type not defined: AsyncProperty_VB5" (StatementExecutor.cs:1545). | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `CallByName` | **Dies** | Zero occurrences anywhere in HexIDE.Runtime; not registered by any Register* partial, so BuildRegistry has no entry. The call reaches ExpressionExecutor's final fallthrough and throws… | ExpressionExecutor.cs:640 |
| `Choose` | **Dies** | Not in BuildRegistry - a grep of every d["..."] registration across all VB6BuiltIns partials returns no Choose. `x = Choose(i, "a", "b")` throws VBSubOrFunctionNotDefinedException: "Sub or… | ExpressionExecutor.cs:640 |
| `ChrB` | **Dies** | No registry entry (VB6BuiltIns.cs:768; no occurrence of "ChrB" under IDE/HexIDE.Runtime). `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (ChrB)". | ExpressionExecutor.cs:640; registry at VB6BuiltIns.cs:768 |
| `ChrW` | **Dies** | No registry entry (VB6BuiltIns.cs:768). `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (ChrW)". | ExpressionExecutor.cs:640; registry at VB6BuiltIns.cs:768 |
| `Circle` | **Dies** | throws `"Only single element supported"` — fails at run time - but NOT with a name-resolution error. VB6.g4 has no CIRCLE token and no circleStmt rule, so `Circle (100, 100), 50` parses as a… | ExpressionExecutor.cs:962 |
| `Clipboard` | **Dies** | never seeded. BasicInterpreter.cs:299-301 seeds only Debug, Err and App as program globals. Measured verbatim: "Compile error: Variable not defined (Clipboard)". | BasicInterpreter.cs:299-301 |
| `Clipboard.Clear` | **Dies** | Measured (statement position): a VBRunTimeException from StatementExecutor.cs:1755 - "Unknown method Clear on <Right(EmptyVariant)>()" - because the Clipboard lead resolves to Empty, so… | StatementExecutor.cs:1755 |
| `Clipboard.GetData` | **Dies** | In expression position the member-chain lead is resolved by ExpressionExecutor.cs:429-434, which throws before the member is ever looked at. Measured verbatim for Clipboard.GetText() and… | ExpressionExecutor.cs:434 |
| `Clipboard.GetFormat` | **Dies** | In expression position the member-chain lead is resolved by ExpressionExecutor.cs:429-434, which throws before the member is ever looked at. Measured verbatim for Clipboard.GetText() and… | ExpressionExecutor.cs:434 |
| `Clipboard.GetText` | **Dies** | In expression position the member-chain lead is resolved by ExpressionExecutor.cs:429-434, which throws before the member is ever looked at. Measured verbatim for Clipboard.GetText() and… | ExpressionExecutor.cs:434 |
| `Clipboard.SetData` | **Dies** | Measured verbatim: "Run-time error: Unknown method SetData on <Right(EmptyVariant)>()". | StatementExecutor.cs:1755 |
| `Clipboard.SetText` | **Dies** | Measured verbatim: "Run-time error: Unknown method SetText on <Right(EmptyVariant)>()". | StatementExecutor.cs:1755 |
| `Close` | **Dies** | `closeStmt : CLOSE (WS valueStmt (WS? COMMA WS? valueStmt)*)?` (Grammar/VB6.g4:234-236) and a visitor exists, but its whole body is the throw. Probed both `Close #1` and bare `Close` ->… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:395 |
| `Cls` | **Dies** | but is not implemented, and FAILS SILENTLY in the bare form. VB6.g4 has no CLS token and no clsStmt rule, so `Cls` parses as a zero-argument bare procedure call. VisitICS_B_ProcedureCall… | StatementExecutor.cs:1802 |
| `Collection` | **Dies** | COLLECTION is a real grammar token (VB6.g4:1102) listed in baseType (VB6.g4:749), so `Dim c As Collection` reaches BaseTypeMapper.Map, which returns null for it (BaseTypeMapper.cs:27… | IDE/HexIDE.Runtime/Interpreter/PrePass.cs:145 |
| `Collection._NewEnum` | **Dies** | The name lexes as an ordinary IDENTIFIER (VB6.g4:2082-2083; the LETTER fragment at :2110 includes `_`), and the bracketed form `[_NewEnum]` parses too — but `_NewEnum`/`NewEnum` appears… | IDE/HexIDE.Runtime/Interpreter/Exceptions.cs:53 |
| `Collection.Add` | **Dies** | No Collection object can exist (see Collection), and no Add handler exists on any runtime proxy. Measured with the call-statement form on a Variant holder: VBRunTimeException: "Unknown… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:623 |
| `Collection.Count` | **Dies** | Measured: VBMethodOrDataMemberNotFoundException: "Method or data member not found (Count in Right(EmptyVariant))". No Collection type exists to carry it. | IDE/HexIDE.Runtime/Interpreter/Exceptions.cs:53 |
| `Collection.Item` | **Dies** | Measured explicit form: VBMethodOrDataMemberNotFoundException: "Method or data member not found (Item(1) in Right(EmptyVariant))". The implicit default-member form `c(1)` fails differently… | IDE/HexIDE.Runtime/Interpreter/Exceptions.cs:53 |
| `Collection.Remove` | **Dies** | Measured: VBRunTimeException: "Unknown method Remove on <Right(EmptyVariant)>()" (StatementExecutor.cs:623). | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:623 |
| `Command` | **Dies** | but is not implemented. Not in BuildRegistry; the sole hit in the whole runtime is HexIDE.Runtime/Editor/VbKeywordNormalizer.cs:75 ("DoEvents", "Shell", "Environ", "Command"), which is… | ExpressionExecutor.cs:640 |
| `ContainedControls` | **Dies** | Measured for `Dim c As ContainedControls`: VBCompileErrorException: "User-defined type not defined: ContainedControls" (StatementExecutor.cs:1545). | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `Controls` | **Dies** | a form's Controls collection does not exist. Measured verbatim: "Compile error: Variable not defined (Controls)". A form binds only its own name and "Me" (VBLoader.cs:378-379); no… | VBLoader.cs:378-379 |
| `Controls.Add` | **Dies** | Measured verbatim: "Run-time error: Unknown method Add on <Right(EmptyVariant)>()". | StatementExecutor.cs:1755 |
| `Controls.Count` | **Dies** | Measured verbatim for Controls.Count: "Compile error: Variable not defined (Controls)". The same applies to Controls.Item(i); the default-member form Controls(i) instead gives "Sub or… | ExpressionExecutor.cs:434 |
| `Controls.Item` | **Dies** | Measured verbatim for Controls.Count: "Compile error: Variable not defined (Controls)". The same applies to Controls.Item(i); the default-member form Controls(i) instead gives "Sub or… | ExpressionExecutor.cs:434 |
| `Controls.Remove` | **Dies** | Measured pattern, identical to Controls.Add: "Run-time error: Unknown method Remove on <Right(EmptyVariant)>()". | StatementExecutor.cs:1755 |
| `CreateObject` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. `Set o = CreateObject("Excel.Application")` throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined… | ExpressionExecutor.cs:640 |
| `CurDir` | **Dies** | fails at name resolution: not registered in BuildRegistry (VB6BuiltIns.cs:768-783 registers Strings/Conversion/Math/Array/Inspection/DateTime/Format + DoEvents only). Probed `Debug.Print… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:640 |
| `CVar` | **Dies** | Not registered in BuildRegistry (VB6BuiltIns.cs:768) or any RegisterXxx partial — the only hits anywhere in IDE/ are editor metadata. A call reaches ExpressionExecutor.cs:639 and throws… | VB6BuiltIns.cs:768 |
| `CVDate` | **Dies** | Zero hits anywhere in IDE/ outside obj/bin — not registered (VB6BuiltIns.cs:768), not in Grammar/VB6.g4, not in VbKeywordNormalizer.cs, not in VbSignatures.cs. A call reaches… | VB6BuiltIns.cs:768 |
| `CVErr` | **Dies** | Not registered in BuildRegistry (VB6BuiltIns.cs:768) or any RegisterXxx partial, so name resolution falls through local arrays and user procedures to the intrinsic registry, gets null back… | VB6BuiltIns.cs:768 |
| `DataBinding` | **Dies** | Measured for `Dim d As DataBinding`: VBCompileErrorException: "User-defined type not defined: DataBinding" (StatementExecutor.cs:1545). The name appears nowhere in the runtime. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `DataBindings` | **Dies** | Measured for `Dim d As DataBindings`: VBCompileErrorException: "User-defined type not defined: DataBindings" (StatementExecutor.cs:1545). The name appears nowhere in the runtime. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `DataMembers` | **Dies** | Measured for `Dim d As DataMembers`: VBCompileErrorException: "User-defined type not defined: DataMembers" (StatementExecutor.cs:1545). The name appears nowhere in the repository. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `DataObject` | **Dies** | `DataObject` is not a grammar keyword, so it takes the complexType path and throws VBCompileErrorException: "User-defined type not defined: DataObject" — measured for `Dim d As DataObject`… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `DataObject.Clear` | **Dies** | Measured: VBRunTimeException: "Unknown method Clear on <Right(EmptyVariant)>()" (StatementExecutor.cs:623). No DataObject type exists to own it. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:623 |
| `DataObject.Files` | **Dies** | Measured: VBMethodOrDataMemberNotFoundException: "Method or data member not found (Files in Right(EmptyVariant))". | IDE/HexIDE.Runtime/Interpreter/Exceptions.cs:53 |
| `DataObject.GetData` | **Dies** | Measured: VBMethodOrDataMemberNotFoundException: "Method or data member not found (GetData(1) in Right(EmptyVariant))". | IDE/HexIDE.Runtime/Interpreter/Exceptions.cs:53 |
| `DataObject.GetFormat` | **Dies** | Measured: VBMethodOrDataMemberNotFoundException: "Method or data member not found (GetFormat(1) in Right(EmptyVariant))". | IDE/HexIDE.Runtime/Interpreter/Exceptions.cs:53 |
| `DataObject.SetData` | **Dies** | Measured: VBRunTimeException: "Unknown method SetData on <Right(EmptyVariant)>()" (StatementExecutor.cs:623). | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:623 |
| `DataObjectFiles` | **Dies** | Measured for `Dim f As DataObjectFiles`: VBCompileErrorException: "User-defined type not defined: DataObjectFiles" (StatementExecutor.cs:1545). The name appears nowhere in the repository. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `Date` | **Dies** | throws `"Date not implemented"` — The grammar has a dedicated rule `dateStmt : DATE WS? EQ WS? valueStmt` (VB6.g4:246-248), listed in the block alternatives at VB6.g4:153 ahead of… | StatementExecutor.cs:415 |
| `DDB` | **Dies** | Not registered in BuildRegistry (VB6BuiltIns.cs:768) or any RegisterXxx partial; the call resolves to no local array, no user procedure and no intrinsic, so ExpressionExecutor.cs:639 throws… | VB6BuiltIns.cs:768 |
| `Debug.Assert` | **Dies** | as an ICS_B_MemberProcedureCall and throws at run time. DebugProxy.Call handles only "Print"; everything else hits `throw new Exception("No method named " + method)`. Measured verbatim:… | IDE/HexIDE.Runtime/Interpreter/DebugProxy.cs:24 |
| `DefBool` | **Dies** | throws `"Deftype not implemented"` — (grammar rule `deftypeStmt` at VB6.g4:254-256, a blockStmt so it reaches the module's top-level block) and then throws at run time: Measured:… | StatementExecutor.cs:425-428 (throw at 427) |
| `DefByte` | **Dies** | throws `"Deftype not implemented"` — the single shared VisitDeftypeStmt covers every Def* token (DEFBOOL\|DEFBYTE\|DEFINT\|DEFLNG\|DEFCUR\|DEFSNG\|DEFDBL\|DEFDEC\|DEFDATE\|DEFSTR\|DEFOBJ\… | StatementExecutor.cs:425-428 (throw at 427) |
| `DefCur` | **Dies** | throws `"Deftype not implemented"` | StatementExecutor.cs:425-428 (throw at 427) |
| `DefDate` | **Dies** | throws `"Deftype not implemented"` | StatementExecutor.cs:425-428 (throw at 427) |
| `DefDbl` | **Dies** | throws `"Deftype not implemented"` | StatementExecutor.cs:425-428 (throw at 427) |
| `DefDec` | **Dies** | throws `"Deftype not implemented"` — (DEFDEC is a lexer token and appears in the deftypeStmt alternation) and then | StatementExecutor.cs:425-428 (throw at 427); token in VB6.g4:255 and… |
| `DefInt` | **Dies** | throws `"Deftype not implemented"` — Measured: `DefInt A-Z` -> that exact message. | StatementExecutor.cs:425-428 (throw at 427) |
| `DefLng` | **Dies** | throws `"Deftype not implemented"` | StatementExecutor.cs:425-428 (throw at 427) |
| `DefObj` | **Dies** | throws `"Deftype not implemented"` | StatementExecutor.cs:425-428 (throw at 427) |
| `DefSng` | **Dies** | throws `"Deftype not implemented"` | StatementExecutor.cs:425-428 (throw at 427) |
| `DefStr` | **Dies** | throws `"Deftype not implemented"` — Measured: `DefStr S` -> that exact message. | StatementExecutor.cs:425-428 (throw at 427) |
| `DefVar` | **Dies** | throws `"Deftype not implemented"` | StatementExecutor.cs:425-428 (throw at 427) |
| `DeleteSetting` | **Dies** | throws `"DeleteSetting not implemented"` — Grammar rule `deleteSettingStmt : DELETESETTING WS valueStmt WS? COMMA WS? valueStmt (WS? COMMA WS? valueStmt)?` (VB6.g4:258-260) with a real… | StatementExecutor.cs:420 |
| `Dir` | **Dies** | fails at name resolution: unregistered in every VB6BuiltIns partial. Probed `Debug.Print Dir("*.*")` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:640 |
| `End` | **Dies** | throws `"End not implemented"` — (endStmt, VB6.g4:268) and throws at run time. Verbatim: Measured: `Debug.Print 1 / End` -> NotImplementedException: End not implemented. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:521 |
| `Environ` | **Dies** | Not in BuildRegistry; the only runtime hit is the highlighter list VbKeywordNormalizer.cs:75. `Environ("PATH")` throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined… | ExpressionExecutor.cs:640 |
| `EOF` | **Dies** | fails at name resolution: unregistered. Probed `Debug.Print EOF(1)` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined (EOF)". docs/interpreter-gaps.md:83. | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:640 |
| `Erl` | **Dies** | but is not in the intrinsic registry and never throws. BuildRegistry (VB6BuiltIns.cs:768) registers no 'Erl'; grep over IDE/HexIDE.Runtime finds no ERL token and no Erl handler. An… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:466 |
| `Err.HelpContext` | **Dies** | TryGetProperty (VbErr.cs:68) has cases only for number/description/source; the fall-through in ExpressionExecutor then requires a Control and otherwise throws… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:556 |
| `Err.HelpFile` | **Dies** | Measured: `Debug.Print Err.HelpFile` -> VBMethodOrDataMemberNotFoundException, "Method or data member not found (HelpFile in Right(CSharpProxyObject))". No case in… | IDE/HexIDE.Runtime/Interpreter/VbErr.cs:68 |
| `Err.LastDllError` | **Dies** | Measured verbatim: "Method or data member not found (LastDllError in Right(CSharpProxyObject))". | IDE/HexIDE.Runtime/Interpreter/VbErr.cs:68 |
| `ErrObject` | **Dies** | as a complexType, throws at run time. BaseTypeMapper.Map has no ErrObject case and it is not a user class/UDT/Enum, so DeclareLocal falls to the final else. Measured verbatim:… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1544 |
| `Error` | **Dies** | `Error(n)` (the function returning an error's message text) is not in BuildRegistry. Measured: `Debug.Print Error(6)` -> VBSubOrFunctionNotDefinedException. `Debug.Print Error$(6)` ->… | IDE/HexIDE.Runtime/Interpreter/VB6BuiltIns.cs:768 |
| `EventInfo` | **Dies** | Measured for `Dim e As EventInfo`: VBCompileErrorException: "User-defined type not defined: EventInfo" (StatementExecutor.cs:1545). The name appears nowhere in the runtime. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `EventParameter` | **Dies** | Measured for `Dim e As EventParameter`: VBCompileErrorException: "User-defined type not defined: EventParameter" (StatementExecutor.cs:1545). The name appears nowhere in the runtime. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `EventParameters` | **Dies** | Measured for `Dim e As EventParameters`: VBCompileErrorException: "User-defined type not defined: EventParameters" (StatementExecutor.cs:1545). The name appears nowhere in the repository. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `FileAttr` | **Dies** | fails at name resolution: unregistered. Probed `Debug.Print FileAttr(1, 1)` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined (FileAttr)"… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:640 |
| `FileCopy` | **Dies** | `filecopyStmt : FILECOPY WS valueStmt WS? COMMA WS? valueStmt` (Grammar/VB6.g4:304-306); the visitor body is the throw. Probed `FileCopy "a.txt", "b.txt"` -> NotImplementedException:… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:632 |
| `FileDateTime` | **Dies** | fails at name resolution: unregistered. Probed `Debug.Print FileDateTime("a.txt")` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined (FileDateTime)"… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:640 |
| `FileLen` | **Dies** | fails at name resolution: unregistered. Probed `Debug.Print FileLen("a.txt")` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined (FileLen)"… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:640 |
| `FormatCurrency` | **Dies** | RegisterFormat registers exactly one name — `d["Format"]` (VB6BuiltIns.Format.cs:18-21); no occurrence of "FormatCurrency" under IDE/HexIDE.Runtime. `throw new… | ExpressionExecutor.cs:640; VB6BuiltIns.Format.cs:18-21 |
| `FormatDateTime` | **Dies** | Not registered (VB6BuiltIns.Format.cs:18-21 registers only "Format"); no occurrence under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not… | ExpressionExecutor.cs:640; VB6BuiltIns.Format.cs:18-21 |
| `FormatNumber` | **Dies** | Not registered (VB6BuiltIns.Format.cs:18-21); no occurrence under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — message "Sub or Function not defined… | ExpressionExecutor.cs:640; VB6BuiltIns.Format.cs:18-21 |
| `FormatPercent` | **Dies** | Not registered (VB6BuiltIns.Format.cs:18-21); no occurrence under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (FormatPercent)". | ExpressionExecutor.cs:640; VB6BuiltIns.Format.cs:18-21 |
| `Forms` | **Dies** | the loaded-forms collection is never built. Measured verbatim: "Compile error: Variable not defined (Forms)". BasicInterpreter.cs:299-301 seeds only Debug, Err and App as program globals. | BasicInterpreter.cs:299-301 |
| `Forms.Count` | **Dies** | Measured verbatim: "Compile error: Variable not defined (Forms)". | ExpressionExecutor.cs:434 |
| `Forms.Item` | **Dies** | Measured verbatim for the default-member form Forms(0).Caption: "Compile error: Sub or Function not defined (Forms)" - a parenthesised lead is routed to EvaluateProcedureOrArrayCall, which… | ExpressionExecutor.cs:637 |
| `FreeFile` | **Dies** | fails at name resolution: unregistered. Probed `Debug.Print FreeFile()` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined (FreeFile)"… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:640 |
| `FV` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs:768); ExpressionExecutor.cs:639 throws "Compile error:\n\nSub or Function not defined (FV)". Named in the deferral list at… | VB6BuiltIns.cs:768 |
| `Get` | **Dies** | `getStmt : GET WS valueStmt WS? COMMA WS? valueStmt? WS? COMMA WS? valueStmt` (Grammar/VB6.g4:320-322); the visitor body is the throw. Probed `Get #1, 1, v` -> NotImplementedException: "Get… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:727 |
| `GetAllSettings` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. Throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (GetAllSettings)". docs/interpreter-gaps.md:84: "Registry… | ExpressionExecutor.cs:640 |
| `GetAttr` | **Dies** | fails at name resolution: unregistered. Probed `Debug.Print GetAttr("a.txt")` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined (GetAttr)"… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:640 |
| `GetObject` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. Throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (GetObject)". docs/interpreter-gaps.md:96 pairs it with… | ExpressionExecutor.cs:640 |
| `GetSetting` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. Throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (GetSetting)". Same docs/interpreter-gaps.md:84 row as… | ExpressionExecutor.cs:640 |
| `GoSub` | **Dies** | throws `"GoSub not implemented"` — (goSubStmt, VB6.g4:324) and throws at run time. Verbatim: Measured. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:732 |
| `GoSub...Return` | **Dies** | throws `"GoSub not implemented"` — Both halves parse and both throw. GoSub: (StatementExecutor.cs:732). Return: `throw new NotImplementedException("Return not implemented")` (:1172)… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:732 |
| `Hyperlink` | **Dies** | Measured for `Dim h As Hyperlink`: VBCompileErrorException: "User-defined type not defined: Hyperlink" (StatementExecutor.cs:1545). The name appears nowhere in the runtime. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `IIf` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. `x = IIf(c, a, b)` throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (IIf)". docs/interpreter-gaps.md:52… | ExpressionExecutor.cs:640 |
| `IMEStatus` | **Dies** | but is not implemented. Not in BuildRegistry and zero occurrences anywhere in HexIDE.Runtime, including the editor-metadata files (VbSignatures.cs / VbKeywordNormalizer.cs). | ExpressionExecutor.cs:640 |
| `Input` | **Dies** | fails at name resolution: `Input` is in `ambiguousKeyword` (Grammar/VB6.g4) so `Input(n, f)` parses as a procedure-or-array call, but the name is unregistered. Probed `Debug.Print Input(5… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:640 |
| `Input #` | **Dies** | `inputStmt : INPUT WS valueStmt (WS? COMMA WS? valueStmt)+` (Grammar/VB6.g4:357-359); the visitor body is the throw. Probed `Input #1, s` -> NotImplementedException: "Input not implemented". | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:802 |
| `InputB` | **Dies** | fails at name resolution: a plain identifier (not a lexer keyword), unregistered. Probed `Debug.Print InputB(5, 1)` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:640 |
| `InStrB` | **Dies** | Not registered (VB6BuiltIns.cs:768); no occurrence of "InStrB" under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (InStrB)". | ExpressionExecutor.cs:640; registry at VB6BuiltIns.cs:768 |
| `IPmt` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs:768); ExpressionExecutor.cs:639 throws "Compile error:\n\nSub or Function not defined (IPmt)". Named in the deferral list at… | VB6BuiltIns.cs:768 |
| `IRR` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs:768); ExpressionExecutor.cs:639 throws "Compile error:\n\nSub or Function not defined (IRR)". Named in the deferral list at… | VB6BuiltIns.cs:768 |
| `Kill` | **Dies** | `killStmt : KILL WS valueStmt` (Grammar/VB6.g4:361-363); the visitor body is the throw. Probed `Kill "z.txt"` -> NotImplementedException: "Kill not implemented". | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:807 |
| `LeftB` | **Dies** | Not registered (VB6BuiltIns.cs:768); no occurrence of "LeftB" under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (LeftB)". | ExpressionExecutor.cs:640; registry at VB6BuiltIns.cs:768 |
| `LenB` | **Dies** | Not registered (VB6BuiltIns.cs:768); no occurrence of "LenB" under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (LenB)". | ExpressionExecutor.cs:640; registry at VB6BuiltIns.cs:768 |
| `LicenseInfo` | **Dies** | Measured for `Dim l As LicenseInfo`: VBCompileErrorException: "User-defined type not defined: LicenseInfo" (StatementExecutor.cs:1545). The name appears nowhere in the repository. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `Licenses` | **Dies** | Measured for `Debug.Print Licenses.Count`: VBVariableNotDefinedException: "Variable not defined (Licenses)" (ExpressionExecutor.cs:434) — the global object is not seeded; only Debug, Err… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `Like` | **Dies** | throws `"Like is not implemented"` — (`valueStmt WS LIKE WS valueStmt # vsLike`) then throws: `public override Task<object?> VisitVsLike(VB6Parser.VsLikeContext context) => Measured… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:1016 |
| `Line` | **Dies** | There is no standalone LINE token; `Line Input` is lexed as ONE token, `LINE_INPUT : L I N E ' ' I N P U T` (Grammar/VB6.g4:1454-1456), consumed by `lineInputStmt : LINE_INPUT WS valueStmt… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:990 |
| `Line Input #` | **Dies** | `lineInputStmt : LINE_INPUT WS valueStmt WS? COMMA WS? valueStmt` (Grammar/VB6.g4:369-371); the visitor body is the throw. Probed `Line Input #1, s` -> NotImplementedException: "LineInput… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:990 |
| `LoadPicture` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. `Set Picture1.Picture = LoadPicture("x.bmp")` throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined… | ExpressionExecutor.cs:640 |
| `LoadResData` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. Throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (LoadResData)". | ExpressionExecutor.cs:640 |
| `LoadResPicture` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. Throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (LoadResPicture)". | ExpressionExecutor.cs:640 |
| `LoadResString` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. Throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (LoadResString)". | ExpressionExecutor.cs:640 |
| `Loc` | **Dies** | fails at name resolution: unregistered. Probed `Debug.Print Loc(1)` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined (Loc)". docs/interpreter-gaps.md:83. | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:640 |
| `Lock` | **Dies** | `lockStmt : LOCK WS valueStmt (WS? COMMA WS? valueStmt (WS TO WS valueStmt)?)?` (Grammar/VB6.g4:377-379); the visitor body is the throw. Probed `Lock #1, 1 To 2` -> NotImplementedException:… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1035 |
| `LOF` | **Dies** | fails at name resolution: unregistered. Probed `Debug.Print LOF(1)` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined (LOF)". docs/interpreter-gaps.md:83. | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:640 |
| `LSet` | **Dies** | throws `"Lset not implemented"` — The grammar has the rule — `lsetStmt : LSET WS implicitCallStmt_InStmt WS? EQ WS? valueStmt` (VB6.g4:381-383), reachable from blockStmt (VB6.g4:178)… | StatementExecutor.cs:1038-1041 (grammar at Grammar/VB6.g4:381) |
| `Mac` | **Dies** | Not defined anywhere in the interpreter. Reachable only from #If, which throws first - measured with '#If Win32 Then ... #ElseIf Mac Then ... #End If': VBCompileErrorException 'Conditional… | PrePass.cs:227 |
| `Mid$` | **Dies** | `Mid$` lexes as MID + DOLLAR, and DOLLAR is a `typeHint` (VB6.g4:823-829) which `iCS_S_ProcedureOrArrayCall` accepts (VB6.g4:680). The letStmt handler rejects it BEFORE reaching the… | StatementExecutor.cs:933-937 |
| `MidB` | **Dies** | Not registered (VB6BuiltIns.cs:768); no occurrence of "MidB" under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (MidB)". | ExpressionExecutor.cs:640; registry at VB6BuiltIns.cs:768 |
| `MIRR` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs:768); ExpressionExecutor.cs:639 throws "Compile error:\n\nSub or Function not defined (MIRR)". Named in the deferral list at… | VB6BuiltIns.cs:768 |
| `MkDir` | **Dies** | `mkdirStmt : MKDIR WS valueStmt` (Grammar/VB6.g4:405-407); the visitor body is the throw. Probed `MkDir "zdir"` -> NotImplementedException: "Mkdir not implemented". | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1055 |
| `Name` | **Dies** | `nameStmt : NAME WS valueStmt WS AS WS valueStmt` (Grammar/VB6.g4:409-411); the visitor body is the throw. Probed `Name "a.txt" As "b.txt"` -> NotImplementedException: "Name not… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1060 |
| `NPer` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs:768); ExpressionExecutor.cs:639 throws "Compile error:\n\nSub or Function not defined (NPer)". Named in the deferral list at… | VB6BuiltIns.cs:768 |
| `NPV` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs:768); ExpressionExecutor.cs:639 throws "Compile error:\n\nSub or Function not defined (NPV)". Named in the deferral list at… | VB6BuiltIns.cs:768 |
| `ObjPtr` | **Dies** | Zero hits anywhere in IDE/ outside obj/bin; not in BuildRegistry (VB6BuiltIns.cs:768), not in Grammar/VB6.g4, not even in the editor metadata (VbKeywordNormalizer.cs / VbSignatures.cs). A… | VB6BuiltIns.cs:768 |
| `On...GoSub` | **Dies** | throws `"OnGoSub not implemented"` — (onGoSubStmt, VB6.g4:421) and throws at run time. Verbatim: Measured: `On i GoSub L1` -> NotImplementedException: OnGoSub not implemented. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1095 |
| `On...GoTo` | **Dies** | throws `"OnGoTo not implemented"` — (onGoToStmt, VB6.g4:417) and throws at run time. Verbatim: Measured: `On i GoTo L1, L2` -> NotImplementedException: OnGoTo not implemented. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1090 |
| `Open` | **Dies** | the keystone of the family. The full VB6 clause grammar is present (`openStmt`, Grammar/VB6.g4:425-427, covering mode, Access, lock and `Len =`) and VisitOpenStmt exists, but its entire… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1100 |
| `Output` | **Dies** | OUTPUT is a lexer token and an openStmt mode alternative (Grammar/VB6.g4:425-427). Probed `Open "z.txt" For Output As #1` -> NotImplementedException "Open not implemented". | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1100 |
| `ParamArray` | **Dies** | throws `"ParamArray parameters are not yet supported"` — and DECLARES fine - the grammar's `arg` rule carries `(PARAMARRAY WS)?` and PrePass.ParseParams records `ParamArray: arg.PARAMARRAY() != null`. It… | BasicInterpreter.cs:671-672 |
| `ParentControls` | **Dies** | Measured for `Dim p As ParentControls`: VBCompileErrorException: "User-defined type not defined: ParentControls" (StatementExecutor.cs:1545). The name appears nowhere in the runtime. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `ParentControls.ParentControlsType` | **Dies** | Measured on a Variant holder: VBMethodOrDataMemberNotFoundException: "Method or data member not found (ParentControlsType in Right(EmptyVariant))". The owning ParentControls type does not… | IDE/HexIDE.Runtime/Interpreter/Exceptions.cs:53 |
| `Partition` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. `Partition(n, lo, hi, size)` throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (Partition)". Named in… | ExpressionExecutor.cs:640 |
| `Pmt` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs:768); ExpressionExecutor.cs:639 throws "Compile error:\n\nSub or Function not defined (Pmt)". Named in the deferral list at… | VB6BuiltIns.cs:768 |
| `PPmt` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs:768); ExpressionExecutor.cs:639 throws "Compile error:\n\nSub or Function not defined (PPmt)". Named in the deferral list at… | VB6BuiltIns.cs:768 |
| `Preserve` | **Dies** | throws `"PRESERVE not implemented"` — (`redimStmt : REDIM WS (PRESERVE WS)? redimSubStmt ...`) and throws on the first line of the visitor: Measured: `Dim a() / ReDim a(2) / ReDim… | StatementExecutor.cs:1446-1447 |
| `Print #` | **Dies** | `printStmt : PRINT WS valueStmt WS? COMMA (WS? outputList)?` (Grammar/VB6.g4:439-441); the visitor body is the throw. Probed `Print #1, "x"`, `Print #1, "a", "b"`, `Print #1, "a"; "b"`… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1105 |
| `Printer` | **Dies** | never seeded. BasicInterpreter.cs:299-301 seeds only Debug, Err and App as program globals. All three positions measured: read -> "Compile error: Variable not defined (Printer)"; assignment… | BasicInterpreter.cs:299-301 |
| `Printer.ColorMode` | **Dies** | because the Printer object it hangs off is never seeded. MEASURED for "Printer.Orientation = 2": "Run-time error '424': Object required Can't find variable Printer"… | BasicInterpreter.cs:299-301 |
| `Printer.Duplex` | **Dies** | because the Printer object it hangs off is never seeded. MEASURED for "Printer.Orientation = 2": "Run-time error '424': Object required Can't find variable Printer"… | BasicInterpreter.cs:299-301 |
| `Printer.Orientation` | **Dies** | because the Printer object it hangs off is never seeded. MEASURED for "Printer.Orientation = 2": "Run-time error '424': Object required Can't find variable Printer"… | BasicInterpreter.cs:299-301 |
| `Printer.PaperBin` | **Dies** | because the Printer object it hangs off is never seeded. MEASURED for "Printer.Orientation = 2": "Run-time error '424': Object required Can't find variable Printer"… | BasicInterpreter.cs:299-301 |
| `Printer.PaperSize` | **Dies** | because the Printer object it hangs off is never seeded. MEASURED for "Printer.Orientation = 2": "Run-time error '424': Object required Can't find variable Printer"… | BasicInterpreter.cs:299-301 |
| `Printer.PrintQuality` | **Dies** | because the Printer object it hangs off is never seeded. MEASURED for "Printer.Orientation = 2": "Run-time error '424': Object required Can't find variable Printer"… | BasicInterpreter.cs:299-301 |
| `Printers` | **Dies** | Measured verbatim for Printers(0).DeviceName: "Compile error: Sub or Function not defined (Printers)"; for Printers.Count: "Compile error: Variable not defined (Printers)"… | BasicInterpreter.cs:299-301 |
| `PropertyBag` | **Dies** | Measured for `Dim p As PropertyBag`: VBCompileErrorException: "User-defined type not defined: PropertyBag" (StatementExecutor.cs:1545); same message for `Set p = New PropertyBag`… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `PropertyBag.Contents` | **Dies** | Measured: VBMethodOrDataMemberNotFoundException: "Method or data member not found (Contents in Right(EmptyVariant))". | IDE/HexIDE.Runtime/Interpreter/Exceptions.cs:53 |
| `PropertyBag.ReadProperty` | **Dies** | Measured on a Variant holder: VBMethodOrDataMemberNotFoundException: "Method or data member not found (ReadProperty(\"A\", 0) in Right(EmptyVariant))". Measured on a real `PropBag As… | IDE/HexIDE.Runtime/Interpreter/Exceptions.cs:53 |
| `PropertyBag.WriteProperty` | **Dies** | Measured: VBRunTimeException: "Unknown method WriteProperty on <Right(EmptyVariant)>()" (StatementExecutor.cs:623). | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:623 |
| `PropertyBag_VB5` | **Dies** | The name lexes as an ordinary IDENTIFIER (digits and `_` are both in LETTERORDIGIT, VB6.g4:2115). Measured for `Dim p As PropertyBag_VB5` and for `Set p = New PropertyBag_VB5`:… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `PSet` | **Dies** | throws `"Only single element supported"` — fails at run time on the coordinate pair, exactly as Circle does. VB6.g4 has no PSET token and no psetStmt rule. `PSet (10, 20), vbRed` parses as a… | ExpressionExecutor.cs:962 |
| `Put` | **Dies** | `putStmt : PUT WS valueStmt WS? COMMA WS? valueStmt? WS? COMMA WS? valueStmt` (Grammar/VB6.g4:455-457); the visitor body is the throw. Probed `Put #1, 1, v` -> NotImplementedException: "Put… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1110 |
| `PV` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs:768); ExpressionExecutor.cs:639 throws "Compile error:\n\nSub or Function not defined (PV)". Named in the deferral list at… | VB6BuiltIns.cs:768 |
| `QBColor` | **Dies** | Not in BuildRegistry; the only runtime hit is VbKeywordNormalizer.cs:74 ("RGB", "QBColor") - highlighter metadata with no execution path. Throws VBSubOrFunctionNotDefinedException: "Sub or… | ExpressionExecutor.cs:640 |
| `Random` | **Dies** | RANDOM is a lexer token and an openStmt mode alternative (Grammar/VB6.g4:425-427). Probed `Open "z.txt" For Random Access Read Write Shared As #1 Len = 32` -> NotImplementedException "Open… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1100 |
| `Rate` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs:768); ExpressionExecutor.cs:639 throws "Compile error:\n\nSub or Function not defined (Rate)". Named in the deferral list at… | VB6BuiltIns.cs:768 |
| `Read` | **Dies** | READ, READ_WRITE, LOCK_READ and LOCK_READ_WRITE are all lexer tokens used by openStmt's Access and lock clauses (Grammar/VB6.g4:425-427, 1459-1471). Probed `Open "z.txt" For Random Access… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1100 |
| `Reset` | **Dies** | `resetStmt : RESET` (Grammar/VB6.g4:475-477) - a bare keyword with no operands; the visitor body is the throw. Probed `Reset` -> NotImplementedException: "Reset not implemented". | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1157 |
| `Return` | **Dies** | throws `"Return not implemented"` — (returnStmt, VB6.g4:483) and throws at run time. Verbatim: | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1172 |
| `RGB` | **Dies** | Not in BuildRegistry; the only runtime hit is the highlighter list VbKeywordNormalizer.cs:74. `Form1.BackColor = RGB(255, 0, 0)` throws VBSubOrFunctionNotDefinedException: "Sub or Function… | ExpressionExecutor.cs:640 |
| `RightB` | **Dies** | Not registered (VB6BuiltIns.cs:768); no occurrence of "RightB" under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (RightB)". | ExpressionExecutor.cs:640; registry at VB6BuiltIns.cs:768 |
| `RmDir` | **Dies** | `rmdirStmt : RMDIR WS valueStmt` (Grammar/VB6.g4:487-489); the visitor body is the throw. Probed `RmDir "zdir"` -> NotImplementedException: "Rmdir not implemented". | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1177 |
| `RSet` | **Dies** | throws `"Rset not implemented"` — The grammar has the rule — `rsetStmt : RSET WS implicitCallStmt_InStmt WS? EQ WS? valueStmt` (VB6.g4:491-493), reachable from blockStmt (VB6.g4:196)… | StatementExecutor.cs:1180-1183 (grammar at Grammar/VB6.g4:491) |
| `SavePicture` | **Dies** | throws `"Savepicture not implemented"` — The grammar has `savepictureStmt : SAVEPICTURE WS valueStmt WS? COMMA WS? valueStmt` (VB6.g4:495-497) and SAVEPICTURE is a real token, but the… | StatementExecutor.cs:1185 |
| `SaveSetting` | **Dies** | throws `"SaveSetting not implemented"` — Grammar rule `saveSettingStmt : SAVESETTING WS valueStmt WS? COMMA WS? valueStmt WS? COMMA WS? valueStmt WS? COMMA WS? valueStmt` (VB6.g4:499-501)… | StatementExecutor.cs:1190 |
| `Scale` | **Dies** | throws `"Only single element supported"` — fails at run time. VB6.g4 has no SCALE token and no scaleStmt rule. `Scale (0, 0)-(100, 100)` parses as a bare procedure call with one argument - a… | ExpressionExecutor.cs:962 |
| `Screen` | **Dies** | never seeded. BasicInterpreter.cs:299-301 seeds only Debug, Err and App as program globals. Measured verbatim: "Compile error: Variable not defined (Screen)"; an assignment such as… | BasicInterpreter.cs:299-301 |
| `Screen.ActiveControl` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs:299-301 |
| `Screen.ActiveForm` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs:299-301 |
| `Screen.FontCount` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs:299-301 |
| `Screen.Fonts` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs:299-301 |
| `Screen.Height` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs:299-301 |
| `Screen.MousePointer` | **Dies** | Measured verbatim for "Screen.MousePointer = 11": "Run-time error '424': Object required Can't find variable Screen" (StatementExecutor.cs:856). | StatementExecutor.cs:856 |
| `Screen.TwipsPerPixelX` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs:299-301 |
| `Screen.TwipsPerPixelY` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs:299-301 |
| `Screen.Width` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs:299-301 |
| `Seek` | **Dies** | fails at name resolution: SEEK is a lexer keyword but is listed in `ambiguousKeyword`, so `Seek(1)` parses as a call. Probed `Debug.Print Seek(1)` -> VBSubOrFunctionNotDefinedException:… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:640 |
| `Seek` | **Dies** | `seekStmt : SEEK WS valueStmt WS? COMMA WS? valueStmt` (Grammar/VB6.g4:503-505); the visitor body is the throw. Probed `Seek #1, 1` -> NotImplementedException: "Seek not implemented". | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1197 |
| `SelectedControls` | **Dies** | Measured for `Dim s As SelectedControls`: VBCompileErrorException: "User-defined type not defined: SelectedControls" (StatementExecutor.cs:1545). | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `SendKeys` | **Dies** | throws `"Sendkeys not implemented"` — Grammar rule `sendkeysStmt : SENDKEYS WS valueStmt (WS? COMMA WS? valueStmt)?` (VB6.g4:527-529) with a real SENDKEYS token; the visitor body is a… | StatementExecutor.cs:1286 |
| `SetAttr` | **Dies** | `setattrStmt : SETATTR WS valueStmt WS? COMMA WS? valueStmt` (Grammar/VB6.g4:531-533); the visitor body is the throw. Probed `SetAttr "a.txt", 0` -> NotImplementedException: "Setattr not… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1293 |
| `Shared` | **Dies** | SHARED is a lexer token, one of openStmt's lock-clause alternatives `(SHARED \| LOCK_READ \| LOCK_WRITE \| LOCK_READ_WRITE)` (Grammar/VB6.g4:425-427). Probed `Open "z.txt" For Random Access… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1100 |
| `Shell` | **Dies** | but is not implemented. Not in BuildRegistry; the only runtime hit is the highlighter list VbKeywordNormalizer.cs:75. Named in docs/interpreter-gaps.md:77. | StatementExecutor.cs:1802 |
| `SLN` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs:768); ExpressionExecutor.cs:639 throws "Compile error:\n\nSub or Function not defined (SLN)". Named in the deferral list at… | VB6BuiltIns.cs:768 |
| `Spc` | **Dies** | in both positions, but is implemented in neither. The grammar gives it a dedicated slot - `outputList_Expression : (SPC \| TAB) (WS? LPAREN WS? argsCall WS? RPAREN)? \| valueStmt`… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1105 |
| `Static` | **Dies** | throws `"non dim variables not supported"` — Two distinct forms, both broken, in different ways. (a) The LOCAL form parses (`variableStmt : (DIM \| STATIC \| visibility) WS ...`) and throws at… | StatementExecutor.cs:1494 (local throw); PrePass.cs:158; grammar at… |
| `StrComp` | **Dies** | Not registered (VB6BuiltIns.cs:768; no occurrence of "StrComp" under IDE/HexIDE.Runtime). `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (StrComp)". | ExpressionExecutor.cs:640; registry at VB6BuiltIns.cs:768 |
| `StrConv` | **Dies** | Not registered (VB6BuiltIns.cs:768; no occurrence of "StrConv" under IDE/HexIDE.Runtime, nor in VbSignatures.cs). `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not… | ExpressionExecutor.cs:640; registry at VB6BuiltIns.cs:768 |
| `StrPtr` | **Dies** | Zero hits anywhere in IDE/ outside obj/bin — not registered, not in the grammar, not in editor metadata; ExpressionExecutor.cs:639 throws "Compile error:\n\nSub or Function not defined… | VB6BuiltIns.cs:768 |
| `Switch` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. `x = Switch(c1, v1, c2, v2)` throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (Switch)". Named in… | ExpressionExecutor.cs:640 |
| `SYD` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs:768); ExpressionExecutor.cs:639 throws "Compile error:\n\nSub or Function not defined (SYD)". Named in the deferral list at… | VB6BuiltIns.cs:768 |
| `Tab` | **Dies** | in both positions, implemented in neither - identical shape to Spc. `outputList_Expression : (SPC \| TAB) ...` (Grammar/VB6.g4); probed `Print #1, Tab(5); "a"` -> NotImplementedException… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1105 |
| `Time` | **Dies** | throws `"Time not implemented"` — Grammar rule `timeStmt : TIME WS? EQ WS? valueStmt` (VB6.g4:547-549); the visitor body is a single throw: | StatementExecutor.cs:1426 |
| `Unlock` | **Dies** | `unlockStmt : UNLOCK WS valueStmt (WS? COMMA WS? valueStmt (WS TO WS valueStmt)?)?` (Grammar/VB6.g4:567-569); the visitor body is the throw. Probed `Unlock #1, 1 To 2` ->… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1441 |
| `VarPtr` | **Dies** | Zero hits anywhere in IDE/ outside obj/bin — not registered, not in the grammar, not in editor metadata; ExpressionExecutor.cs:639 throws "Compile error:\n\nSub or Function not defined… | VB6BuiltIns.cs:768 |
| `VB_MemberFlags` | **Dies** | throws `"Attribute not implemented"` — VB6 writes this only at member level - inside a procedure body, or immediately after a module-level declaration - and both positions land in a block… | StatementExecutor.cs:321 |
| `VB_PredeclaredId` | **Dies** | but is silently discarded - no throw, and no behaviour. Emitted in the canonical class header (ModuleFileFormat.cs:37) and preserved verbatim, but a repo-wide grep for a consumer across… | ModuleFileFormat.cs:37 |
| `VB_ProcData.VB_Invoke_Func` | **Dies** | throws. The dotted name is handled by the grammar (attributeStmt takes an implicitCallStmt_InStmt, VB6.g4:137-139), so 'Attribute Foo.VB_ProcData.VB_Invoke_Func = "M14"' inside a Sub… | StatementExecutor.cs:321 |
| `VB_UserMemId` | **Dies** | throws `"Attribute not implemented"` — throws. Member-level only; the statement reaches StatementExecutor.VisitAttributeStmt, which is '' (StatementExecutor.cs:321). Measured: 'Public… | StatementExecutor.cs:321 |
| `VB_VarDescription` | **Dies** | throws `"Attribute not implemented"` — throws. VB6 writes it in the declarations section immediately after the variable it describes, which is past the contiguous header run, so it is… | StatementExecutor.cs:321 |
| `VB_VarHelpID` | **Dies** | throws `"Attribute not implemented"` — throws. Same declarations-section position as VB_VarDescription, therefore the same block-statement path: '' (StatementExecutor.cs:321). | StatementExecutor.cs:321 |
| `VB_VarMemberFlags` | **Dies** | throws. Measured: 'Public Foo As Long' followed by 'Attribute Foo.VB_VarMemberFlags = "40"' raises 'NotImplementedException: Attribute not implemented' (StatementExecutor.cs:321). | StatementExecutor.cs:321 |
| `VB_VarUserMemId` | **Dies** | throws `"Attribute not implemented"` — throws. Declarations-section position, so the block path applies: '' (StatementExecutor.cs:321). Measured at module top (before any code) it is… | StatementExecutor.cs:321 |
| `Vba6` | **Dies** | Not defined anywhere in the interpreter (repo-wide grep for Vba6/VBA6 across IDE/ and LspServer/ returned nothing). Reachable only from #If, which throws first (PrePass.cs:227 /… | PrePass.cs:227 |
| `vbBack` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs:26-745), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:475 |
| `vbFormFeed` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs:26-745), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:475 |
| `vbNewLine` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs:26-745), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:475 |
| `vbNullChar` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs:26-745), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:475 |
| `vbNullString` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs:26-745), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:475 |
| `vbTab` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs:26-745), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:475 |
| `vbUseCompareOption` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs:26-745), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:475 |
| `vbVerticalTab` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs:26-745), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:475 |
| `Width #` | **Dies** | `widthStmt : WIDTH WS valueStmt WS? COMMA WS? valueStmt` (Grammar/VB6.g4:622-624); the visitor body is the throw. Probed `Width #1, 80` -> NotImplementedException: "Width not implemented". | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1643 |
| `Win16` | **Dies** | Not defined anywhere in the interpreter (the same repo-wide grep as Win32 returned no Win16 hits at all). Reachable only from #If, which throws first (PrePass.cs:227 /… | PrePass.cs:227 |
| `Win32` | **Dies** | Not defined anywhere in the interpreter. A repo-wide grep for a Win32 conditional-compilation constant across IDE/ and LspServer/ (.cs and .g4) returned only unrelated hits - Avalonia… | PrePass.cs:227 |
| `Write` | **Dies** | WRITE is a lexer token used both in openStmt's `Access Write` / `Access Read Write` clause and as the head of writeStmt (Grammar/VB6.g4:425-427, 630-632). As a clause keyword: probed `Open… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1100 |
| `Write #` | **Dies** | `writeStmt : WRITE WS valueStmt WS? COMMA (WS? outputList)?` (Grammar/VB6.g4:630-632); the visitor body is the throw. Probed `Write #1, "x"` -> NotImplementedException: "Write not… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1714 |

## Full inventory by category

Every name, including the ones that work. Sorted within each table by F5 impact — Won't load, Dies,
Partial, No-op, Supported — because a reader of a coverage document is looking for what is missing.


### Statements — 105 names (47 absent, 35 partial, 23 supported)

| Name | Status | Detail | Source |
|---|---|---|---|
| `AppActivate` | **Dies** | throws `"AppActivate not implemented"` — Grammar rule `appActivateStmt : APPACTIVATE WS valueStmt (WS? COMMA WS? valueStmt)?` (VB6.g4:218-220); the visitor body is a single throw:… | StatementExecutor.cs:314 |
| `Close` | **Dies** | `closeStmt : CLOSE (WS valueStmt (WS? COMMA WS? valueStmt)*)?` (Grammar/VB6.g4:234-236) and a visitor exists, but its whole body is the throw. Probed both `Close #1` and bare `Close` ->… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:395 |
| `Date` | **Dies** | throws `"Date not implemented"` — The grammar has a dedicated rule `dateStmt : DATE WS? EQ WS? valueStmt` (VB6.g4:246-248), listed in the block alternatives at VB6.g4:153 ahead of… | StatementExecutor.cs:415 |
| `DefBool` | **Dies** | throws `"Deftype not implemented"` — (grammar rule `deftypeStmt` at VB6.g4:254-256, a blockStmt so it reaches the module's top-level block) and then throws at run time: Measured:… | StatementExecutor.cs:425-428 (throw at 427) |
| `DefByte` | **Dies** | throws `"Deftype not implemented"` — the single shared VisitDeftypeStmt covers every Def* token (DEFBOOL\|DEFBYTE\|DEFINT\|DEFLNG\|DEFCUR\|DEFSNG\|DEFDBL\|DEFDEC\|DEFDATE\|DEFSTR\|DEFOBJ\… | StatementExecutor.cs:425-428 (throw at 427) |
| `DefCur` | **Dies** | throws `"Deftype not implemented"` | StatementExecutor.cs:425-428 (throw at 427) |
| `DefDate` | **Dies** | throws `"Deftype not implemented"` | StatementExecutor.cs:425-428 (throw at 427) |
| `DefDbl` | **Dies** | throws `"Deftype not implemented"` | StatementExecutor.cs:425-428 (throw at 427) |
| `DefDec` | **Dies** | throws `"Deftype not implemented"` — (DEFDEC is a lexer token and appears in the deftypeStmt alternation) and then | StatementExecutor.cs:425-428 (throw at 427); token in VB6.g4:255 and… |
| `DefInt` | **Dies** | throws `"Deftype not implemented"` — Measured: `DefInt A-Z` -> that exact message. | StatementExecutor.cs:425-428 (throw at 427) |
| `DefLng` | **Dies** | throws `"Deftype not implemented"` | StatementExecutor.cs:425-428 (throw at 427) |
| `DefObj` | **Dies** | throws `"Deftype not implemented"` | StatementExecutor.cs:425-428 (throw at 427) |
| `DefSng` | **Dies** | throws `"Deftype not implemented"` | StatementExecutor.cs:425-428 (throw at 427) |
| `DefStr` | **Dies** | throws `"Deftype not implemented"` — Measured: `DefStr S` -> that exact message. | StatementExecutor.cs:425-428 (throw at 427) |
| `DefVar` | **Dies** | throws `"Deftype not implemented"` | StatementExecutor.cs:425-428 (throw at 427) |
| `DeleteSetting` | **Dies** | throws `"DeleteSetting not implemented"` — Grammar rule `deleteSettingStmt : DELETESETTING WS valueStmt WS? COMMA WS? valueStmt (WS? COMMA WS? valueStmt)?` (VB6.g4:258-260) with a real… | StatementExecutor.cs:420 |
| `End` | **Dies** | throws `"End not implemented"` — (endStmt, VB6.g4:268) and throws at run time. Verbatim: Measured: `Debug.Print 1 / End` -> NotImplementedException: End not implemented. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:521 |
| `FileCopy` | **Dies** | `filecopyStmt : FILECOPY WS valueStmt WS? COMMA WS? valueStmt` (Grammar/VB6.g4:304-306); the visitor body is the throw. Probed `FileCopy "a.txt", "b.txt"` -> NotImplementedException:… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:632 |
| `Get` | **Dies** | `getStmt : GET WS valueStmt WS? COMMA WS? valueStmt? WS? COMMA WS? valueStmt` (Grammar/VB6.g4:320-322); the visitor body is the throw. Probed `Get #1, 1, v` -> NotImplementedException: "Get… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:727 |
| `GoSub...Return` | **Dies** | throws `"GoSub not implemented"` — Both halves parse and both throw. GoSub: (StatementExecutor.cs:732). Return: `throw new NotImplementedException("Return not implemented")` (:1172)… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:732 |
| `Input #` | **Dies** | `inputStmt : INPUT WS valueStmt (WS? COMMA WS? valueStmt)+` (Grammar/VB6.g4:357-359); the visitor body is the throw. Probed `Input #1, s` -> NotImplementedException: "Input not implemented". | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:802 |
| `Kill` | **Dies** | `killStmt : KILL WS valueStmt` (Grammar/VB6.g4:361-363); the visitor body is the throw. Probed `Kill "z.txt"` -> NotImplementedException: "Kill not implemented". | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:807 |
| `Line Input #` | **Dies** | `lineInputStmt : LINE_INPUT WS valueStmt WS? COMMA WS? valueStmt` (Grammar/VB6.g4:369-371); the visitor body is the throw. Probed `Line Input #1, s` -> NotImplementedException: "LineInput… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:990 |
| `Lock` | **Dies** | `lockStmt : LOCK WS valueStmt (WS? COMMA WS? valueStmt (WS TO WS valueStmt)?)?` (Grammar/VB6.g4:377-379); the visitor body is the throw. Probed `Lock #1, 1 To 2` -> NotImplementedException:… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1035 |
| `LSet` | **Dies** | throws `"Lset not implemented"` — The grammar has the rule — `lsetStmt : LSET WS implicitCallStmt_InStmt WS? EQ WS? valueStmt` (VB6.g4:381-383), reachable from blockStmt (VB6.g4:178)… | StatementExecutor.cs:1038-1041 (grammar at Grammar/VB6.g4:381) |
| `Mid$` | **Dies** | `Mid$` lexes as MID + DOLLAR, and DOLLAR is a `typeHint` (VB6.g4:823-829) which `iCS_S_ProcedureOrArrayCall` accepts (VB6.g4:680). The letStmt handler rejects it BEFORE reaching the… | StatementExecutor.cs:933-937 |
| `MkDir` | **Dies** | `mkdirStmt : MKDIR WS valueStmt` (Grammar/VB6.g4:405-407); the visitor body is the throw. Probed `MkDir "zdir"` -> NotImplementedException: "Mkdir not implemented". | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1055 |
| `Name` | **Dies** | `nameStmt : NAME WS valueStmt WS AS WS valueStmt` (Grammar/VB6.g4:409-411); the visitor body is the throw. Probed `Name "a.txt" As "b.txt"` -> NotImplementedException: "Name not… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1060 |
| `On...GoSub` | **Dies** | throws `"OnGoSub not implemented"` — (onGoSubStmt, VB6.g4:421) and throws at run time. Verbatim: Measured: `On i GoSub L1` -> NotImplementedException: OnGoSub not implemented. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1095 |
| `On...GoTo` | **Dies** | throws `"OnGoTo not implemented"` — (onGoToStmt, VB6.g4:417) and throws at run time. Verbatim: Measured: `On i GoTo L1, L2` -> NotImplementedException: OnGoTo not implemented. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1090 |
| `Open` | **Dies** | the keystone of the family. The full VB6 clause grammar is present (`openStmt`, Grammar/VB6.g4:425-427, covering mode, Access, lock and `Len =`) and VisitOpenStmt exists, but its entire… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1100 |
| `Preserve` | **Dies** | throws `"PRESERVE not implemented"` — (`redimStmt : REDIM WS (PRESERVE WS)? redimSubStmt ...`) and throws on the first line of the visitor: Measured: `Dim a() / ReDim a(2) / ReDim… | StatementExecutor.cs:1446-1447 |
| `Print #` | **Dies** | `printStmt : PRINT WS valueStmt WS? COMMA (WS? outputList)?` (Grammar/VB6.g4:439-441); the visitor body is the throw. Probed `Print #1, "x"`, `Print #1, "a", "b"`, `Print #1, "a"; "b"`… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1105 |
| `Put` | **Dies** | `putStmt : PUT WS valueStmt WS? COMMA WS? valueStmt? WS? COMMA WS? valueStmt` (Grammar/VB6.g4:455-457); the visitor body is the throw. Probed `Put #1, 1, v` -> NotImplementedException: "Put… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1110 |
| `Reset` | **Dies** | `resetStmt : RESET` (Grammar/VB6.g4:475-477) - a bare keyword with no operands; the visitor body is the throw. Probed `Reset` -> NotImplementedException: "Reset not implemented". | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1157 |
| `Return` | **Dies** | throws `"Return not implemented"` — (returnStmt, VB6.g4:483) and throws at run time. Verbatim: | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1172 |
| `RmDir` | **Dies** | `rmdirStmt : RMDIR WS valueStmt` (Grammar/VB6.g4:487-489); the visitor body is the throw. Probed `RmDir "zdir"` -> NotImplementedException: "Rmdir not implemented". | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1177 |
| `RSet` | **Dies** | throws `"Rset not implemented"` — The grammar has the rule — `rsetStmt : RSET WS implicitCallStmt_InStmt WS? EQ WS? valueStmt` (VB6.g4:491-493), reachable from blockStmt (VB6.g4:196)… | StatementExecutor.cs:1180-1183 (grammar at Grammar/VB6.g4:491) |
| `SavePicture` | **Dies** | throws `"Savepicture not implemented"` — The grammar has `savepictureStmt : SAVEPICTURE WS valueStmt WS? COMMA WS? valueStmt` (VB6.g4:495-497) and SAVEPICTURE is a real token, but the… | StatementExecutor.cs:1185 |
| `SaveSetting` | **Dies** | throws `"SaveSetting not implemented"` — Grammar rule `saveSettingStmt : SAVESETTING WS valueStmt WS? COMMA WS? valueStmt WS? COMMA WS? valueStmt WS? COMMA WS? valueStmt` (VB6.g4:499-501)… | StatementExecutor.cs:1190 |
| `Seek` | **Dies** | `seekStmt : SEEK WS valueStmt WS? COMMA WS? valueStmt` (Grammar/VB6.g4:503-505); the visitor body is the throw. Probed `Seek #1, 1` -> NotImplementedException: "Seek not implemented". | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1197 |
| `SendKeys` | **Dies** | throws `"Sendkeys not implemented"` — Grammar rule `sendkeysStmt : SENDKEYS WS valueStmt (WS? COMMA WS? valueStmt)?` (VB6.g4:527-529) with a real SENDKEYS token; the visitor body is a… | StatementExecutor.cs:1286 |
| `SetAttr` | **Dies** | `setattrStmt : SETATTR WS valueStmt WS? COMMA WS? valueStmt` (Grammar/VB6.g4:531-533); the visitor body is the throw. Probed `SetAttr "a.txt", 0` -> NotImplementedException: "Setattr not… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1293 |
| `Time` | **Dies** | throws `"Time not implemented"` — Grammar rule `timeStmt : TIME WS? EQ WS? valueStmt` (VB6.g4:547-549); the visitor body is a single throw: | StatementExecutor.cs:1426 |
| `Unlock` | **Dies** | `unlockStmt : UNLOCK WS valueStmt (WS? COMMA WS? valueStmt (WS TO WS valueStmt)?)?` (Grammar/VB6.g4:567-569); the visitor body is the throw. Probed `Unlock #1, 1 To 2` ->… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1441 |
| `Width #` | **Dies** | `widthStmt : WIDTH WS valueStmt WS? COMMA WS? valueStmt` (Grammar/VB6.g4:622-624); the visitor body is the throw. Probed `Width #1, 80` -> NotImplementedException: "Width not implemented". | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1643 |
| `Write #` | **Dies** | `writeStmt : WRITE WS valueStmt WS? COMMA (WS? outputList)?` (Grammar/VB6.g4:630-632); the visitor body is the throw. Probed `Write #1, "x"` -> NotImplementedException: "Write not… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1714 |
| `Resume line` | Partial | `resumeStmt : RESUME (WS (NEXT \| ambiguousIdentifier))?` (VB6.g4:479). An identifier target becomes ResumeSignal(ResumeKind.Label) and the driver does `ResolveLabel(labels, r.Label!)`… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1890 |
| `ChDir` | Partial | Implemented and oracle-pinned. VisitChDirStmt evaluates the argument, coerces to string, assigns Environment.CurrentDirectory, and maps… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:360-372 |
| `Const` | Partial | Module-level names are hoisted by PrePass (so a Sub declared before the Const can still see it) and filled by the runtime visitor; local Consts inside a Sub work; multiple per line work… | StatementExecutor.cs:398-412 (comment at 400-402, throw at 406)… |
| `Dim` | Partial | Dim is treated as a DECLARATION, not an executable statement: HoistDeclaredLocals allocates every local a procedure declares before the body runs, in declaration order, and DeclareLocal is… | StatementExecutor.cs:1474-1567 (DeclareLocal 1508-1565, hoist… |
| `Do...Loop` | Partial | All three shapes implemented - bare `Do/Loop` (VisitDoBlockLoop, :430), pre-tested `Do While\|Until ... Loop` (VisitDoWhileBlockLoop, :483), post-tested `Do ... Loop While\|Until`… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:463 |
| `Enum` | Partial | PrePass.VisitEnumerationStmt collects members as Long constants (explicit literal, else previous+1 from 0), hoists each unqualified name and registers the set for qualified MyEnum.Member… | PrePass.cs:185-213 (throw at PrePass.cs:201) |
| `Enum...End Enum` | Partial | The whole construct is handled by PrePass.VisitEnumerationStmt - see the `Enum` row. Works: auto-increment from 0, explicit integer literals (including negatives) resuming the increment… | PrePass.cs:185-213 |
| `Erase` | Partial | VisitEraseStmt implements the oracle-verified split: a DYNAMIC array is freed (arr.Free(), so a later LBound/UBound/index raises Err 9) and a FIXED array keeps its bounds with every element… | StatementExecutor.cs:524-541 (name at 529, throw at 534) |
| `Event` | Partial | PrePass.VisitEventStmt records the event NAME only. Dispatch is by handler name - RaiseEvent walks the source object's sinks and calls `{sinkVarName}_{eventName}` on each listener… | PrePass.cs:215-221; RaiseEvent dispatch at StatementExecutor.cs:1113-1… |
| `For Each...Next` | Partial | VisitForEachStmt iterates array elements in VBArray order (first subscript fastest - ForEachTests.TwoDimensional_FirstSubscriptFastest), auto-declares the loop variable if absent, and binds… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:646 |
| `For...Next` | Partial | VisitForNextStmt implements VB6's `<=`/`>=` termination test (not equality), leaves the counter one step past the limit, and picks Integer vs Double for the counter by magnitude - all… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:669 |
| `Function` | Partial | PrePass.VisitFunctionStmt registers a ProcedureInfo (name, params, return type, body, IsPrivate). The return is by assignment to the procedure's own name; an unassigned return yields the… | PrePass.cs:316-328; BasicInterpreter.cs:491-560 (RunProcedure)… |
| `Function...End Function` | Partial | The whole construct - `(visibility WS)? (STATIC WS)? FUNCTION WS name (argList)? (asTypeClause)? ... END_FUNCTION` - is collected by PrePass.VisitFunctionStmt and executed by… | PrePass.cs:316-328; BasicInterpreter.cs:491-560 |
| `GoTo` | Partial | VisitGoToStmt throws a private GoToSignal that unwinds to ExecuteProcedureBody, which repositions the program counter (StatementExecutor.cs:1877). Only TOP-LEVEL statements of the procedure… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:739 |
| `If...Then...Else (block form)` | Partial | VisitBlockIfThenElse handles If / ElseIf* / Else / End If with correct first-match semantics. The single restriction is the condition type check, verbatim: `throw new… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:745 |
| `If...Then...Else (single-line form)` | Partial | VisitInlineIfThenElse runs blockStmt(0) on true and blockStmt(1) (the Else arm) on false. Condition unpacked with TryUnpack<bool>, which accepts ValueType.Boolean only (VB6Visitor.cs:46)… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:775 |
| `Implements` | Partial | Fully modelled and oracle-measured: PrePass collects the claimed interface names; VbInterface.VerifyConformance runs at FIRST INSTANTIATION (memoised per class) and raises VB6's own… | VbInterface.cs:1-108; conformance hook at BasicInterpreter.cs:135… |
| `Let` | Partial | Both forms work - the grammar is `(LET WS)? implicitCallStmt_InStmt ... EQ ... valueStmt`, and the explicit keyword is measured working (`Let x = 3` prints 3). VisitLetStmt covers plain… | StatementExecutor.cs:810-986 (object-chain throw at 889, dict throw… |
| `Load` | Partial | Implemented for control arrays ONLY. VisitLoadStmt carries the comment "Only control-array element loading is modelled (Load Command1(i)); a bare form Load isn't." and calls… | StatementExecutor.cs:999 |
| `Mid` | Partial | The `Mid(target, start[, length]) = replacement` STATEMENT is implemented, but NOT via VisitMidStmt — that visitor still throws `NotImplementedException("Mid not implemented")`… | StatementExecutor.cs:944-960 (helper at VB6BuiltIns.Strings.cs:67-80… |
| `On Error GoTo line` | Partial | Installs errorMode = ErrorMode.GoToLabel with the target text as handlerLabel (StatementExecutor.cs:1081). ExecuteProcedureBody's catch at :1894 captures the error into Err, records… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1894 |
| `Property` | Partial | The three accessors are collected into a PropertyInfo keyed by property name and dispatched by ACCESS KIND: Get on read, Let on value-assign, Set on object-assign. TWO restrictions. (1)… | PrePass.cs:229-281; dispatch at ExpressionExecutor.cs:534… |
| `Property Get` | Partial | Function-like: returns via its own name, dispatched on a member READ of a class instance. A `As <Class>` return carries the concrete name so the return slot seeds a real UDT or Nothing… | PrePass.cs:229-247 (throws at 236 and 239); dispatch at… |
| `Property Let` | Partial | Sub-like: its single parameter receives the assigned value, coerced to that parameter's declared type, and it WINS over a raw field write on `obj.Member = v`. Works through a `With` target… | PrePass.cs:249-254 and the shared mutator parser at… |
| `Property Set` | Partial | Sub-like, dispatched on `Set obj.Member = o` and winning over a raw object-field write; the reference flows through the accessor's parameter (counted on bind, released at scope-exit) so… | PrePass.cs:256-261 and PrePass.cs:273-281; dispatch at… |
| `ReDim` | Partial | A plain ReDim of an already-declared simple variable works, including multi-dimensional bounds and a trailing `As T` clause - measured: `Dim a() / ReDim a(2,3) / a(1,1)=7` prints 7, and… | StatementExecutor.cs:1444-1472 (Preserve throw 1447, unknown-variable… |
| `Resume` | Partial | VisitResumeStmt throws a ResumeSignal(ResumeKind.Same) caught by ExecuteProcedureBody, which sets pc = faultPc to retry the faulting statement and clears the fault. A Resume with no active… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1885 |
| `Resume Next` | Partial | ResumeSignal(ResumeKind.Next) -> `pc = faultPc + 1`. Tested by ErrorHandlingGoToTests.OnErrorGoTo_Handler_Then_ResumeNext. RESTRICTION is the same nested granularity as Resume: measured `On… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1889 |
| `Select Case` | Partial | VisitSelectCaseStmt evaluates the selector once, then tries each Case in order: exact values, comma lists, `a To b` ranges (TryCompareTo) and `Is <op>` (TryCompareTo for ordering, Equals… | IDE/HexIDE.Runtime/Interpreter/Vb6Value.cs:13 |
| `Set` | Partial | VisitSetStmt stores the REFERENCE (never a copy) with correct AddRef-before-Release ordering, handles WithEvents advise/unadvise on rebind, and supports three targets: a bare variable… | StatementExecutor.cs:1296-1412 (chain throw at 1358, final throw at… |
| `Type` | Partial | PrePass.VisitTypeStmt builds a UdtTypeDef of scalar fields plus nested-type names resolved at instantiation. Scalar fields, untyped (Variant) fields and nested UDT fields work (measured:… | PrePass.cs:283-314 (throws at PrePass.cs:294 and PrePass.cs:303) |
| `Type...End Type` | Partial | The whole construct is handled by PrePass.VisitTypeStmt - see the `Type` row. Works: scalar fields, untyped (Variant) fields, nested UDT fields at any depth, arrays OF a UDT, cross-module… | PrePass.cs:283-314; UDT model at VbUdt.cs |
| `Unload` | Partial | Implemented for control arrays ONLY. VisitUnloadStmt carries the comment "Only control-array element unloading is modelled (Unload Command1(i)); a bare form Unload isn't." Anything else… | StatementExecutor.cs:1431 |
| `While...Wend` | Partial | VisitWhileWendStmt is a pre-tested loop; the comment records the design correctly: "VB6 has no `Exit While`/`Continue While`, so any non-Nothing control flow (Exit Sub/Function/Property)… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1612 |
| `With...End With` | Partial | Per-activation withTargets stack, pushed in VisitWithStmt and popped in a finally; nesting resolves innermost-first and a leading dot with no active With raises Error 91 (WithTests:… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1651 |
| `Beep` | Supported | Really implemented, not a no-op. VisitBeepStmt guards the platform then calls Console.Beep(): `if (OperatingSystem.IsWindows() \|\| OperatingSystem.IsLinux() \|\| OperatingSystem.IsMacOS())… | StatementExecutor.cs:324 |
| `Call` | Supported | Two forms, both implemented: VisitECS_ProcedureCall for `Call Foo(a, b)` (StatementExecutor.cs:581) and VisitECS_MemberProcedureCall for `Call Module1.Foo(...)` / `Call obj.Method(...)`… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:581 |
| `ChDrive` | Supported | Implemented and oracle-pinned. VisitChDriveStmt string-coerces the argument, takes the FIRST character, no-ops on an empty string, raises InvalidProcedureCall (5) for a non-A-Z first char… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:374-391 |
| `End Enum` | Supported | END_ENUM is a single lexer token closing the `enumerationStmt` rule (`(publicPrivateVisibility WS)? ENUM WS ambiguousIdentifier NEWLINE + (enumerationStmt_Constant)* END_ENUM`)… | VB6.g4:272-274 (rule) and VB6.g4:1221-1223 (token: `END_ENUM : E N D… |
| `End Function` | Supported | END_FUNCTION is a single lexer token closing the `functionStmt` rule; PrePass.VisitFunctionStmt consumes the whole construct and the terminator carries no separate behaviour. Exercised by… | VB6.g4:316-318 (rule) and VB6.g4:1226-1228 (token: `END_FUNCTION : E… |
| `End Property` | Supported | END_PROPERTY is a single lexer token closing all three of propertyGetStmt, propertyLetStmt and propertySetStmt; the terminator carries no separate behaviour. Exercised by every Property… | VB6.g4:443-453 (rules) and VB6.g4:1236-1238 (token: `END_PROPERTY : E… |
| `End Sub` | Supported | END_SUB is a single lexer token closing the `subStmt` rule; PrePass.VisitSubStmt consumes the whole construct and the terminator carries no separate behaviour. Exercised by essentially… | VB6.g4:543-545 (rule) and VB6.g4:1246-1248 (token: `END_SUB : E N D '… |
| `End Type` | Supported | END_TYPE is a single lexer token closing the `typeStmt` rule (`(visibility WS)? TYPE WS ambiguousIdentifier NEWLINE + (typeStmt_Element)* END_TYPE`). PrePass.VisitTypeStmt consumes the… | VB6.g4:551-553 (rule) and VB6.g4:1251-1253 (token: `END_TYPE : E N D… |
| `Error` | Supported | VisitErrorStmt evaluates the number, then `interpreter.Err.Raise((long)d)` - comment verbatim: "Legacy `Error n` statement - equivalent to Err.Raise n." A non-numeric operand raises… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:543 |
| `Exit` | Supported | VisitExitStmt maps all five VB6 forms to ControlFlow values: EXIT_DO -> ExitDo, EXIT_FOR -> ExitFor, EXIT_FUNCTION -> ExitFunction, EXIT_PROPERTY -> ExitProperty, EXIT_SUB -> ExitSub… | StatementExecutor.cs:560-575; ControlFlow.cs:9-11; propagation at… |
| `Exit Do` | Supported | VisitExitStmt returns ControlFlow.ExitDo (StatementExecutor.cs:562); all three Do visitors convert it to a normal exit. Tested by DoLoopTests.DoLoopUntil_ShouldExitLoopEarlyWithExitDo and… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:562 |
| `Exit For` | Supported | Returns ControlFlow.ExitFor; both VisitForNextStmt (:713) and VisitForEachStmt (:658) convert it to a normal exit. Tested by StatementTests.ExitFor_ShouldTerminateLoopEarly and… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:564 |
| `Exit Function` | Supported | Returns ControlFlow.ExitFunction; ExecuteProcedureBody returns on any non-Nothing ControlFlow (`if (await Visit(stmts[pc]) != ControlFlow.Nothing) return;`), leaving the return value… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:566 |
| `Exit Property` | Supported | Returns ControlFlow.ExitProperty (StatementExecutor.cs:568), which ExecuteProcedureBody treats like every other Exit. Property Get/Let/Set bodies run through the same driver… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:568 |
| `Exit Sub` | Supported | Returns ControlFlow.ExitSub (StatementExecutor.cs:570). Used throughout ErrorHandlingGoToTests to skip past the handler label, which is the canonical VB6 idiom and is confirmed working. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:570 |
| `On Error GoTo 0` | Supported | VisitOnErrorStmt compares the target's source text: `if (target == "0") { errorMode = ErrorMode.None; handlerLabel = null; }`. Tested twice -… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1074 |
| `On Error Resume Next` | Supported | Sets errorMode = ErrorMode.ResumeNext. Trapped at two levels: per-statement inside every nested block (VisitBlock, StatementExecutor.cs:297) and at the top-level pc driver (:1901); both… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:297 |
| `On Local Error` | Supported | The lexer emits a single ON_LOCAL_ERROR token accepted in the same production as ON_ERROR (VB6.g4:414), and VisitOnErrorStmt branches only on RESUME vs GOTO, so both spellings behave… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1063 |
| `RaiseEvent` | Supported | VisitRaiseEventStmt resolves Me to the source VbObject, evaluates the args ONCE with Locations so ByRef params alias the raiser's locals and multicast sinks share writes, snapshots… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1113 |
| `Randomize` | Supported | VisitRandomizeStmt evaluates the optional seed and calls interpreter.BuiltIns.Reseed(seed); Reseed sets the 24-bit LCG state Rnd consumes: `long bits = number is { } n ?… | StatementExecutor.cs:1143 |
| `Stop` | Supported | VisitStopStmt: with a DebugController attached it calls EnterBreakFromStopStatementAsync (IDE break mode, like a breakpoint on that line); headless it throws Debugging.StopExecutionSignal… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1414 |
| `Sub` | Supported | PrePass.VisitSubStmt registers a ProcedureInfo with IsFunction false. Bare call (`Foo 1, 2`), `Call Foo(1, 2)`, cross-module resolution and instance-method dispatch all route through… | PrePass.cs:330-336; RunProcedure at BasicInterpreter.cs:491 |
| `Sub...End Sub` | Supported | The whole construct - `(visibility WS)? (STATIC WS)? SUB WS name (argList)? ... END_SUB` - is collected by PrePass.VisitSubStmt and executed by BasicInterpreter.RunProcedure, shared with… | PrePass.cs:330-336; BasicInterpreter.cs:491-560 |

### Operators — 32 names (3 absent, 18 partial, 11 supported)

| Name | Status | Detail | Source |
|---|---|---|---|
| `! (dictionary access)` | **Dies** | throws `"dictionaryCallStmt is not supported"` — The grammar yields the pair (`dictionaryCallStmt : EXCLAMATIONMARK ambiguousIdentifier typeHint?`) but every consuming site throws. Measured on a… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:450 |
| `AddressOf` | **Dies** | throws `"ADDRESSOF is not implemented"` — (`ADDRESSOF WS valueStmt # vsAddressOf`) then throws at run time: `public override Task<object?> VisitVsAddressOf(VB6Parser.VsAddressOfContext… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:957 |
| `Like` | **Dies** | throws `"Like is not implemented"` — (`valueStmt WS LIKE WS valueStmt # vsLike`) then throws: `public override Task<object?> VisitVsLike(VB6Parser.VsLikeContext context) => Measured… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:1016 |
| `& (string concatenation)` | Partial | `VisitVsAmp`: both Null -> Null; a single Null or Empty concatenates as "" — "Null and Empty both concatenate as \"\" (Empty.Value is null, so the old code NPE'd on it)"; otherwise… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:727 |
| `* (multiplication)` | Partial | `VisitVsMult` -> `VbNumeric.Mul` -> `Arith(l, r, ctx, '*')`, with the measured VB6 result-type ladder (Byte < Integer < Long < Single < Currency < Decimal < Double), Currency/Decimal… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:760 |
| `+ (addition)` | Partial | `VisitVsAdd`: Null on either side -> Null; String + String concatenates (`// VB6: String + String concatenates`); otherwise `VbNumeric.Add`, which special-cases Date — "Date + anything ->… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:742 |
| `+ (string concatenation)` | Partial | String + String concatenates: `if (leftValue.Type == Vb6Value.ValueType.String && rightValue.Type == Vb6Value.ValueType.String) return new Vb6Value((string)leftValue.Value! +… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:748 |
| `+ (unary plus)` | Partial | The grammar has the alternative — `\| PLUS WS? valueStmt # vsPlus` — but ExpressionExecutor has NO `VisitVsPlus` override (grepped: the name occurs only in the generated obj/ parser and… | IDE/HexIDE.Runtime/Interpreter/Grammar/VB6.g4:575 |
| `- (subtraction)` | Partial | `VisitVsMinus` -> `VbNumeric.Sub`, with the Date rules written out: "Date - Date -> Double (day difference)", "Date - n (or n - Date) -> Date". Null propagates. Measured `5 - 3` = 2… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:752 |
| `- (unary negation)` | Partial | `VisitVsNegation`: Null -> Null, otherwise `VbNumeric.Negate(value, context)`. Measured `-a` with a = 5 -> Integer -5, `-32768` -> Integer -32768, `-&H10` -> -16, `-7 Mod 2` -> -1… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:914 |
| `. (member access)` | Partial | Works for: a UDT field chain of ANY depth (`GetUdtField`/`SetUdtField` walk the owned bags — measured `e.A.B = 9` then read back 9); a namespace qualifier (`Module1.Foo`, `MyEnum.Member`… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:527 |
| `/ (floating-point division)` | Partial | `VisitVsDiv` dispatches on the token text (`context.DIV().GetText() == "/"`) into `VbNumeric.RealDivide`: divide-by-zero raises `VBStandardError.DivisionByZero` (Err 11); the result is… | IDE/HexIDE.Runtime/Interpreter/VbNumeric.cs:102 |
| `< (less than)` | Partial | Two STRINGS compare ordinally — VB6 default Option Compare Binary, oracle-verified ("B" < "a" True, "10" < "9" True). Two numerics promote to the wider subtype via `GetTwoValuesSameTypes`… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:810 |
| `<= (less than or equal to)` | Partial | Same cascade as `<`: ordinal string compare, then int/float/double unpack, else `throw new VBRunTimeException(context, VBStandardError.TypeMismatch)`. Measured `(1 <= 2)` True. RESTRICTION:… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:841 |
| `<> (not equal to)` | Partial | `VisitVsNeq` -> `GetTwoValuesSameTypesOrNull`; Null on either side -> Null; otherwise `!leftValue.Equals(rightValue)` (Vb6Value equality is type-first). Measured `(1 <> 2)` True, `("a" <>… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:802 |
| `= (equal to)` | Partial | `VisitVsEq` -> `GetTwoValuesSameTypesOrNull`; Null -> Null; otherwise `leftValue.Equals(rightValue)`. `Empty` coerces to its partner's ZERO — the comment records the measurement: "`Empty =… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:794 |
| `> (greater than)` | Partial | Same cascade as `<`. Measured `(2 > 1)` True, `("b" > "a")` True. RESTRICTION: a String-vs-number pair raises Err 13. | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:827 |
| `>= (greater than or equal to)` | Partial | Same cascade as `<`. Measured `(2 >= 1)` True. RESTRICTION: a String-vs-number pair raises Err 13. | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:855 |
| `\ (integer division)` | Partial | The same `VisitVsDiv` visitor routes a non-`/` token to `VbNumeric.IntDivide`; operands are rounded to integers first. Measured `7 \ 2` = 3 Integer and `7.6 \ 2` = 4 Long (7.6 rounds to 8)… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:776 |
| `^ (exponentiation)` | Partial | `VisitVsPow` -> `VbNumeric.Power`, with the measured error split recorded in the source: "`^` distinguishes a DOMAIN error from an overflow, and the two get different numbers (measured): 0… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:786 |
| `Mod` | Partial | `VisitVsMod` -> `VbNumeric.Modulo`; operands are rounded to integers before the operation. Null on either side -> Null. Measured `7 Mod 2` = 1 Integer, `-7 Mod 2` = -1, `7.6 Mod 2` = 0… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:768 |
| `. (With-block leading dot)` | Supported | `VisitWithStmt` evaluates the target and pushes it on this activation's `withTargets`, popping in a `finally`; a member call with no leading part resolves against `withTargets.Peek()`, and… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1646 |
| `= (assignment)` | Supported | `VisitLetStmt` covers a bare or undeclared variable (VB6 creates it on first use unless Option Explicit — "VB6 creates an undeclared variable on first use — a procedure-local Variant… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:810 |
| `And` | Supported | Implemented over the oracle-measured `VbBitwise` ladder rather than by coercing to a common type: "Bitwise operators do not coerce to a common type; they reduce each operand to bits… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:878 |
| `Eqv` | Supported | `VisitVsEqv` takes the RAW pair ("raw — see VisitVsAnd"), returns Null when either side is Null, then applies `~(a ^ b)` through the same `VbBitwise` ladder. Measured `(True Eqv True)` ->… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:934 |
| `Imp` | Supported | `VisitVsImp` applies `~a \| b` through the `VbBitwise` ladder, with VB6's three-valued Null table written out explicitly: both Null -> Null; Null Imp True -> True; Null Imp False -> Null… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:943 |
| `Is` | Supported | VisitVsIs performs reference identity on the underlying object: `return new Vb6Value(ReferenceEquals(left.Value, right.Value))`, with the comment "One line covers a Is b, x Is Nothing, and… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:1018 |
| `Is (object identity)` | Supported | `VisitVsIs` compares the underlying references: "Reference identity on the underlying object (Nothing = null). One line covers a Is b, x Is Nothing, and Nothing Is Nothing — NOT Vb6Value… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:1018 |
| `Not` | Supported | `VisitVsNot`: Null -> Null; otherwise `VbBitwise.TryUnpack` then `VbBitwise.Not(bits, width)` — "Not keeps its operand's OWN width rather than promoting: Not CByte(5) is 250, an eight-bit… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:922 |
| `Or` | Supported | `VisitVsOr` — same raw-pair `VbBitwise` treatment as `And` (`static (a, b) => a \| b`), with an unusable operand raising Err 13. Measured `(True Or False)` -> Boolean True. Tests:… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:890 |
| `TypeOf ... Is` | Supported | `VisitVsTypeOf`, including an explicit grammar-greediness workaround: "`TypeOf p Is Clock` parses the operand as the vsIs expression `p Is Clock` with NO Is-type clause. Recover by… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:982 |
| `Xor` | Supported | `VisitVsXor` — same raw-pair `VbBitwise` treatment (`static (a, b) => a ^ b`). Measured `(True Xor False)` -> Boolean True. | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:902 |

### Intrinsic functions — 153 names (70 absent, 37 partial, 45 supported)


**array** (3)

| Name | Status | Detail | Source |
|---|---|---|---|
| `Array` | Supported | Registered: d["Array"] = (_, a, _) => MakeArray(Vb6Value.ValueType.EmptyVariant, a) — a 0-based 1-D Variant array. Zero arguments produce bounds (0,-1) (ArgSlots returns an empty slot list… | VB6BuiltIns.Array.cs:16 |
| `LBound` | Supported | Registered; Bound() accepts 1-2 args, requires a VBArray (else Err 13 Type mismatch), and maps dimension < 1 or > Rank to Err 9 rather than an untrappable ArgumentOutOfRangeException… | VB6BuiltIns.Array.cs:14 |
| `UBound` | Supported | Registered; same Bound() helper as LBound — 1-2 args, non-array -> Err 13, bad dimension -> Err 9. Exercised throughout ArrayFunctionsTests (an empty array reads UBound = -1). | VB6BuiltIns.Array.cs:15 |

**conversion** (17)

| Name | Status | Detail | Source |
|---|---|---|---|
| `CVar` | **Dies** | Not registered in BuildRegistry (VB6BuiltIns.cs:768) or any RegisterXxx partial — the only hits anywhere in IDE/ are editor metadata. A call reaches ExpressionExecutor.cs:639 and throws… | VB6BuiltIns.cs:768 |
| `CVDate` | **Dies** | Zero hits anywhere in IDE/ outside obj/bin — not registered (VB6BuiltIns.cs:768), not in Grammar/VB6.g4, not in VbKeywordNormalizer.cs, not in VbSignatures.cs. A call reaches… | VB6BuiltIns.cs:768 |
| `CVErr` | **Dies** | Not registered in BuildRegistry (VB6BuiltIns.cs:768) or any RegisterXxx partial, so name resolution falls through local arrays and user procedures to the intrinsic registry, gets null back… | VB6BuiltIns.cs:768 |
| `CBool` | Partial | Registered; CBool() maps Null -> Err 94, passes a Boolean through, parses a String via bool.TryParse then a numeric parse (Err 13 on garbage), and otherwise tests ToNum() != 0. Pinned:… | VB6BuiltIns.Conversion.cs:21 |
| `CCur` | Partial | Registered: NarrowNum(a[0], VT.Currency) -> MakeCurrency, which rounds to 4 dp half-to-even and raises Err 6 outside +/-922337203685477.5807 (VbNumeric.cs:244-249). Pinned: CCur(5) yields… | VB6BuiltIns.Conversion.cs:19 |
| `CDate` | Partial | Registered; Null -> Err 94, a DateTime passes through, a String goes through DateTime.TryParse with InvariantCulture (Err 13 on failure), and a number goes through DateTime.FromOADate with… | VB6BuiltIns.Conversion.cs:23 |
| `CDec` | Partial | Registered: NarrowNum(a[0], VT.Decimal) -> Vb6Value.NewDecimal(ToDecimal(v)) (VbNumeric.cs:185-186). Pinned: CDec(5) yields Vb6Value.NewDecimal(5m) (ConversionFunctionsTests.cs:51,60)… | VB6BuiltIns.Conversion.cs:20 |
| `Hex` | Partial | Registered; Null in -> Null out; RadixStr picks the width from the operand's declared type — Byte/Integer -> 16-bit two's complement, Long (or a value outside Integer range) -> 32-bit… | VB6BuiltIns.Conversion.cs:24 |
| `Oct` | Partial | Registered; same RadixStr path as Hex with radix 8 — Null -> Null, 16-bit width for Byte/Integer, 32-bit for Long. Pinned: Oct(8)="10", Oct(-1)="177777" (ConversionFunctionsTests.cs:18-19). | VB6BuiltIns.Conversion.cs:25 |
| `CByte` | Supported | Registered: NarrowNum(a[0], VT.Byte) — strings/booleans coerce through ToNum first, then VbNumeric.Narrow rounds half-to-even and range-checks, raising Err 6 out of range. Pinned:… | VB6BuiltIns.Conversion.cs:14 |
| `CDbl` | Supported | Registered: NarrowNum(a[0], VT.Double); Narrow's Double arm is a lossless widen of ToDouble. Pinned: CDbl("3.14") = 3.14 Double (ConversionFunctionsTests.cs:48). | VB6BuiltIns.Conversion.cs:18 |
| `CInt` | Supported | Registered: NarrowNum(a[0], VT.Integer) — half-to-even rounding then a 16-bit range check raising Err 6. Pinned: CInt("42")=42 Integer, CInt(40000) -> Err 6… | VB6BuiltIns.Conversion.cs:15 |
| `CLng` | Supported | Registered: NarrowNum(a[0], VT.Long) — half-to-even rounding then a 32-bit range check raising Err 6 (VbNumeric.NarrowLong). Pinned: CLng(5) yields Vb6Value(5L), and… | VB6BuiltIns.Conversion.cs:16 |
| `CSng` | Supported | Registered: NarrowNum(a[0], VT.Single) -> NarrowDouble(..., VT.Single), which range-checks into Single and maps NaN -> Err 5 / Infinity -> Err 6. Pinned: CSng(2.5) yields Vb6Value(2.5f)… | VB6BuiltIns.Conversion.cs:17 |
| `CStr` | Supported | Registered: "a[0].IsNull ? throw InvalidUseOfNull() : new Vb6Value(AsStr(a[0]))" with the trailing comment "// no leading space" — Null raises Err 94, everything else stringifies with no… | VB6BuiltIns.Conversion.cs:22 |
| `Str` | Supported | Registered; Null -> Null, otherwise StrFn: "Str returns the number as text with a leading space for non-negatives (CStr has none)" (VB6BuiltIns.Strings.cs:155). Tested: Str(5)=" 5"… | VB6BuiltIns.Strings.cs:32 |
| `Val` | Supported | Registered; leading-numeric parse returning a Double. Comment: "Leading-numeric parse: ignores embedded whitespace, honours &H/&O, stops at the first non-numeric char." Tested:… | VB6BuiltIns.Strings.cs:33 |

**date/time** (19)

| Name | Status | Detail | Source |
|---|---|---|---|
| `DatePart` | Partial | Registered and working for every interval, but two documented arguments are not honoured: the optional firstweekofyear (args[3]) is never read, and DatePart("ww", ...) routes to… | VB6BuiltIns.DateTime.cs:42 |
| `TimeSerial` | Partial | d["TimeSerial"] = (_, a, _) => TimeSerial(AsInt(a[0]), AsInt(a[1]), AsInt(a[2])) - builds on the 1899-12-30 epoch so hours roll over. Overflow is Err 6, deliberately NOT Err 5: comment "Err… | VB6BuiltIns.DateTime.cs:35 |
| `WeekdayName` | Partial | Registered (d["WeekdayName"] = (_, a, _) => WeekdayName(AsInt(a[0]), a.Count >= 2 && AsDouble(a[1]) != 0, Fdow(a, 2))) and correct when firstdayofweek is passed explicitly, but the… | VB6BuiltIns.DateTime.cs:46 |
| `Date` | Supported | Registered in the intrinsic registry: d["Date"] = (_, _, _) => new Vb6Value(DateTime.Now.Date). Returns a Date-typed Vb6Value. Exercised by… | VB6BuiltIns.DateTime.cs:20 |
| `DateAdd` | Supported | d["DateAdd"] = (_, a, _) => DateAdd(Interval(a[0]), AsInt(a[1]), AsDate(a[2])). Month-end clamping comes free from DateTime.AddMonths; oracle-pinned by… | VB6BuiltIns.DateTime.cs:40 |
| `DateDiff` | Supported | d["DateDiff"] = (_, a, _) => new Vb6Value(DateDiff(Interval(a[0]), AsDate(a[1]), AsDate(a[2]), Fdow(a, 3))). Returns Long. Implements VB6's boundary-counting rule (8:30->9:15 = 1 hour)… | VB6BuiltIns.DateTime.cs:41 |
| `DateSerial` | Supported | d["DateSerial"] = (_, a, _) => DateSerial(AsInt(a[0]), AsInt(a[1]), AsInt(a[2])). Rolls over out-of-range month/day via AddMonths/AddDays; a result past the Date range raises Err 5 (`catch… | VB6BuiltIns.DateTime.cs:34 |
| `DateValue` | Supported | d["DateValue"] = (_, a, _) => new Vb6Value(AsDate(a[0]).Date). Oracle row exists: DateValue("March 15, 2020") -> 2020-03-15. | VB6BuiltIns.DateTime.cs:36 |
| `Day` | Supported | d["Day"] = (_, a, _) => new Vb6Value(AsDate(a[0]).Day). Returns Integer (oracle-verified return type); pinned by DateFunctionsTests.Parts_And_Weekday. | VB6BuiltIns.DateTime.cs:27 |
| `Hour` | Supported | d["Hour"] = (_, a, _) => new Vb6Value(AsDate(a[0]).Hour). Returns Integer; pinned by DateFunctionsTests.Parts_And_Weekday (14 for 2:30:45 PM). | VB6BuiltIns.DateTime.cs:28 |
| `Minute` | Supported | d["Minute"] = (_, a, _) => new Vb6Value(AsDate(a[0]).Minute). Returns Integer; pinned by DateFunctionsTests.Parts_And_Weekday. | VB6BuiltIns.DateTime.cs:29 |
| `Month` | Supported | d["Month"] = (_, a, _) => new Vb6Value(AsDate(a[0]).Month). Returns Integer; pinned by DateFunctionsTests.Parts_And_Weekday. | VB6BuiltIns.DateTime.cs:26 |
| `MonthName` | Supported | d["MonthName"] = (_, a, _) => MonthName(AsInt(a[0]), a.Count >= 2 && AsDouble(a[1]) != 0). A month outside 1..12 raises Err 5. Pinned by DateFunctionsTests.MonthName_And_WeekdayName. | VB6BuiltIns.DateTime.cs:45 |
| `Now` | Supported | d["Now"] = (_, _, _) => new Vb6Value(DateTime.Now). TypeName(Now) = "Date" is asserted by DateFunctionsTests.Clock_ReturnsRightTypes_AndIsCoherent. | VB6BuiltIns.DateTime.cs:19 |
| `Second` | Supported | d["Second"] = (_, a, _) => new Vb6Value(AsDate(a[0]).Second). Returns Integer; pinned by DateFunctionsTests.Parts_And_Weekday. | VB6BuiltIns.DateTime.cs:30 |
| `Time` | Supported | d["Time"] = (_, _, _) => new Vb6Value(Epoch + DateTime.Now.TimeOfDay), where Epoch is the VB6 date serial 0 (1899-12-30), so a time-only value sits on the correct epoch. | VB6BuiltIns.DateTime.cs:21 |
| `Timer` | Supported | d["Timer"] = (_, _, _) => new Vb6Value((float)DateTime.Now.TimeOfDay.TotalSeconds) - Single, as VB6. TypeName(Timer) = "Single" is asserted by… | VB6BuiltIns.DateTime.cs:22 |
| `TimeValue` | Supported | d["TimeValue"] = (_, a, _) => new Vb6Value(Epoch + AsDate(a[0]).TimeOfDay). Oracle row exists: TimeValue("2:30:45 PM") -> 14:30:45. | VB6BuiltIns.DateTime.cs:37 |
| `Weekday` | Supported | d["Weekday"] = (_, a, _) => new Vb6Value(WeekdayNum(AsDate(a[0]), Fdow(a, 1))); WeekdayNum shifts so the firstDayOfWeek day is 1. Returns Integer. Oracle-pinned: Weekday(<Sunday>) = 1… | VB6BuiltIns.DateTime.cs:31 |

**file/filesystem** (15)

| Name | Status | Detail | Source |
|---|---|---|---|
| `CurDir` | **Dies** | fails at name resolution: not registered in BuildRegistry (VB6BuiltIns.cs:768-783 registers Strings/Conversion/Math/Array/Inspection/DateTime/Format + DoEvents only). Probed `Debug.Print… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:640 |
| `Dir` | **Dies** | fails at name resolution: unregistered in every VB6BuiltIns partial. Probed `Debug.Print Dir("*.*")` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:640 |
| `EOF` | **Dies** | fails at name resolution: unregistered. Probed `Debug.Print EOF(1)` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined (EOF)". docs/interpreter-gaps.md:83. | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:640 |
| `FileAttr` | **Dies** | fails at name resolution: unregistered. Probed `Debug.Print FileAttr(1, 1)` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined (FileAttr)"… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:640 |
| `FileDateTime` | **Dies** | fails at name resolution: unregistered. Probed `Debug.Print FileDateTime("a.txt")` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined (FileDateTime)"… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:640 |
| `FileLen` | **Dies** | fails at name resolution: unregistered. Probed `Debug.Print FileLen("a.txt")` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined (FileLen)"… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:640 |
| `FreeFile` | **Dies** | fails at name resolution: unregistered. Probed `Debug.Print FreeFile()` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined (FreeFile)"… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:640 |
| `GetAttr` | **Dies** | fails at name resolution: unregistered. Probed `Debug.Print GetAttr("a.txt")` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined (GetAttr)"… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:640 |
| `Input` | **Dies** | fails at name resolution: `Input` is in `ambiguousKeyword` (Grammar/VB6.g4) so `Input(n, f)` parses as a procedure-or-array call, but the name is unregistered. Probed `Debug.Print Input(5… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:640 |
| `InputB` | **Dies** | fails at name resolution: a plain identifier (not a lexer keyword), unregistered. Probed `Debug.Print InputB(5, 1)` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:640 |
| `Loc` | **Dies** | fails at name resolution: unregistered. Probed `Debug.Print Loc(1)` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined (Loc)". docs/interpreter-gaps.md:83. | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:640 |
| `LOF` | **Dies** | fails at name resolution: unregistered. Probed `Debug.Print LOF(1)` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined (LOF)". docs/interpreter-gaps.md:83. | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:640 |
| `Seek` | **Dies** | fails at name resolution: SEEK is a lexer keyword but is listed in `ambiguousKeyword`, so `Seek(1)` parses as a call. Probed `Debug.Print Seek(1)` -> VBSubOrFunctionNotDefinedException:… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:640 |
| `Spc` | **Dies** | in both positions, but is implemented in neither. The grammar gives it a dedicated slot - `outputList_Expression : (SPC \| TAB) (WS? LPAREN WS? argsCall WS? RPAREN)? \| valueStmt`… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1105 |
| `Tab` | **Dies** | in both positions, implemented in neither - identical shape to Spc. `outputList_Expression : (SPC \| TAB) ...` (Grammar/VB6.g4); probed `Print #1, Tab(5); "a"` -> NotImplementedException… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1105 |

**inspection** (15)

| Name | Status | Detail | Source |
|---|---|---|---|
| `Erl` | **Dies** | but is not in the intrinsic registry and never throws. BuildRegistry (VB6BuiltIns.cs:768) registers no 'Erl'; grep over IDE/HexIDE.Runtime finds no ERL token and no Erl handler. An… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:466 |
| `Error` | **Dies** | `Error(n)` (the function returning an error's message text) is not in BuildRegistry. Measured: `Debug.Print Error(6)` -> VBSubOrFunctionNotDefinedException. `Debug.Print Error$(6)` ->… | IDE/HexIDE.Runtime/Interpreter/VB6BuiltIns.cs:768 |
| `ObjPtr` | **Dies** | Zero hits anywhere in IDE/ outside obj/bin; not in BuildRegistry (VB6BuiltIns.cs:768), not in Grammar/VB6.g4, not even in the editor metadata (VbKeywordNormalizer.cs / VbSignatures.cs). A… | VB6BuiltIns.cs:768 |
| `StrPtr` | **Dies** | Zero hits anywhere in IDE/ outside obj/bin — not registered, not in the grammar, not in editor metadata; ExpressionExecutor.cs:639 throws "Compile error:\n\nSub or Function not defined… | VB6BuiltIns.cs:768 |
| `VarPtr` | **Dies** | Zero hits anywhere in IDE/ outside obj/bin — not registered, not in the grammar, not in editor metadata; ExpressionExecutor.cs:639 throws "Compile error:\n\nSub or Function not defined… | VB6BuiltIns.cs:768 |
| `IsError` | Partial | Implemented as new Vb6Value(a[0].IsMissing) — it reports ONLY an omitted argument, because that is the sole vbError value the value model can hold. In-code comment: "An omitted argument is… | VB6BuiltIns.Inspection.cs:35 |
| `TypeName` | Partial | Registered and correct for every primitive, array and VB class instance (a VbObject reports its own ClassName; arrays append "()"). The restriction is objects that are not VB classes: for… | VB6BuiltIns.Inspection.cs:18 |
| `IsArray` | Supported | Registered: new Vb6Value(a[0].Type.IsArray) — reports the value model's array flag directly. Tested: IsArray(a)=True for Dim a(1 To 3) As Integer, IsArray(123)=False… | VB6BuiltIns.Inspection.cs:24 |
| `IsDate` | Supported | Registered; IsDateValue is True for a Date value or a string DateTime.TryParse accepts; a bare number is False. Comment: "IsDate: a Date value, or a String parseable as a date/time. A bare… | VB6BuiltIns.Inspection.cs:21 |
| `IsEmpty` | Supported | Registered: new Vb6Value(a[0].Type == VT.EmptyVariant). Tested: IsEmpty(v)=True for an undeclared Variant, IsEmpty(0)=False (InspectionFunctionsTests.cs:97-106). Deliberately False for an… | VB6BuiltIns.Inspection.cs:22 |
| `IsMissing` | Supported | Registered: new Vb6Value(a[0].IsMissing). True in exactly one case — an Optional parameter with neither a declared type nor a default, left out (or left blank mid-list). Comment: "True only… | VB6BuiltIns.Inspection.cs:30 |
| `IsNull` | Supported | Registered: new Vb6Value(a[0].Type == VT.Null). Tested: IsNull(Null)=True, IsNull(Empty)=False, IsNull(0)=False (InspectionFunctionsTests.cs:97-106), matching the oracle's… | VB6BuiltIns.Inspection.cs:23 |
| `IsNumeric` | Supported | Registered; IsNumericValue reports numerics, Boolean, Empty and Color as True, Date/Null/objects/arrays as False, and strings via IsNumericString (&H/&O literals, thousands separators… | VB6BuiltIns.Inspection.cs:20 |
| `IsObject` | Supported | Registered; IsObjectValue is True for VT.Control, VT.CSharpProxyObject, VT.Nothing and VT.Object — so IsObject(Nothing)=True as the oracle requires. Tested (False case) at… | VB6BuiltIns.Inspection.cs:25 |
| `VarType` | Supported | Registered: new Vb6Value(TypeInfo(a[0]).code) returning an Integer. Full VbVarType map at VB6BuiltIns.Inspection.cs:74-97 (vbEmpty 0, vbNull 1, vbInteger 2, vbLong 3, vbSingle 4, vbDouble… | VB6BuiltIns.Inspection.cs:19 |

**interaction** (22)

| Name | Status | Detail | Source |
|---|---|---|---|
| `CallByName` | **Dies** | Zero occurrences anywhere in HexIDE.Runtime; not registered by any Register* partial, so BuildRegistry has no entry. The call reaches ExpressionExecutor's final fallthrough and throws… | ExpressionExecutor.cs:640 |
| `Choose` | **Dies** | Not in BuildRegistry - a grep of every d["..."] registration across all VB6BuiltIns partials returns no Choose. `x = Choose(i, "a", "b")` throws VBSubOrFunctionNotDefinedException: "Sub or… | ExpressionExecutor.cs:640 |
| `Command` | **Dies** | but is not implemented. Not in BuildRegistry; the sole hit in the whole runtime is HexIDE.Runtime/Editor/VbKeywordNormalizer.cs:75 ("DoEvents", "Shell", "Environ", "Command"), which is… | ExpressionExecutor.cs:640 |
| `CreateObject` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. `Set o = CreateObject("Excel.Application")` throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined… | ExpressionExecutor.cs:640 |
| `Environ` | **Dies** | Not in BuildRegistry; the only runtime hit is the highlighter list VbKeywordNormalizer.cs:75. `Environ("PATH")` throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined… | ExpressionExecutor.cs:640 |
| `GetAllSettings` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. Throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (GetAllSettings)". docs/interpreter-gaps.md:84: "Registry… | ExpressionExecutor.cs:640 |
| `GetObject` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. Throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (GetObject)". docs/interpreter-gaps.md:96 pairs it with… | ExpressionExecutor.cs:640 |
| `GetSetting` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. Throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (GetSetting)". Same docs/interpreter-gaps.md:84 row as… | ExpressionExecutor.cs:640 |
| `IIf` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. `x = IIf(c, a, b)` throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (IIf)". docs/interpreter-gaps.md:52… | ExpressionExecutor.cs:640 |
| `IMEStatus` | **Dies** | but is not implemented. Not in BuildRegistry and zero occurrences anywhere in HexIDE.Runtime, including the editor-metadata files (VbSignatures.cs / VbKeywordNormalizer.cs). | ExpressionExecutor.cs:640 |
| `LoadPicture` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. `Set Picture1.Picture = LoadPicture("x.bmp")` throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined… | ExpressionExecutor.cs:640 |
| `LoadResData` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. Throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (LoadResData)". | ExpressionExecutor.cs:640 |
| `LoadResPicture` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. Throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (LoadResPicture)". | ExpressionExecutor.cs:640 |
| `LoadResString` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. Throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (LoadResString)". | ExpressionExecutor.cs:640 |
| `Partition` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. `Partition(n, lo, hi, size)` throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (Partition)". Named in… | ExpressionExecutor.cs:640 |
| `QBColor` | **Dies** | Not in BuildRegistry; the only runtime hit is VbKeywordNormalizer.cs:74 ("RGB", "QBColor") - highlighter metadata with no execution path. Throws VBSubOrFunctionNotDefinedException: "Sub or… | ExpressionExecutor.cs:640 |
| `RGB` | **Dies** | Not in BuildRegistry; the only runtime hit is the highlighter list VbKeywordNormalizer.cs:74. `Form1.BackColor = RGB(255, 0, 0)` throws VBSubOrFunctionNotDefinedException: "Sub or Function… | ExpressionExecutor.cs:640 |
| `Shell` | **Dies** | but is not implemented. Not in BuildRegistry; the only runtime hit is the highlighter list VbKeywordNormalizer.cs:75. Named in docs/interpreter-gaps.md:77. | StatementExecutor.cs:1802 |
| `Switch` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. `x = Switch(c1, v1, c2, v2)` throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (Switch)". Named in… | ExpressionExecutor.cs:640 |
| `InputBox` | Partial | Implemented, but only the first three arguments are read: `var prompt = args.Count >= 1 ...; var caption = args.Count >= 2 ...; var defaultText = args.Count >= 3 ...` then `await… | VB6BuiltIns.cs:804 |
| `MsgBox` | Partial | Implemented and returns the correct VBMsgBoxResult, but the Buttons argument is unboxed with a raw type test: `var style = (VBMsgBoxStyle)(args.Count >= 2 ? args[1].Value as int? ?? 0 :… | VB6BuiltIns.cs:816 |
| `DoEvents` | No-op | Deliberately accepted and ignored. Registered in BuildRegistry with the comment that says why: "DoEvents - a no-op here (the tree-walking interpreter has no message pump). VB6 yields to the… | VB6BuiltIns.cs:781 |

**math** (26)

| Name | Status | Detail | Source |
|---|---|---|---|
| `DDB` | **Dies** | Not registered in BuildRegistry (VB6BuiltIns.cs:768) or any RegisterXxx partial; the call resolves to no local array, no user procedure and no intrinsic, so ExpressionExecutor.cs:639 throws… | VB6BuiltIns.cs:768 |
| `FV` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs:768); ExpressionExecutor.cs:639 throws "Compile error:\n\nSub or Function not defined (FV)". Named in the deferral list at… | VB6BuiltIns.cs:768 |
| `IPmt` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs:768); ExpressionExecutor.cs:639 throws "Compile error:\n\nSub or Function not defined (IPmt)". Named in the deferral list at… | VB6BuiltIns.cs:768 |
| `IRR` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs:768); ExpressionExecutor.cs:639 throws "Compile error:\n\nSub or Function not defined (IRR)". Named in the deferral list at… | VB6BuiltIns.cs:768 |
| `MIRR` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs:768); ExpressionExecutor.cs:639 throws "Compile error:\n\nSub or Function not defined (MIRR)". Named in the deferral list at… | VB6BuiltIns.cs:768 |
| `NPer` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs:768); ExpressionExecutor.cs:639 throws "Compile error:\n\nSub or Function not defined (NPer)". Named in the deferral list at… | VB6BuiltIns.cs:768 |
| `NPV` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs:768); ExpressionExecutor.cs:639 throws "Compile error:\n\nSub or Function not defined (NPV)". Named in the deferral list at… | VB6BuiltIns.cs:768 |
| `Pmt` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs:768); ExpressionExecutor.cs:639 throws "Compile error:\n\nSub or Function not defined (Pmt)". Named in the deferral list at… | VB6BuiltIns.cs:768 |
| `PPmt` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs:768); ExpressionExecutor.cs:639 throws "Compile error:\n\nSub or Function not defined (PPmt)". Named in the deferral list at… | VB6BuiltIns.cs:768 |
| `PV` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs:768); ExpressionExecutor.cs:639 throws "Compile error:\n\nSub or Function not defined (PV)". Named in the deferral list at… | VB6BuiltIns.cs:768 |
| `Rate` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs:768); ExpressionExecutor.cs:639 throws "Compile error:\n\nSub or Function not defined (Rate)". Named in the deferral list at… | VB6BuiltIns.cs:768 |
| `SLN` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs:768); ExpressionExecutor.cs:639 throws "Compile error:\n\nSub or Function not defined (SLN)". Named in the deferral list at… | VB6BuiltIns.cs:768 |
| `SYD` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs:768); ExpressionExecutor.cs:639 throws "Compile error:\n\nSub or Function not defined (SYD)". Named in the deferral list at… | VB6BuiltIns.cs:768 |
| `Abs` | Partial | Registered; Abs preserves the operand's subtype (byte/int/long/float/double, Currency vs Decimal kept distinct). Oracle rows: Abs(-4) = 4 Integer, Abs(-4.5) = 4.5 Double; pinned by… | VB6BuiltIns.Math.cs:16 |
| `Atn` | Partial | Registered: new Vb6Value(Math.Atan(AsDouble(a[0]))) — returns Double, matching the file header's rule "Sqr/trig/Exp/Log -> Double (all verified against vb6.exe)". No domain guard is needed:… | VB6BuiltIns.Math.cs:24 |
| `Cos` | Partial | Registered: new Vb6Value(Math.Cos(AsDouble(a[0]))) -> Double. Pinned: Cos(0)=1.0 in MathFunctionsTests.Exp_Sin_Cos. | VB6BuiltIns.Math.cs:22 |
| `Exp` | Partial | Registered and routed through Finite(), which converts a non-representable result into the VB6 error rather than an IEEE special. Comment: "Exp is the one intrinsic here that can overflow a… | VB6BuiltIns.Math.cs:28 |
| `Log` | Partial | Registered with a domain guard: "var x = AsDouble(a[0]); if (x <= 0) throw InvalidCall();" — Err 5 for zero or negative, otherwise Math.Log as a Double (VB6's Log is the natural logarithm). | VB6BuiltIns.Math.cs:29 |
| `Rnd` | Partial | The forward sequence is bit-exact: seed 0x50000, then seed = (seed * 0x43FD43FD + 0xC39EC3) And 0xFFFFFF returned as seed/2^24 as a Single; MathFunctionsTests.Rnd_MatchesVb6Sequence pins… | VB6BuiltIns.Math.cs:31 |
| `Round` | Partial | Registered: d["Round"] = (_, a, _) => new Vb6Value(Math.Round(AsDouble(a[0]), a.Count >= 2 ? AsInt(a[1]) : 0, MidpointRounding.ToEven)). Banker's rounding is oracle-pinned (oracle rows:… | VB6BuiltIns.Math.cs:30 |
| `Sgn` | Partial | Registered: new Vb6Value(Math.Sign(AsDouble(a[0]))) -> Integer, matching the oracle row Sgn(-5) = -1 Integer. Pinned for -5/0/3 in MathFunctionsTests.Sgn_Sqr_Round. | VB6BuiltIns.Math.cs:19 |
| `Sin` | Partial | Registered: new Vb6Value(Math.Sin(AsDouble(a[0]))) -> Double. Pinned: Sin(0)=0.0 in MathFunctionsTests.Exp_Sin_Cos. | VB6BuiltIns.Math.cs:21 |
| `Sqr` | Partial | Registered with a domain guard: "var x = AsDouble(a[0]); if (x < 0) throw InvalidCall();" — Err 5 for a negative argument, otherwise Math.Sqrt as a Double. Oracle rows Sqr(9) = 3 and… | VB6BuiltIns.Math.cs:20 |
| `Tan` | Partial | Registered: new Vb6Value(Math.Tan(AsDouble(a[0]))) -> Double. No guard, deliberately: "Tan's asymptote is unreachable in binary floating point" (VB6BuiltIns.Math.cs:27). | VB6BuiltIns.Math.cs:23 |
| `Fix` | Supported | Registered: Whole(a[0], truncate: true) — truncates toward zero and preserves the operand type (Integer/Long/Byte returned untouched; Single stays Single; Currency and Decimal kept… | VB6BuiltIns.Math.cs:18 |
| `Int` | Supported | Registered: Whole(a[0], truncate: false) — floors toward -infinity, type-preserving. Oracle rows Int(-2.5) = -3 and Int(2.7) = 2 pinned by MathFunctionsTests.IntFloors_FixTruncates. "Int"… | VB6BuiltIns.Math.cs:17 |

**string** (36)

| Name | Status | Detail | Source |
|---|---|---|---|
| `AscB` | **Dies** | No registry entry: BuildRegistry (VB6BuiltIns.cs:768-783) calls RegisterStrings/Conversion/Math/Array/Inspection/DateTime/Format and adds only DoEvents; grep for "AscB" across… | ExpressionExecutor.cs:640; registry at VB6BuiltIns.cs:768 |
| `AscW` | **Dies** | No registry entry (VB6BuiltIns.cs:768; no occurrence of "AscW" anywhere under IDE/HexIDE.Runtime). Reaches `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not… | ExpressionExecutor.cs:640; registry at VB6BuiltIns.cs:768 |
| `ChrB` | **Dies** | No registry entry (VB6BuiltIns.cs:768; no occurrence of "ChrB" under IDE/HexIDE.Runtime). `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (ChrB)". | ExpressionExecutor.cs:640; registry at VB6BuiltIns.cs:768 |
| `ChrW` | **Dies** | No registry entry (VB6BuiltIns.cs:768). `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (ChrW)". | ExpressionExecutor.cs:640; registry at VB6BuiltIns.cs:768 |
| `FormatCurrency` | **Dies** | RegisterFormat registers exactly one name — `d["Format"]` (VB6BuiltIns.Format.cs:18-21); no occurrence of "FormatCurrency" under IDE/HexIDE.Runtime. `throw new… | ExpressionExecutor.cs:640; VB6BuiltIns.Format.cs:18-21 |
| `FormatDateTime` | **Dies** | Not registered (VB6BuiltIns.Format.cs:18-21 registers only "Format"); no occurrence under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not… | ExpressionExecutor.cs:640; VB6BuiltIns.Format.cs:18-21 |
| `FormatNumber` | **Dies** | Not registered (VB6BuiltIns.Format.cs:18-21); no occurrence under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — message "Sub or Function not defined… | ExpressionExecutor.cs:640; VB6BuiltIns.Format.cs:18-21 |
| `FormatPercent` | **Dies** | Not registered (VB6BuiltIns.Format.cs:18-21); no occurrence under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (FormatPercent)". | ExpressionExecutor.cs:640; VB6BuiltIns.Format.cs:18-21 |
| `InStrB` | **Dies** | Not registered (VB6BuiltIns.cs:768); no occurrence of "InStrB" under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (InStrB)". | ExpressionExecutor.cs:640; registry at VB6BuiltIns.cs:768 |
| `LeftB` | **Dies** | Not registered (VB6BuiltIns.cs:768); no occurrence of "LeftB" under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (LeftB)". | ExpressionExecutor.cs:640; registry at VB6BuiltIns.cs:768 |
| `LenB` | **Dies** | Not registered (VB6BuiltIns.cs:768); no occurrence of "LenB" under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (LenB)". | ExpressionExecutor.cs:640; registry at VB6BuiltIns.cs:768 |
| `MidB` | **Dies** | Not registered (VB6BuiltIns.cs:768); no occurrence of "MidB" under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (MidB)". | ExpressionExecutor.cs:640; registry at VB6BuiltIns.cs:768 |
| `RightB` | **Dies** | Not registered (VB6BuiltIns.cs:768); no occurrence of "RightB" under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (RightB)". | ExpressionExecutor.cs:640; registry at VB6BuiltIns.cs:768 |
| `StrComp` | **Dies** | Not registered (VB6BuiltIns.cs:768; no occurrence of "StrComp" under IDE/HexIDE.Runtime). `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (StrComp)". | ExpressionExecutor.cs:640; registry at VB6BuiltIns.cs:768 |
| `StrConv` | **Dies** | Not registered (VB6BuiltIns.cs:768; no occurrence of "StrConv" under IDE/HexIDE.Runtime, nor in VbSignatures.cs). `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not… | ExpressionExecutor.cs:640; registry at VB6BuiltIns.cs:768 |
| `Asc` | Partial | Registered as `d["Asc"] = (_, a, _) => Asc(a[0]);`. The helper returns `new Vb6Value((int)s[0])` — the first UTF-16 code unit, i.e. AscW semantics, not an ANSI-codepage byte. Null… | VB6BuiltIns.Strings.cs:31 (helper at VB6BuiltIns.Strings.cs:145-151) |
| `Chr` | Partial | Registered as `d["Chr"] = (_, a, _) => new Vb6Value(((char)AsInt(a[0])).ToString());`. Restriction: the argument is cast straight to a UTF-16 char with no range validation and no ANSI code… | VB6BuiltIns.Strings.cs:30 |
| `Filter` | Partial | Registered as `d["Filter"] = (_, a, _) => Filter(a);`. Handles Filter(array, match, [include=True], [compare]) and returns a 0-based Variant array; no matches yields bounds (0,-1). The file… | VB6BuiltIns.Array.cs:19 (helper at VB6BuiltIns.Array.cs:95-111) |
| `InStr` | Partial | Registered as `d["InStr"] = (_, a, _) => InStr(a);`. Supports InStr([start,] string1, string2[, compare]) with 1-based results, Null propagation, start<1 raising Err 5, and start past the… | VB6BuiltIns.Strings.cs:19 (helper at VB6BuiltIns.Strings.cs:82-94) |
| `InStrRev` | Partial | Registered as `d["InStrRev"] = (_, a, _) => InStrRev(a);`. Supports InStrRev(string1, string2[, start[, compare]]) with Null propagation and a default start of -1. Restrictions: (i)… | VB6BuiltIns.Strings.cs:20 (helper at VB6BuiltIns.Strings.cs:96-107) |
| `LCase` | Partial | Registered as `d["LCase"] = (_, a, _) => NullOrStr(a[0], s => s.ToLowerInvariant());`. Null propagates to Null. Restriction: casing is INVARIANT, not the current locale, so locale-specific… | VB6BuiltIns.Strings.cs:26 |
| `Len` | Partial | Registered as `d["Len"] = (_, a, _) => a[0].IsNull ? Vb6Value.Null : new Vb6Value(AsStr(a[0]).Length);`. Restriction: it is the CHARACTER COUNT OF THE STRING FORM ONLY. `AsStr` is… | VB6BuiltIns.Strings.cs:18 |
| `Mid` | Partial | Registered as `d["Mid"] = (_, a, _) => NullOrStr(a[0], s => Mid(s, AsInt(a[1]), a.Count >= 3 ? AsInt(a[2]) : (int?)null));`. Null propagates; start<1 raises Err 5; start past the end… | VB6BuiltIns.Strings.cs:17 (helper at VB6BuiltIns.Strings.cs:53-60) |
| `Replace` | Partial | Registered as `d["Replace"] = (_, a, _) => new Vb6Value(Replace(a));`. Implements Replace(expression, find, replace[, start[, count[, compare]]]) including the VB6 rule that the result… | VB6BuiltIns.Strings.cs:21 (helper at VB6BuiltIns.Strings.cs:109-138) |
| `Space` | Partial | Registered as `d["Space"] = (_, a, _) => new Vb6Value(new string(' ', Math.Max(0, AsInt(a[0]))));`. Restriction: a NEGATIVE count is clamped to zero by `Math.Max(0, ...)` and returns "", so… | VB6BuiltIns.Strings.cs:28 |
| `Split` | Partial | Registered as `d["Split"] = (_, a, _) => Split(a);`. Split(expr, [delimiter], [limit], [compare]) returning a 0-based String array. The VB6 defaults are honoured and stated as… | VB6BuiltIns.Array.cs:17 (helper at VB6BuiltIns.Array.cs:48-77) |
| `String` | Partial | Registered as `d["String"] = (_, a, _) => new Vb6Value(new string(CharArg(a[1]), Math.Max(0, AsInt(a[0]))));`. Both argument forms work — String(n, "*") and String(n, 65) — via `CharArg`… | VB6BuiltIns.Strings.cs:29 |
| `UCase` | Partial | Registered as `d["UCase"] = (_, a, _) => NullOrStr(a[0], s => s.ToUpperInvariant());`. Null propagates — asserted by StringFunctionsTests.cs:64 NullArgument_Propagates. Restriction: casing… | VB6BuiltIns.Strings.cs:25 |
| `Format` | Supported | Registered as `d["Format"] = (_, a, _) => FormatValue(a);`. Covers the named numeric formats (General Number / Fixed / Standard / Percent / Scientific / Currency), the named date/time… | VB6BuiltIns.Format.cs:20 (implementation at… |
| `Join` | Supported | Registered as `d["Join"] = (_, a, _) => Join(a);`. Join(array, [delimiter]) with the VB6 default delimiter of a single SPACE (not a comma); the file header states this was pinned against… | VB6BuiltIns.Array.cs:18 (helper at VB6BuiltIns.Array.cs:80-92) |
| `Left` | Supported | Registered as `d["Left"] = (_, a, _) => NullOrStr(a[0], s => Left(s, AsInt(a[1])));`. Null propagates; n<0 raises Err 5 (`if (n < 0) throw InvalidCall();`); n beyond the length returns the… | VB6BuiltIns.Strings.cs:15 (helper at VB6BuiltIns.Strings.cs:41-45) |
| `LTrim` | Supported | Registered as `d["LTrim"] = (_, a, _) => NullOrStr(a[0], s => s.TrimStart(' '));`. Trims only U+0020 (VB6's space-only trim, not .NET's default whitespace set); Null propagates. Tested:… | VB6BuiltIns.Strings.cs:23 |
| `Right` | Supported | Registered as `d["Right"] = (_, a, _) => NullOrStr(a[0], s => Right(s, AsInt(a[1])));`. Null propagates; n<0 raises Err 5 (`if (n < 0) throw InvalidCall();`); n beyond the length returns… | VB6BuiltIns.Strings.cs:16 (helper at VB6BuiltIns.Strings.cs:47-51) |
| `RTrim` | Supported | Registered as `d["RTrim"] = (_, a, _) => NullOrStr(a[0], s => s.TrimEnd(' '));`. Trims only U+0020; Null propagates. Tested: StringFunctionsTests.cs:21. | VB6BuiltIns.Strings.cs:24 |
| `StrReverse` | Supported | Registered as `d["StrReverse"] = (_, a, _) => NullOrStr(a[0], s => { var c = s.ToCharArray(); Array.Reverse(c); return new string(c); });`. Null propagates. Tested:… | VB6BuiltIns.Strings.cs:27 |
| `Trim` | Supported | Registered as `d["Trim"] = (_, a, _) => NullOrStr(a[0], s => s.Trim(' '));`. Trims only U+0020 from both ends (VB6's space-only semantics, not .NET's default whitespace trim); Null… | VB6BuiltIns.Strings.cs:22 |

### Keywords and modifiers — 51 names (11 absent, 22 partial, 17 supported)

| Name | Status | Detail | Source |
|---|---|---|---|
| `Access` | **Dies** | ACCESS is a real lexer token and the clause is in the grammar - `openStmt : OPEN WS valueStmt WS FOR WS (APPEND\|BINARY\|INPUT\|OUTPUT\|RANDOM) (WS ACCESS WS (READ\|WRITE\|READ_WRITE))?… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1100 |
| `Append` | **Dies** | APPEND is a lexer token and an openStmt mode alternative (Grammar/VB6.g4:425-427). Probed `Open "z.txt" For Append As #1` -> NotImplementedException "Open not implemented". | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1100 |
| `GoSub` | **Dies** | throws `"GoSub not implemented"` — (goSubStmt, VB6.g4:324) and throws at run time. Verbatim: Measured. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:732 |
| `Line` | **Dies** | There is no standalone LINE token; `Line Input` is lexed as ONE token, `LINE_INPUT : L I N E ' ' I N P U T` (Grammar/VB6.g4:1454-1456), consumed by `lineInputStmt : LINE_INPUT WS valueStmt… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:990 |
| `Output` | **Dies** | OUTPUT is a lexer token and an openStmt mode alternative (Grammar/VB6.g4:425-427). Probed `Open "z.txt" For Output As #1` -> NotImplementedException "Open not implemented". | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1100 |
| `ParamArray` | **Dies** | throws `"ParamArray parameters are not yet supported"` — and DECLARES fine - the grammar's `arg` rule carries `(PARAMARRAY WS)?` and PrePass.ParseParams records `ParamArray: arg.PARAMARRAY() != null`. It… | BasicInterpreter.cs:671-672 |
| `Random` | **Dies** | RANDOM is a lexer token and an openStmt mode alternative (Grammar/VB6.g4:425-427). Probed `Open "z.txt" For Random Access Read Write Shared As #1 Len = 32` -> NotImplementedException "Open… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1100 |
| `Read` | **Dies** | READ, READ_WRITE, LOCK_READ and LOCK_READ_WRITE are all lexer tokens used by openStmt's Access and lock clauses (Grammar/VB6.g4:425-427, 1459-1471). Probed `Open "z.txt" For Random Access… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1100 |
| `Shared` | **Dies** | SHARED is a lexer token, one of openStmt's lock-clause alternatives `(SHARED \| LOCK_READ \| LOCK_WRITE \| LOCK_READ_WRITE)` (Grammar/VB6.g4:425-427). Probed `Open "z.txt" For Random Access… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1100 |
| `Static` | **Dies** | throws `"non dim variables not supported"` — Two distinct forms, both broken, in different ways. (a) The LOCAL form parses (`variableStmt : (DIM \| STATIC \| visibility) WS ...`) and throws at… | StatementExecutor.cs:1494 (local throw); PrePass.cs:158; grammar at… |
| `Write` | **Dies** | WRITE is a lexer token used both in openStmt's `Access Write` / `Access Read Write` clause and as the head of writeStmt (Grammar/VB6.g4:425-427, 630-632). As a clause keyword: probed `Open… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1100 |
| `As` | Partial | BaseTypeMapper.Map covers ten base types - String, Integer, Long, Byte, Single, Double, Boolean, Currency, Date, Variant - and returns null for everything else. A complexType (`As Employee`… | BaseTypeMapper.cs:14-28; DeclareLocal at StatementExecutor.cs:1524-154… |
| `ByRef` | Partial | ByRef is the default convention (ParamInfo.ByRef = arg.BYVAL() == null) and is implemented by slot aliasing across the shared ExecutionState: `callee.DefineVariable(p.Name… | BasicInterpreter.cs:680-683 (ParamInfo at ProcedureModel.cs:85-91… |
| `ByRef (call-site modifier)` | Partial | Split behaviour. For a USER procedure `ResolveCallArgs` never inspects `BYREF` and a bare lvalue aliases by default, so the keyword is effectively honoured — measured `Bump ByRef a` against… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:229 |
| `ByVal` | Partial | A ByVal parameter is copied into a fresh callee slot, narrowed to its declared type when that type is core-numeric (VbNumeric.Narrow), and UDT values are deep-copied so callee mutation… | BasicInterpreter.cs:685-699 |
| `Case` | Partial | sC_Case (VB6.g4:513) is iterated by VisitSelectCaseStmt; each case's sC_Cond is dispatched by context class (CaseCondElse / CaseCondExpr) and comma-separated sub-conditions are all tried… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1203 |
| `Case Is` | Partial | CaseCondExprIs branch handles LT, LEQ, GT, GEQ, EQ and NEQ. Ordering comparisons go through Vb6Value.TryCompareTo, which promotes across numeric subtypes. Measured: `Case Is > 0` matches 1. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1216 |
| `Do` | Partial | Heads all three doLoopStmt alternatives, each with a real visitor: VisitDoBlockLoop (StatementExecutor.cs:430), VisitDoBlockWhileLoop (:447), VisitDoWhileBlockLoop (:483). Restriction is in… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:463 |
| `Each` | Partial | Recognised by forEachStmt (VB6.g4:308) and driven by VisitForEachStmt. The visitor accepts arrays only: `if (collection.Value is not VBArray array) throw new VBRunTimeException(context… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:646 |
| `For` | Partial | Heads both forNextStmt (VisitForNextStmt, StatementExecutor.cs:669) and forEachStmt (VisitForEachStmt, :635). For...Next is integral-only; For Each is arrays-only. Type hints and an As… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:669 |
| `Global` | Partial | GLOBAL is an alternative of the `visibility` rule (VB6.g4:832-837) and of `publicPrivateGlobalVisibility` (VB6.g4:813-817), so `Global x As Integer`, `Global Const K = 5` and `Global Type… | PrePass.cs:90-92 (comment and check); grammar at VB6.g4:832-837 |
| `If` | Partial | Both ifThenElseStmt alternatives have visitors: VisitBlockIfThenElse (StatementExecutor.cs:742) and VisitInlineIfThenElse (:775). Both require a strictly Boolean condition. Block form: `if… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:745 |
| `In` | Partial | Only occurrence in VB6 is `For Each x In coll` (VB6.g4:309); the grammar consumes it and VisitForEachStmt evaluates the following valueStmt as the collection. Same arrays-only restriction… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:640 |
| `Is (Select Case comparison prefix)` | Partial | `sC_CondExpr : IS WS? comparisonOperator WS? valueStmt # caseCondExprIs` is handled for six operators — `<`, `<=`, `>`, `>=`, `=`, `<>` — via `value.TryCompareTo(val)` /… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1246 |
| `New` | Partial | `Set x = New ClassName` is implemented: `VisitVsNew` extracts the class name SYNTACTICALLY ("evaluating it would try to resolve a variable and throw"), creates the instance via… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:967 |
| `Next` | Partial | Closes forNextStmt and forEachStmt; also the `Resume Next` / `On Error Resume Next` modifier (StatementExecutor.cs:1067, :1164). Loop restriction: the grammar is `NEXT (WS… | IDE/HexIDE.Runtime/Interpreter/Grammar/VB6.g4:313 |
| `On` | Partial | `On Error ...` is fully wired (VisitOnErrorStmt, StatementExecutor.cs:1063). The two computed-branch forms are not: VisitOnGoToStmt throws "OnGoTo not implemented" (:1090) and… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1088 |
| `Private` | Partial | Enforced in exactly ONE place: cross-module procedure resolution skips other modules' Private procedures (`if (m.PrePass.Procedures.TryGetValue(name, out var p) && !p.IsPrivate)`). NOT… | BasicInterpreter.cs:621 (the one enforcement); PrePass.cs:90-91 and… |
| `Rem` | Partial | Handled purely in the lexer: COMMENT is `WS? ('\'' \| COLON? REM ' ') (LINE_CONTINUATION \| ~('\n'\|'\r'))* -> channel(HIDDEN)`. The alternative requires REM followed by a literal SPACE, so… | IDE/HexIDE.Runtime/Interpreter/Grammar/VB6.g4:2099 |
| `Select` | Partial | Heads selectCaseStmt with a full visitor (StatementExecutor.cs:1200). Restriction is in the exact-match comparison - see the Select Case row: equality is type-first, so a declared-type… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1200 |
| `Step` | Partial | by forNextStmt (VB6.g4:313) and read by VisitForNextStmt; a missing Step defaults to 1. Integral values only - the visitor truncation-checks all three bounds and throws: `throw new… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:685 |
| `While` | Partial | Two roles, both implemented: as the Do modifier (`Do While` / `Loop While`, StatementExecutor.cs:447/:483) and as the head of While...Wend (VisitWhileWendStmt, :1612). Tests: DoLoopTests… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1612 |
| `With` | Partial | VisitWithStmt pushes the evaluated target onto a per-activation withTargets stack and pops it in a finally; leading-dot members resolve against the innermost entry… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1651 |
| `Friend` | No-op | as one alternative of the `visibility` rule and then deliberately ignored - only PRIVATE is read. Verbatim comment above the check: "// A module-level procedure is Public unless explicitly… | PrePass.cs:338-340; also documented on ProcedureInfo at… |
| `ByVal (call-site modifier)` | Supported | For a user procedure a call-site `ByVal` suppresses aliasing: `int? location = arg.BYVAL() != null ? null : TryGetArgLocation(arg.valueStmt());` — "A call-site ByVal keyword (or a… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:262 |
| `Case Else` | Supported | `if (cond is VB6Parser.CaseCondElseContext) return await Visit(@case.block())`. The grammar puts ELSE first in sC_Cond specifically so it is not mis-parsed as a variable call (VB6.g4:519… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1206 |
| `Else` | Supported | Block form: ifElseBlockStmt visited when no If/ElseIf condition matched (StatementExecutor.cs:768). Single-line form: `context.blockStmt(1)` (:783). Measured both: `If 1 = 2 Then… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:768 |
| `ElseIf` | Supported | VisitBlockIfThenElse loops `foreach (var elseIf in context.ifElseIfBlockStmt())`, evaluating each condition and returning on the first true one. Measured: n=2 with `If n = 1 / ElseIf n = 2… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:759 |
| `End If` | Supported | A single END_IF lexer token closing blockIfThenElse (VB6.g4:334); purely structural, consumed by the parser. Exercised by every block-If test. | IDE/HexIDE.Runtime/Interpreter/Grammar/VB6.g4:334 |
| `End Select` | Supported | END_SELECT closes selectCaseStmt (VB6.g4:508); structural only. Exercised by StatementTests.SelectCaseTests. | IDE/HexIDE.Runtime/Interpreter/Grammar/VB6.g4:508 |
| `End With` | Supported | END_WITH closes withStmt (VB6.g4:627). The withTargets pop is in a `finally`, so the target is released even if the body exits abnormally (StatementExecutor.cs:1664). | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1664 |
| `Local` | Supported | Its only VB6 use is `On Local Error`. The grammar folds it into a single ON_LOCAL_ERROR token accepted alongside ON_ERROR (VB6.g4:414) and VisitOnErrorStmt treats the two identically… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1063 |
| `Loop` | Supported | Closes all three doLoopStmt alternatives (VB6.g4:262-264) and carries the post-test condition in the `Do ... Loop While\|Until` form (VisitDoBlockWhileLoop, StatementExecutor.cs:447)… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:447 |
| `Optional` | Supported | Three cases are distinguished, all oracle-verified per the code comment: a default expression -> that value (IsMissing False); a declared type with no default -> that type's zero (IsMissing… | BasicInterpreter.cs:702-716 |
| `Public` | Supported | Public is the default for a module-level procedure (IsPrivate is true only for an explicit PRIVATE) and is what cross-module resolution looks for. `Public` on a variable, a Const, a Type… | PrePass.cs:338-340; BasicInterpreter.cs:621-628; PrePass.cs:92 |
| `Then` | Supported | Required by both ifThenElseStmt alternatives (VB6.g4:333, :337) and consumed structurally; no separate execution. Both If forms dispatch on the condition value, not on the token. | IDE/HexIDE.Runtime/Interpreter/Grammar/VB6.g4:333 |
| `To` | Supported | Three roles, all working: For...Next bounds (VB6.g4:313 -> StatementExecutor.cs:675), `Case a To b` (sC_CondExpr caseCondExprTo -> StatementExecutor.cs:1265, uses TryCompareTo so it… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1265 |
| `To (range)` | Supported | Three sites, all working. Array bounds: `ExtractDimensions` reads a two-`valueStmt` subscript as (lower, upper) and a one-`valueStmt` subscript as (`currentModule.PrePass.ArrayBase`, upper)… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1585 |
| `Until` | Supported | Detected as `context.UNTIL() != null` in both pre-tested and post-tested Do visitors, inverting the loop test (StatementExecutor.cs:449, :484). Measured: `Do / n = n + 1 / Loop Until n = 3`… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:449 |
| `Wend` | Supported | Closes whileWendStmt (VB6.g4:619); structural only. WhileWendTests covers accumulate, zero-iteration and Exit-Sub-from-inside cases. | IDE/HexIDE.Runtime/Interpreter/Grammar/VB6.g4:619 |
| `WithEvents` | Supported | PrePass records each WithEvents name as an event sink and hoists the slot to Nothing; the runtime VisitVariableStmt re-seeds it per instance at New; the Set path detects a WithEvents field… | PrePass.cs:76-88; StatementExecutor.cs:1476-1483; Set-binding at… |

### Literals, types and suffixes — 50 names (10 absent, 17 partial, 23 supported)

| Name | Status | Detail | Source |
|---|---|---|---|
| `DExponentLiteral` | **Won't load** | `DOUBLELITERAL` and `INTEGERLITERAL` accept only an `e`/`E` exponent marker, never `D`. Measured: `Debug.Print 1.5D2` -> "Compile error: mismatched input 'D2' expecting <EOF>". | IDE/HexIDE.Runtime/Interpreter/Grammar/VB6.g4:2059 |
| `Line number` | **Won't load** | Grammar-level gap - does not parse. lineLabel requires an ambiguousIdentifier (IDENTIFIER \| ambiguousKeyword), and INTEGERLITERAL is neither, so a numeric label has no production… | IDE/HexIDE.Runtime/Interpreter/Grammar/VB6.g4:789 |
| `LineNumber` | **Won't load** | at all — a grammar-level gap. `lineLabel : ambiguousIdentifier COLON` and `ambiguousIdentifier : (IDENTIFIER \| ambiguousKeyword) +` with `IDENTIFIER : LETTER LETTERORDIGIT*`, so a bare… | IDE/HexIDE.Runtime/Interpreter/Grammar/VB6.g4:789 |
| `#` | **Dies** | and it is the one piece of this batch that fails even outside a file statement. `#1` lexes as its own token, `FILENUMBER : HASH LETTERORDIGIT+` (Grammar/VB6.g4:2063-2065), and FILENUMBER is… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:369 |
| `#n` | **Dies** | `FILENUMBER : HASH LETTERORDIGIT +` is a lexer token listed in the `literal` rule — then throws at run time, because `VisitVsLiteralCore` has no FILENUMBER branch and falls into `throw new… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:369 |
| `$` | **Dies** | There is NO literal form for `$` at all (a String literal carries no suffix), so every occurrence is an identifier or function type hint and every one throws. Measured: `Debug.Print s$` ->… | IDE/HexIDE.Runtime/Interpreter/VB6BuiltIns.Strings.cs:9 |
| `,` | **Dies** | The 14-column print-zone comma is grammar-level only: `outputList : outputList_Expression (WS? (SEMICOLON \| COMMA) WS? outputList_Expression?)* \| outputList_Expression? (WS? (SEMICOLON \|… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1105 |
| `:= (named argument)` | **Dies** | throws `"Assign is not implemented"` — `implicitCallStmt_InStmt WS? ASSIGN WS? valueStmt # vsAssign` with `ASSIGN : ':='` — then throws at run time: `public override Task<object?>… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:1014 |
| `;` | **Dies** | The suppress-newline / adjacent-item semicolon is in `outputList` (Grammar/VB6.g4) and nothing consumes it - probed `Print #1, "a"; "b"` -> NotImplementedException "Print not implemented". | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1105 |
| `ErrObject` | **Dies** | as a complexType, throws at run time. BaseTypeMapper.Map has no ErrObject case and it is not a user class/UDT/Enum, so DeclareLocal falls to the final else. Measured verbatim:… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1544 |
| `Decimal` | Partial | `Decimal` has no token in the grammar's `baseType` rule (BOOLEAN, BYTE, COLLECTION, CURRENCY, DATE, DOUBLE, INTEGER, LONG, OBJECT, SINGLE, STRING, VARIANT), so `As Decimal` parses as a… | IDE/HexIDE.Runtime/Interpreter/Grammar/VB6.g4:746 |
| `!` | Partial | Honoured on a numeric LITERAL, never on an identifier. Literal: `ClassifyIntegerLiteral` and the DOUBLELITERAL branch read the trailing suffix (`'!' => new Vb6Value(float.Parse(body))`) —… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:381 |
| `#` | Partial | Honoured on a numeric LITERAL, never on an identifier. Literal: `_ => new Vb6Value(double.Parse(body))` for the `#` suffix — measured `5#` -> Double 5, `2.5#` -> Double… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:381 |
| `%` | Partial | Honoured on a numeric LITERAL, never on an identifier. Literal: `'%' => new Vb6Value(int.Parse(body)) // Integer (magnitude ctor keeps it Integer in Int16 range)` — measured `5%` -> Integer… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:381 |
| `&` | Partial | Honoured on a numeric LITERAL, never on an identifier. Literal: `'&' => new Vb6Value(long.TryParse(body, out var lp) ? lp : (long)double.Parse(body))` — measured `5&` -> Long 5… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:381 |
| `&H` | Partial | Implemented and oracle-faithful in `ClassifyRadixLiteral`, whose comment records the measurement: "VB6 &H hex / &O octal literals are unsigned bit-patterns (verified against vb6.exe): with… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:401 |
| `&O` | Partial | The same `ClassifyRadixLiteral` path with radix 8. Measured `&O17` = 15 Integer; a trailing `&`/`%` forces the Long/Integer reading as for hex. RESTRICTION: `OCTALLITERAL : (PLUS \| MINUS)?… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:401 |
| `() (call / array index)` | Partial | `EvaluateProcedureOrArrayCall` resolves in the documented VB6 order: a control-array element (missing element -> Err 340, oracle-pinned), then a local ARRAY variable (multi-dimensional… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:594 |
| `:` | Partial | Only a colon IMMEDIATELY FOLLOWED BY A SPACE separates statements: the lexer rule is `NEWLINE : WS? ('\r'? '\n' \| COLON ' ') WS?`. Measured: `a = 1: Debug.Print a` runs and prints 1; `a =… | IDE/HexIDE.Runtime/Interpreter/Grammar/VB6.g4:2093 |
| `@` | Partial | Honoured on a numeric LITERAL, never on an identifier. Literal: `'@' => Vb6Value.NewCurrency(decimal.Parse(body)) // @ forces Currency` — measured `5@` -> Currency 5… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:381 |
| `Comment` | Partial | `COMMENT : WS? ('\'' \| COLON? REM ' ') (LINE_CONTINUATION \| ~ ('\n' \| '\r'))* -> channel(HIDDEN)` — comments lex to the hidden channel and are ignored. Measured working: a full-line `'… | IDE/HexIDE.Runtime/Interpreter/Grammar/VB6.g4:2098 |
| `Line label` | Partial | `lineLabel : ambiguousIdentifier COLON` (VB6.g4:789). VisitLineLabel is a deliberate no-op - comment verbatim: "A label is just a jump target; the pc-driver maps its position. Executing it… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:993 |
| `LineContinuation` | Partial | `LINE_CONTINUATION : ' ' '_' '\r'? '\n' -> channel(HIDDEN)` — exactly one SPACE before the underscore, and the newline must follow the underscore immediately. Measured: `Debug.Print 1 +… | IDE/HexIDE.Runtime/Interpreter/Grammar/VB6.g4:2088 |
| `LineLabel` | Partial | `lineLabel : ambiguousIdentifier COLON`; the visitor is a no-op — "A label is just a jump target; the pc-driver maps its position. Executing it is a no-op." — and `ExecuteProcedureBody`… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:993 |
| `Object` | Partial | As a DECLARED type it is absent: `BaseTypeMapper.Map` falls through to `return null; // COLLECTION / OBJECT / CURRENCY (no token yet) / anything else -> caller decides`, and `ExtractType`… | IDE/HexIDE.Runtime/Interpreter/VB6Visitor.cs:136 |
| `String * N` | Partial | `fieldLength : MULT WS? (INTEGERLITERAL \| ambiguousIdentifier)` and `asTypeClause : AS WS (NEW WS)? type (WS fieldLength)?` — then throws. As a Dim / parameter / return type: `throw new… | IDE/HexIDE.Runtime/Interpreter/VB6Visitor.cs:128 |
| `StringLiteral` | Partial | The LEXER accepts the doubled-quote escape (`STRINGLITERAL : '"' (~ ["\r\n] \| '""')* '"'`) but the VISITOR never un-doubles it — it only strips the delimiters: `var str =… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:300 |
| `(( )) (redundant parens force ByVal)` | Supported | Falls out of the lvalue test rather than a dedicated rule: `TryGetArgLocation` returns a caller slot only for a bare `VsICSContext` with no typeHint and no dictionaryCall, so a… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:262 |
| `() (grouping)` | Supported | `VisitVsStruct` evaluates the single inner expression and returns it. Measured `(2 + 3) * 4` = 20 against `2 + 3 * 4` = 14, and `Not (True)` = False. The multi-element form `(a, b)` — not… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:959 |
| `, (argument separator / omitted argument)` | Supported | `ArgSlots` deliberately derives argument POSITIONS from the separators rather than from the present `argCall` nodes, so a blank slot becomes `Vb6Value.Missing` and later arguments do not… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:186 |
| `Boolean` | Supported | `if (baseType.BOOLEAN() != null) return Vb6Value.ValueType.Boolean;`. Measured `Dim b As Boolean : b = True` -> Boolean True. | IDE/HexIDE.Runtime/Interpreter/BaseTypeMapper.cs:22 |
| `Byte` | Supported | `if (baseType.BYTE() != null) return Vb6Value.ValueType.Byte;`; the declared type is recorded on the slot so later stores run `VbNumeric.CoerceOnStore`. Measured `Dim b As Byte : b = 5` ->… | IDE/HexIDE.Runtime/Interpreter/BaseTypeMapper.cs:20 |
| `Currency` | Supported | `if (baseType.CURRENCY() != null) return Vb6Value.ValueType.Currency;` — a fixed 4-decimal-place decimal, banker's-rounded at construction, rank 4 on the widening ladder. Measured `Dim c As… | IDE/HexIDE.Runtime/Interpreter/BaseTypeMapper.cs:23 |
| `Date` | Supported | `if (baseType.DATE() != null) return Vb6Value.ValueType.Date;`; Date is special-cased throughout VbNumeric — "Date + anything -> Date (adds serial days)", "Date - Date -> Double (day… | IDE/HexIDE.Runtime/Interpreter/BaseTypeMapper.cs:24 |
| `DateLiteral` | Supported | `DATELITERAL : HASH (~ [#\r\n])* HASH`; the visitor strips the hashes and parses invariantly — "VB6 date literals are culture-independent, US month/day/year — parse invariantly." `return… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:359 |
| `DecimalIntegerLiteral` | Supported | `ClassifyIntegerLiteral`: Integer when the value fits Int16, else Long when it fits Int32, else Double — "otherwise Integer if it fits Int16, else Long if it fits Int32, else Double."… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:375 |
| `Double` | Supported | `if (baseType.DOUBLE() != null) return Vb6Value.ValueType.Double;`. Measured `Dim d As Double : d = 1.5` -> Double 1.5. Tests: DeclaredTypeTests. | IDE/HexIDE.Runtime/Interpreter/BaseTypeMapper.cs:21 |
| `Empty` | Supported | `EMPTY_` is a real lexer token and the visitor returns `new Vb6Value(Vb6Value.ValueType.EmptyVariant)`. The comment records why it is a keyword: "It is a keyword, not a constant: `Dim Empty… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:345 |
| `ExponentLiteral` | Supported | An `e`/`E` exponent yields Double with or without a decimal point — "exponent without a dot -> Double (e.g. 1e5)". Measured `1.5E2` = 150#, `1.5E+2` = 150#, `1.5E-2` = 0.015#, `15e1` =… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:308 |
| `False` | Supported | `if (literalContext.literal().FALSE() is { }) { Vb6Value val = new Vb6Value(false); return val; }`. Measured -> Boolean False. | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:332 |
| `FloatingPointLiteral` | Supported | "VB6: an unsuffixed floating-point literal (with a '.' or exponent) defaults to Double"; the type-char suffixes `!`/`#`/`&`/`@`/`%` force Single/Double/Long/Currency/Integer at… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:308 |
| `Integer` | Supported | `if (baseType.INTEGER() != null) return Vb6Value.ValueType.Integer;`; the declared type is recorded so an assignment coerces to it and overflows raise Err 6 rather than widening — the… | IDE/HexIDE.Runtime/Interpreter/BaseTypeMapper.cs:18 |
| `Long` | Supported | `if (baseType.LONG() != null) return Vb6Value.ValueType.Long;`. Measured `Dim l As Long : l = 5` -> Long 5. | IDE/HexIDE.Runtime/Interpreter/BaseTypeMapper.cs:19 |
| `Nothing` | Supported | `return Vb6Value.Nothing; // a null object reference`. Measured: `Set o = Nothing` then `o Is Nothing` -> True; a class-typed `Dim` seeds Nothing so `c Is Nothing` is True until Set. Tests:… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:341 |
| `Null` | Supported | `return Vb6Value.Null;`. Propagation is implemented per operator: arithmetic returns Null, `&` returns Null only when BOTH operands are Null, `Eqv` returns Null, `Imp` carries the full… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:337 |
| `Single` | Supported | `if (baseType.SINGLE() != null) return Vb6Value.ValueType.Single;`. Measured `Dim s As Single : s = 1.5` -> Single 1.5. Single is rank 3 on the widening ladder, and `/` returns Single iff… | IDE/HexIDE.Runtime/Interpreter/BaseTypeMapper.cs:21 |
| `String` | Supported | `if (baseType.STRING() != null) return Vb6Value.ValueType.String;`; a declared String slot stringifies whatever is stored (oracle row `Dim s As String : s = 5` -> "5"). Measured `Dim s As… | IDE/HexIDE.Runtime/Interpreter/BaseTypeMapper.cs:17 |
| `True` | Supported | `if (literalContext.literal().TRUE() is { }) { Vb6Value val = new Vb6Value(true); return val; }`. Measured -> Boolean True; VB6's numeric True = -1 is honoured on the bitwise path (`if… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:327 |
| `UserDefinedTypeName` | Supported | `Dim x As <name>` resolves the complexType in order against `interpreter.Types` (a `Type` -> a fresh UDT instance), `interpreter.Enums` (-> Long 0) and class modules (-> Nothing, with the… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1530 |
| `Variant` | Supported | `if (baseType.VARIANT() != null) return Vb6Value.ValueType.EmptyVariant;`, and the declaration path deliberately records NO declared type for it — "an array, an object, a UDT and a Variant… | IDE/HexIDE.Runtime/Interpreter/BaseTypeMapper.cs:26 |

### Compiler directives and options — 47 names (17 absent, 9 partial, 8 supported)

| Name | Status | Detail | Source |
|---|---|---|---|
| `#Const` | **Won't load** | VB6.g4 has no #Const lexer token and no parser rule; the only HASH-prefixed directive tokens are MACRO_IF/MACRO_ELSEIF/MACRO_ELSE/MACRO_END_IF (VB6.g4:1479-1496). '#Const' instead lexes as… | IDE/HexIDE.Runtime/Interpreter/Grammar/VB6.g4:2062 |
| `#Else` | **Dies** | throws. macroElseBlockStmt (VB6.g4:397-399) is part of macroIfThenElseStmt and shares its fate: measured, '#If Win32 Then ... #Else ... #End If' raises VBCompileErrorException 'Conditional… | PrePass.cs:227 |
| `#ElseIf` | **Dies** | throws. macroElseIfBlockStmt (VB6.g4:393-395) is only reachable as part of macroIfThenElseStmt, so it fails with its parent: VBCompileErrorException 'Conditional compilation (#If / #Const)… | PrePass.cs:227 |
| `#End If` | **Dies** | throws, as the terminator of macroIfThenElseStmt (VB6.g4:385-387) - same two exceptions as #If (PrePass.cs:227 at module level, StatementExecutor.cs:1045 inside a procedure). | PrePass.cs:227 |
| `#If` | **Dies** | throws `"Conditional compilation (#If / #Const) is not supported"` — throws - with two different exceptions depending on where it sits. At module level PrePass hits it first: '' (PrePass.cs:227), under the comment… | PrePass.cs:227 |
| `Mac` | **Dies** | Not defined anywhere in the interpreter. Reachable only from #If, which throws first - measured with '#If Win32 Then ... #ElseIf Mac Then ... #End If': VBCompileErrorException 'Conditional… | PrePass.cs:227 |
| `VB_MemberFlags` | **Dies** | throws `"Attribute not implemented"` — VB6 writes this only at member level - inside a procedure body, or immediately after a module-level declaration - and both positions land in a block… | StatementExecutor.cs:321 |
| `VB_PredeclaredId` | **Dies** | but is silently discarded - no throw, and no behaviour. Emitted in the canonical class header (ModuleFileFormat.cs:37) and preserved verbatim, but a repo-wide grep for a consumer across… | ModuleFileFormat.cs:37 |
| `VB_ProcData.VB_Invoke_Func` | **Dies** | throws. The dotted name is handled by the grammar (attributeStmt takes an implicitCallStmt_InStmt, VB6.g4:137-139), so 'Attribute Foo.VB_ProcData.VB_Invoke_Func = "M14"' inside a Sub… | StatementExecutor.cs:321 |
| `VB_UserMemId` | **Dies** | throws `"Attribute not implemented"` — throws. Member-level only; the statement reaches StatementExecutor.VisitAttributeStmt, which is '' (StatementExecutor.cs:321). Measured: 'Public… | StatementExecutor.cs:321 |
| `VB_VarDescription` | **Dies** | throws `"Attribute not implemented"` — throws. VB6 writes it in the declarations section immediately after the variable it describes, which is past the contiguous header run, so it is… | StatementExecutor.cs:321 |
| `VB_VarHelpID` | **Dies** | throws `"Attribute not implemented"` — throws. Same declarations-section position as VB_VarDescription, therefore the same block-statement path: '' (StatementExecutor.cs:321). | StatementExecutor.cs:321 |
| `VB_VarMemberFlags` | **Dies** | throws. Measured: 'Public Foo As Long' followed by 'Attribute Foo.VB_VarMemberFlags = "40"' raises 'NotImplementedException: Attribute not implemented' (StatementExecutor.cs:321). | StatementExecutor.cs:321 |
| `VB_VarUserMemId` | **Dies** | throws `"Attribute not implemented"` — throws. Declarations-section position, so the block path applies: '' (StatementExecutor.cs:321). Measured at module top (before any code) it is… | StatementExecutor.cs:321 |
| `Vba6` | **Dies** | Not defined anywhere in the interpreter (repo-wide grep for Vba6/VBA6 across IDE/ and LspServer/ returned nothing). Reachable only from #If, which throws first (PrePass.cs:227 /… | PrePass.cs:227 |
| `Win16` | **Dies** | Not defined anywhere in the interpreter (the same repo-wide grep as Win32 returned no Win16 hits at all). Reachable only from #If, which throws first (PrePass.cs:227 /… | PrePass.cs:227 |
| `Win32` | **Dies** | Not defined anywhere in the interpreter. A repo-wide grep for a Win32 conditional-compilation constant across IDE/ and LspServer/ (.cs and .g4) returned only unrelated hits - Avalonia… | PrePass.cs:227 |
| `Attribute` | Partial | The statement parses everywhere VB6 allows it (attributeStmt, VB6.g4:137-139; reachable as a module header element at VB6.g4:62-64 and as a block statement at VB6.g4:147), but only ONE… | StatementExecutor.cs:321 |
| `Compare` | Partial | Exists only as the second word of the composite OPTION_COMPARE token (VB6.g4:1587-1589), consumed by optionCompareStmt (VB6.g4:72) and then discarded - PrePass.VisitOptionCompareStmt is '=>… | PrePass.cs:55 |
| `Explicit` | Partial | Exists only as the second word of the composite OPTION_EXPLICIT token (VB6.g4:1582-1584), consumed by optionExplicitStmt (VB6.g4:73) and honoured - 'RequireVariableDefinitions = true;'… | PrePass.cs:64 |
| `Option` | Partial | There is no standalone OPTION token. The four directives are single composite lexer tokens - OPTION_BASE, OPTION_EXPLICIT, OPTION_COMPARE, OPTION_PRIVATE_MODULE (VB6.g4:1577-1595) - each… | IDE/HexIDE.Runtime/Interpreter/Grammar/VB6.g4:1577 |
| `Option Compare` | Partial | and is accepted, but never honoured. PrePass.VisitOptionCompareStmt is '=> default;' under the comment: 'Accepted but not honoured - HexIDE always compares strings ordinally (Option Compare… | PrePass.cs:55 |
| `Option Explicit` | Partial | Collected by PrePass ('RequireVariableDefinitions = true;', PrePass.cs:62-66) and actually read at two sites: ExpressionExecutor.cs:475 ('if… | PrePass.cs:64 |
| `VB_Description` | Partial | Position decides the outcome, and the two positions differ. In the contiguous top-of-file header run it is accepted and ignored, and ModuleFileFormat names it as the reason header… | StatementExecutor.cs:321 |
| `VB_HelpID` | Partial | Same position split as VB_Description. Module-level, inside the contiguous top-of-file Attribute run: accepted and dropped (measured - 'Attribute VB_HelpID = 1234' at module top parses and… | StatementExecutor.cs:321 |
| `VB_Name` | Partial | The only VB_* attribute with any consumer, and the consumer is the serializer, not the interpreter. ModuleFileFormat.Header emits it for a new .bas/.cls (ModuleFileFormat.cs:34,40) and… | ModuleFileFormat.cs:75 |
| `Alias` | No-op | Optional clause of declareStmt ('(WS ALIAS WS STRINGLITERAL)?', VB6.g4:250-252; token ALIAS at VB6.g4:1002-1004). Parsed and discarded with the statement (PrePass.cs:178-183). Measured in… | PrePass.cs:183 |
| `Any` | No-op | There is no ANY token in VB6.g4 (grep confirms; the type keywords are enumerated as baseType). 'As Any' parses only because 'Any' falls through complexType -> ambiguousIdentifier. Inside a… | PrePass.cs:183 |
| `Declare` | No-op | in full (declareStmt, VB6.g4:250-252 - visibility, Sub/Function, type hints, argList and As-clause) and is then dropped whole. PrePass.VisitDeclareStmt is '=> default;' under the comment:… | PrePass.cs:183 |
| `Lib` | No-op | Mandatory clause of declareStmt ('... WS LIB WS STRINGLITERAL ...', VB6.g4:250-252; token LIB at VB6.g4:1444-1446). Parsed and then discarded with the whole statement by… | PrePass.cs:183 |
| `Module` | No-op | Exists only as the third word of the composite OPTION_PRIVATE_MODULE token (VB6.g4:1592-1594), consumed by optionPrivateModuleStmt (VB6.g4:74) and discarded - 'Accepted as a no-op - HexIDE… | PrePass.cs:60 |
| `Option Private Module` | No-op | PrePass.VisitOptionPrivateModuleStmt is '=> default;' under the comment: 'Accepted as a no-op - HexIDE doesn't enforce cross-project module-member visibility. Skipped (not thrown) so a… | PrePass.cs:60 |
| `Text` | No-op | A real standalone token (TEXT, VB6.g4:1792-1794) accepted as an Option Compare operand (VB6.g4:72) and then deliberately ignored: 'Accepted but not honoured - HexIDE always compares strings… | PrePass.cs:55 |
| `VB_Base` | No-op | Header-position attribute: accepted and dropped. Measured - 'Attribute VB_Base = "0{FCFB3D2A-A0FA-1068-A738-08002B3371B5}"' at module top parses (moduleAttributes, VB6.g4:62-64) and the… | ModuleFileFormat.cs:118 |
| `VB_Creatable` | No-op | Emitted verbatim in the canonical class header ("Attribute VB_Creatable = True\r\n", ModuleFileFormat.cs:36), stripped from the editable body on load and re-emitted unchanged on save. Read… | ModuleFileFormat.cs:36 |
| `VB_Customizable` | No-op | Header-position attribute: accepted and dropped. Measured - 'Attribute VB_Customizable = True' at module top parses and the module runs; in a .cls it is inside the contiguous run… | ModuleFileFormat.cs:118 |
| `VB_Exposed` | No-op | Emitted in the canonical class header ("Attribute VB_Exposed = False\r\n", ModuleFileFormat.cs:38), preserved verbatim on round-trip, read by nothing. docs/interpreter-gaps.md:163-164:… | ModuleFileFormat.cs:38 |
| `VB_GlobalNameSpace` | No-op | Emitted in the canonical class header ("Attribute VB_GlobalNameSpace = False\r\n", ModuleFileFormat.cs:35), preserved verbatim, read by nothing. docs/interpreter-gaps.md:163-164 groups it… | ModuleFileFormat.cs:35 |
| `VB_TemplateDerived` | No-op | Header-position attribute: accepted and dropped. Measured - 'Attribute VB_TemplateDerived = False' at module top parses and the module runs; preserved verbatim by ModuleFileFormat's… | ModuleFileFormat.cs:118 |
| `Base` | Supported | Exists only as the second word of the composite OPTION_BASE token (VB6.g4:1577-1579), consumed by optionBaseStmt (VB6.g4:71) and honoured at PrePass.cs:47. See Option Base for the full… | PrePass.cs:47 |
| `Begin` | Supported | Two distinct roles, both handled. In a .frm it opens a control node: 'else if (line.StartsWith("Begin")) { var component = ParseBegin(line); ... componentStack.Push(component); }'… | IDE/HexIDE.Runtime/Serialization/VbFrmFormatDeserializer.cs:118 |
| `BeginProperty` | Supported | Opens a nested property bag; implemented with a stack so bags nest (VbFrmFormatDeserializer.cs:67-88), written back at VbFrmFormatSerializer.cs:223. The stack is deliberate: 'A stack rather… | IDE/HexIDE.Runtime/Serialization/VbFrmFormatDeserializer.cs:67 |
| `Binary` | Supported | A real standalone token (BINARY, VB6.g4:1047-1049) accepted as an Option Compare operand (VB6.g4:72). The directive itself is discarded (PrePass.cs:51-55), but HexIDE's fixed behaviour IS… | PrePass.cs:55 |
| `Class` | Supported | The CLASS suffix that distinguishes a .cls header from a .frm one. ModuleFileFormat.SplitHeader tests for it to decide the header shape - 'lines[i].IndexOf("CLASS"… | ModuleFileFormat.cs:105 |
| `EndProperty` | Supported | Closes the innermost property bag and folds its verbatim lines into the parent, or onto the component when it was outermost (VbFrmFormatDeserializer.cs:89-104); written back at… | IDE/HexIDE.Runtime/Serialization/VbFrmFormatDeserializer.cs:89 |
| `Option Base` | Supported | Collected by PrePass ('ArrayBase = int.Parse(context.INTEGERLITERAL().GetText());', PrePass.cs:45-49) and applied on both declaration paths: module-level Dim arrays (PrePass.cs:118) and… | PrePass.cs:47 |
| `Version` | Supported | Handled on three independent paths. Form/control files: the deserializer skips the line ('if (line.StartsWith("VERSION")) { continue; }', VbFrmFormatDeserializer.cs:52-55) and the… | IDE/HexIDE.Runtime/Serialization/VbFrmFormatDeserializer.cs:52 |

### In-box objects — 123 names (98 absent, 8 partial, 17 supported)

| Name | Status | Detail | Source |
|---|---|---|---|
| `AmbientProperties` | **Dies** | Measured for `Dim a As AmbientProperties`: VBCompileErrorException: "User-defined type not defined: AmbientProperties" (StatementExecutor.cs:1545). The route to a real instance is gone too:… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `AmbientProperties.BackColor` | **Dies** | Unreachable: there is no `Ambient` and no `UserControl` global to read it from — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" and "Variable not defined… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `AmbientProperties.DisplayAsDefault` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `AmbientProperties.DisplayName` | **Dies** | Measured directly: `Debug.Print UserControl.Ambient.DisplayName` gives VBVariableNotDefinedException: "Variable not defined (UserControl)" (ExpressionExecutor.cs:434). The property name… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `AmbientProperties.Font` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `AmbientProperties.ForeColor` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `AmbientProperties.LocaleID` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `AmbientProperties.MessageReflect` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `AmbientProperties.Palette` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `AmbientProperties.RightToLeft` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `AmbientProperties.ScaleUnits` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `AmbientProperties.ShowGrabHandles` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `AmbientProperties.ShowHatching` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `AmbientProperties.SupportsMnemonics` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `AmbientProperties.TextAlign` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `AmbientProperties.UIDead` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `AmbientProperties.UserMode` | **Dies** | Measured directly: `Debug.Print Ambient.UserMode` gives VBVariableNotDefinedException: "Variable not defined (Ambient)" and `Debug.Print UserControl.Ambient.UserMode` gives "Variable not… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `App.HelpFile` | **Dies** | the name is absent from VbApp.TryGetProperty's switch, so the read falls through to ExpressionExecutor.cs:557. Measured verbatim: "Compile error: Method or data member not found (HelpFile… | VbApp.cs:123-148 |
| `App.LogEvent` | **Dies** | VbApp.Call has no dispatch at all - VbApp.cs:112-113 is: public void Call(string method, List<Vb6Value> args) => throw new VBRunTimeException(VBStandardError.MethodOrDataMemberNotFound… | VbApp.cs:112 |
| `App.LogMode` | **Dies** | the name is absent from VbApp.TryGetProperty's switch, so the read falls through to ExpressionExecutor.cs:557. Measured verbatim: "Compile error: Method or data member not found (LogMode in… | VbApp.cs:123-148 |
| `App.LogPath` | **Dies** | the name is absent from VbApp.TryGetProperty's switch, so the read falls through to ExpressionExecutor.cs:557. Measured verbatim: "Compile error: Method or data member not found (LogPath in… | VbApp.cs:123-148 |
| `App.StartLogging` | **Dies** | VbApp.Call has no dispatch at all - VbApp.cs:112-113 is: public void Call(string method, List<Vb6Value> args) => throw new VBRunTimeException(VBStandardError.MethodOrDataMemberNotFound… | VbApp.cs:112 |
| `App.StartMode` | **Dies** | the name is absent from VbApp.TryGetProperty's switch, so the read falls through to ExpressionExecutor.cs:557. Measured verbatim: "Compile error: Method or data member not found (StartMode… | VbApp.cs:123-148 |
| `App.TaskVisible` | **Dies** | the name is absent from VbApp.TryGetProperty's switch, so the read falls through to ExpressionExecutor.cs:557. Measured verbatim: "Compile error: Method or data member not found… | VbApp.cs:123-148 |
| `AsyncProperty` | **Dies** | Measured for `Dim a As AsyncProperty`: VBCompileErrorException: "User-defined type not defined: AsyncProperty" (StatementExecutor.cs:1545). The name appears nowhere in the repository. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `AsyncProperty_VB5` | **Dies** | Measured for `Dim a As AsyncProperty_VB5`: VBCompileErrorException: "User-defined type not defined: AsyncProperty_VB5" (StatementExecutor.cs:1545). | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `Circle` | **Dies** | throws `"Only single element supported"` — fails at run time - but NOT with a name-resolution error. VB6.g4 has no CIRCLE token and no circleStmt rule, so `Circle (100, 100), 50` parses as a… | ExpressionExecutor.cs:962 |
| `Clipboard` | **Dies** | never seeded. BasicInterpreter.cs:299-301 seeds only Debug, Err and App as program globals. Measured verbatim: "Compile error: Variable not defined (Clipboard)". | BasicInterpreter.cs:299-301 |
| `Clipboard.Clear` | **Dies** | Measured (statement position): a VBRunTimeException from StatementExecutor.cs:1755 - "Unknown method Clear on <Right(EmptyVariant)>()" - because the Clipboard lead resolves to Empty, so… | StatementExecutor.cs:1755 |
| `Clipboard.GetData` | **Dies** | In expression position the member-chain lead is resolved by ExpressionExecutor.cs:429-434, which throws before the member is ever looked at. Measured verbatim for Clipboard.GetText() and… | ExpressionExecutor.cs:434 |
| `Clipboard.GetFormat` | **Dies** | In expression position the member-chain lead is resolved by ExpressionExecutor.cs:429-434, which throws before the member is ever looked at. Measured verbatim for Clipboard.GetText() and… | ExpressionExecutor.cs:434 |
| `Clipboard.GetText` | **Dies** | In expression position the member-chain lead is resolved by ExpressionExecutor.cs:429-434, which throws before the member is ever looked at. Measured verbatim for Clipboard.GetText() and… | ExpressionExecutor.cs:434 |
| `Clipboard.SetData` | **Dies** | Measured verbatim: "Run-time error: Unknown method SetData on <Right(EmptyVariant)>()". | StatementExecutor.cs:1755 |
| `Clipboard.SetText` | **Dies** | Measured verbatim: "Run-time error: Unknown method SetText on <Right(EmptyVariant)>()". | StatementExecutor.cs:1755 |
| `Cls` | **Dies** | but is not implemented, and FAILS SILENTLY in the bare form. VB6.g4 has no CLS token and no clsStmt rule, so `Cls` parses as a zero-argument bare procedure call. VisitICS_B_ProcedureCall… | StatementExecutor.cs:1802 |
| `Collection` | **Dies** | COLLECTION is a real grammar token (VB6.g4:1102) listed in baseType (VB6.g4:749), so `Dim c As Collection` reaches BaseTypeMapper.Map, which returns null for it (BaseTypeMapper.cs:27… | IDE/HexIDE.Runtime/Interpreter/PrePass.cs:145 |
| `Collection._NewEnum` | **Dies** | The name lexes as an ordinary IDENTIFIER (VB6.g4:2082-2083; the LETTER fragment at :2110 includes `_`), and the bracketed form `[_NewEnum]` parses too — but `_NewEnum`/`NewEnum` appears… | IDE/HexIDE.Runtime/Interpreter/Exceptions.cs:53 |
| `Collection.Add` | **Dies** | No Collection object can exist (see Collection), and no Add handler exists on any runtime proxy. Measured with the call-statement form on a Variant holder: VBRunTimeException: "Unknown… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:623 |
| `Collection.Count` | **Dies** | Measured: VBMethodOrDataMemberNotFoundException: "Method or data member not found (Count in Right(EmptyVariant))". No Collection type exists to carry it. | IDE/HexIDE.Runtime/Interpreter/Exceptions.cs:53 |
| `Collection.Item` | **Dies** | Measured explicit form: VBMethodOrDataMemberNotFoundException: "Method or data member not found (Item(1) in Right(EmptyVariant))". The implicit default-member form `c(1)` fails differently… | IDE/HexIDE.Runtime/Interpreter/Exceptions.cs:53 |
| `Collection.Remove` | **Dies** | Measured: VBRunTimeException: "Unknown method Remove on <Right(EmptyVariant)>()" (StatementExecutor.cs:623). | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:623 |
| `ContainedControls` | **Dies** | Measured for `Dim c As ContainedControls`: VBCompileErrorException: "User-defined type not defined: ContainedControls" (StatementExecutor.cs:1545). | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `Controls` | **Dies** | a form's Controls collection does not exist. Measured verbatim: "Compile error: Variable not defined (Controls)". A form binds only its own name and "Me" (VBLoader.cs:378-379); no… | VBLoader.cs:378-379 |
| `Controls.Add` | **Dies** | Measured verbatim: "Run-time error: Unknown method Add on <Right(EmptyVariant)>()". | StatementExecutor.cs:1755 |
| `Controls.Count` | **Dies** | Measured verbatim for Controls.Count: "Compile error: Variable not defined (Controls)". The same applies to Controls.Item(i); the default-member form Controls(i) instead gives "Sub or… | ExpressionExecutor.cs:434 |
| `Controls.Item` | **Dies** | Measured verbatim for Controls.Count: "Compile error: Variable not defined (Controls)". The same applies to Controls.Item(i); the default-member form Controls(i) instead gives "Sub or… | ExpressionExecutor.cs:434 |
| `Controls.Remove` | **Dies** | Measured pattern, identical to Controls.Add: "Run-time error: Unknown method Remove on <Right(EmptyVariant)>()". | StatementExecutor.cs:1755 |
| `DataBinding` | **Dies** | Measured for `Dim d As DataBinding`: VBCompileErrorException: "User-defined type not defined: DataBinding" (StatementExecutor.cs:1545). The name appears nowhere in the runtime. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `DataBindings` | **Dies** | Measured for `Dim d As DataBindings`: VBCompileErrorException: "User-defined type not defined: DataBindings" (StatementExecutor.cs:1545). The name appears nowhere in the runtime. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `DataMembers` | **Dies** | Measured for `Dim d As DataMembers`: VBCompileErrorException: "User-defined type not defined: DataMembers" (StatementExecutor.cs:1545). The name appears nowhere in the repository. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `DataObject` | **Dies** | `DataObject` is not a grammar keyword, so it takes the complexType path and throws VBCompileErrorException: "User-defined type not defined: DataObject" — measured for `Dim d As DataObject`… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `DataObject.Clear` | **Dies** | Measured: VBRunTimeException: "Unknown method Clear on <Right(EmptyVariant)>()" (StatementExecutor.cs:623). No DataObject type exists to own it. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:623 |
| `DataObject.Files` | **Dies** | Measured: VBMethodOrDataMemberNotFoundException: "Method or data member not found (Files in Right(EmptyVariant))". | IDE/HexIDE.Runtime/Interpreter/Exceptions.cs:53 |
| `DataObject.GetData` | **Dies** | Measured: VBMethodOrDataMemberNotFoundException: "Method or data member not found (GetData(1) in Right(EmptyVariant))". | IDE/HexIDE.Runtime/Interpreter/Exceptions.cs:53 |
| `DataObject.GetFormat` | **Dies** | Measured: VBMethodOrDataMemberNotFoundException: "Method or data member not found (GetFormat(1) in Right(EmptyVariant))". | IDE/HexIDE.Runtime/Interpreter/Exceptions.cs:53 |
| `DataObject.SetData` | **Dies** | Measured: VBRunTimeException: "Unknown method SetData on <Right(EmptyVariant)>()" (StatementExecutor.cs:623). | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:623 |
| `DataObjectFiles` | **Dies** | Measured for `Dim f As DataObjectFiles`: VBCompileErrorException: "User-defined type not defined: DataObjectFiles" (StatementExecutor.cs:1545). The name appears nowhere in the repository. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `Debug.Assert` | **Dies** | as an ICS_B_MemberProcedureCall and throws at run time. DebugProxy.Call handles only "Print"; everything else hits `throw new Exception("No method named " + method)`. Measured verbatim:… | IDE/HexIDE.Runtime/Interpreter/DebugProxy.cs:24 |
| `Err.HelpContext` | **Dies** | TryGetProperty (VbErr.cs:68) has cases only for number/description/source; the fall-through in ExpressionExecutor then requires a Control and otherwise throws… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:556 |
| `Err.HelpFile` | **Dies** | Measured: `Debug.Print Err.HelpFile` -> VBMethodOrDataMemberNotFoundException, "Method or data member not found (HelpFile in Right(CSharpProxyObject))". No case in… | IDE/HexIDE.Runtime/Interpreter/VbErr.cs:68 |
| `Err.LastDllError` | **Dies** | Measured verbatim: "Method or data member not found (LastDllError in Right(CSharpProxyObject))". | IDE/HexIDE.Runtime/Interpreter/VbErr.cs:68 |
| `EventInfo` | **Dies** | Measured for `Dim e As EventInfo`: VBCompileErrorException: "User-defined type not defined: EventInfo" (StatementExecutor.cs:1545). The name appears nowhere in the runtime. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `EventParameter` | **Dies** | Measured for `Dim e As EventParameter`: VBCompileErrorException: "User-defined type not defined: EventParameter" (StatementExecutor.cs:1545). The name appears nowhere in the runtime. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `EventParameters` | **Dies** | Measured for `Dim e As EventParameters`: VBCompileErrorException: "User-defined type not defined: EventParameters" (StatementExecutor.cs:1545). The name appears nowhere in the repository. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `Forms` | **Dies** | the loaded-forms collection is never built. Measured verbatim: "Compile error: Variable not defined (Forms)". BasicInterpreter.cs:299-301 seeds only Debug, Err and App as program globals. | BasicInterpreter.cs:299-301 |
| `Forms.Count` | **Dies** | Measured verbatim: "Compile error: Variable not defined (Forms)". | ExpressionExecutor.cs:434 |
| `Forms.Item` | **Dies** | Measured verbatim for the default-member form Forms(0).Caption: "Compile error: Sub or Function not defined (Forms)" - a parenthesised lead is routed to EvaluateProcedureOrArrayCall, which… | ExpressionExecutor.cs:637 |
| `Hyperlink` | **Dies** | Measured for `Dim h As Hyperlink`: VBCompileErrorException: "User-defined type not defined: Hyperlink" (StatementExecutor.cs:1545). The name appears nowhere in the runtime. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `LicenseInfo` | **Dies** | Measured for `Dim l As LicenseInfo`: VBCompileErrorException: "User-defined type not defined: LicenseInfo" (StatementExecutor.cs:1545). The name appears nowhere in the repository. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `Licenses` | **Dies** | Measured for `Debug.Print Licenses.Count`: VBVariableNotDefinedException: "Variable not defined (Licenses)" (ExpressionExecutor.cs:434) — the global object is not seeded; only Debug, Err… | IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.cs:299 |
| `ParentControls` | **Dies** | Measured for `Dim p As ParentControls`: VBCompileErrorException: "User-defined type not defined: ParentControls" (StatementExecutor.cs:1545). The name appears nowhere in the runtime. | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `ParentControls.ParentControlsType` | **Dies** | Measured on a Variant holder: VBMethodOrDataMemberNotFoundException: "Method or data member not found (ParentControlsType in Right(EmptyVariant))". The owning ParentControls type does not… | IDE/HexIDE.Runtime/Interpreter/Exceptions.cs:53 |
| `Printer` | **Dies** | never seeded. BasicInterpreter.cs:299-301 seeds only Debug, Err and App as program globals. All three positions measured: read -> "Compile error: Variable not defined (Printer)"; assignment… | BasicInterpreter.cs:299-301 |
| `Printer.ColorMode` | **Dies** | because the Printer object it hangs off is never seeded. MEASURED for "Printer.Orientation = 2": "Run-time error '424': Object required Can't find variable Printer"… | BasicInterpreter.cs:299-301 |
| `Printer.Duplex` | **Dies** | because the Printer object it hangs off is never seeded. MEASURED for "Printer.Orientation = 2": "Run-time error '424': Object required Can't find variable Printer"… | BasicInterpreter.cs:299-301 |
| `Printer.Orientation` | **Dies** | because the Printer object it hangs off is never seeded. MEASURED for "Printer.Orientation = 2": "Run-time error '424': Object required Can't find variable Printer"… | BasicInterpreter.cs:299-301 |
| `Printer.PaperBin` | **Dies** | because the Printer object it hangs off is never seeded. MEASURED for "Printer.Orientation = 2": "Run-time error '424': Object required Can't find variable Printer"… | BasicInterpreter.cs:299-301 |
| `Printer.PaperSize` | **Dies** | because the Printer object it hangs off is never seeded. MEASURED for "Printer.Orientation = 2": "Run-time error '424': Object required Can't find variable Printer"… | BasicInterpreter.cs:299-301 |
| `Printer.PrintQuality` | **Dies** | because the Printer object it hangs off is never seeded. MEASURED for "Printer.Orientation = 2": "Run-time error '424': Object required Can't find variable Printer"… | BasicInterpreter.cs:299-301 |
| `Printers` | **Dies** | Measured verbatim for Printers(0).DeviceName: "Compile error: Sub or Function not defined (Printers)"; for Printers.Count: "Compile error: Variable not defined (Printers)"… | BasicInterpreter.cs:299-301 |
| `PropertyBag` | **Dies** | Measured for `Dim p As PropertyBag`: VBCompileErrorException: "User-defined type not defined: PropertyBag" (StatementExecutor.cs:1545); same message for `Set p = New PropertyBag`… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `PropertyBag.Contents` | **Dies** | Measured: VBMethodOrDataMemberNotFoundException: "Method or data member not found (Contents in Right(EmptyVariant))". | IDE/HexIDE.Runtime/Interpreter/Exceptions.cs:53 |
| `PropertyBag.ReadProperty` | **Dies** | Measured on a Variant holder: VBMethodOrDataMemberNotFoundException: "Method or data member not found (ReadProperty(\"A\", 0) in Right(EmptyVariant))". Measured on a real `PropBag As… | IDE/HexIDE.Runtime/Interpreter/Exceptions.cs:53 |
| `PropertyBag.WriteProperty` | **Dies** | Measured: VBRunTimeException: "Unknown method WriteProperty on <Right(EmptyVariant)>()" (StatementExecutor.cs:623). | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:623 |
| `PropertyBag_VB5` | **Dies** | The name lexes as an ordinary IDENTIFIER (digits and `_` are both in LETTERORDIGIT, VB6.g4:2115). Measured for `Dim p As PropertyBag_VB5` and for `Set p = New PropertyBag_VB5`:… | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `PSet` | **Dies** | throws `"Only single element supported"` — fails at run time on the coordinate pair, exactly as Circle does. VB6.g4 has no PSET token and no psetStmt rule. `PSet (10, 20), vbRed` parses as a… | ExpressionExecutor.cs:962 |
| `Scale` | **Dies** | throws `"Only single element supported"` — fails at run time. VB6.g4 has no SCALE token and no scaleStmt rule. `Scale (0, 0)-(100, 100)` parses as a bare procedure call with one argument - a… | ExpressionExecutor.cs:962 |
| `Screen` | **Dies** | never seeded. BasicInterpreter.cs:299-301 seeds only Debug, Err and App as program globals. Measured verbatim: "Compile error: Variable not defined (Screen)"; an assignment such as… | BasicInterpreter.cs:299-301 |
| `Screen.ActiveControl` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs:299-301 |
| `Screen.ActiveForm` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs:299-301 |
| `Screen.FontCount` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs:299-301 |
| `Screen.Fonts` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs:299-301 |
| `Screen.Height` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs:299-301 |
| `Screen.MousePointer` | **Dies** | Measured verbatim for "Screen.MousePointer = 11": "Run-time error '424': Object required Can't find variable Screen" (StatementExecutor.cs:856). | StatementExecutor.cs:856 |
| `Screen.TwipsPerPixelX` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs:299-301 |
| `Screen.TwipsPerPixelY` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs:299-301 |
| `Screen.Width` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs:299-301 |
| `SelectedControls` | **Dies** | Measured for `Dim s As SelectedControls`: VBCompileErrorException: "User-defined type not defined: SelectedControls" (StatementExecutor.cs:1545). | IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs:1545 |
| `App.ProductName` | Partial | Resolves, but is hardwired to the empty string regardless of the project - it is not read from AppInfo at all. VbApp.cs:133 is: case "productname": value = ""; return true; | VbApp.cs:133 |
| `ClipBoardConstants` | Partial | The nine MEMBER constants are all registered in the built-in constant table and resolve as bare names - vbCFBitmap=2, vbCFDIB=8, vbCFEMetafile=14, vbCFFiles=15, vbCFLink=-16640… | VB6BuiltIns.cs:91-99 |
| `Debug` | Partial | Seeded as a program-global CSharpProxy in every module env (BasicInterpreter.cs:299). DebugProxy.Call implements exactly one member: `if (method == "Print")`. Every other member falls to… | IDE/HexIDE.Runtime/Interpreter/DebugProxy.cs:19 |
| `Debug.Print` | Partial | DebugProxy.Call routes to IBasicStandardLibrary.DebugPrint - the Immediate window in the IDE, a capture list under test - preserving the typed Vb6Value. A bare `Debug.Print` prints a blank… | IDE/HexIDE.Runtime/Interpreter/DebugProxy.cs:22 |
| `Err` | Partial | One program-global VbErr shared by every module env (BasicInterpreter.cs:300, BasicInterpreter.cs:320). Number/Description/Source are readable and writable through ICSharpPropertyBag; Clear… | IDE/HexIDE.Runtime/Interpreter/VbErr.cs:68 |
| `Err.Raise` | Partial | VbErr.Call `case "raise"` forwards only the first three arguments - `Raise(ToLong(args[0]), args.Count >= 2 ? ... : null, args.Count >= 3 ? ... : null)`. Arguments 4 (HelpFile) and 5… | IDE/HexIDE.Runtime/Interpreter/VbErr.cs:56 |
| `Err.Source` | Partial | Readable and writable (VbErr.cs:74, VbErr.cs:86) and set by Err.Raise when a Source argument is supplied - measured `Err.Raise 5, "src", "desc"` -> Source "src". But Capture deliberately… | IDE/HexIDE.Runtime/Interpreter/VbErr.cs:32 |
| `Global` | Partial | Two distinct VB6 meanings; only one is implemented. (1) The DECLARATION KEYWORD works: GLOBAL is in the visibility rule (Grammar/VB6.g4:836, "visibility : PRIVATE \| PUBLIC \| FRIEND \|… | PrePass.cs:92 |
| `App` | Supported | Seeded as a program-global ICSharpProxy/ICSharpPropertyBag into every module env at BasicInterpreter.cs:301 ("SeedProgramGlobal(\"App\", () => new Vb6Value(App));"). Project identity is… | VbApp.cs:108 |
| `App.Comments` | Supported | Read via VbApp.TryGetProperty. Measured: App.Comments returned the empty string cleanly (probe printed "[]"). | VbApp.cs:142 |
| `App.CompanyName` | Supported | Read via VbApp.TryGetProperty. Measured: App.Comments returned the empty string cleanly (probe printed "[]"). | VbApp.cs:138 |
| `App.EXEName` | Supported | Read via VbApp.TryGetProperty; the value is the .vbp file's own name without extension (AppInfo.FromProject, VbApp.cs:47). | VbApp.cs:128 |
| `App.FileDescription` | Supported | Read via VbApp.TryGetProperty. Measured: App.Comments returned the empty string cleanly (probe printed "[]"). | VbApp.cs:139 |
| `App.LegalCopyright` | Supported | Read via VbApp.TryGetProperty. Measured: App.Comments returned the empty string cleanly (probe printed "[]"). | VbApp.cs:140 |
| `App.LegalTrademarks` | Supported | Read via VbApp.TryGetProperty. Measured: App.Comments returned the empty string cleanly (probe printed "[]"). | VbApp.cs:141 |
| `App.Major` | Supported | Read via VbApp.TryGetProperty from the .vbp MajorVer/MinorVer/RevisionVer keys. | VbApp.cs:135 |
| `App.Minor` | Supported | Read via VbApp.TryGetProperty from the .vbp MajorVer/MinorVer/RevisionVer keys. | VbApp.cs:136 |
| `App.Path` | Supported | Read via VbApp.TryGetProperty; the folder containing the .vbp, no trailing separator. | VbApp.cs:129 |
| `App.PrevInstance` | Supported | Read via VbApp.TryGetProperty; hardwired to False. | VbApp.cs:145 |
| `App.Revision` | Supported | Read via VbApp.TryGetProperty from the .vbp MajorVer/MinorVer/RevisionVer keys. | VbApp.cs:137 |
| `App.Title` | Supported | Read at VbApp.cs:127 and WRITTEN at VbApp.cs:151-157 - the only App member with a setter. Also fills an omitted MsgBox/InputBox caption via TitleOrNull (VbApp.cs:121). | VbApp.cs:127 |
| `Err.Clear` | Supported | VbErr.Call dispatches `case "clear"` (case-insensitive on the method name) to Clear(), which zeroes Number and empties Description and Source. Covered by… | IDE/HexIDE.Runtime/Interpreter/VbErr.cs:53 |
| `Err.Description` | Supported | Read via TryGetProperty case "description" (VbErr.cs:73), written via TrySetProperty (VbErr.cs:85). A trapped runtime error populates it from VBStandardError.Description in Capture… | IDE/HexIDE.Runtime/Interpreter/VbErr.cs:73 |
| `Err.Number` | Supported | Read as a VB6 Long (VbErr.cs:72 comment: "VB6 Err.Number is a Long"), assignable via TrySetProperty (VbErr.cs:84). Populated by Capture on a trapped VBRunTimeException. Measured:… | IDE/HexIDE.Runtime/Interpreter/VbErr.cs:72 |
| `Me` | Supported | Bound as an ordinary env variable named "Me" at each of the three sites that can have an instance. CLASS MODULE: BasicInterpreter.cs:531, "ExecutionContext.AllocVariable(callee, \"Me\"… | BasicInterpreter.cs:531 |

## Intrinsic constants — 621 names in 89 families

Constants are half the language surface by count and would swamp a table, so they are summarised by
family. They also have almost no nuance individually: a constant either resolves to the right value or it
does not.

**The interesting gap is structural, not per-name.** In VB6 these are not flat constants — they are
members of *enums* published by the VBA and VB type libraries (`ColorConstants`, `VbMsgBoxStyle`,
`KeyCodeConstants`, `VbVarType`, and so on), which is why the Object Browser was full of them. HexIDE
implements the **values** as a flat name→value table (`TryGetBuiltInConstant`) but does **not** register
the **enum types**. Two consequences: `Dim x As VbMsgBoxStyle` fails, and the Object Browser has no
enum→members tree to render even though it holds every value. Many of the Partial rows below are exactly
this — the bare name resolves, the type-qualified form does not.

A second, larger cause of Partial: a constant whose **consumer** is absent. `vbSrcCopy` is a correct
value that nothing can use while `PaintPicture` is unimplemented, and the same is true across the
drawing, OLE, DDE and drag-drop families.

| Family (VB6 enum) | Supported | Partial | Absent | Total |
|---|---:|---:|---:|---:|
| Constants: menu accelerators (MenuAccelleratorConstants) | 75 | 0 | 0 | 75 |
| Printer paper size (PrinterObjectConstants / DMPAPER_*) | 42 | 0 | 0 | 42 |
| OLE container control (OLEContainerConstants family) | 0 | 34 | 0 | 34 |
| Constants: system colour (SystemColorConstants) | 29 | 0 | 0 | 29 |
| VarType (VbVarType) | 14 | 4 | 0 | 18 |
| Constants: mouse pointer (MousePointerConstants) | 0 | 17 | 0 | 17 |
| Constants: draw mode (DrawModeConstants) | 16 | 0 | 0 | 16 |
| AsyncRead status codes (AsyncStatusCodeConstants) | 0 | 13 | 0 | 13 |
| Constants: raster op (RasterOpConstants) | 13 | 0 | 0 | 13 |
| Data control Validate action | 0 | 12 | 0 | 12 |
| Printer paper bin (PrinterObjectConstants / DMBIN_*) | 12 | 0 | 0 | 12 |
| Constants: scale mode (ScaleModeConstants) | 11 | 0 | 0 | 11 |
| IME mode (VbIMEMode) | 0 | 11 | 0 | 11 |
| String constants (miscellaneous) | 3 | 0 | 7 | 10 |
| Clipboard formats (ClipBoardConstants) | 0 | 9 | 0 | 9 |
| IME status (IMEStatus returns) | 0 | 9 | 0 | 9 |
| StrConv (VbStrConv) | 0 | 9 | 0 | 9 |
| VarType - legacy vbV* aliases | 9 | 0 | 0 | 9 |
| Constants: colour (ColorConstants) | 8 | 0 | 0 | 8 |
| Constants: fill style (FillStyleConstants) | 8 | 0 | 0 | 8 |
| DDE link modes (LinkConstants) | 0 | 8 | 0 | 8 |
| Date - FirstDayOfWeek (VbDayOfWeek) | 1 | 7 | 0 | 8 |
| File attributes (VbFileAttribute) | 0 | 8 | 0 | 8 |
| Form BorderStyle | 1 | 6 | 0 | 7 |
| MsgBox return | 7 | 0 | 0 | 7 |
| App log mode (LogModeConstants) | 6 | 0 | 0 | 6 |
| Constants: draw style (DrawStyleConstants) | 6 | 0 | 0 | 6 |
| Constants: palette mode (PaletteModeConstants) | 6 | 0 | 0 | 6 |
| Constants: shape (ShapeConstants) | 6 | 0 | 0 | 6 |
| MsgBox buttons | 6 | 0 | 0 | 6 |
| QueryUnload UnloadMode | 0 | 6 | 0 | 6 |
| Shape/Line BorderStyle | 6 | 0 | 0 | 6 |
| Shell window style (VbAppWinStyle) | 6 | 0 | 0 | 6 |
| AsyncRead option flags (AsyncReadConstants) | 0 | 5 | 0 | 5 |
| Control Align | 0 | 5 | 0 | 5 |
| Date format (VbDateTimeFormat) | 0 | 5 | 0 | 5 |
| LoadPicture size (LoadPictureSizeConstants) | 5 | 0 | 0 | 5 |
| OLE drag-drop modes (OLEDragConstants / OLEDropConstants) | 0 | 5 | 0 | 5 |
| Picture type (PictureTypeConstants) | 5 | 0 | 0 | 5 |
| PopupMenu flags | 0 | 5 | 0 | 5 |
| CallType (VbCallType) | 0 | 4 | 0 | 4 |
| Comparison (VbCompareMethod) | 1 | 2 | 1 | 4 |
| Constants: hit-test (HitResultConstants) | 4 | 0 | 0 | 4 |
| DDE LinkError codes (LinkErrorConstants) | 0 | 4 | 0 | 4 |
| Form StartUpPosition | 3 | 1 | 0 | 4 |
| LoadPicture colour depth (LoadPictureColorConstants) | 4 | 0 | 0 | 4 |
| MDI Arrange | 0 | 4 | 0 | 4 |
| MsgBox default button | 1 | 3 | 0 | 4 |
| MsgBox flags | 0 | 4 | 0 | 4 |
| MsgBox icon | 4 | 0 | 0 | 4 |
| OLE drag-drop effects (OLEDropEffectConstants) | 0 | 4 | 0 | 4 |
| Printer print quality (PrinterObjectConstants / DMRES_*) | 4 | 0 | 0 | 4 |
| ScrollBars | 0 | 4 | 0 | 4 |
| App log event type (LogEventTypeConstants) | 3 | 0 | 0 | 3 |
| AsyncRead target types (AsyncTypeConstants) | 0 | 3 | 0 | 3 |
| CheckBox Value | 3 | 0 | 0 | 3 |
| ComboBox Style | 0 | 3 | 0 | 3 |
| Constants: mouse buttons (MouseButtonConstants) | 3 | 0 | 0 | 3 |
| Constants: shift mask (ShiftConstants) | 3 | 0 | 0 | 3 |
| Data control DefaultCursorType | 0 | 3 | 0 | 3 |
| Data control EOFAction | 0 | 3 | 0 | 3 |
| Data control RecordsetType | 0 | 3 | 0 | 3 |
| Date - FirstWeekOfYear (VbFirstWeekOfYear) | 0 | 3 | 0 | 3 |
| Drag-over state (DragOverConstants) | 0 | 3 | 0 | 3 |
| ListBox MultiSelect | 0 | 3 | 0 | 3 |
| LoadResPicture resource type (VbResourceType) | 3 | 0 | 0 | 3 |
| Printer duplex (PrinterObjectConstants / DMDUP_*) | 1 | 2 | 0 | 3 |
| Text alignment | 3 | 0 | 0 | 3 |
| TriState (VbTriState) | 0 | 3 | 0 | 3 |
| App.StartMode (VbAppStartMode) | 2 | 0 | 0 | 2 |
| Button Style | 0 | 2 | 0 | 2 |
| Calendar (VbCalendar) | 0 | 2 | 0 | 2 |
| Data control BOFAction | 0 | 2 | 0 | 2 |
| Data control Error response | 0 | 2 | 0 | 2 |
| Drag action (DragConstants) | 0 | 2 | 0 | 2 |
| Drag mode (DragModeConstants) | 0 | 2 | 0 | 2 |
| Extender | 0 | 2 | 0 | 2 |
| Form WindowState | 0 | 2 | 0 | 2 |
| Form show modality | 0 | 2 | 0 | 2 |
| ListBox Style | 0 | 2 | 0 | 2 |
| MsgBox modality | 1 | 1 | 0 | 2 |
| Printer colour mode (PrinterObjectConstants / DMCOLOR_*) | 2 | 0 | 0 | 2 |
| Printer orientation (PrinterObjectConstants / DMORIENT_*) | 2 | 0 | 0 | 2 |
| ZOrder | 0 | 2 | 0 | 2 |
| Constants: back style (BackStyleConstants) | 1 | 0 | 0 | 1 |
| Constants: key codes (KeyCodeConstants) | 1 | 0 | 0 | 1 |
| Date - FirstWeekOfYear / FirstDayOfWeek | 0 | 1 | 0 | 1 |
| DrawStyle (DrawStyleConstants) - NOT a Shell window style | 1 | 0 | 0 | 1 |
| Error numbering | 1 | 0 | 0 | 1 |

### Constants that do not resolve at all — 8

| Name | Status | Detail | Source |
|---|---|---|---|
| `vbBack` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs:26-745), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:475 |
| `vbFormFeed` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs:26-745), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:475 |
| `vbNewLine` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs:26-745), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:475 |
| `vbNullChar` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs:26-745), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:475 |
| `vbNullString` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs:26-745), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:475 |
| `vbTab` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs:26-745), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:475 |
| `vbUseCompareOption` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs:26-745), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:475 |
| `vbVerticalTab` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs:26-745), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs:475 |

## The autocomplete trap

A name the IDE offers is not a name the interpreter runs. The LSP signature table
(`LspServer/HexIDE.VbLspServer/VbSignatures.cs`) and the editor keyword list
(`IDE/HexIDE/Editor/VbKeywordNormalizer.cs`) are **editor metadata with no execution path**. Several
intrinsics appear there, complete with signature help, and then fail at run time — `StrComp`, `StrConv`,
`CVar`, `CVErr`, `Shell`, `Environ`, `Command`, `IIf`, `Choose`, `Switch`, `RGB`, `QBColor`, `Dir`,
`FreeFile`, `EOF`, `LOF`, `Loc`, `AscW`, `AscB`.

This is worse than a plain gap: the IDE actively suggests the call. Closing the gap between those two
lists — either by implementing the intrinsic or by not offering it — is cheap and disproportionately
improves how the IDE *feels*.

## Maintenance

This document was generated from a full sweep of the VB6 surface and then hand-edited. When a status
changes, edit the row. When adding a construct, add its row in the same change — a coverage document that
drifts is worse than none, because it is quoted rather than checked.

Two known drift hazards, both live:

1. [`interpreter-gaps.md`](interpreter-gaps.md) has its own `Partial` section describing many of the same
   constructs. It owns *classification*; this file owns *coverage*. Do not restate its reasoning here —
   link to it.
2. `IDE/HexIDE.Runtime/README.md` carries a walls table that has already gone stale at least once
   (it listed `Implements` as permanently out of scope after it had been implemented).

