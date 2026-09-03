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
| **Silently wrong** | Runs, does nothing, program continues | **Worst of all.** Nothing announces it, so the user debugs their own correct code. |
| **Won't load** | The module never opens | Nothing runs, and the user cannot even look around. |
| **Dies** | Parses, then throws partway through | The program gets going and falls off a cliff — but it *tells you*. |
| Partial | Runs, with a real restriction | Fine *if the restriction is written down* — which is what the Detail column is for. |
| No-op | Accepted and deliberately ignored | Acceptable only where ignoring it cannot mislead. See below. |
| Supported | Works for the ordinary case | |

**Why "Silently wrong" outranks "Won't load".** A file that will not open is obvious in a second, and the
user knows to blame the tool. A `Shell` that launches nothing, or a `Time = Now` that silently creates a
variable called `Time`, sends someone hunting through their own logic for a bug that is ours. That is the
same harm shape as a silently lossy save, which
[`serialization-outcomes.md`](serialization-outcomes.md) rules out entirely.

**The mechanism, because it generalises.** An unregistered bare name is not an error in VB6's default
configuration and was not one here either: it fell through to implicit declaration and evaluated to
`Empty`, so `s = CurDir` yielded Empty rather than raising, with only the `Option Explicit` path — **off
by default**, as it was in VB6 — turning it into an error. Every intrinsic *called bare*, without
parentheses, was therefore a candidate for this category rather than for Dies.

**That mechanism is closed** as of [#191](https://github.com/hexide-io/HexIDE/issues/191): both the read
path and the bare-statement path now raise for any name VB6 defines as an intrinsic, keyed off a list of
VB6's *surface* rather than of our gaps — so implementing a function needs no edit to it, and a name we
have never heard of still gets VB6's own "Sub or Function not defined".

The category is kept at zero rather than deleted, because the shape is what matters: **anything that
fails without saying so belongs here rather than under Dies.** If a construct is found doing that again,
this is where it goes.

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
was to disprove it. It returned a verdict on 323 originally-supported rows and **demoted 217 of them** —
**two thirds**. It did not reach all of them. So read Supported as *no one has yet shown this broken*,
and expect the real figure to be materially lower.

A worked example of what that means in practice: this document downgraded `Split` and `Filter` for
mishandling omitted optional arguments, and left `Join` as Supported — even though `Join` uses the *same*
`HasArg` helper, three lines away in the same file. The demotions are as incomplete as the attacks were.

**These statuses were derived by reading code, then corrected by execution where a verifier bothered to
run something.** Where a row says a construct throws, that is usually solid. Where it says something
*works*, treat it as a lead.

**The sweep is a snapshot, and work landed after it.** Rows overtaken since are marked inline with
**Superseded by #N**, rather than quietly rewritten, so it is visible which parts are second-hand. The
known case is [#184](https://github.com/hexide-io/HexIDE/pull/184), which made object member chains fold
on the *read* path — seven rows describe the single-dot restriction it removed, and it now survives only
on the write path (`obj.a.b = x`, `Set obj.a.b = o`).

## At a glance

| Category | Silently wrong | Won't load | Dies | Partial | No-op | Supported | Total |
|---|---:|---:|---:|---:|---:|---:|---:|
| Statements | 0 | 0 | 47 | 35 | 0 | 23 | 105 |
| Operators | 0 | 0 | 3 | 18 | 0 | 11 | 32 |
| Intrinsic functions | 0 | 0 | 71 | 37 | 1 | 44 | 153 |
| Keywords and modifiers | 0 | 0 | 11 | 22 | 1 | 17 | 51 |
| Literals, types and suffixes | 0 | 1 | 7 | 17 | 0 | 25 | 50 |
| Compiler directives and options | 0 | 1 | 16 | 9 | 13 | 8 | 47 |
| In-box objects | 0 | 0 | 98 | 8 | 0 | 17 | 123 |
| Intrinsic constants | 0 | 0 | 8 | 261 | 0 | 352 | 621 |
| **Total** | **0** | **2** | **261** | **407** | **15** | **497** | **1182** |

The *Silently wrong* column counts only what a verifier proved by running it. It is a floor, not a
census — see the caveat above.


### The single most useful number here is 3

**Only three constructs in the whole VB6 language fail to parse**: `#Const`, the `D` exponent marker
(`1.5D2`), and two `#`-prefixed file numbers on one physical line. Every other gap parses cleanly and
fails at run time. The grammar comprehends essentially the whole of VB6; what is missing is execution.

It was four until numeric line labels were ported in from the LSP server's grammar, which already had
the rule — see below.

That matters for three reasons. It is the empirical form of the claim in `CLAUDE.md` that no VB6
construct is incomprehensible to the CST, which until now was an architectural assertion rather than a
measurement. It means a user opening real VB6 source will nearly always *see* their code, correctly
highlighted, with working navigation, even where it will not run — a far better first impression than a
file that refuses to open. And it means the remaining work is almost entirely additive: implementing a
visitor, not changing how the language is understood.

### Where the damage actually is

**Operators are nearly complete** — 3 absent of 32 — so expressions and arithmetic generally work. The
damage is concentrated in **statements** and the **in-box object model**: 44% of VB6's statements and
79% of the in-box surface (`App`, `Screen`, `Printer`, `Clipboard`, `Collection`) are unavailable.

That is the honest answer to "will my code run?" — the arithmetic will; the plumbing around it often will
not. The classic file-I/O family is absent as a block, which is the most common way a real program dies
here, usually within its first few statements.

## Silently wrong — 0 names

**Closed by [#191](https://github.com/hexide-io/HexIDE/issues/191).** Eight constructs used to sit here:
`Shell`, `FreeFile`, `Command`, `CurDir`, `Cls`, `Erl`, `Dir` and `Time = x`. Each parsed, ran, did
nothing, and let the program continue — so the user saw no error and went looking for a bug in their own
code.

Two causes, both now fixed. An unregistered bare name fell through to VB6's implicit-declaration rule and
evaluated to `Empty`; the read path now raises for any name VB6 defines as an intrinsic, and the
bare-statement path no longer discards the "not a builtin" result. And `Time = x` was grammar-shadowed —
`TIME` is an ambiguousKeyword and `letStmt` was matched ahead of `timeStmt`, so it silently created a
*variable* called `Time` and left the throw in `VisitTimeStmt` as unreachable dead code. `dateStmt`
already sat ahead of `letStmt`, which is the only reason `Date = x` threw and `Time = x` did not.

**The category stays in this document even at zero**, because it is the failure mode worth watching for:
anything that fails without saying so belongs here rather than under Dies, and the reasoning above is how
to recognise the next one. The check keys off a list of VB6's *surface* rather than of our gaps, so
implementing a function does not require maintaining it.

## Won't load — 2 names

The whole list. A parse failure takes the entire module down — nothing in the file runs and the editor
cannot open it usefully — so despite how obscure these look, each one has a blast radius of a file rather
than a statement. That is why they are worth fixing ahead of far more commonly used constructs that merely
throw.

**A third, found by verification and not in the table.** Any single physical source line containing two
`#`-prefixed file numbers — `Print #1, #2` and the like — fails to parse, taking the whole module with
it. This has the widest blast radius of the three and no row of its own, because the enumeration listed
`#n` once as a literal rather than as a sequence that can repeat on a line.

**Numeric line labels used to be here**, and how they were found is the useful part: the LSP server's
grammar already had a working `lineNumber` rule that the interpreter's lacked, so the *editor* accepted
`10 Debug.Print 1` with no syntax error and the module then refused to load. HexIDE's two halves
disagreed about what VB6 is. `GrammarParityTests` now compares the two rule inventories so the next such
gap is a build failure rather than a bug report.

Worth noting what is *not* here: `#If` parses fine and throws, and so does the named-argument operator
`:=` — both are execution gaps rather than grammar gaps, which is the cheaper kind to close.

| Name | Status | Detail | Source |
|---|---|---|---|
| `#Const` | **Won't load** | VB6.g4 has no #Const lexer token and no parser rule; the only HASH-prefixed directive tokens are MACRO_IF/MACRO_ELSEIF/MACRO_ELSE/MACRO_END_IF (VB6.g4). '#Const' instead lexes as FILENUMBER… | VB6.g4 |
| `DExponentLiteral` | **Won't load** | `DOUBLELITERAL` and `INTEGERLITERAL` accept only an `e`/`E` exponent marker, never `D`. Measured: `Debug.Print 1.5D2` -> "Compile error: mismatched input 'D2' expecting <EOF>". | VB6.g4 |
| `Line number` | Supported | **Fixed** — numeric line labels now parse, and work as `GoTo`, `On Error GoTo` and `Resume` targets. Ported from the LSP grammar, which already had the rule | VB6.g4, StatementExecutor.cs |
| `LineNumber` | Supported | **Fixed** — numeric line labels now parse, and work as `GoTo`, `On Error GoTo` and `Resume` targets. Ported from the LSP grammar, which already had the rule | VB6.g4, StatementExecutor.cs |

## Dies mid-run — 253 names

These parse. The program starts, reaches the statement, and throws. Grouped by area below so the clusters
are visible — and they are very clustered: the classic file-I/O family is absent as a block, and so is
most of the in-box object model.

| Name | Status | Detail | Source |
|---|---|---|---|
| `! (dictionary access)` | **Dies** | throws `"dictionaryCallStmt is not supported"` — The grammar yields the pair (`dictionaryCallStmt : EXCLAMATIONMARK ambiguousIdentifier typeHint?`) but every consuming site throws. Measured on a… | ExpressionExecutor.cs |
| `#` | **Dies** | and it is the one piece of this batch that fails even outside a file statement. `#1` lexes as its own token, `FILENUMBER : HASH LETTERORDIGIT+` (Grammar/VB6.g4), and FILENUMBER is an… | ExpressionExecutor.cs |
| `#Else` | **Dies** | throws. macroElseBlockStmt (VB6.g4) is part of macroIfThenElseStmt and shares its fate: measured, '#If Win32 Then ... #Else ... #End If' raises VBCompileErrorException 'Conditional… | PrePass.cs |
| `#ElseIf` | **Dies** | throws. macroElseIfBlockStmt (VB6.g4) is only reachable as part of macroIfThenElseStmt, so it fails with its parent: VBCompileErrorException 'Conditional compilation (#If / #Const) is not… | PrePass.cs |
| `#End If` | **Dies** | throws, as the terminator of macroIfThenElseStmt (VB6.g4) - same two exceptions as #If (PrePass.cs at module level, StatementExecutor.cs inside a procedure). | PrePass.cs |
| `#If` | **Dies** | throws `"Conditional compilation (#If / #Const) is not supported"` — throws - with two different exceptions depending on where it sits. At module level PrePass hits it first: '' (PrePass.cs), under the comment… | PrePass.cs |
| `#n` | **Dies** | `FILENUMBER : HASH LETTERORDIGIT +` is a lexer token listed in the `literal` rule — then throws at run time, because `VisitVsLiteralCore` has no FILENUMBER branch and falls into `throw new… | ExpressionExecutor.cs |
| `$` | **Dies** | There is NO literal form for `$` at all (a String literal carries no suffix), so every occurrence is an identifier or function type hint and every one throws. Measured: `Debug.Print s$` ->… | VB6BuiltIns.Strings.cs |
| `,` | **Dies** | The 14-column print-zone comma is grammar-level only: `outputList : outputList_Expression (WS? (SEMICOLON \| COMMA) WS? outputList_Expression?)* \| outputList_Expression? (WS? (SEMICOLON \|… | StatementExecutor.cs |
| `:= (named argument)` | **Dies** | throws `"Assign is not implemented"` — `implicitCallStmt_InStmt WS? ASSIGN WS? valueStmt # vsAssign` with `ASSIGN : ':='` — then throws at run time: `public override Task<object?>… | ExpressionExecutor.cs |
| `;` | **Dies** | The suppress-newline / adjacent-item semicolon is in `outputList` (Grammar/VB6.g4) and nothing consumes it - probed `Print #1, "a"; "b"` -> NotImplementedException "Print not implemented". | StatementExecutor.cs |
| `Access` | **Dies** | ACCESS is a real lexer token and the clause is in the grammar - `openStmt : OPEN WS valueStmt WS FOR WS (APPEND\|BINARY\|INPUT\|OUTPUT\|RANDOM) (WS ACCESS WS (READ\|WRITE\|READ_WRITE))?… | StatementExecutor.cs |
| `AddressOf` | **Dies** | throws `"ADDRESSOF is not implemented"` — (`ADDRESSOF WS valueStmt # vsAddressOf`) then throws at run time: `public override Task<object?> VisitVsAddressOf(VB6Parser.VsAddressOfContext… | ExpressionExecutor.cs |
| `AmbientProperties` | **Dies** | Measured for `Dim a As AmbientProperties`: VBCompileErrorException: "User-defined type not defined: AmbientProperties" (StatementExecutor.cs). The route to a real instance is gone too:… | StatementExecutor.cs |
| `AmbientProperties.BackColor` | **Dies** | Unreachable: there is no `Ambient` and no `UserControl` global to read it from — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" and "Variable not defined… | BasicInterpreter.cs |
| `AmbientProperties.DisplayAsDefault` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | BasicInterpreter.cs |
| `AmbientProperties.DisplayName` | **Dies** | Measured directly: `Debug.Print UserControl.Ambient.DisplayName` gives VBVariableNotDefinedException: "Variable not defined (UserControl)" (ExpressionExecutor.cs). The property name appears… | BasicInterpreter.cs |
| `AmbientProperties.Font` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | BasicInterpreter.cs |
| `AmbientProperties.ForeColor` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | BasicInterpreter.cs |
| `AmbientProperties.LocaleID` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | BasicInterpreter.cs |
| `AmbientProperties.MessageReflect` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | BasicInterpreter.cs |
| `AmbientProperties.Palette` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | BasicInterpreter.cs |
| `AmbientProperties.RightToLeft` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | BasicInterpreter.cs |
| `AmbientProperties.ScaleUnits` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | BasicInterpreter.cs |
| `AmbientProperties.ShowGrabHandles` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | BasicInterpreter.cs |
| `AmbientProperties.ShowHatching` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | BasicInterpreter.cs |
| `AmbientProperties.SupportsMnemonics` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | BasicInterpreter.cs |
| `AmbientProperties.TextAlign` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | BasicInterpreter.cs |
| `AmbientProperties.UIDead` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | BasicInterpreter.cs |
| `AmbientProperties.UserMode` | **Dies** | Measured directly: `Debug.Print Ambient.UserMode` gives VBVariableNotDefinedException: "Variable not defined (Ambient)" and `Debug.Print UserControl.Ambient.UserMode` gives "Variable not… | BasicInterpreter.cs |
| `App.HelpFile` | **Dies** | the name is absent from VbApp.TryGetProperty's switch, so the read falls through to ExpressionExecutor.cs. Measured verbatim: "Compile error: Method or data member not found (HelpFile in… | VbApp.cs |
| `App.LogEvent` | **Dies** | VbApp.Call has no dispatch at all - VbApp.cs is: public void Call(string method, List<Vb6Value> args) => throw new VBRunTimeException(VBStandardError.MethodOrDataMemberNotFound, "App." +… | VbApp.cs |
| `App.LogMode` | **Dies** | the name is absent from VbApp.TryGetProperty's switch, so the read falls through to ExpressionExecutor.cs. Measured verbatim: "Compile error: Method or data member not found (LogMode in… | VbApp.cs |
| `App.LogPath` | **Dies** | the name is absent from VbApp.TryGetProperty's switch, so the read falls through to ExpressionExecutor.cs. Measured verbatim: "Compile error: Method or data member not found (LogPath in… | VbApp.cs |
| `App.StartLogging` | **Dies** | VbApp.Call has no dispatch at all - VbApp.cs is: public void Call(string method, List<Vb6Value> args) => throw new VBRunTimeException(VBStandardError.MethodOrDataMemberNotFound, "App." +… | VbApp.cs |
| `App.StartMode` | **Dies** | the name is absent from VbApp.TryGetProperty's switch, so the read falls through to ExpressionExecutor.cs. Measured verbatim: "Compile error: Method or data member not found (StartMode in… | VbApp.cs |
| `App.TaskVisible` | **Dies** | the name is absent from VbApp.TryGetProperty's switch, so the read falls through to ExpressionExecutor.cs. Measured verbatim: "Compile error: Method or data member not found (TaskVisible in… | VbApp.cs |
| `AppActivate` | **Dies** | throws `"AppActivate not implemented"` — Grammar rule `appActivateStmt : APPACTIVATE WS valueStmt (WS? COMMA WS? valueStmt)?` (VB6.g4); the visitor body is a single throw:… | StatementExecutor.cs |
| `Append` | **Dies** | APPEND is a lexer token and an openStmt mode alternative (Grammar/VB6.g4). Probed `Open "z.txt" For Append As #1` -> NotImplementedException "Open not implemented". | StatementExecutor.cs |
| `AscB` | **Dies** | No registry entry: BuildRegistry (VB6BuiltIns.cs) calls RegisterStrings/Conversion/Math/Array/Inspection/DateTime/Format and adds only DoEvents; grep for "AscB" across IDE/HexIDE.Runtime… | ExpressionExecutor.cs, VB6BuiltIns.cs |
| `AscW` | **Dies** | No registry entry (VB6BuiltIns.cs; no occurrence of "AscW" anywhere under IDE/HexIDE.Runtime). Reaches `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined… | ExpressionExecutor.cs, VB6BuiltIns.cs |
| `AsyncProperty` | **Dies** | Measured for `Dim a As AsyncProperty`: VBCompileErrorException: "User-defined type not defined: AsyncProperty" (StatementExecutor.cs). The name appears nowhere in the repository. | StatementExecutor.cs |
| `AsyncProperty_VB5` | **Dies** | Measured for `Dim a As AsyncProperty_VB5`: VBCompileErrorException: "User-defined type not defined: AsyncProperty_VB5" (StatementExecutor.cs). | StatementExecutor.cs |
| `CallByName` | **Dies** | Zero occurrences anywhere in HexIDE.Runtime; not registered by any Register* partial, so BuildRegistry has no entry. The call reaches ExpressionExecutor's final fallthrough and throws… | ExpressionExecutor.cs |
| `Choose` | **Dies** | Not in BuildRegistry - a grep of every d["..."] registration across all VB6BuiltIns partials returns no Choose. `x = Choose(i, "a", "b")` throws VBSubOrFunctionNotDefinedException: "Sub or… | ExpressionExecutor.cs |
| `ChrB` | **Dies** | No registry entry (VB6BuiltIns.cs; no occurrence of "ChrB" under IDE/HexIDE.Runtime). `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (ChrB)". | ExpressionExecutor.cs, VB6BuiltIns.cs |
| `ChrW` | **Dies** | No registry entry (VB6BuiltIns.cs). `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (ChrW)". | ExpressionExecutor.cs, VB6BuiltIns.cs |
| `Circle` | **Dies** | throws `"Only single element supported"` — fails at run time - but NOT with a name-resolution error. VB6.g4 has no CIRCLE token and no circleStmt rule, so `Circle (100, 100), 50` parses as a… | ExpressionExecutor.cs |
| `Clipboard` | **Dies** | never seeded. BasicInterpreter.cs seeds only Debug, Err and App as program globals. Measured verbatim: "Compile error: Variable not defined (Clipboard)". | BasicInterpreter.cs |
| `Clipboard.Clear` | **Dies** | Measured (statement position): a VBRunTimeException from StatementExecutor.cs - "Unknown method Clear on <Right(EmptyVariant)>()" - because the Clipboard lead resolves to Empty, so neither… | StatementExecutor.cs |
| `Clipboard.GetData` | **Dies** | In expression position the member-chain lead is resolved by ExpressionExecutor.cs, which throws before the member is ever looked at. Measured verbatim for Clipboard.GetText() and… | ExpressionExecutor.cs |
| `Clipboard.GetFormat` | **Dies** | In expression position the member-chain lead is resolved by ExpressionExecutor.cs, which throws before the member is ever looked at. Measured verbatim for Clipboard.GetText() and… | ExpressionExecutor.cs |
| `Clipboard.GetText` | **Dies** | In expression position the member-chain lead is resolved by ExpressionExecutor.cs, which throws before the member is ever looked at. Measured verbatim for Clipboard.GetText() and… | ExpressionExecutor.cs |
| `Clipboard.SetData` | **Dies** | Measured verbatim: "Run-time error: Unknown method SetData on <Right(EmptyVariant)>()". | StatementExecutor.cs |
| `Clipboard.SetText` | **Dies** | Measured verbatim: "Run-time error: Unknown method SetText on <Right(EmptyVariant)>()". | StatementExecutor.cs |
| `Close` | **Dies** | `closeStmt : CLOSE (WS valueStmt (WS? COMMA WS? valueStmt)*)?` (Grammar/VB6.g4) and a visitor exists, but its whole body is the throw. Probed both `Close #1` and bare `Close` ->… | StatementExecutor.cs |
| `Collection` | **Dies** | COLLECTION is a real grammar token (VB6.g4) listed in baseType (VB6.g4), so `Dim c As Collection` reaches BaseTypeMapper.Map, which returns null for it (BaseTypeMapper.cs comment:… | PrePass.cs |
| `Collection._NewEnum` | **Dies** | The name lexes as an ordinary IDENTIFIER (VB6.g4; the LETTER fragment at :2110 includes `_`), and the bracketed form `[_NewEnum]` parses too — but `_NewEnum`/`NewEnum` appears nowhere in… | Exceptions.cs |
| `Collection.Add` | **Dies** | No Collection object can exist (see Collection), and no Add handler exists on any runtime proxy. Measured with the call-statement form on a Variant holder: VBRunTimeException: "Unknown… | StatementExecutor.cs |
| `Collection.Count` | **Dies** | Measured: VBMethodOrDataMemberNotFoundException: "Method or data member not found (Count in Right(EmptyVariant))". No Collection type exists to carry it. | Exceptions.cs |
| `Collection.Item` | **Dies** | Measured explicit form: VBMethodOrDataMemberNotFoundException: "Method or data member not found (Item(1) in Right(EmptyVariant))". The implicit default-member form `c(1)` fails differently… | Exceptions.cs |
| `Collection.Remove` | **Dies** | Measured: VBRunTimeException: "Unknown method Remove on <Right(EmptyVariant)>()" (StatementExecutor.cs). | StatementExecutor.cs |
| `ContainedControls` | **Dies** | Measured for `Dim c As ContainedControls`: VBCompileErrorException: "User-defined type not defined: ContainedControls" (StatementExecutor.cs). | StatementExecutor.cs |
| `Controls` | **Dies** | a form's Controls collection does not exist. Measured verbatim: "Compile error: Variable not defined (Controls)". A form binds only its own name and "Me" (VBLoader.cs); no collection is… | VBLoader.cs |
| `Controls.Add` | **Dies** | Measured verbatim: "Run-time error: Unknown method Add on <Right(EmptyVariant)>()". | StatementExecutor.cs |
| `Controls.Count` | **Dies** | Measured verbatim for Controls.Count: "Compile error: Variable not defined (Controls)". The same applies to Controls.Item(i); the default-member form Controls(i) instead gives "Sub or… | ExpressionExecutor.cs |
| `Controls.Item` | **Dies** | Measured verbatim for Controls.Count: "Compile error: Variable not defined (Controls)". The same applies to Controls.Item(i); the default-member form Controls(i) instead gives "Sub or… | ExpressionExecutor.cs |
| `Controls.Remove` | **Dies** | Measured pattern, identical to Controls.Add: "Run-time error: Unknown method Remove on <Right(EmptyVariant)>()". | StatementExecutor.cs |
| `CreateObject` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. `Set o = CreateObject("Excel.Application")` throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined… | ExpressionExecutor.cs |
| `CVar` | **Dies** | Not registered in BuildRegistry (VB6BuiltIns.cs) or any RegisterXxx partial — the only hits anywhere in IDE/ are editor metadata. A call reaches ExpressionExecutor.cs and throws "Compile… | VB6BuiltIns.cs |
| `CVDate` | **Dies** | Zero hits anywhere in IDE/ outside obj/bin — not registered (VB6BuiltIns.cs), not in Grammar/VB6.g4, not in VbKeywordNormalizer.cs, not in VbSignatures.cs. A call reaches… | VB6BuiltIns.cs |
| `CVErr` | **Dies** | Not registered in BuildRegistry (VB6BuiltIns.cs) or any RegisterXxx partial, so name resolution falls through local arrays and user procedures to the intrinsic registry, gets null back, and… | VB6BuiltIns.cs |
| `DataBinding` | **Dies** | Measured for `Dim d As DataBinding`: VBCompileErrorException: "User-defined type not defined: DataBinding" (StatementExecutor.cs). The name appears nowhere in the runtime. | StatementExecutor.cs |
| `DataBindings` | **Dies** | Measured for `Dim d As DataBindings`: VBCompileErrorException: "User-defined type not defined: DataBindings" (StatementExecutor.cs). The name appears nowhere in the runtime. | StatementExecutor.cs |
| `DataMembers` | **Dies** | Measured for `Dim d As DataMembers`: VBCompileErrorException: "User-defined type not defined: DataMembers" (StatementExecutor.cs). The name appears nowhere in the repository. | StatementExecutor.cs |
| `DataObject` | **Dies** | `DataObject` is not a grammar keyword, so it takes the complexType path and throws VBCompileErrorException: "User-defined type not defined: DataObject" — measured for `Dim d As DataObject`… | StatementExecutor.cs |
| `DataObject.Clear` | **Dies** | Measured: VBRunTimeException: "Unknown method Clear on <Right(EmptyVariant)>()" (StatementExecutor.cs). No DataObject type exists to own it. | StatementExecutor.cs |
| `DataObject.Files` | **Dies** | Measured: VBMethodOrDataMemberNotFoundException: "Method or data member not found (Files in Right(EmptyVariant))". | Exceptions.cs |
| `DataObject.GetData` | **Dies** | Measured: VBMethodOrDataMemberNotFoundException: "Method or data member not found (GetData(1) in Right(EmptyVariant))". | Exceptions.cs |
| `DataObject.GetFormat` | **Dies** | Measured: VBMethodOrDataMemberNotFoundException: "Method or data member not found (GetFormat(1) in Right(EmptyVariant))". | Exceptions.cs |
| `DataObject.SetData` | **Dies** | Measured: VBRunTimeException: "Unknown method SetData on <Right(EmptyVariant)>()" (StatementExecutor.cs). | StatementExecutor.cs |
| `DataObjectFiles` | **Dies** | Measured for `Dim f As DataObjectFiles`: VBCompileErrorException: "User-defined type not defined: DataObjectFiles" (StatementExecutor.cs). The name appears nowhere in the repository. | StatementExecutor.cs |
| `Date` | **Dies** | throws `"Date not implemented"` — The grammar has a dedicated rule `dateStmt : DATE WS? EQ WS? valueStmt` (VB6.g4), listed in the block alternatives at VB6.g4 ahead of letStmt so it… | StatementExecutor.cs |
| `DDB` | **Dies** | Not registered in BuildRegistry (VB6BuiltIns.cs) or any RegisterXxx partial; the call resolves to no local array, no user procedure and no intrinsic, so ExpressionExecutor.cs throws… | VB6BuiltIns.cs |
| `Debug.Assert` | **Dies** | as an ICS_B_MemberProcedureCall and throws at run time. DebugProxy.Call handles only "Print"; everything else hits `throw new Exception("No method named " + method)`. Measured verbatim:… | DebugProxy.cs |
| `DefBool` | **Dies** | throws `"Deftype not implemented"` — (grammar rule `deftypeStmt` at VB6.g4, a blockStmt so it reaches the module's top-level block) and then throws at run time: Measured: `DefBool A-Z`… | StatementExecutor.cs |
| `DefByte` | **Dies** | throws `"Deftype not implemented"` — the single shared VisitDeftypeStmt covers every Def* token (DEFBOOL\|DEFBYTE\|DEFINT\|DEFLNG\|DEFCUR\|DEFSNG\|DEFDBL\|DEFDEC\|DEFDATE\|DEFSTR\|DEFOBJ\… | StatementExecutor.cs |
| `DefCur` | **Dies** | throws `"Deftype not implemented"` | StatementExecutor.cs |
| `DefDate` | **Dies** | throws `"Deftype not implemented"` | StatementExecutor.cs |
| `DefDbl` | **Dies** | throws `"Deftype not implemented"` | StatementExecutor.cs |
| `DefDec` | **Dies** | throws `"Deftype not implemented"` — (DEFDEC is a lexer token and appears in the deftypeStmt alternation) and then | StatementExecutor.cs, VB6.g4 |
| `DefInt` | **Dies** | throws `"Deftype not implemented"` — Measured: `DefInt A-Z` -> that exact message. | StatementExecutor.cs |
| `DefLng` | **Dies** | throws `"Deftype not implemented"` | StatementExecutor.cs |
| `DefObj` | **Dies** | throws `"Deftype not implemented"` | StatementExecutor.cs |
| `DefSng` | **Dies** | throws `"Deftype not implemented"` | StatementExecutor.cs |
| `DefStr` | **Dies** | throws `"Deftype not implemented"` — Measured: `DefStr S` -> that exact message. | StatementExecutor.cs |
| `DefVar` | **Dies** | throws `"Deftype not implemented"` | StatementExecutor.cs |
| `DeleteSetting` | **Dies** | throws `"DeleteSetting not implemented"` — Grammar rule `deleteSettingStmt : DELETESETTING WS valueStmt WS? COMMA WS? valueStmt (WS? COMMA WS? valueStmt)?` (VB6.g4) with a real DELETESETTING… | StatementExecutor.cs |
| `Dir` | **Dies** | fails at name resolution: unregistered in every VB6BuiltIns partial. Probed `Debug.Print Dir("*.*")` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined… | ExpressionExecutor.cs |
| `End` | **Dies** | throws `"End not implemented"` — (endStmt, VB6.g4) and throws at run time. Verbatim: Measured: `Debug.Print 1 / End` -> NotImplementedException: End not implemented. | StatementExecutor.cs |
| `Environ` | **Dies** | Not in BuildRegistry; the only runtime hit is the highlighter list VbKeywordNormalizer.cs. `Environ("PATH")` throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined… | ExpressionExecutor.cs |
| `EOF` | **Dies** | fails at name resolution: unregistered. Probed `Debug.Print EOF(1)` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined (EOF)". docs/interpreter-gaps.md:83. | ExpressionExecutor.cs |
| `Err.HelpContext` | **Dies** | TryGetProperty (VbErr.cs) has cases only for number/description/source; the fall-through in ExpressionExecutor then requires a Control and otherwise throws… | ExpressionExecutor.cs |
| `Err.HelpFile` | **Dies** | Measured: `Debug.Print Err.HelpFile` -> VBMethodOrDataMemberNotFoundException, "Method or data member not found (HelpFile in Right(CSharpProxyObject))". No case in… | VbErr.cs |
| `Err.LastDllError` | **Dies** | Measured verbatim: "Method or data member not found (LastDllError in Right(CSharpProxyObject))". | VbErr.cs |
| `ErrObject` | **Dies** | as a complexType, throws at run time. BaseTypeMapper.Map has no ErrObject case and it is not a user class/UDT/Enum, so DeclareLocal falls to the final else. Measured verbatim:… | StatementExecutor.cs |
| `Error` | **Dies** | `Error(n)` (the function returning an error's message text) is not in BuildRegistry. Measured: `Debug.Print Error(6)` -> VBSubOrFunctionNotDefinedException. `Debug.Print Error$(6)` ->… | VB6BuiltIns.cs |
| `EventInfo` | **Dies** | Measured for `Dim e As EventInfo`: VBCompileErrorException: "User-defined type not defined: EventInfo" (StatementExecutor.cs). The name appears nowhere in the runtime. | StatementExecutor.cs |
| `EventParameter` | **Dies** | Measured for `Dim e As EventParameter`: VBCompileErrorException: "User-defined type not defined: EventParameter" (StatementExecutor.cs). The name appears nowhere in the runtime. | StatementExecutor.cs |
| `EventParameters` | **Dies** | Measured for `Dim e As EventParameters`: VBCompileErrorException: "User-defined type not defined: EventParameters" (StatementExecutor.cs). The name appears nowhere in the repository. | StatementExecutor.cs |
| `FileAttr` | **Dies** | fails at name resolution: unregistered. Probed `Debug.Print FileAttr(1, 1)` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined (FileAttr)"… | ExpressionExecutor.cs |
| `FileCopy` | **Dies** | `filecopyStmt : FILECOPY WS valueStmt WS? COMMA WS? valueStmt` (Grammar/VB6.g4); the visitor body is the throw. Probed `FileCopy "a.txt", "b.txt"` -> NotImplementedException: "Filecopy not… | StatementExecutor.cs |
| `FileDateTime` | **Dies** | fails at name resolution: unregistered. Probed `Debug.Print FileDateTime("a.txt")` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined (FileDateTime)"… | ExpressionExecutor.cs |
| `FileLen` | **Dies** | fails at name resolution: unregistered. Probed `Debug.Print FileLen("a.txt")` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined (FileLen)"… | ExpressionExecutor.cs |
| `FormatCurrency` | **Dies** | RegisterFormat registers exactly one name — `d["Format"]` (VB6BuiltIns.Format.cs); no occurrence of "FormatCurrency" under IDE/HexIDE.Runtime. `throw new… | ExpressionExecutor.cs, VB6BuiltIns.Format.cs |
| `FormatDateTime` | **Dies** | Not registered (VB6BuiltIns.Format.cs registers only "Format"); no occurrence under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined… | ExpressionExecutor.cs, VB6BuiltIns.Format.cs |
| `FormatNumber` | **Dies** | Not registered (VB6BuiltIns.Format.cs); no occurrence under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — message "Sub or Function not defined (FormatNumber)". | ExpressionExecutor.cs, VB6BuiltIns.Format.cs |
| `FormatPercent` | **Dies** | Not registered (VB6BuiltIns.Format.cs); no occurrence under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (FormatPercent)". | ExpressionExecutor.cs, VB6BuiltIns.Format.cs |
| `Forms` | **Dies** | the loaded-forms collection is never built. Measured verbatim: "Compile error: Variable not defined (Forms)". BasicInterpreter.cs seeds only Debug, Err and App as program globals. | BasicInterpreter.cs |
| `Forms.Count` | **Dies** | Measured verbatim: "Compile error: Variable not defined (Forms)". | ExpressionExecutor.cs |
| `Forms.Item` | **Dies** | Measured verbatim for the default-member form Forms(0).Caption: "Compile error: Sub or Function not defined (Forms)" - a parenthesised lead is routed to EvaluateProcedureOrArrayCall, which… | ExpressionExecutor.cs |
| `FV` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs); ExpressionExecutor.cs throws "Compile error:\n\nSub or Function not defined (FV)". Named in the deferral list at docs/interpreter-gaps.md:77. | VB6BuiltIns.cs |
| `Get` | **Dies** | `getStmt : GET WS valueStmt WS? COMMA WS? valueStmt? WS? COMMA WS? valueStmt` (Grammar/VB6.g4); the visitor body is the throw. Probed `Get #1, 1, v` -> NotImplementedException: "Get not… | StatementExecutor.cs |
| `GetAllSettings` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. Throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (GetAllSettings)". docs/interpreter-gaps.md:84: "Registry… | ExpressionExecutor.cs |
| `GetAttr` | **Dies** | fails at name resolution: unregistered. Probed `Debug.Print GetAttr("a.txt")` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined (GetAttr)"… | ExpressionExecutor.cs |
| `GetObject` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. Throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (GetObject)". docs/interpreter-gaps.md:96 pairs it with… | ExpressionExecutor.cs |
| `GetSetting` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. Throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (GetSetting)". Same docs/interpreter-gaps.md:84 row as… | ExpressionExecutor.cs |
| `GoSub` | **Dies** | throws `"GoSub not implemented"` — (goSubStmt, VB6.g4) and throws at run time. Verbatim: Measured. | StatementExecutor.cs |
| `GoSub...Return` | **Dies** | throws `"GoSub not implemented"` — Both halves parse and both throw. GoSub: (StatementExecutor.cs). Return: `throw new NotImplementedException("Return not implemented")` (:1172)… | StatementExecutor.cs |
| `Hyperlink` | **Dies** | Measured for `Dim h As Hyperlink`: VBCompileErrorException: "User-defined type not defined: Hyperlink" (StatementExecutor.cs). The name appears nowhere in the runtime. | StatementExecutor.cs |
| `IIf` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. `x = IIf(c, a, b)` throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (IIf)". docs/interpreter-gaps.md:52… | ExpressionExecutor.cs |
| `IMEStatus` | **Dies** | but is not implemented. Not in BuildRegistry and zero occurrences anywhere in HexIDE.Runtime, including the editor-metadata files (VbSignatures.cs / VbKeywordNormalizer.cs). | ExpressionExecutor.cs |
| `Input` | **Dies** | fails at name resolution: `Input` is in `ambiguousKeyword` (Grammar/VB6.g4) so `Input(n, f)` parses as a procedure-or-array call, but the name is unregistered. Probed `Debug.Print Input(5… | ExpressionExecutor.cs |
| `Input #` | **Dies** | `inputStmt : INPUT WS valueStmt (WS? COMMA WS? valueStmt)+` (Grammar/VB6.g4); the visitor body is the throw. Probed `Input #1, s` -> NotImplementedException: "Input not implemented". | StatementExecutor.cs |
| `InputB` | **Dies** | fails at name resolution: a plain identifier (not a lexer keyword), unregistered. Probed `Debug.Print InputB(5, 1)` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function… | ExpressionExecutor.cs |
| `InStrB` | **Dies** | Not registered (VB6BuiltIns.cs); no occurrence of "InStrB" under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (InStrB)". | ExpressionExecutor.cs, VB6BuiltIns.cs |
| `IPmt` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs); ExpressionExecutor.cs throws "Compile error:\n\nSub or Function not defined (IPmt)". Named in the deferral list at… | VB6BuiltIns.cs |
| `IRR` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs); ExpressionExecutor.cs throws "Compile error:\n\nSub or Function not defined (IRR)". Named in the deferral list at docs/interpreter-gaps.md:77. | VB6BuiltIns.cs |
| `Kill` | **Dies** | `killStmt : KILL WS valueStmt` (Grammar/VB6.g4); the visitor body is the throw. Probed `Kill "z.txt"` -> NotImplementedException: "Kill not implemented". | StatementExecutor.cs |
| `LeftB` | **Dies** | Not registered (VB6BuiltIns.cs); no occurrence of "LeftB" under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (LeftB)". | ExpressionExecutor.cs, VB6BuiltIns.cs |
| `LenB` | **Dies** | Not registered (VB6BuiltIns.cs); no occurrence of "LenB" under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (LenB)". | ExpressionExecutor.cs, VB6BuiltIns.cs |
| `LicenseInfo` | **Dies** | Measured for `Dim l As LicenseInfo`: VBCompileErrorException: "User-defined type not defined: LicenseInfo" (StatementExecutor.cs). The name appears nowhere in the repository. | StatementExecutor.cs |
| `Licenses` | **Dies** | Measured for `Debug.Print Licenses.Count`: VBVariableNotDefinedException: "Variable not defined (Licenses)" (ExpressionExecutor.cs) — the global object is not seeded; only Debug, Err and… | BasicInterpreter.cs |
| `Like` | **Dies** | throws `"Like is not implemented"` — (`valueStmt WS LIKE WS valueStmt # vsLike`) then throws: `public override Task<object?> VisitVsLike(VB6Parser.VsLikeContext context) => Measured… | ExpressionExecutor.cs |
| `Line` | **Dies** | There is no standalone LINE token; `Line Input` is lexed as ONE token, `LINE_INPUT : L I N E ' ' I N P U T` (Grammar/VB6.g4), consumed by `lineInputStmt : LINE_INPUT WS valueStmt WS? COMMA… | StatementExecutor.cs |
| `Line Input #` | **Dies** | `lineInputStmt : LINE_INPUT WS valueStmt WS? COMMA WS? valueStmt` (Grammar/VB6.g4); the visitor body is the throw. Probed `Line Input #1, s` -> NotImplementedException: "LineInput not… | StatementExecutor.cs |
| `LoadPicture` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. `Set Picture1.Picture = LoadPicture("x.bmp")` throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined… | ExpressionExecutor.cs |
| `LoadResData` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. Throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (LoadResData)". | ExpressionExecutor.cs |
| `LoadResPicture` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. Throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (LoadResPicture)". | ExpressionExecutor.cs |
| `LoadResString` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. Throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (LoadResString)". | ExpressionExecutor.cs |
| `Loc` | **Dies** | fails at name resolution: unregistered. Probed `Debug.Print Loc(1)` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined (Loc)". docs/interpreter-gaps.md:83. | ExpressionExecutor.cs |
| `Lock` | **Dies** | `lockStmt : LOCK WS valueStmt (WS? COMMA WS? valueStmt (WS TO WS valueStmt)?)?` (Grammar/VB6.g4); the visitor body is the throw. Probed `Lock #1, 1 To 2` -> NotImplementedException: "Lock… | StatementExecutor.cs |
| `LOF` | **Dies** | fails at name resolution: unregistered. Probed `Debug.Print LOF(1)` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined (LOF)". docs/interpreter-gaps.md:83. | ExpressionExecutor.cs |
| `LSet` | **Dies** | throws `"Lset not implemented"` — The grammar has the rule — `lsetStmt : LSET WS implicitCallStmt_InStmt WS? EQ WS? valueStmt` (VB6.g4), reachable from blockStmt (VB6.g4) — but the… | StatementExecutor.cs, VB6.g4 |
| `Mac` | **Dies** | Not defined anywhere in the interpreter. Reachable only from #If, which throws first - measured with '#If Win32 Then ... #ElseIf Mac Then ... #End If': VBCompileErrorException 'Conditional… | PrePass.cs |
| `Mid$` | **Dies** | `Mid$` lexes as MID + DOLLAR, and DOLLAR is a `typeHint` (VB6.g4) which `iCS_S_ProcedureOrArrayCall` accepts (VB6.g4). The letStmt handler rejects it BEFORE reaching the Mid-statement… | StatementExecutor.cs |
| `MidB` | **Dies** | Not registered (VB6BuiltIns.cs); no occurrence of "MidB" under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (MidB)". | ExpressionExecutor.cs, VB6BuiltIns.cs |
| `MIRR` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs); ExpressionExecutor.cs throws "Compile error:\n\nSub or Function not defined (MIRR)". Named in the deferral list at… | VB6BuiltIns.cs |
| `MkDir` | **Dies** | `mkdirStmt : MKDIR WS valueStmt` (Grammar/VB6.g4); the visitor body is the throw. Probed `MkDir "zdir"` -> NotImplementedException: "Mkdir not implemented". | StatementExecutor.cs |
| `Name` | **Dies** | `nameStmt : NAME WS valueStmt WS AS WS valueStmt` (Grammar/VB6.g4); the visitor body is the throw. Probed `Name "a.txt" As "b.txt"` -> NotImplementedException: "Name not implemented". | StatementExecutor.cs |
| `NPer` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs); ExpressionExecutor.cs throws "Compile error:\n\nSub or Function not defined (NPer)". Named in the deferral list at… | VB6BuiltIns.cs |
| `NPV` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs); ExpressionExecutor.cs throws "Compile error:\n\nSub or Function not defined (NPV)". Named in the deferral list at docs/interpreter-gaps.md:77. | VB6BuiltIns.cs |
| `ObjPtr` | **Dies** | Zero hits anywhere in IDE/ outside obj/bin; not in BuildRegistry (VB6BuiltIns.cs), not in Grammar/VB6.g4, not even in the editor metadata (VbKeywordNormalizer.cs / VbSignatures.cs). A call… | VB6BuiltIns.cs |
| `On...GoSub` | **Dies** | throws `"OnGoSub not implemented"` — (onGoSubStmt, VB6.g4) and throws at run time. Verbatim: Measured: `On i GoSub L1` -> NotImplementedException: OnGoSub not implemented. | StatementExecutor.cs |
| `On...GoTo` | **Dies** | throws `"OnGoTo not implemented"` — (onGoToStmt, VB6.g4) and throws at run time. Verbatim: Measured: `On i GoTo L1, L2` -> NotImplementedException: OnGoTo not implemented. | StatementExecutor.cs |
| `Open` | **Dies** | the keystone of the family. The full VB6 clause grammar is present (`openStmt`, Grammar/VB6.g4, covering mode, Access, lock and `Len =`) and VisitOpenStmt exists, but its entire body is the… | StatementExecutor.cs |
| `Output` | **Dies** | OUTPUT is a lexer token and an openStmt mode alternative (Grammar/VB6.g4). Probed `Open "z.txt" For Output As #1` -> NotImplementedException "Open not implemented". | StatementExecutor.cs |
| `ParamArray` | **Dies** | throws `"ParamArray parameters are not yet supported"` — and DECLARES fine - the grammar's `arg` rule carries `(PARAMARRAY WS)?` and PrePass.ParseParams records `ParamArray: arg.PARAMARRAY() != null`. It… | BasicInterpreter.cs |
| `ParentControls` | **Dies** | Measured for `Dim p As ParentControls`: VBCompileErrorException: "User-defined type not defined: ParentControls" (StatementExecutor.cs). The name appears nowhere in the runtime. | StatementExecutor.cs |
| `ParentControls.ParentControlsType` | **Dies** | Measured on a Variant holder: VBMethodOrDataMemberNotFoundException: "Method or data member not found (ParentControlsType in Right(EmptyVariant))". The owning ParentControls type does not… | Exceptions.cs |
| `Partition` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. `Partition(n, lo, hi, size)` throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (Partition)". Named in… | ExpressionExecutor.cs |
| `Pmt` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs); ExpressionExecutor.cs throws "Compile error:\n\nSub or Function not defined (Pmt)". Named in the deferral list at docs/interpreter-gaps.md:77. | VB6BuiltIns.cs |
| `PPmt` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs); ExpressionExecutor.cs throws "Compile error:\n\nSub or Function not defined (PPmt)". Named in the deferral list at… | VB6BuiltIns.cs |
| `Preserve` | **Dies** | throws `"PRESERVE not implemented"` — (`redimStmt : REDIM WS (PRESERVE WS)? redimSubStmt ...`) and throws on the first line of the visitor: Measured: `Dim a() / ReDim a(2) / ReDim… | StatementExecutor.cs |
| `Print #` | **Dies** | `printStmt : PRINT WS valueStmt WS? COMMA (WS? outputList)?` (Grammar/VB6.g4); the visitor body is the throw. Probed `Print #1, "x"`, `Print #1, "a", "b"`, `Print #1, "a"; "b"`, `Print #1… | StatementExecutor.cs |
| `Printer` | **Dies** | never seeded. BasicInterpreter.cs seeds only Debug, Err and App as program globals. All three positions measured: read -> "Compile error: Variable not defined (Printer)"; assignment ->… | BasicInterpreter.cs |
| `Printer.ColorMode` | **Dies** | because the Printer object it hangs off is never seeded. MEASURED for "Printer.Orientation = 2": "Run-time error '424': Object required Can't find variable Printer" (StatementExecutor.cs)… | BasicInterpreter.cs |
| `Printer.Duplex` | **Dies** | because the Printer object it hangs off is never seeded. MEASURED for "Printer.Orientation = 2": "Run-time error '424': Object required Can't find variable Printer" (StatementExecutor.cs)… | BasicInterpreter.cs |
| `Printer.Orientation` | **Dies** | because the Printer object it hangs off is never seeded. MEASURED for "Printer.Orientation = 2": "Run-time error '424': Object required Can't find variable Printer" (StatementExecutor.cs)… | BasicInterpreter.cs |
| `Printer.PaperBin` | **Dies** | because the Printer object it hangs off is never seeded. MEASURED for "Printer.Orientation = 2": "Run-time error '424': Object required Can't find variable Printer" (StatementExecutor.cs)… | BasicInterpreter.cs |
| `Printer.PaperSize` | **Dies** | because the Printer object it hangs off is never seeded. MEASURED for "Printer.Orientation = 2": "Run-time error '424': Object required Can't find variable Printer" (StatementExecutor.cs)… | BasicInterpreter.cs |
| `Printer.PrintQuality` | **Dies** | because the Printer object it hangs off is never seeded. MEASURED for "Printer.Orientation = 2": "Run-time error '424': Object required Can't find variable Printer" (StatementExecutor.cs)… | BasicInterpreter.cs |
| `Printers` | **Dies** | Measured verbatim for Printers(0).DeviceName: "Compile error: Sub or Function not defined (Printers)"; for Printers.Count: "Compile error: Variable not defined (Printers)"… | BasicInterpreter.cs |
| `PropertyBag` | **Dies** | Measured for `Dim p As PropertyBag`: VBCompileErrorException: "User-defined type not defined: PropertyBag" (StatementExecutor.cs); same message for `Set p = New PropertyBag`… | StatementExecutor.cs |
| `PropertyBag.Contents` | **Dies** | Measured: VBMethodOrDataMemberNotFoundException: "Method or data member not found (Contents in Right(EmptyVariant))". | Exceptions.cs |
| `PropertyBag.ReadProperty` | **Dies** | Measured on a Variant holder: VBMethodOrDataMemberNotFoundException: "Method or data member not found (ReadProperty(\"A\", 0) in Right(EmptyVariant))". Measured on a real `PropBag As… | Exceptions.cs |
| `PropertyBag.WriteProperty` | **Dies** | Measured: VBRunTimeException: "Unknown method WriteProperty on <Right(EmptyVariant)>()" (StatementExecutor.cs). | StatementExecutor.cs |
| `PropertyBag_VB5` | **Dies** | The name lexes as an ordinary IDENTIFIER (digits and `_` are both in LETTERORDIGIT, VB6.g4). Measured for `Dim p As PropertyBag_VB5` and for `Set p = New PropertyBag_VB5`:… | StatementExecutor.cs |
| `PSet` | **Dies** | throws `"Only single element supported"` — fails at run time on the coordinate pair, exactly as Circle does. VB6.g4 has no PSET token and no psetStmt rule. `PSet (10, 20), vbRed` parses as a… | ExpressionExecutor.cs |
| `Put` | **Dies** | `putStmt : PUT WS valueStmt WS? COMMA WS? valueStmt? WS? COMMA WS? valueStmt` (Grammar/VB6.g4); the visitor body is the throw. Probed `Put #1, 1, v` -> NotImplementedException: "Put not… | StatementExecutor.cs |
| `PV` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs); ExpressionExecutor.cs throws "Compile error:\n\nSub or Function not defined (PV)". Named in the deferral list at docs/interpreter-gaps.md:77. | VB6BuiltIns.cs |
| `QBColor` | **Dies** | Not in BuildRegistry; the only runtime hit is VbKeywordNormalizer.cs ("RGB", "QBColor") - highlighter metadata with no execution path. Throws VBSubOrFunctionNotDefinedException: "Sub or… | ExpressionExecutor.cs |
| `Random` | **Dies** | RANDOM is a lexer token and an openStmt mode alternative (Grammar/VB6.g4). Probed `Open "z.txt" For Random Access Read Write Shared As #1 Len = 32` -> NotImplementedException "Open not… | StatementExecutor.cs |
| `Rate` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs); ExpressionExecutor.cs throws "Compile error:\n\nSub or Function not defined (Rate)". Named in the deferral list at… | VB6BuiltIns.cs |
| `Read` | **Dies** | READ, READ_WRITE, LOCK_READ and LOCK_READ_WRITE are all lexer tokens used by openStmt's Access and lock clauses (Grammar/VB6.g4, 1459-1471). Probed `Open "z.txt" For Random Access Read… | StatementExecutor.cs |
| `Reset` | **Dies** | `resetStmt : RESET` (Grammar/VB6.g4) - a bare keyword with no operands; the visitor body is the throw. Probed `Reset` -> NotImplementedException: "Reset not implemented". | StatementExecutor.cs |
| `Return` | **Dies** | throws `"Return not implemented"` — (returnStmt, VB6.g4) and throws at run time. Verbatim: | StatementExecutor.cs |
| `RGB` | **Dies** | Not in BuildRegistry; the only runtime hit is the highlighter list VbKeywordNormalizer.cs. `Form1.BackColor = RGB(255, 0, 0)` throws VBSubOrFunctionNotDefinedException: "Sub or Function not… | ExpressionExecutor.cs |
| `RightB` | **Dies** | Not registered (VB6BuiltIns.cs); no occurrence of "RightB" under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (RightB)". | ExpressionExecutor.cs, VB6BuiltIns.cs |
| `RmDir` | **Dies** | `rmdirStmt : RMDIR WS valueStmt` (Grammar/VB6.g4); the visitor body is the throw. Probed `RmDir "zdir"` -> NotImplementedException: "Rmdir not implemented". | StatementExecutor.cs |
| `RSet` | **Dies** | throws `"Rset not implemented"` — The grammar has the rule — `rsetStmt : RSET WS implicitCallStmt_InStmt WS? EQ WS? valueStmt` (VB6.g4), reachable from blockStmt (VB6.g4) — but the… | StatementExecutor.cs, VB6.g4 |
| `SavePicture` | **Dies** | throws `"Savepicture not implemented"` — The grammar has `savepictureStmt : SAVEPICTURE WS valueStmt WS? COMMA WS? valueStmt` (VB6.g4) and SAVEPICTURE is a real token, but the visitor body… | StatementExecutor.cs |
| `SaveSetting` | **Dies** | throws `"SaveSetting not implemented"` — Grammar rule `saveSettingStmt : SAVESETTING WS valueStmt WS? COMMA WS? valueStmt WS? COMMA WS? valueStmt WS? COMMA WS? valueStmt` (VB6.g4); the… | StatementExecutor.cs |
| `Scale` | **Dies** | throws `"Only single element supported"` — fails at run time. VB6.g4 has no SCALE token and no scaleStmt rule. `Scale (0, 0)-(100, 100)` parses as a bare procedure call with one argument - a… | ExpressionExecutor.cs |
| `Screen` | **Dies** | never seeded. BasicInterpreter.cs seeds only Debug, Err and App as program globals. Measured verbatim: "Compile error: Variable not defined (Screen)"; an assignment such as… | BasicInterpreter.cs |
| `Screen.ActiveControl` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs |
| `Screen.ActiveForm` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs |
| `Screen.FontCount` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs |
| `Screen.Fonts` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs |
| `Screen.Height` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs |
| `Screen.MousePointer` | **Dies** | Measured verbatim for "Screen.MousePointer = 11": "Run-time error '424': Object required Can't find variable Screen" (StatementExecutor.cs). | StatementExecutor.cs |
| `Screen.TwipsPerPixelX` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs |
| `Screen.TwipsPerPixelY` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs |
| `Screen.Width` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs |
| `Seek` | **Dies** | fails at name resolution: SEEK is a lexer keyword but is listed in `ambiguousKeyword`, so `Seek(1)` parses as a call. Probed `Debug.Print Seek(1)` -> VBSubOrFunctionNotDefinedException:… | ExpressionExecutor.cs |
| `Seek` | **Dies** | `seekStmt : SEEK WS valueStmt WS? COMMA WS? valueStmt` (Grammar/VB6.g4); the visitor body is the throw. Probed `Seek #1, 1` -> NotImplementedException: "Seek not implemented". | StatementExecutor.cs |
| `SelectedControls` | **Dies** | Measured for `Dim s As SelectedControls`: VBCompileErrorException: "User-defined type not defined: SelectedControls" (StatementExecutor.cs). | StatementExecutor.cs |
| `SendKeys` | **Dies** | throws `"Sendkeys not implemented"` — Grammar rule `sendkeysStmt : SENDKEYS WS valueStmt (WS? COMMA WS? valueStmt)?` (VB6.g4) with a real SENDKEYS token; the visitor body is a single… | StatementExecutor.cs |
| `SetAttr` | **Dies** | `setattrStmt : SETATTR WS valueStmt WS? COMMA WS? valueStmt` (Grammar/VB6.g4); the visitor body is the throw. Probed `SetAttr "a.txt", 0` -> NotImplementedException: "Setattr not… | StatementExecutor.cs |
| `Shared` | **Dies** | SHARED is a lexer token, one of openStmt's lock-clause alternatives `(SHARED \| LOCK_READ \| LOCK_WRITE \| LOCK_READ_WRITE)` (Grammar/VB6.g4). Probed `Open "z.txt" For Random Access Read… | StatementExecutor.cs |
| `SLN` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs); ExpressionExecutor.cs throws "Compile error:\n\nSub or Function not defined (SLN)". Named in the deferral list at docs/interpreter-gaps.md:77. | VB6BuiltIns.cs |
| `Spc` | **Dies** | in both positions, but is implemented in neither. The grammar gives it a dedicated slot - `outputList_Expression : (SPC \| TAB) (WS? LPAREN WS? argsCall WS? RPAREN)? \| valueStmt`… | StatementExecutor.cs |
| `Static` | **Dies** | throws `"non dim variables not supported"` — Two distinct forms, both broken, in different ways. (a) The LOCAL form parses (`variableStmt : (DIM \| STATIC \| visibility) WS ...`) and throws at… | StatementExecutor.cs, PrePass.cs, VB6.g4 |
| `StrComp` | **Dies** | Not registered (VB6BuiltIns.cs; no occurrence of "StrComp" under IDE/HexIDE.Runtime). `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (StrComp)". | ExpressionExecutor.cs, VB6BuiltIns.cs |
| `StrConv` | **Dies** | Not registered (VB6BuiltIns.cs; no occurrence of "StrConv" under IDE/HexIDE.Runtime, nor in VbSignatures.cs). `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not… | ExpressionExecutor.cs, VB6BuiltIns.cs |
| `StrPtr` | **Dies** | Zero hits anywhere in IDE/ outside obj/bin — not registered, not in the grammar, not in editor metadata; ExpressionExecutor.cs throws "Compile error:\n\nSub or Function not defined… | VB6BuiltIns.cs |
| `Switch` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. `x = Switch(c1, v1, c2, v2)` throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (Switch)". Named in… | ExpressionExecutor.cs |
| `SYD` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs); ExpressionExecutor.cs throws "Compile error:\n\nSub or Function not defined (SYD)". Named in the deferral list at docs/interpreter-gaps.md:77. | VB6BuiltIns.cs |
| `Tab` | **Dies** | in both positions, implemented in neither - identical shape to Spc. `outputList_Expression : (SPC \| TAB) ...` (Grammar/VB6.g4); probed `Print #1, Tab(5); "a"` -> NotImplementedException… | StatementExecutor.cs |
| `Unlock` | **Dies** | `unlockStmt : UNLOCK WS valueStmt (WS? COMMA WS? valueStmt (WS TO WS valueStmt)?)?` (Grammar/VB6.g4); the visitor body is the throw. Probed `Unlock #1, 1 To 2` -> NotImplementedException:… | StatementExecutor.cs |
| `VarPtr` | **Dies** | Zero hits anywhere in IDE/ outside obj/bin — not registered, not in the grammar, not in editor metadata; ExpressionExecutor.cs throws "Compile error:\n\nSub or Function not defined… | VB6BuiltIns.cs |
| `VB_MemberFlags` | **Dies** | throws `"Attribute not implemented"` — VB6 writes this only at member level - inside a procedure body, or immediately after a module-level declaration - and both positions land in a block… | StatementExecutor.cs |
| `VB_PredeclaredId` | **Dies** | but is silently discarded - no throw, and no behaviour. Emitted in the canonical class header (ModuleFileFormat.cs) and preserved verbatim, but a repo-wide grep for a consumer across… | ModuleFileFormat.cs |
| `VB_ProcData.VB_Invoke_Func` | **Dies** | throws. The dotted name is handled by the grammar (attributeStmt takes an implicitCallStmt_InStmt, VB6.g4), so 'Attribute Foo.VB_ProcData.VB_Invoke_Func = "M14"' inside a Sub parses… | StatementExecutor.cs |
| `VB_UserMemId` | **Dies** | throws `"Attribute not implemented"` — throws. Member-level only; the statement reaches StatementExecutor.VisitAttributeStmt, which is '' (StatementExecutor.cs). Measured: 'Public Function… | StatementExecutor.cs |
| `VB_VarDescription` | **Dies** | throws `"Attribute not implemented"` — throws. VB6 writes it in the declarations section immediately after the variable it describes, which is past the contiguous header run, so it is… | StatementExecutor.cs |
| `VB_VarHelpID` | **Dies** | throws `"Attribute not implemented"` — throws. Same declarations-section position as VB_VarDescription, therefore the same block-statement path: '' (StatementExecutor.cs). | StatementExecutor.cs |
| `VB_VarMemberFlags` | **Dies** | throws. Measured: 'Public Foo As Long' followed by 'Attribute Foo.VB_VarMemberFlags = "40"' raises 'NotImplementedException: Attribute not implemented' (StatementExecutor.cs). | StatementExecutor.cs |
| `VB_VarUserMemId` | **Dies** | throws `"Attribute not implemented"` — throws. Declarations-section position, so the block path applies: '' (StatementExecutor.cs). Measured at module top (before any code) it is instead… | StatementExecutor.cs |
| `Vba6` | **Dies** | Not defined anywhere in the interpreter (repo-wide grep for Vba6/VBA6 across IDE/ and LspServer/ returned nothing). Reachable only from #If, which throws first (PrePass.cs /… | PrePass.cs |
| `vbBack` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | ExpressionExecutor.cs |
| `vbFormFeed` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | ExpressionExecutor.cs |
| `vbNewLine` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | ExpressionExecutor.cs |
| `vbNullChar` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | ExpressionExecutor.cs |
| `vbNullString` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | ExpressionExecutor.cs |
| `vbTab` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | ExpressionExecutor.cs |
| `vbUseCompareOption` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | ExpressionExecutor.cs |
| `vbVerticalTab` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | ExpressionExecutor.cs |
| `Width #` | **Dies** | `widthStmt : WIDTH WS valueStmt WS? COMMA WS? valueStmt` (Grammar/VB6.g4); the visitor body is the throw. Probed `Width #1, 80` -> NotImplementedException: "Width not implemented". | StatementExecutor.cs |
| `Win16` | **Dies** | Not defined anywhere in the interpreter (the same repo-wide grep as Win32 returned no Win16 hits at all). Reachable only from #If, which throws first (PrePass.cs / StatementExecutor.cs). | PrePass.cs |
| `Win32` | **Dies** | Not defined anywhere in the interpreter. A repo-wide grep for a Win32 conditional-compilation constant across IDE/ and LspServer/ (.cs and .g4) returned only unrelated hits - Avalonia… | PrePass.cs |
| `Write` | **Dies** | WRITE is a lexer token used both in openStmt's `Access Write` / `Access Read Write` clause and as the head of writeStmt (Grammar/VB6.g4, 630-632). As a clause keyword: probed `Open "z.txt"… | StatementExecutor.cs |
| `Write #` | **Dies** | `writeStmt : WRITE WS valueStmt WS? COMMA (WS? outputList)?` (Grammar/VB6.g4); the visitor body is the throw. Probed `Write #1, "x"` -> NotImplementedException: "Write not implemented". | StatementExecutor.cs |

## Full inventory by category

Every name, including the ones that work. Sorted within each table by F5 impact — Won't load, Dies,
Partial, No-op, Supported — because a reader of a coverage document is looking for what is missing.


### Statements — 105 names (47 absent, 35 partial, 23 supported)

| Name | Status | Detail | Source |
|---|---|---|---|
| `Time` | **Dies** | **Fixed by #191** — was Silently wrong: it ran, did nothing and let the program continue. Now raises, naming the intrinsic | StatementExecutor.cs |
| `AppActivate` | **Dies** | throws `"AppActivate not implemented"` — Grammar rule `appActivateStmt : APPACTIVATE WS valueStmt (WS? COMMA WS? valueStmt)?` (VB6.g4); the visitor body is a single throw:… | StatementExecutor.cs |
| `Close` | **Dies** | `closeStmt : CLOSE (WS valueStmt (WS? COMMA WS? valueStmt)*)?` (Grammar/VB6.g4) and a visitor exists, but its whole body is the throw. Probed both `Close #1` and bare `Close` ->… | StatementExecutor.cs |
| `Date` | **Dies** | throws `"Date not implemented"` — The grammar has a dedicated rule `dateStmt : DATE WS? EQ WS? valueStmt` (VB6.g4), listed in the block alternatives at VB6.g4 ahead of letStmt so it… | StatementExecutor.cs |
| `DefBool` | **Dies** | throws `"Deftype not implemented"` — (grammar rule `deftypeStmt` at VB6.g4, a blockStmt so it reaches the module's top-level block) and then throws at run time: Measured: `DefBool A-Z`… | StatementExecutor.cs |
| `DefByte` | **Dies** | throws `"Deftype not implemented"` — the single shared VisitDeftypeStmt covers every Def* token (DEFBOOL\|DEFBYTE\|DEFINT\|DEFLNG\|DEFCUR\|DEFSNG\|DEFDBL\|DEFDEC\|DEFDATE\|DEFSTR\|DEFOBJ\… | StatementExecutor.cs |
| `DefCur` | **Dies** | throws `"Deftype not implemented"` | StatementExecutor.cs |
| `DefDate` | **Dies** | throws `"Deftype not implemented"` | StatementExecutor.cs |
| `DefDbl` | **Dies** | throws `"Deftype not implemented"` | StatementExecutor.cs |
| `DefDec` | **Dies** | throws `"Deftype not implemented"` — (DEFDEC is a lexer token and appears in the deftypeStmt alternation) and then | StatementExecutor.cs, VB6.g4 |
| `DefInt` | **Dies** | throws `"Deftype not implemented"` — Measured: `DefInt A-Z` -> that exact message. | StatementExecutor.cs |
| `DefLng` | **Dies** | throws `"Deftype not implemented"` | StatementExecutor.cs |
| `DefObj` | **Dies** | throws `"Deftype not implemented"` | StatementExecutor.cs |
| `DefSng` | **Dies** | throws `"Deftype not implemented"` | StatementExecutor.cs |
| `DefStr` | **Dies** | throws `"Deftype not implemented"` — Measured: `DefStr S` -> that exact message. | StatementExecutor.cs |
| `DefVar` | **Dies** | throws `"Deftype not implemented"` | StatementExecutor.cs |
| `DeleteSetting` | **Dies** | throws `"DeleteSetting not implemented"` — Grammar rule `deleteSettingStmt : DELETESETTING WS valueStmt WS? COMMA WS? valueStmt (WS? COMMA WS? valueStmt)?` (VB6.g4) with a real DELETESETTING… | StatementExecutor.cs |
| `End` | **Dies** | throws `"End not implemented"` — (endStmt, VB6.g4) and throws at run time. Verbatim: Measured: `Debug.Print 1 / End` -> NotImplementedException: End not implemented. | StatementExecutor.cs |
| `FileCopy` | **Dies** | `filecopyStmt : FILECOPY WS valueStmt WS? COMMA WS? valueStmt` (Grammar/VB6.g4); the visitor body is the throw. Probed `FileCopy "a.txt", "b.txt"` -> NotImplementedException: "Filecopy not… | StatementExecutor.cs |
| `Get` | **Dies** | `getStmt : GET WS valueStmt WS? COMMA WS? valueStmt? WS? COMMA WS? valueStmt` (Grammar/VB6.g4); the visitor body is the throw. Probed `Get #1, 1, v` -> NotImplementedException: "Get not… | StatementExecutor.cs |
| `GoSub...Return` | **Dies** | throws `"GoSub not implemented"` — Both halves parse and both throw. GoSub: (StatementExecutor.cs). Return: `throw new NotImplementedException("Return not implemented")` (:1172)… | StatementExecutor.cs |
| `Input #` | **Dies** | `inputStmt : INPUT WS valueStmt (WS? COMMA WS? valueStmt)+` (Grammar/VB6.g4); the visitor body is the throw. Probed `Input #1, s` -> NotImplementedException: "Input not implemented". | StatementExecutor.cs |
| `Kill` | **Dies** | `killStmt : KILL WS valueStmt` (Grammar/VB6.g4); the visitor body is the throw. Probed `Kill "z.txt"` -> NotImplementedException: "Kill not implemented". | StatementExecutor.cs |
| `Line Input #` | **Dies** | `lineInputStmt : LINE_INPUT WS valueStmt WS? COMMA WS? valueStmt` (Grammar/VB6.g4); the visitor body is the throw. Probed `Line Input #1, s` -> NotImplementedException: "LineInput not… | StatementExecutor.cs |
| `Lock` | **Dies** | `lockStmt : LOCK WS valueStmt (WS? COMMA WS? valueStmt (WS TO WS valueStmt)?)?` (Grammar/VB6.g4); the visitor body is the throw. Probed `Lock #1, 1 To 2` -> NotImplementedException: "Lock… | StatementExecutor.cs |
| `LSet` | **Dies** | throws `"Lset not implemented"` — The grammar has the rule — `lsetStmt : LSET WS implicitCallStmt_InStmt WS? EQ WS? valueStmt` (VB6.g4), reachable from blockStmt (VB6.g4) — but the… | StatementExecutor.cs, VB6.g4 |
| `Mid$` | **Dies** | `Mid$` lexes as MID + DOLLAR, and DOLLAR is a `typeHint` (VB6.g4) which `iCS_S_ProcedureOrArrayCall` accepts (VB6.g4). The letStmt handler rejects it BEFORE reaching the Mid-statement… | StatementExecutor.cs |
| `MkDir` | **Dies** | `mkdirStmt : MKDIR WS valueStmt` (Grammar/VB6.g4); the visitor body is the throw. Probed `MkDir "zdir"` -> NotImplementedException: "Mkdir not implemented". | StatementExecutor.cs |
| `Name` | **Dies** | `nameStmt : NAME WS valueStmt WS AS WS valueStmt` (Grammar/VB6.g4); the visitor body is the throw. Probed `Name "a.txt" As "b.txt"` -> NotImplementedException: "Name not implemented". | StatementExecutor.cs |
| `On...GoSub` | **Dies** | throws `"OnGoSub not implemented"` — (onGoSubStmt, VB6.g4) and throws at run time. Verbatim: Measured: `On i GoSub L1` -> NotImplementedException: OnGoSub not implemented. | StatementExecutor.cs |
| `On...GoTo` | **Dies** | throws `"OnGoTo not implemented"` — (onGoToStmt, VB6.g4) and throws at run time. Verbatim: Measured: `On i GoTo L1, L2` -> NotImplementedException: OnGoTo not implemented. | StatementExecutor.cs |
| `Open` | **Dies** | the keystone of the family. The full VB6 clause grammar is present (`openStmt`, Grammar/VB6.g4, covering mode, Access, lock and `Len =`) and VisitOpenStmt exists, but its entire body is the… | StatementExecutor.cs |
| `Preserve` | **Dies** | throws `"PRESERVE not implemented"` — (`redimStmt : REDIM WS (PRESERVE WS)? redimSubStmt ...`) and throws on the first line of the visitor: Measured: `Dim a() / ReDim a(2) / ReDim… | StatementExecutor.cs |
| `Print #` | **Dies** | `printStmt : PRINT WS valueStmt WS? COMMA (WS? outputList)?` (Grammar/VB6.g4); the visitor body is the throw. Probed `Print #1, "x"`, `Print #1, "a", "b"`, `Print #1, "a"; "b"`, `Print #1… | StatementExecutor.cs |
| `Put` | **Dies** | `putStmt : PUT WS valueStmt WS? COMMA WS? valueStmt? WS? COMMA WS? valueStmt` (Grammar/VB6.g4); the visitor body is the throw. Probed `Put #1, 1, v` -> NotImplementedException: "Put not… | StatementExecutor.cs |
| `Reset` | **Dies** | `resetStmt : RESET` (Grammar/VB6.g4) - a bare keyword with no operands; the visitor body is the throw. Probed `Reset` -> NotImplementedException: "Reset not implemented". | StatementExecutor.cs |
| `Return` | **Dies** | throws `"Return not implemented"` — (returnStmt, VB6.g4) and throws at run time. Verbatim: | StatementExecutor.cs |
| `RmDir` | **Dies** | `rmdirStmt : RMDIR WS valueStmt` (Grammar/VB6.g4); the visitor body is the throw. Probed `RmDir "zdir"` -> NotImplementedException: "Rmdir not implemented". | StatementExecutor.cs |
| `RSet` | **Dies** | throws `"Rset not implemented"` — The grammar has the rule — `rsetStmt : RSET WS implicitCallStmt_InStmt WS? EQ WS? valueStmt` (VB6.g4), reachable from blockStmt (VB6.g4) — but the… | StatementExecutor.cs, VB6.g4 |
| `SavePicture` | **Dies** | throws `"Savepicture not implemented"` — The grammar has `savepictureStmt : SAVEPICTURE WS valueStmt WS? COMMA WS? valueStmt` (VB6.g4) and SAVEPICTURE is a real token, but the visitor body… | StatementExecutor.cs |
| `SaveSetting` | **Dies** | throws `"SaveSetting not implemented"` — Grammar rule `saveSettingStmt : SAVESETTING WS valueStmt WS? COMMA WS? valueStmt WS? COMMA WS? valueStmt WS? COMMA WS? valueStmt` (VB6.g4); the… | StatementExecutor.cs |
| `Seek` | **Dies** | `seekStmt : SEEK WS valueStmt WS? COMMA WS? valueStmt` (Grammar/VB6.g4); the visitor body is the throw. Probed `Seek #1, 1` -> NotImplementedException: "Seek not implemented". | StatementExecutor.cs |
| `SendKeys` | **Dies** | throws `"Sendkeys not implemented"` — Grammar rule `sendkeysStmt : SENDKEYS WS valueStmt (WS? COMMA WS? valueStmt)?` (VB6.g4) with a real SENDKEYS token; the visitor body is a single… | StatementExecutor.cs |
| `SetAttr` | **Dies** | `setattrStmt : SETATTR WS valueStmt WS? COMMA WS? valueStmt` (Grammar/VB6.g4); the visitor body is the throw. Probed `SetAttr "a.txt", 0` -> NotImplementedException: "Setattr not… | StatementExecutor.cs |
| `Unlock` | **Dies** | `unlockStmt : UNLOCK WS valueStmt (WS? COMMA WS? valueStmt (WS TO WS valueStmt)?)?` (Grammar/VB6.g4); the visitor body is the throw. Probed `Unlock #1, 1 To 2` -> NotImplementedException:… | StatementExecutor.cs |
| `Width #` | **Dies** | `widthStmt : WIDTH WS valueStmt WS? COMMA WS? valueStmt` (Grammar/VB6.g4); the visitor body is the throw. Probed `Width #1, 80` -> NotImplementedException: "Width not implemented". | StatementExecutor.cs |
| `Write #` | **Dies** | `writeStmt : WRITE WS valueStmt WS? COMMA (WS? outputList)?` (Grammar/VB6.g4); the visitor body is the throw. Probed `Write #1, "x"` -> NotImplementedException: "Write not implemented". | StatementExecutor.cs |
| `Resume line` | Partial | `resumeStmt : RESUME (WS (NEXT \| ambiguousIdentifier))?` (VB6.g4). An identifier target becomes ResumeSignal(ResumeKind.Label) and the driver does `ResolveLabel(labels, r.Label!)`… | StatementExecutor.cs |
| `ChDir` | Partial | Implemented and oracle-pinned. VisitChDirStmt evaluates the argument, coerces to string, assigns Environment.CurrentDirectory, and maps… | StatementExecutor.cs |
| `Const` | Partial | Module-level names are hoisted by PrePass (so a Sub declared before the Const can still see it) and filled by the runtime visitor; local Consts inside a Sub work; multiple per line work… | StatementExecutor.cs, PrePass.cs |
| `Dim` | Partial | Dim is treated as a DECLARATION, not an executable statement: HoistDeclaredLocals allocates every local a procedure declares before the body runs, in declaration order, and DeclareLocal is… | StatementExecutor.cs, PrePass.cs |
| `Do...Loop` | Partial | All three shapes implemented - bare `Do/Loop` (VisitDoBlockLoop, :430), pre-tested `Do While\|Until ... Loop` (VisitDoWhileBlockLoop, :483), post-tested `Do ... Loop While\|Until`… | StatementExecutor.cs |
| `Enum` | Partial | **Improved** — a member's value is now a constant EXPRESSION, not a decimal literal: `&H80000005` (-2147483643, the high bit making it a negative Long), `&O17`, negatives, `xFirst + 1`, `2 ^ 3` and bit-ors of earlier members all work, and `/` is real division coerced to Long by rounding half to even. Evaluated in a single forward walk, which is measured rather than convenient — VB6 refuses a forward reference with "Constant expression required". Members are now READ-ONLY: `pTwo = 5` was silently accepted and is now refused, as VB6 refuses it. **Also fixed**: module scope (Public/Private honoured, two modules exporting one Public Enum name refused at load, a member name two enums share is "Ambiguous name detected" at the bare use), the three-part `Module1.MyEnum.Member` form, the PROJECT qualifier (`Project1.MyEnum.Foo`), and a zero-member enum is refused. Note `Module1.MyEnum` is NOT a type name in VB6 - an enum's identity is project-scoped - so only `Project1.MyEnum` qualifies in type position | PrePass.cs |
| `Enum...End Enum` | Partial | The whole construct is handled by PrePass.VisitEnumerationStmt — see the `Enum` row. Addressing: bare `Value`, `Enum.Value` and `Module1.Value` all resolve; `Module1.Enum.Value` and the library-qualified `VBRUN.AlignConstants.vbAlignBottom` do not yet, though both are measured legal. Two enums sharing a member name silently return the last one where VB6 says "Ambiguous name detected" | PrePass.cs |
| `Erase` | Partial | VisitEraseStmt implements the oracle-verified split: a DYNAMIC array is freed (arr.Free(), so a later LBound/UBound/index raises Err 9) and a FIXED array keeps its bounds with every element… | StatementExecutor.cs |
| `Event` | Partial | PrePass.VisitEventStmt records the event NAME only. Dispatch is by handler name - RaiseEvent walks the source object's sinks and calls `{sinkVarName}_{eventName}` on each listener… | PrePass.cs, StatementExecutor.cs |
| `For Each...Next` | Partial | VisitForEachStmt iterates array elements in VBArray order (first subscript fastest - ForEachTests.TwoDimensional_FirstSubscriptFastest), auto-declares the loop variable if absent, and binds… | StatementExecutor.cs |
| `For...Next` | Partial | VisitForNextStmt implements VB6's `<=`/`>=` termination test (not equality), leaves the counter one step past the limit, and picks Integer vs Double for the counter by magnitude - all… | StatementExecutor.cs |
| `Function` | Partial | PrePass.VisitFunctionStmt registers a ProcedureInfo (name, params, return type, body, IsPrivate). The return is by assignment to the procedure's own name; an unassigned return yields the… | PrePass.cs, BasicInterpreter.cs |
| `Function...End Function` | Partial | The whole construct - `(visibility WS)? (STATIC WS)? FUNCTION WS name (argList)? (asTypeClause)? ... END_FUNCTION` - is collected by PrePass.VisitFunctionStmt and executed by… | PrePass.cs, BasicInterpreter.cs |
| `GoTo` | Partial | VisitGoToStmt throws a private GoToSignal that unwinds to ExecuteProcedureBody, which repositions the program counter (StatementExecutor.cs). Only TOP-LEVEL statements of the procedure body… | StatementExecutor.cs |
| `If...Then...Else (block form)` | Partial | VisitBlockIfThenElse handles If / ElseIf* / Else / End If with correct first-match semantics. The single restriction is the condition type check, verbatim: `throw new… | StatementExecutor.cs |
| `If...Then...Else (single-line form)` | Partial | VisitInlineIfThenElse runs blockStmt(0) on true and blockStmt(1) (the Else arm) on false. Condition unpacked with TryUnpack<bool>, which accepts ValueType.Boolean only (VB6Visitor.cs)… | StatementExecutor.cs |
| `Implements` | Partial | Fully modelled and oracle-measured: PrePass collects the claimed interface names; VbInterface.VerifyConformance runs at FIRST INSTANTIATION (memoised per class) and raises VB6's own… | VbInterface.cs, BasicInterpreter.cs, StatementExecutor.cs |
| `Let` | Partial | **Still current, but narrower than it reads:** #184 folded READ-path chains; this restriction is now write-path only. — Both forms work - the grammar is `(LET WS)? implicitCallStmt_InStmt ... EQ ... valueStmt`, and the explicit keyword is measured working (`Let x = 3` prints 3). VisitLetStmt covers plain… | StatementExecutor.cs |
| `Load` | Partial | Implemented for control arrays ONLY. VisitLoadStmt carries the comment "Only control-array element loading is modelled (Load Command1(i)); a bare form Load isn't." and calls… | StatementExecutor.cs |
| `Mid` | Partial | The `Mid(target, start[, length]) = replacement` STATEMENT is implemented, but NOT via VisitMidStmt — that visitor still throws `NotImplementedException("Mid not implemented")`… | StatementExecutor.cs, VB6BuiltIns.Strings.cs |
| `On Error GoTo line` | Partial | Installs errorMode = ErrorMode.GoToLabel with the target text as handlerLabel (StatementExecutor.cs). ExecuteProcedureBody's catch at :1894 captures the error into Err, records faultPc… | StatementExecutor.cs |
| `Property` | Partial | The three accessors are collected into a PropertyInfo keyed by property name and dispatched by ACCESS KIND: Get on read, Let on value-assign, Set on object-assign. TWO restrictions. (1)… | PrePass.cs, ExpressionExecutor.cs, StatementExecutor.cs |
| `Property Get` | Partial | Function-like: returns via its own name, dispatched on a member READ of a class instance. A `As <Class>` return carries the concrete name so the return slot seeds a real UDT or Nothing… | PrePass.cs, ExpressionExecutor.cs |
| `Property Let` | Partial | Sub-like: its single parameter receives the assigned value, coerced to that parameter's declared type, and it WINS over a raw field write on `obj.Member = v`. Works through a `With` target… | PrePass.cs |
| `Property Set` | Partial | Sub-like, dispatched on `Set obj.Member = o` and winning over a raw object-field write; the reference flows through the accessor's parameter (counted on bind, released at scope-exit) so… | PrePass.cs, StatementExecutor.cs |
| `ReDim` | Partial | A plain ReDim of an already-declared simple variable works, including multi-dimensional bounds and a trailing `As T` clause - measured: `Dim a() / ReDim a(2,3) / a(1,1)=7` prints 7, and… | StatementExecutor.cs |
| `Resume` | Partial | VisitResumeStmt throws a ResumeSignal(ResumeKind.Same) caught by ExecuteProcedureBody, which sets pc = faultPc to retry the faulting statement and clears the fault. A Resume with no active… | StatementExecutor.cs |
| `Resume Next` | Partial | ResumeSignal(ResumeKind.Next) -> `pc = faultPc + 1`. Tested by ErrorHandlingGoToTests.OnErrorGoTo_Handler_Then_ResumeNext. RESTRICTION is the same nested granularity as Resume: measured `On… | StatementExecutor.cs |
| `Select Case` | Partial | VisitSelectCaseStmt evaluates the selector once, then tries each Case in order: exact values, comma lists, `a To b` ranges (TryCompareTo) and `Is <op>` (TryCompareTo for ordering, Equals… | Vb6Value.cs |
| `Set` | Partial | **Still current, but narrower than it reads:** #184 folded READ-path chains; this restriction is now write-path only. — VisitSetStmt stores the REFERENCE (never a copy) with correct AddRef-before-Release ordering, handles WithEvents advise/unadvise on rebind, and supports three targets: a bare variable… | StatementExecutor.cs |
| `Type` | Partial | PrePass.VisitTypeStmt builds a UdtTypeDef of scalar fields plus nested-type names resolved at instantiation. Scalar fields, untyped (Variant) fields and nested UDT fields work (measured:… | PrePass.cs **Module scope now honoured**: a UDT's identity is module-scoped, so two modules may each own a `Public Type` of one name, `Module2.Point` names one specifically and overrides a local declaration, `Private` is invisible from elsewhere even when qualified, and two foreign Publics raise "Ambiguous name detected" (#180) |
| `Type...End Type` | Partial | The whole construct is handled by PrePass.VisitTypeStmt - see the `Type` row. Works: scalar fields, untyped (Variant) fields, nested UDT fields at any depth, arrays OF a UDT, cross-module… | PrePass.cs, VbUdt.cs **Module scope now honoured**: a UDT's identity is module-scoped, so two modules may each own a `Public Type` of one name, `Module2.Point` names one specifically and overrides a local declaration, `Private` is invisible from elsewhere even when qualified, and two foreign Publics raise "Ambiguous name detected" (#180) |
| `Unload` | Partial | Implemented for control arrays ONLY. VisitUnloadStmt carries the comment "Only control-array element unloading is modelled (Unload Command1(i)); a bare form Unload isn't." Anything else… | StatementExecutor.cs |
| `While...Wend` | Partial | VisitWhileWendStmt is a pre-tested loop; the comment records the design correctly: "VB6 has no `Exit While`/`Continue While`, so any non-Nothing control flow (Exit Sub/Function/Property)… | StatementExecutor.cs |
| `With...End With` | Partial | **Superseded by #184:** object member chains now FOLD on the read path, so `a.Inner.V` works. The single-dot restriction described below survives only on the WRITE path (`obj.a.b = x`, `Set obj.a.b = o`). — Per-activation withTargets stack, pushed in VisitWithStmt and popped in a finally; nesting resolves innermost-first and a leading dot with no active With raises Error 91 (WithTests:… | StatementExecutor.cs |
| `Beep` | Supported | Really implemented, not a no-op. VisitBeepStmt guards the platform then calls Console.Beep(): `if (OperatingSystem.IsWindows() \|\| OperatingSystem.IsLinux() \|\| OperatingSystem.IsMacOS())… | StatementExecutor.cs |
| `Call` | Supported | Two forms, both implemented: VisitECS_ProcedureCall for `Call Foo(a, b)` (StatementExecutor.cs) and VisitECS_MemberProcedureCall for `Call Module1.Foo(...)` / `Call obj.Method(...)` (:590)… | StatementExecutor.cs |
| `ChDrive` | Supported | Implemented and oracle-pinned. VisitChDriveStmt string-coerces the argument, takes the FIRST character, no-ops on an empty string, raises InvalidProcedureCall (5) for a non-A-Z first char… | StatementExecutor.cs |
| `End Enum` | Supported | END_ENUM is a single lexer token closing the `enumerationStmt` rule (`(publicPrivateVisibility WS)? ENUM WS ambiguousIdentifier NEWLINE + (enumerationStmt_Constant)* END_ENUM`)… | VB6.g4 |
| `End Function` | Supported | END_FUNCTION is a single lexer token closing the `functionStmt` rule; PrePass.VisitFunctionStmt consumes the whole construct and the terminator carries no separate behaviour. Exercised by… | VB6.g4 |
| `End Property` | Supported | END_PROPERTY is a single lexer token closing all three of propertyGetStmt, propertyLetStmt and propertySetStmt; the terminator carries no separate behaviour. Exercised by every Property… | VB6.g4 |
| `End Sub` | Supported | END_SUB is a single lexer token closing the `subStmt` rule; PrePass.VisitSubStmt consumes the whole construct and the terminator carries no separate behaviour. Exercised by essentially… | VB6.g4 |
| `End Type` | Supported | END_TYPE is a single lexer token closing the `typeStmt` rule (`(visibility WS)? TYPE WS ambiguousIdentifier NEWLINE + (typeStmt_Element)* END_TYPE`). PrePass.VisitTypeStmt consumes the… | VB6.g4 |
| `Error` | Supported | VisitErrorStmt evaluates the number, then `interpreter.Err.Raise((long)d)` - comment verbatim: "Legacy `Error n` statement - equivalent to Err.Raise n." A non-numeric operand raises… | StatementExecutor.cs |
| `Exit` | Supported | VisitExitStmt maps all five VB6 forms to ControlFlow values: EXIT_DO -> ExitDo, EXIT_FOR -> ExitFor, EXIT_FUNCTION -> ExitFunction, EXIT_PROPERTY -> ExitProperty, EXIT_SUB -> ExitSub… | StatementExecutor.cs, ControlFlow.cs |
| `Exit Do` | Supported | VisitExitStmt returns ControlFlow.ExitDo (StatementExecutor.cs); all three Do visitors convert it to a normal exit. Tested by DoLoopTests.DoLoopUntil_ShouldExitLoopEarlyWithExitDo and two… | StatementExecutor.cs |
| `Exit For` | Supported | Returns ControlFlow.ExitFor; both VisitForNextStmt (:713) and VisitForEachStmt (:658) convert it to a normal exit. Tested by StatementTests.ExitFor_ShouldTerminateLoopEarly and… | StatementExecutor.cs |
| `Exit Function` | Supported | Returns ControlFlow.ExitFunction; ExecuteProcedureBody returns on any non-Nothing ControlFlow (`if (await Visit(stmts[pc]) != ControlFlow.Nothing) return;`), leaving the return value… | StatementExecutor.cs |
| `Exit Property` | Supported | Returns ControlFlow.ExitProperty (StatementExecutor.cs), which ExecuteProcedureBody treats like every other Exit. Property Get/Let/Set bodies run through the same driver… | StatementExecutor.cs |
| `Exit Sub` | Supported | Returns ControlFlow.ExitSub (StatementExecutor.cs). Used throughout ErrorHandlingGoToTests to skip past the handler label, which is the canonical VB6 idiom and is confirmed working. | StatementExecutor.cs |
| `On Error GoTo 0` | Supported | VisitOnErrorStmt compares the target's source text: `if (target == "0") { errorMode = ErrorMode.None; handlerLabel = null; }`. Tested twice -… | StatementExecutor.cs |
| `On Error Resume Next` | Supported | Sets errorMode = ErrorMode.ResumeNext. Trapped at two levels: per-statement inside every nested block (VisitBlock, StatementExecutor.cs) and at the top-level pc driver (:1901); both call… | StatementExecutor.cs |
| `On Local Error` | Supported | The lexer emits a single ON_LOCAL_ERROR token accepted in the same production as ON_ERROR (VB6.g4), and VisitOnErrorStmt branches only on RESUME vs GOTO, so both spellings behave… | StatementExecutor.cs |
| `RaiseEvent` | Supported | VisitRaiseEventStmt resolves Me to the source VbObject, evaluates the args ONCE with Locations so ByRef params alias the raiser's locals and multicast sinks share writes, snapshots… | StatementExecutor.cs |
| `Randomize` | Supported | VisitRandomizeStmt evaluates the optional seed and calls interpreter.BuiltIns.Reseed(seed); Reseed sets the 24-bit LCG state Rnd consumes: `long bits = number is { } n ?… | StatementExecutor.cs |
| `Stop` | Supported | VisitStopStmt: with a DebugController attached it calls EnterBreakFromStopStatementAsync (IDE break mode, like a breakpoint on that line); headless it throws Debugging.StopExecutionSignal… | StatementExecutor.cs |
| `Sub` | Supported | PrePass.VisitSubStmt registers a ProcedureInfo with IsFunction false. Bare call (`Foo 1, 2`), `Call Foo(1, 2)`, cross-module resolution and instance-method dispatch all route through… | PrePass.cs, BasicInterpreter.cs |
| `Sub...End Sub` | Supported | The whole construct - `(visibility WS)? (STATIC WS)? SUB WS name (argList)? ... END_SUB` - is collected by PrePass.VisitSubStmt and executed by BasicInterpreter.RunProcedure, shared with… | PrePass.cs, BasicInterpreter.cs |

### Operators — 32 names (3 absent, 18 partial, 11 supported)

| Name | Status | Detail | Source |
|---|---|---|---|
| `! (dictionary access)` | **Dies** | throws `"dictionaryCallStmt is not supported"` — The grammar yields the pair (`dictionaryCallStmt : EXCLAMATIONMARK ambiguousIdentifier typeHint?`) but every consuming site throws. Measured on a… | ExpressionExecutor.cs |
| `AddressOf` | **Dies** | throws `"ADDRESSOF is not implemented"` — (`ADDRESSOF WS valueStmt # vsAddressOf`) then throws at run time: `public override Task<object?> VisitVsAddressOf(VB6Parser.VsAddressOfContext… | ExpressionExecutor.cs |
| `Like` | **Dies** | throws `"Like is not implemented"` — (`valueStmt WS LIKE WS valueStmt # vsLike`) then throws: `public override Task<object?> VisitVsLike(VB6Parser.VsLikeContext context) => Measured… | ExpressionExecutor.cs |
| `& (string concatenation)` | Partial | `VisitVsAmp`: both Null -> Null; a single Null or Empty concatenates as "" — "Null and Empty both concatenate as \"\" (Empty.Value is null, so the old code NPE'd on it)"; otherwise… | ExpressionExecutor.cs |
| `* (multiplication)` | Partial | `VisitVsMult` -> `VbNumeric.Mul` -> `Arith(l, r, ctx, '*')`, with the measured VB6 result-type ladder (Byte < Integer < Long < Single < Currency < Decimal < Double), Currency/Decimal… | ExpressionExecutor.cs |
| `+ (addition)` | Partial | `VisitVsAdd`: Null on either side -> Null; String + String concatenates (`// VB6: String + String concatenates`); otherwise `VbNumeric.Add`, which special-cases Date — "Date + anything ->… | ExpressionExecutor.cs |
| `+ (string concatenation)` | Partial | String + String concatenates: `if (leftValue.Type == Vb6Value.ValueType.String && rightValue.Type == Vb6Value.ValueType.String) return new Vb6Value((string)leftValue.Value! +… | ExpressionExecutor.cs |
| `+ (unary plus)` | Partial | The grammar has the alternative — `\| PLUS WS? valueStmt # vsPlus` — but ExpressionExecutor has NO `VisitVsPlus` override (grepped: the name occurs only in the generated obj/ parser and… | VB6.g4 |
| `- (subtraction)` | Partial | `VisitVsMinus` -> `VbNumeric.Sub`, with the Date rules written out: "Date - Date -> Double (day difference)", "Date - n (or n - Date) -> Date". Null propagates. Measured `5 - 3` = 2… | ExpressionExecutor.cs |
| `- (unary negation)` | Partial | `VisitVsNegation`: Null -> Null, otherwise `VbNumeric.Negate(value, context)`. Measured `-a` with a = 5 -> Integer -5, `-32768` -> Integer -32768, `-&H10` -> -16, `-7 Mod 2` -> -1… | ExpressionExecutor.cs |
| `. (member access)` | Partial | **Superseded by #184:** object member chains now FOLD on the read path, so `a.Inner.V` works. The single-dot restriction described below survives only on the WRITE path (`obj.a.b = x`, `Set obj.a.b = o`). — Works for: a UDT field chain of ANY depth (`GetUdtField`/`SetUdtField` walk the owned bags — measured `e.A.B = 9` then read back 9); a namespace qualifier (`Module1.Foo`, `MyEnum.Member`… | ExpressionExecutor.cs |
| `/ (floating-point division)` | Partial | `VisitVsDiv` dispatches on the token text (`context.DIV().GetText() == "/"`) into `VbNumeric.RealDivide`: divide-by-zero raises `VBStandardError.DivisionByZero` (Err 11); the result is… | VbNumeric.cs |
| `< (less than)` | Partial | Two STRINGS compare ordinally — VB6 default Option Compare Binary, oracle-verified ("B" < "a" True, "10" < "9" True). Two numerics promote to the wider subtype via `GetTwoValuesSameTypes`… | ExpressionExecutor.cs |
| `<= (less than or equal to)` | Partial | Same cascade as `<`: ordinal string compare, then int/float/double unpack, else `throw new VBRunTimeException(context, VBStandardError.TypeMismatch)`. Measured `(1 <= 2)` True. RESTRICTION:… | ExpressionExecutor.cs |
| `<> (not equal to)` | Partial | `VisitVsNeq` -> `GetTwoValuesSameTypesOrNull`; Null on either side -> Null; otherwise `!leftValue.Equals(rightValue)` (Vb6Value equality is type-first). Measured `(1 <> 2)` True, `("a" <>… | ExpressionExecutor.cs |
| `= (equal to)` | Partial | `VisitVsEq` -> `GetTwoValuesSameTypesOrNull`; Null -> Null; otherwise `leftValue.Equals(rightValue)`. `Empty` coerces to its partner's ZERO — the comment records the measurement: "`Empty =… | ExpressionExecutor.cs |
| `> (greater than)` | Partial | Same cascade as `<`. Measured `(2 > 1)` True, `("b" > "a")` True. RESTRICTION: a String-vs-number pair raises Err 13. | ExpressionExecutor.cs |
| `>= (greater than or equal to)` | Partial | Same cascade as `<`. Measured `(2 >= 1)` True. RESTRICTION: a String-vs-number pair raises Err 13. | ExpressionExecutor.cs |
| `\ (integer division)` | Partial | The same `VisitVsDiv` visitor routes a non-`/` token to `VbNumeric.IntDivide`; operands are rounded to integers first. Measured `7 \ 2` = 3 Integer and `7.6 \ 2` = 4 Long (7.6 rounds to 8)… | ExpressionExecutor.cs |
| `^ (exponentiation)` | Partial | `VisitVsPow` -> `VbNumeric.Power`, with the measured error split recorded in the source: "`^` distinguishes a DOMAIN error from an overflow, and the two get different numbers (measured): 0… | ExpressionExecutor.cs |
| `Mod` | Partial | `VisitVsMod` -> `VbNumeric.Modulo`; operands are rounded to integers before the operation. Null on either side -> Null. Measured `7 Mod 2` = 1 Integer, `-7 Mod 2` = -1, `7.6 Mod 2` = 0… | ExpressionExecutor.cs |
| `. (With-block leading dot)` | Supported | **Superseded by #184:** object member chains now FOLD on the read path, so `a.Inner.V` works. The single-dot restriction described below survives only on the WRITE path (`obj.a.b = x`, `Set obj.a.b = o`). — `VisitWithStmt` evaluates the target and pushes it on this activation's `withTargets`, popping in a `finally`; a member call with no leading part resolves against `withTargets.Peek()`, and… | StatementExecutor.cs |
| `= (assignment)` | Supported | `VisitLetStmt` covers a bare or undeclared variable (VB6 creates it on first use unless Option Explicit — "VB6 creates an undeclared variable on first use — a procedure-local Variant… | StatementExecutor.cs |
| `And` | Supported | Implemented over the oracle-measured `VbBitwise` ladder rather than by coercing to a common type: "Bitwise operators do not coerce to a common type; they reduce each operand to bits… | ExpressionExecutor.cs |
| `Eqv` | Supported | `VisitVsEqv` takes the RAW pair ("raw — see VisitVsAnd"), returns Null when either side is Null, then applies `~(a ^ b)` through the same `VbBitwise` ladder. Measured `(True Eqv True)` ->… | ExpressionExecutor.cs |
| `Imp` | Supported | `VisitVsImp` applies `~a \| b` through the `VbBitwise` ladder, with VB6's three-valued Null table written out explicitly: both Null -> Null; Null Imp True -> True; Null Imp False -> Null… | ExpressionExecutor.cs |
| `Is` | Supported | VisitVsIs performs reference identity on the underlying object: `return new Vb6Value(ReferenceEquals(left.Value, right.Value))`, with the comment "One line covers a Is b, x Is Nothing, and… | ExpressionExecutor.cs |
| `Is (object identity)` | Supported | `VisitVsIs` compares the underlying references: "Reference identity on the underlying object (Nothing = null). One line covers a Is b, x Is Nothing, and Nothing Is Nothing — NOT Vb6Value… | ExpressionExecutor.cs |
| `Not` | Supported | `VisitVsNot`: Null -> Null; otherwise `VbBitwise.TryUnpack` then `VbBitwise.Not(bits, width)` — "Not keeps its operand's OWN width rather than promoting: Not CByte(5) is 250, an eight-bit… | ExpressionExecutor.cs |
| `Or` | Supported | `VisitVsOr` — same raw-pair `VbBitwise` treatment as `And` (`static (a, b) => a \| b`), with an unusable operand raising Err 13. Measured `(True Or False)` -> Boolean True. Tests:… | ExpressionExecutor.cs |
| `TypeOf ... Is` | Supported | `VisitVsTypeOf`, including an explicit grammar-greediness workaround: "`TypeOf p Is Clock` parses the operand as the vsIs expression `p Is Clock` with NO Is-type clause. Recover by… | ExpressionExecutor.cs |
| `Xor` | Supported | `VisitVsXor` — same raw-pair `VbBitwise` treatment (`static (a, b) => a ^ b`). Measured `(True Xor False)` -> Boolean True. | ExpressionExecutor.cs |

### Intrinsic functions — 153 names (70 absent, 37 partial, 45 supported)


**array** (3)

| Name | Status | Detail | Source |
|---|---|---|---|
| `Array` | Supported | Registered: d["Array"] = (_, a, _) => MakeArray(Vb6Value.ValueType.EmptyVariant, a) — a 0-based 1-D Variant array. Zero arguments produce bounds (0,-1) (ArgSlots returns an empty slot list… | VB6BuiltIns.Array.cs |
| `LBound` | Supported | Registered; Bound() accepts 1-2 args, requires a VBArray (else Err 13 Type mismatch), and maps dimension < 1 or > Rank to Err 9 rather than an untrappable ArgumentOutOfRangeException… | VB6BuiltIns.Array.cs |
| `UBound` | Supported | Registered; same Bound() helper as LBound — 1-2 args, non-array -> Err 13, bad dimension -> Err 9. Exercised throughout ArrayFunctionsTests (an empty array reads UBound = -1). | VB6BuiltIns.Array.cs |

**conversion** (17)

| Name | Status | Detail | Source |
|---|---|---|---|
| `CVar` | **Dies** | Not registered in BuildRegistry (VB6BuiltIns.cs) or any RegisterXxx partial — the only hits anywhere in IDE/ are editor metadata. A call reaches ExpressionExecutor.cs and throws "Compile… | VB6BuiltIns.cs |
| `CVDate` | **Dies** | Zero hits anywhere in IDE/ outside obj/bin — not registered (VB6BuiltIns.cs), not in Grammar/VB6.g4, not in VbKeywordNormalizer.cs, not in VbSignatures.cs. A call reaches… | VB6BuiltIns.cs |
| `CVErr` | **Dies** | Not registered in BuildRegistry (VB6BuiltIns.cs) or any RegisterXxx partial, so name resolution falls through local arrays and user procedures to the intrinsic registry, gets null back, and… | VB6BuiltIns.cs |
| `CBool` | Partial | Registered; CBool() maps Null -> Err 94, passes a Boolean through, parses a String via bool.TryParse then a numeric parse (Err 13 on garbage), and otherwise tests ToNum() != 0. Pinned:… | VB6BuiltIns.Conversion.cs |
| `CCur` | Partial | Registered: NarrowNum(a[0], VT.Currency) -> MakeCurrency, which rounds to 4 dp half-to-even and raises Err 6 outside +/-922337203685477.5807 (VbNumeric.cs). Pinned: CCur(5) yields… | VB6BuiltIns.Conversion.cs |
| `CDate` | Partial | Registered; Null -> Err 94, a DateTime passes through, a String goes through DateTime.TryParse with InvariantCulture (Err 13 on failure), and a number goes through DateTime.FromOADate with… | VB6BuiltIns.Conversion.cs |
| `CDec` | Partial | Registered: NarrowNum(a[0], VT.Decimal) -> Vb6Value.NewDecimal(ToDecimal(v)) (VbNumeric.cs). Pinned: CDec(5) yields Vb6Value.NewDecimal(5m) (ConversionFunctionsTests.cs,60). RESTRICTION:… | VB6BuiltIns.Conversion.cs |
| `Hex` | Partial | Registered; Null in -> Null out; RadixStr picks the width from the operand's declared type — Byte/Integer -> 16-bit two's complement, Long (or a value outside Integer range) -> 32-bit… | VB6BuiltIns.Conversion.cs |
| `Oct` | Partial | Registered; same RadixStr path as Hex with radix 8 — Null -> Null, 16-bit width for Byte/Integer, 32-bit for Long. Pinned: Oct(8)="10", Oct(-1)="177777" (ConversionFunctionsTests.cs). | VB6BuiltIns.Conversion.cs |
| `CByte` | Supported | Registered: NarrowNum(a[0], VT.Byte) — strings/booleans coerce through ToNum first, then VbNumeric.Narrow rounds half-to-even and range-checks, raising Err 6 out of range. Pinned:… | VB6BuiltIns.Conversion.cs |
| `CDbl` | Supported | Registered: NarrowNum(a[0], VT.Double); Narrow's Double arm is a lossless widen of ToDouble. Pinned: CDbl("3.14") = 3.14 Double (ConversionFunctionsTests.cs). | VB6BuiltIns.Conversion.cs |
| `CInt` | Supported | Registered: NarrowNum(a[0], VT.Integer) — half-to-even rounding then a 16-bit range check raising Err 6. Pinned: CInt("42")=42 Integer, CInt(40000) -> Err 6 (ConversionFunctionsTests.cs,74). | VB6BuiltIns.Conversion.cs |
| `CLng` | Supported | Registered: NarrowNum(a[0], VT.Long) — half-to-even rounding then a 32-bit range check raising Err 6 (VbNumeric.NarrowLong). Pinned: CLng(5) yields Vb6Value(5L), and… | VB6BuiltIns.Conversion.cs |
| `CSng` | Supported | Registered: NarrowNum(a[0], VT.Single) -> NarrowDouble(..., VT.Single), which range-checks into Single and maps NaN -> Err 5 / Infinity -> Err 6. Pinned: CSng(2.5) yields Vb6Value(2.5f)… | VB6BuiltIns.Conversion.cs |
| `CStr` | Supported | Registered: "a[0].IsNull ? throw InvalidUseOfNull() : new Vb6Value(AsStr(a[0]))" with the trailing comment "// no leading space" — Null raises Err 94, everything else stringifies with no… | VB6BuiltIns.Conversion.cs |
| `Str` | Supported | Registered; Null -> Null, otherwise StrFn: "Str returns the number as text with a leading space for non-negatives (CStr has none)" (VB6BuiltIns.Strings.cs). Tested: Str(5)=" 5"… | VB6BuiltIns.Strings.cs |
| `Val` | Supported | Registered; leading-numeric parse returning a Double. Comment: "Leading-numeric parse: ignores embedded whitespace, honours &H/&O, stops at the first non-numeric char." Tested:… | VB6BuiltIns.Strings.cs |

**date/time** (19)

| Name | Status | Detail | Source |
|---|---|---|---|
| `Time` | **Dies** | **Fixed by #191** — was Silently wrong: it ran, did nothing and let the program continue. Now raises, naming the intrinsic | VB6BuiltIns.DateTime.cs |
| `DatePart` | Partial | Registered and working for every interval, but two documented arguments are not honoured: the optional firstweekofyear (args[3]) is never read, and DatePart("ww", ...) routes to… | VB6BuiltIns.DateTime.cs |
| `TimeSerial` | Partial | d["TimeSerial"] = (_, a, _) => TimeSerial(AsInt(a[0]), AsInt(a[1]), AsInt(a[2])) - builds on the 1899-12-30 epoch so hours roll over. Overflow is Err 6, deliberately NOT Err 5: comment "Err… | VB6BuiltIns.DateTime.cs |
| `WeekdayName` | Partial | Registered (d["WeekdayName"] = (_, a, _) => WeekdayName(AsInt(a[0]), a.Count >= 2 && AsDouble(a[1]) != 0, Fdow(a, 2))) and correct when firstdayofweek is passed explicitly, but the… | VB6BuiltIns.DateTime.cs |
| `Date` | Supported | Registered in the intrinsic registry: d["Date"] = (_, _, _) => new Vb6Value(DateTime.Now.Date). Returns a Date-typed Vb6Value. Exercised by… | VB6BuiltIns.DateTime.cs |
| `DateAdd` | Supported | d["DateAdd"] = (_, a, _) => DateAdd(Interval(a[0]), AsInt(a[1]), AsDate(a[2])). Month-end clamping comes free from DateTime.AddMonths; oracle-pinned by… | VB6BuiltIns.DateTime.cs |
| `DateDiff` | Supported | d["DateDiff"] = (_, a, _) => new Vb6Value(DateDiff(Interval(a[0]), AsDate(a[1]), AsDate(a[2]), Fdow(a, 3))). Returns Long. Implements VB6's boundary-counting rule (8:30->9:15 = 1 hour)… | VB6BuiltIns.DateTime.cs |
| `DateSerial` | Supported | d["DateSerial"] = (_, a, _) => DateSerial(AsInt(a[0]), AsInt(a[1]), AsInt(a[2])). Rolls over out-of-range month/day via AddMonths/AddDays; a result past the Date range raises Err 5 (`catch… | VB6BuiltIns.DateTime.cs |
| `DateValue` | Supported | d["DateValue"] = (_, a, _) => new Vb6Value(AsDate(a[0]).Date). Oracle row exists: DateValue("March 15, 2020") -> 2020-03-15. | VB6BuiltIns.DateTime.cs |
| `Day` | Supported | d["Day"] = (_, a, _) => new Vb6Value(AsDate(a[0]).Day). Returns Integer (oracle-verified return type); pinned by DateFunctionsTests.Parts_And_Weekday. | VB6BuiltIns.DateTime.cs |
| `Hour` | Supported | d["Hour"] = (_, a, _) => new Vb6Value(AsDate(a[0]).Hour). Returns Integer; pinned by DateFunctionsTests.Parts_And_Weekday (14 for 2:30:45 PM). | VB6BuiltIns.DateTime.cs |
| `Minute` | Supported | d["Minute"] = (_, a, _) => new Vb6Value(AsDate(a[0]).Minute). Returns Integer; pinned by DateFunctionsTests.Parts_And_Weekday. | VB6BuiltIns.DateTime.cs |
| `Month` | Supported | d["Month"] = (_, a, _) => new Vb6Value(AsDate(a[0]).Month). Returns Integer; pinned by DateFunctionsTests.Parts_And_Weekday. | VB6BuiltIns.DateTime.cs |
| `MonthName` | Supported | d["MonthName"] = (_, a, _) => MonthName(AsInt(a[0]), a.Count >= 2 && AsDouble(a[1]) != 0). A month outside 1..12 raises Err 5. Pinned by DateFunctionsTests.MonthName_And_WeekdayName. | VB6BuiltIns.DateTime.cs |
| `Now` | Supported | d["Now"] = (_, _, _) => new Vb6Value(DateTime.Now). TypeName(Now) = "Date" is asserted by DateFunctionsTests.Clock_ReturnsRightTypes_AndIsCoherent. | VB6BuiltIns.DateTime.cs |
| `Second` | Supported | d["Second"] = (_, a, _) => new Vb6Value(AsDate(a[0]).Second). Returns Integer; pinned by DateFunctionsTests.Parts_And_Weekday. | VB6BuiltIns.DateTime.cs |
| `Timer` | Supported | d["Timer"] = (_, _, _) => new Vb6Value((float)DateTime.Now.TimeOfDay.TotalSeconds) - Single, as VB6. TypeName(Timer) = "Single" is asserted by… | VB6BuiltIns.DateTime.cs |
| `TimeValue` | Supported | d["TimeValue"] = (_, a, _) => new Vb6Value(Epoch + AsDate(a[0]).TimeOfDay). Oracle row exists: TimeValue("2:30:45 PM") -> 14:30:45. | VB6BuiltIns.DateTime.cs |
| `Weekday` | Supported | d["Weekday"] = (_, a, _) => new Vb6Value(WeekdayNum(AsDate(a[0]), Fdow(a, 1))); WeekdayNum shifts so the firstDayOfWeek day is 1. Returns Integer. Oracle-pinned: Weekday(<Sunday>) = 1… | VB6BuiltIns.DateTime.cs |

**file/filesystem** (15)

| Name | Status | Detail | Source |
|---|---|---|---|
| `CurDir` | **Dies** | **Fixed by #191** — was Silently wrong: it ran, did nothing and let the program continue. Now raises, naming the intrinsic | ExpressionExecutor.cs |
| `FreeFile` | **Dies** | **Fixed by #191** — was Silently wrong: it ran, did nothing and let the program continue. Now raises, naming the intrinsic | ExpressionExecutor.cs |
| `Dir` | **Dies** | fails at name resolution: unregistered in every VB6BuiltIns partial. Probed `Debug.Print Dir("*.*")` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined… | ExpressionExecutor.cs |
| `EOF` | **Dies** | fails at name resolution: unregistered. Probed `Debug.Print EOF(1)` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined (EOF)". docs/interpreter-gaps.md:83. | ExpressionExecutor.cs |
| `FileAttr` | **Dies** | fails at name resolution: unregistered. Probed `Debug.Print FileAttr(1, 1)` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined (FileAttr)"… | ExpressionExecutor.cs |
| `FileDateTime` | **Dies** | fails at name resolution: unregistered. Probed `Debug.Print FileDateTime("a.txt")` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined (FileDateTime)"… | ExpressionExecutor.cs |
| `FileLen` | **Dies** | fails at name resolution: unregistered. Probed `Debug.Print FileLen("a.txt")` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined (FileLen)"… | ExpressionExecutor.cs |
| `GetAttr` | **Dies** | fails at name resolution: unregistered. Probed `Debug.Print GetAttr("a.txt")` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined (GetAttr)"… | ExpressionExecutor.cs |
| `Input` | **Dies** | fails at name resolution: `Input` is in `ambiguousKeyword` (Grammar/VB6.g4) so `Input(n, f)` parses as a procedure-or-array call, but the name is unregistered. Probed `Debug.Print Input(5… | ExpressionExecutor.cs |
| `InputB` | **Dies** | fails at name resolution: a plain identifier (not a lexer keyword), unregistered. Probed `Debug.Print InputB(5, 1)` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function… | ExpressionExecutor.cs |
| `Loc` | **Dies** | fails at name resolution: unregistered. Probed `Debug.Print Loc(1)` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined (Loc)". docs/interpreter-gaps.md:83. | ExpressionExecutor.cs |
| `LOF` | **Dies** | fails at name resolution: unregistered. Probed `Debug.Print LOF(1)` -> VBSubOrFunctionNotDefinedException: "Compile error: Sub or Function not defined (LOF)". docs/interpreter-gaps.md:83. | ExpressionExecutor.cs |
| `Seek` | **Dies** | fails at name resolution: SEEK is a lexer keyword but is listed in `ambiguousKeyword`, so `Seek(1)` parses as a call. Probed `Debug.Print Seek(1)` -> VBSubOrFunctionNotDefinedException:… | ExpressionExecutor.cs |
| `Spc` | **Dies** | in both positions, but is implemented in neither. The grammar gives it a dedicated slot - `outputList_Expression : (SPC \| TAB) (WS? LPAREN WS? argsCall WS? RPAREN)? \| valueStmt`… | StatementExecutor.cs |
| `Tab` | **Dies** | in both positions, implemented in neither - identical shape to Spc. `outputList_Expression : (SPC \| TAB) ...` (Grammar/VB6.g4); probed `Print #1, Tab(5); "a"` -> NotImplementedException… | StatementExecutor.cs |

**inspection** (15)

| Name | Status | Detail | Source |
|---|---|---|---|
| `Erl` | **Dies** | **Fixed by #191** — was Silently wrong: it ran, did nothing and let the program continue. Now raises, naming the intrinsic | ExpressionExecutor.cs |
| `Error` | **Dies** | `Error(n)` (the function returning an error's message text) is not in BuildRegistry. Measured: `Debug.Print Error(6)` -> VBSubOrFunctionNotDefinedException. `Debug.Print Error$(6)` ->… | VB6BuiltIns.cs |
| `ObjPtr` | **Dies** | Zero hits anywhere in IDE/ outside obj/bin; not in BuildRegistry (VB6BuiltIns.cs), not in Grammar/VB6.g4, not even in the editor metadata (VbKeywordNormalizer.cs / VbSignatures.cs). A call… | VB6BuiltIns.cs |
| `StrPtr` | **Dies** | Zero hits anywhere in IDE/ outside obj/bin — not registered, not in the grammar, not in editor metadata; ExpressionExecutor.cs throws "Compile error:\n\nSub or Function not defined… | VB6BuiltIns.cs |
| `VarPtr` | **Dies** | Zero hits anywhere in IDE/ outside obj/bin — not registered, not in the grammar, not in editor metadata; ExpressionExecutor.cs throws "Compile error:\n\nSub or Function not defined… | VB6BuiltIns.cs |
| `IsError` | Partial | Implemented as new Vb6Value(a[0].IsMissing) — it reports ONLY an omitted argument, because that is the sole vbError value the value model can hold. In-code comment: "An omitted argument is… | VB6BuiltIns.Inspection.cs |
| `TypeName` | Partial | Registered and correct for every primitive, array and VB class instance (a VbObject reports its own ClassName; arrays append "()"). The restriction is objects that are not VB classes: for… | VB6BuiltIns.Inspection.cs |
| `IsArray` | Supported | Registered: new Vb6Value(a[0].Type.IsArray) — reports the value model's array flag directly. Tested: IsArray(a)=True for Dim a(1 To 3) As Integer, IsArray(123)=False… | VB6BuiltIns.Inspection.cs |
| `IsDate` | Supported | Registered; IsDateValue is True for a Date value or a string DateTime.TryParse accepts; a bare number is False. Comment: "IsDate: a Date value, or a String parseable as a date/time. A bare… | VB6BuiltIns.Inspection.cs |
| `IsEmpty` | Supported | Registered: new Vb6Value(a[0].Type == VT.EmptyVariant). Tested: IsEmpty(v)=True for an undeclared Variant, IsEmpty(0)=False (InspectionFunctionsTests.cs). Deliberately False for an omitted… | VB6BuiltIns.Inspection.cs |
| `IsMissing` | Supported | Registered: new Vb6Value(a[0].IsMissing). True in exactly one case — an Optional parameter with neither a declared type nor a default, left out (or left blank mid-list). Comment: "True only… | VB6BuiltIns.Inspection.cs |
| `IsNull` | Supported | Registered: new Vb6Value(a[0].Type == VT.Null). Tested: IsNull(Null)=True, IsNull(Empty)=False, IsNull(0)=False (InspectionFunctionsTests.cs), matching the oracle's "IsNull(Empty)=False". | VB6BuiltIns.Inspection.cs |
| `IsNumeric` | Supported | Registered; IsNumericValue reports numerics, Boolean, Empty and Color as True, Date/Null/objects/arrays as False, and strings via IsNumericString (&H/&O literals, thousands separators… | VB6BuiltIns.Inspection.cs |
| `IsObject` | Supported | Registered; IsObjectValue is True for VT.Control, VT.CSharpProxyObject, VT.Nothing and VT.Object — so IsObject(Nothing)=True as the oracle requires. Tested (False case) at… | VB6BuiltIns.Inspection.cs |
| `VarType` | Supported | **Fixed by #193** — returned an Integer, VB6 returns Long. Registered: new Vb6Value((long)TypeInfo(a[0]).code). Full VbVarType map at VB6BuiltIns.Inspection.cs (vbEmpty 0, vbNull 1, vbInteger 2, vbLong 3, vbSingle 4, vbDouble 5… | VB6BuiltIns.Inspection.cs |

**interaction** (22)

| Name | Status | Detail | Source |
|---|---|---|---|
| `Command` | **Dies** | **Fixed by #191** — was Silently wrong: it ran, did nothing and let the program continue. Now raises, naming the intrinsic | ExpressionExecutor.cs |
| `Shell` | **Dies** | **Fixed by #191** — was Silently wrong: it ran, did nothing and let the program continue. Now raises, naming the intrinsic | StatementExecutor.cs |
| `CallByName` | **Dies** | Zero occurrences anywhere in HexIDE.Runtime; not registered by any Register* partial, so BuildRegistry has no entry. The call reaches ExpressionExecutor's final fallthrough and throws… | ExpressionExecutor.cs |
| `Choose` | **Dies** | Not in BuildRegistry - a grep of every d["..."] registration across all VB6BuiltIns partials returns no Choose. `x = Choose(i, "a", "b")` throws VBSubOrFunctionNotDefinedException: "Sub or… | ExpressionExecutor.cs |
| `CreateObject` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. `Set o = CreateObject("Excel.Application")` throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined… | ExpressionExecutor.cs |
| `Environ` | **Dies** | Not in BuildRegistry; the only runtime hit is the highlighter list VbKeywordNormalizer.cs. `Environ("PATH")` throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined… | ExpressionExecutor.cs |
| `GetAllSettings` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. Throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (GetAllSettings)". docs/interpreter-gaps.md:84: "Registry… | ExpressionExecutor.cs |
| `GetObject` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. Throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (GetObject)". docs/interpreter-gaps.md:96 pairs it with… | ExpressionExecutor.cs |
| `GetSetting` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. Throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (GetSetting)". Same docs/interpreter-gaps.md:84 row as… | ExpressionExecutor.cs |
| `IIf` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. `x = IIf(c, a, b)` throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (IIf)". docs/interpreter-gaps.md:52… | ExpressionExecutor.cs |
| `IMEStatus` | **Dies** | but is not implemented. Not in BuildRegistry and zero occurrences anywhere in HexIDE.Runtime, including the editor-metadata files (VbSignatures.cs / VbKeywordNormalizer.cs). | ExpressionExecutor.cs |
| `LoadPicture` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. `Set Picture1.Picture = LoadPicture("x.bmp")` throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined… | ExpressionExecutor.cs |
| `LoadResData` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. Throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (LoadResData)". | ExpressionExecutor.cs |
| `LoadResPicture` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. Throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (LoadResPicture)". | ExpressionExecutor.cs |
| `LoadResString` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. Throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (LoadResString)". | ExpressionExecutor.cs |
| `Partition` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. `Partition(n, lo, hi, size)` throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (Partition)". Named in… | ExpressionExecutor.cs |
| `QBColor` | **Dies** | Not in BuildRegistry; the only runtime hit is VbKeywordNormalizer.cs ("RGB", "QBColor") - highlighter metadata with no execution path. Throws VBSubOrFunctionNotDefinedException: "Sub or… | ExpressionExecutor.cs |
| `RGB` | **Dies** | Not in BuildRegistry; the only runtime hit is the highlighter list VbKeywordNormalizer.cs. `Form1.BackColor = RGB(255, 0, 0)` throws VBSubOrFunctionNotDefinedException: "Sub or Function not… | ExpressionExecutor.cs |
| `Switch` | **Dies** | Not in BuildRegistry; zero occurrences in HexIDE.Runtime. `x = Switch(c1, v1, c2, v2)` throws VBSubOrFunctionNotDefinedException: "Sub or Function not defined (Switch)". Named in… | ExpressionExecutor.cs |
| `InputBox` | Partial | Implemented, but only the first three arguments are read: `var prompt = args.Count >= 1 ...; var caption = args.Count >= 2 ...; var defaultText = args.Count >= 3 ...` then `await… | VB6BuiltIns.cs |
| `MsgBox` | Partial | Implemented and returns the correct VBMsgBoxResult, but the Buttons argument is unboxed with a raw type test: `var style = (VBMsgBoxStyle)(args.Count >= 2 ? args[1].Value as int? ?? 0 :… | VB6BuiltIns.cs |
| `DoEvents` | No-op | Deliberately accepted and ignored. Registered in BuildRegistry with the comment that says why: "DoEvents - a no-op here (the tree-walking interpreter has no message pump). VB6 yields to the… | VB6BuiltIns.cs |

**math** (26)

| Name | Status | Detail | Source |
|---|---|---|---|
| `DDB` | **Dies** | Not registered in BuildRegistry (VB6BuiltIns.cs) or any RegisterXxx partial; the call resolves to no local array, no user procedure and no intrinsic, so ExpressionExecutor.cs throws… | VB6BuiltIns.cs |
| `FV` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs); ExpressionExecutor.cs throws "Compile error:\n\nSub or Function not defined (FV)". Named in the deferral list at docs/interpreter-gaps.md:77. | VB6BuiltIns.cs |
| `IPmt` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs); ExpressionExecutor.cs throws "Compile error:\n\nSub or Function not defined (IPmt)". Named in the deferral list at… | VB6BuiltIns.cs |
| `IRR` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs); ExpressionExecutor.cs throws "Compile error:\n\nSub or Function not defined (IRR)". Named in the deferral list at docs/interpreter-gaps.md:77. | VB6BuiltIns.cs |
| `MIRR` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs); ExpressionExecutor.cs throws "Compile error:\n\nSub or Function not defined (MIRR)". Named in the deferral list at… | VB6BuiltIns.cs |
| `NPer` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs); ExpressionExecutor.cs throws "Compile error:\n\nSub or Function not defined (NPer)". Named in the deferral list at… | VB6BuiltIns.cs |
| `NPV` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs); ExpressionExecutor.cs throws "Compile error:\n\nSub or Function not defined (NPV)". Named in the deferral list at docs/interpreter-gaps.md:77. | VB6BuiltIns.cs |
| `Pmt` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs); ExpressionExecutor.cs throws "Compile error:\n\nSub or Function not defined (Pmt)". Named in the deferral list at docs/interpreter-gaps.md:77. | VB6BuiltIns.cs |
| `PPmt` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs); ExpressionExecutor.cs throws "Compile error:\n\nSub or Function not defined (PPmt)". Named in the deferral list at… | VB6BuiltIns.cs |
| `PV` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs); ExpressionExecutor.cs throws "Compile error:\n\nSub or Function not defined (PV)". Named in the deferral list at docs/interpreter-gaps.md:77. | VB6BuiltIns.cs |
| `Rate` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs); ExpressionExecutor.cs throws "Compile error:\n\nSub or Function not defined (Rate)". Named in the deferral list at… | VB6BuiltIns.cs |
| `SLN` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs); ExpressionExecutor.cs throws "Compile error:\n\nSub or Function not defined (SLN)". Named in the deferral list at docs/interpreter-gaps.md:77. | VB6BuiltIns.cs |
| `SYD` | **Dies** | Unregistered in BuildRegistry (VB6BuiltIns.cs); ExpressionExecutor.cs throws "Compile error:\n\nSub or Function not defined (SYD)". Named in the deferral list at docs/interpreter-gaps.md:77. | VB6BuiltIns.cs |
| `Abs` | Partial | Registered; Abs preserves the operand's subtype (byte/int/long/float/double, Currency vs Decimal kept distinct). Oracle rows: Abs(-4) = 4 Integer, Abs(-4.5) = 4.5 Double; pinned by… | VB6BuiltIns.Math.cs |
| `Atn` | Partial | Registered: new Vb6Value(Math.Atan(AsDouble(a[0]))) — returns Double, matching the file header's rule "Sqr/trig/Exp/Log -> Double (all verified against vb6.exe)". No domain guard is needed:… | VB6BuiltIns.Math.cs |
| `Cos` | Partial | Registered: new Vb6Value(Math.Cos(AsDouble(a[0]))) -> Double. Pinned: Cos(0)=1.0 in MathFunctionsTests.Exp_Sin_Cos. | VB6BuiltIns.Math.cs |
| `Exp` | Partial | Registered and routed through Finite(), which converts a non-representable result into the VB6 error rather than an IEEE special. Comment: "Exp is the one intrinsic here that can overflow a… | VB6BuiltIns.Math.cs |
| `Log` | Partial | Registered with a domain guard: "var x = AsDouble(a[0]); if (x <= 0) throw InvalidCall();" — Err 5 for zero or negative, otherwise Math.Log as a Double (VB6's Log is the natural logarithm). | VB6BuiltIns.Math.cs |
| `Rnd` | Partial | The forward sequence is bit-exact: seed 0x50000, then seed = (seed * 0x43FD43FD + 0xC39EC3) And 0xFFFFFF returned as seed/2^24 as a Single; MathFunctionsTests.Rnd_MatchesVb6Sequence pins… | VB6BuiltIns.Math.cs |
| `Round` | Partial | Registered: d["Round"] = (_, a, _) => new Vb6Value(Math.Round(AsDouble(a[0]), a.Count >= 2 ? AsInt(a[1]) : 0, MidpointRounding.ToEven)). Banker's rounding is oracle-pinned (oracle rows:… | VB6BuiltIns.Math.cs |
| `Sgn` | Partial | Registered: new Vb6Value(Math.Sign(AsDouble(a[0]))) -> Integer, matching the oracle row Sgn(-5) = -1 Integer. Pinned for -5/0/3 in MathFunctionsTests.Sgn_Sqr_Round. | VB6BuiltIns.Math.cs |
| `Sin` | Partial | Registered: new Vb6Value(Math.Sin(AsDouble(a[0]))) -> Double. Pinned: Sin(0)=0.0 in MathFunctionsTests.Exp_Sin_Cos. | VB6BuiltIns.Math.cs |
| `Sqr` | Partial | Registered with a domain guard: "var x = AsDouble(a[0]); if (x < 0) throw InvalidCall();" — Err 5 for a negative argument, otherwise Math.Sqrt as a Double. Oracle rows Sqr(9) = 3 and… | VB6BuiltIns.Math.cs |
| `Tan` | Partial | Registered: new Vb6Value(Math.Tan(AsDouble(a[0]))) -> Double. No guard, deliberately: "Tan's asymptote is unreachable in binary floating point" (VB6BuiltIns.Math.cs). | VB6BuiltIns.Math.cs |
| `Fix` | Supported | Registered: Whole(a[0], truncate: true) — truncates toward zero and preserves the operand type (Integer/Long/Byte returned untouched; Single stays Single; Currency and Decimal kept… | VB6BuiltIns.Math.cs |
| `Int` | Supported | Registered: Whole(a[0], truncate: false) — floors toward -infinity, type-preserving. Oracle rows Int(-2.5) = -3 and Int(2.7) = 2 pinned by MathFunctionsTests.IntFloors_FixTruncates. "Int"… | VB6BuiltIns.Math.cs |

**string** (36)

| Name | Status | Detail | Source |
|---|---|---|---|
| `AscB` | **Dies** | No registry entry: BuildRegistry (VB6BuiltIns.cs) calls RegisterStrings/Conversion/Math/Array/Inspection/DateTime/Format and adds only DoEvents; grep for "AscB" across IDE/HexIDE.Runtime… | ExpressionExecutor.cs, VB6BuiltIns.cs |
| `AscW` | **Dies** | No registry entry (VB6BuiltIns.cs; no occurrence of "AscW" anywhere under IDE/HexIDE.Runtime). Reaches `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined… | ExpressionExecutor.cs, VB6BuiltIns.cs |
| `ChrB` | **Dies** | No registry entry (VB6BuiltIns.cs; no occurrence of "ChrB" under IDE/HexIDE.Runtime). `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (ChrB)". | ExpressionExecutor.cs, VB6BuiltIns.cs |
| `ChrW` | **Dies** | No registry entry (VB6BuiltIns.cs). `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (ChrW)". | ExpressionExecutor.cs, VB6BuiltIns.cs |
| `FormatCurrency` | **Dies** | RegisterFormat registers exactly one name — `d["Format"]` (VB6BuiltIns.Format.cs); no occurrence of "FormatCurrency" under IDE/HexIDE.Runtime. `throw new… | ExpressionExecutor.cs, VB6BuiltIns.Format.cs |
| `FormatDateTime` | **Dies** | Not registered (VB6BuiltIns.Format.cs registers only "Format"); no occurrence under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined… | ExpressionExecutor.cs, VB6BuiltIns.Format.cs |
| `FormatNumber` | **Dies** | Not registered (VB6BuiltIns.Format.cs); no occurrence under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — message "Sub or Function not defined (FormatNumber)". | ExpressionExecutor.cs, VB6BuiltIns.Format.cs |
| `FormatPercent` | **Dies** | Not registered (VB6BuiltIns.Format.cs); no occurrence under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (FormatPercent)". | ExpressionExecutor.cs, VB6BuiltIns.Format.cs |
| `InStrB` | **Dies** | Not registered (VB6BuiltIns.cs); no occurrence of "InStrB" under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (InStrB)". | ExpressionExecutor.cs, VB6BuiltIns.cs |
| `LeftB` | **Dies** | Not registered (VB6BuiltIns.cs); no occurrence of "LeftB" under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (LeftB)". | ExpressionExecutor.cs, VB6BuiltIns.cs |
| `LenB` | **Dies** | Not registered (VB6BuiltIns.cs); no occurrence of "LenB" under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (LenB)". | ExpressionExecutor.cs, VB6BuiltIns.cs |
| `MidB` | **Dies** | Not registered (VB6BuiltIns.cs); no occurrence of "MidB" under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (MidB)". | ExpressionExecutor.cs, VB6BuiltIns.cs |
| `RightB` | **Dies** | Not registered (VB6BuiltIns.cs); no occurrence of "RightB" under IDE/HexIDE.Runtime. `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (RightB)". | ExpressionExecutor.cs, VB6BuiltIns.cs |
| `StrComp` | **Dies** | Not registered (VB6BuiltIns.cs; no occurrence of "StrComp" under IDE/HexIDE.Runtime). `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not defined (StrComp)". | ExpressionExecutor.cs, VB6BuiltIns.cs |
| `StrConv` | **Dies** | Not registered (VB6BuiltIns.cs; no occurrence of "StrConv" under IDE/HexIDE.Runtime, nor in VbSignatures.cs). `throw new VBSubOrFunctionNotDefinedException(name)` — "Sub or Function not… | ExpressionExecutor.cs, VB6BuiltIns.cs |
| `Asc` | Partial | Registered as `d["Asc"] = (_, a, _) => Asc(a[0]);`. The helper returns `new Vb6Value((int)s[0])` — the first UTF-16 code unit, i.e. AscW semantics, not an ANSI-codepage byte. Null… | VB6BuiltIns.Strings.cs |
| `Chr` | Partial | Registered as `d["Chr"] = (_, a, _) => new Vb6Value(((char)AsInt(a[0])).ToString());`. Restriction: the argument is cast straight to a UTF-16 char with no range validation and no ANSI code… | VB6BuiltIns.Strings.cs |
| `Filter` | Partial | Registered as `d["Filter"] = (_, a, _) => Filter(a);`. Handles Filter(array, match, [include=True], [compare]) and returns a 0-based Variant array; no matches yields bounds (0,-1). The file… | VB6BuiltIns.Array.cs |
| `InStr` | Partial | Registered as `d["InStr"] = (_, a, _) => InStr(a);`. Supports InStr([start,] string1, string2[, compare]) with 1-based results, Null propagation, start<1 raising Err 5, and start past the… | VB6BuiltIns.Strings.cs |
| `InStrRev` | Partial | Registered as `d["InStrRev"] = (_, a, _) => InStrRev(a);`. Supports InStrRev(string1, string2[, start[, compare]]) with Null propagation and a default start of -1. Restrictions: (i)… | VB6BuiltIns.Strings.cs |
| `LCase` | Partial | Registered as `d["LCase"] = (_, a, _) => NullOrStr(a[0], s => s.ToLowerInvariant());`. Null propagates to Null. Restriction: casing is INVARIANT, not the current locale, so locale-specific… | VB6BuiltIns.Strings.cs |
| `Len` | Partial | Registered as `d["Len"] = (_, a, _) => a[0].IsNull ? Vb6Value.Null : new Vb6Value(AsStr(a[0]).Length);`. Restriction: it is the CHARACTER COUNT OF THE STRING FORM ONLY. `AsStr` is… | VB6BuiltIns.Strings.cs |
| `Mid` | Partial | Registered as `d["Mid"] = (_, a, _) => NullOrStr(a[0], s => Mid(s, AsInt(a[1]), a.Count >= 3 ? AsInt(a[2]) : (int?)null));`. Null propagates; start<1 raises Err 5; start past the end… | VB6BuiltIns.Strings.cs |
| `Replace` | Partial | Registered as `d["Replace"] = (_, a, _) => new Vb6Value(Replace(a));`. Implements Replace(expression, find, replace[, start[, count[, compare]]]) including the VB6 rule that the result… | VB6BuiltIns.Strings.cs |
| `Space` | Partial | Registered as `d["Space"] = (_, a, _) => new Vb6Value(new string(' ', Math.Max(0, AsInt(a[0]))));`. Restriction: a NEGATIVE count is clamped to zero by `Math.Max(0, ...)` and returns "", so… | VB6BuiltIns.Strings.cs |
| `Split` | Partial | Registered as `d["Split"] = (_, a, _) => Split(a);`. Split(expr, [delimiter], [limit], [compare]) returning a 0-based String array. The VB6 defaults are honoured and stated as… | VB6BuiltIns.Array.cs |
| `String` | Partial | Registered as `d["String"] = (_, a, _) => new Vb6Value(new string(CharArg(a[1]), Math.Max(0, AsInt(a[0]))));`. Both argument forms work — String(n, "*") and String(n, 65) — via `CharArg`… | VB6BuiltIns.Strings.cs |
| `UCase` | Partial | Registered as `d["UCase"] = (_, a, _) => NullOrStr(a[0], s => s.ToUpperInvariant());`. Null propagates — asserted by StringFunctionsTests.cs NullArgument_Propagates. Restriction: casing is… | VB6BuiltIns.Strings.cs |
| `Format` | Supported | Registered as `d["Format"] = (_, a, _) => FormatValue(a);`. Covers the named numeric formats (General Number / Fixed / Standard / Percent / Scientific / Currency), the named date/time… | VB6BuiltIns.Format.cs |
| `Join` | Supported | Registered as `d["Join"] = (_, a, _) => Join(a);`. Join(array, [delimiter]) with the VB6 default delimiter of a single SPACE (not a comma); the file header states this was pinned against… | VB6BuiltIns.Array.cs |
| `Left` | Supported | Registered as `d["Left"] = (_, a, _) => NullOrStr(a[0], s => Left(s, AsInt(a[1])));`. Null propagates; n<0 raises Err 5 (`if (n < 0) throw InvalidCall();`); n beyond the length returns the… | VB6BuiltIns.Strings.cs |
| `LTrim` | Supported | Registered as `d["LTrim"] = (_, a, _) => NullOrStr(a[0], s => s.TrimStart(' '));`. Trims only U+0020 (VB6's space-only trim, not .NET's default whitespace set); Null propagates. Tested:… | VB6BuiltIns.Strings.cs |
| `Right` | Supported | Registered as `d["Right"] = (_, a, _) => NullOrStr(a[0], s => Right(s, AsInt(a[1])));`. Null propagates; n<0 raises Err 5 (`if (n < 0) throw InvalidCall();`); n beyond the length returns… | VB6BuiltIns.Strings.cs |
| `RTrim` | Supported | Registered as `d["RTrim"] = (_, a, _) => NullOrStr(a[0], s => s.TrimEnd(' '));`. Trims only U+0020; Null propagates. Tested: StringFunctionsTests.cs. | VB6BuiltIns.Strings.cs |
| `StrReverse` | Supported | Registered as `d["StrReverse"] = (_, a, _) => NullOrStr(a[0], s => { var c = s.ToCharArray(); Array.Reverse(c); return new string(c); });`. Null propagates. Tested: StringFunctionsTests.cs. | VB6BuiltIns.Strings.cs |
| `Trim` | Supported | Registered as `d["Trim"] = (_, a, _) => NullOrStr(a[0], s => s.Trim(' '));`. Trims only U+0020 from both ends (VB6's space-only semantics, not .NET's default whitespace trim); Null… | VB6BuiltIns.Strings.cs |

### Keywords and modifiers — 51 names (11 absent, 22 partial, 17 supported)

| Name | Status | Detail | Source |
|---|---|---|---|
| `Access` | **Dies** | ACCESS is a real lexer token and the clause is in the grammar - `openStmt : OPEN WS valueStmt WS FOR WS (APPEND\|BINARY\|INPUT\|OUTPUT\|RANDOM) (WS ACCESS WS (READ\|WRITE\|READ_WRITE))?… | StatementExecutor.cs |
| `Append` | **Dies** | APPEND is a lexer token and an openStmt mode alternative (Grammar/VB6.g4). Probed `Open "z.txt" For Append As #1` -> NotImplementedException "Open not implemented". | StatementExecutor.cs |
| `GoSub` | **Dies** | throws `"GoSub not implemented"` — (goSubStmt, VB6.g4) and throws at run time. Verbatim: Measured. | StatementExecutor.cs |
| `Line` | **Dies** | There is no standalone LINE token; `Line Input` is lexed as ONE token, `LINE_INPUT : L I N E ' ' I N P U T` (Grammar/VB6.g4), consumed by `lineInputStmt : LINE_INPUT WS valueStmt WS? COMMA… | StatementExecutor.cs |
| `Output` | **Dies** | OUTPUT is a lexer token and an openStmt mode alternative (Grammar/VB6.g4). Probed `Open "z.txt" For Output As #1` -> NotImplementedException "Open not implemented". | StatementExecutor.cs |
| `ParamArray` | **Dies** | throws `"ParamArray parameters are not yet supported"` — and DECLARES fine - the grammar's `arg` rule carries `(PARAMARRAY WS)?` and PrePass.ParseParams records `ParamArray: arg.PARAMARRAY() != null`. It… | BasicInterpreter.cs |
| `Random` | **Dies** | RANDOM is a lexer token and an openStmt mode alternative (Grammar/VB6.g4). Probed `Open "z.txt" For Random Access Read Write Shared As #1 Len = 32` -> NotImplementedException "Open not… | StatementExecutor.cs |
| `Read` | **Dies** | READ, READ_WRITE, LOCK_READ and LOCK_READ_WRITE are all lexer tokens used by openStmt's Access and lock clauses (Grammar/VB6.g4, 1459-1471). Probed `Open "z.txt" For Random Access Read… | StatementExecutor.cs |
| `Shared` | **Dies** | SHARED is a lexer token, one of openStmt's lock-clause alternatives `(SHARED \| LOCK_READ \| LOCK_WRITE \| LOCK_READ_WRITE)` (Grammar/VB6.g4). Probed `Open "z.txt" For Random Access Read… | StatementExecutor.cs |
| `Static` | **Dies** | throws `"non dim variables not supported"` — Two distinct forms, both broken, in different ways. (a) The LOCAL form parses (`variableStmt : (DIM \| STATIC \| visibility) WS ...`) and throws at… | StatementExecutor.cs, PrePass.cs, VB6.g4 |
| `Write` | **Dies** | WRITE is a lexer token used both in openStmt's `Access Write` / `Access Read Write` clause and as the head of writeStmt (Grammar/VB6.g4, 630-632). As a clause keyword: probed `Open "z.txt"… | StatementExecutor.cs |
| `As` | Partial | BaseTypeMapper.Map covers ten base types - String, Integer, Long, Byte, Single, Double, Boolean, Currency, Date, Variant - and returns null for everything else. A complexType (`As Employee`… | BaseTypeMapper.cs, StatementExecutor.cs, VB6Visitor.cs, PrePass.cs |
| `ByRef` | Partial | ByRef is the default convention (ParamInfo.ByRef = arg.BYVAL() == null) and is implemented by slot aliasing across the shared ExecutionState: `callee.DefineVariable(p.Name… | BasicInterpreter.cs, ProcedureModel.cs, PrePass.cs |
| `ByRef (call-site modifier)` | Partial | Split behaviour. For a USER procedure `ResolveCallArgs` never inspects `BYREF` and a bare lvalue aliases by default, so the keyword is effectively honoured — measured `Bump ByRef a` against… | ExpressionExecutor.cs |
| `ByVal` | Partial | A ByVal parameter is copied into a fresh callee slot, narrowed to its declared type when that type is core-numeric (VbNumeric.Narrow), and UDT values are deep-copied so callee mutation… | BasicInterpreter.cs |
| `Case` | Partial | sC_Case (VB6.g4) is iterated by VisitSelectCaseStmt; each case's sC_Cond is dispatched by context class (CaseCondElse / CaseCondExpr) and comma-separated sub-conditions are all tried… | StatementExecutor.cs |
| `Case Is` | Partial | CaseCondExprIs branch handles LT, LEQ, GT, GEQ, EQ and NEQ. Ordering comparisons go through Vb6Value.TryCompareTo, which promotes across numeric subtypes. Measured: `Case Is > 0` matches 1. | StatementExecutor.cs |
| `Do` | Partial | Heads all three doLoopStmt alternatives, each with a real visitor: VisitDoBlockLoop (StatementExecutor.cs), VisitDoBlockWhileLoop (:447), VisitDoWhileBlockLoop (:483). Restriction is in the… | StatementExecutor.cs |
| `Each` | Partial | Recognised by forEachStmt (VB6.g4) and driven by VisitForEachStmt. The visitor accepts arrays only: `if (collection.Value is not VBArray array) throw new VBRunTimeException(context… | StatementExecutor.cs |
| `For` | Partial | Heads both forNextStmt (VisitForNextStmt, StatementExecutor.cs) and forEachStmt (VisitForEachStmt, :635). For...Next is integral-only; For Each is arrays-only. Type hints and an As clause… | StatementExecutor.cs |
| `Global` | Partial | GLOBAL is an alternative of the `visibility` rule (VB6.g4) and of `publicPrivateGlobalVisibility` (VB6.g4), so `Global x As Integer`, `Global Const K = 5` and `Global Type T` all parse. The… | PrePass.cs, VB6.g4 |
| `If` | Partial | Both ifThenElseStmt alternatives have visitors: VisitBlockIfThenElse (StatementExecutor.cs) and VisitInlineIfThenElse (:775). Both require a strictly Boolean condition. Block form: `if… | StatementExecutor.cs |
| `In` | Partial | Only occurrence in VB6 is `For Each x In coll` (VB6.g4); the grammar consumes it and VisitForEachStmt evaluates the following valueStmt as the collection. Same arrays-only restriction as… | StatementExecutor.cs |
| `Is (Select Case comparison prefix)` | Partial | `sC_CondExpr : IS WS? comparisonOperator WS? valueStmt # caseCondExprIs` is handled for six operators — `<`, `<=`, `>`, `>=`, `=`, `<>` — via `value.TryCompareTo(val)` /… | StatementExecutor.cs |
| `New` | Partial | `Set x = New ClassName` is implemented: `VisitVsNew` extracts the class name SYNTACTICALLY ("evaluating it would try to resolve a variable and throw"), creates the instance via… | ExpressionExecutor.cs |
| `Next` | Partial | Closes forNextStmt and forEachStmt; also the `Resume Next` / `On Error Resume Next` modifier (StatementExecutor.cs, :1164). Loop restriction: the grammar is `NEXT (WS ambiguousIdentifier… | VB6.g4 |
| `On` | Partial | `On Error ...` is fully wired (VisitOnErrorStmt, StatementExecutor.cs). The two computed-branch forms are not: VisitOnGoToStmt throws "OnGoTo not implemented" (:1090) and VisitOnGoSubStmt… | StatementExecutor.cs |
| `Private` | Partial | Enforced in exactly ONE place: cross-module procedure resolution skips other modules' Private procedures (`if (m.PrePass.Procedures.TryGetValue(name, out var p) && !p.IsPrivate)`). NOT… | BasicInterpreter.cs, PrePass.cs, VbInterface.cs |
| `Rem` | Faithful | Handled purely in the lexer, on the hidden channel. Takes NO separator — `Rem`, `Rem:`, `Rem=1`, `Rem'x`, `Rem"x"` and a bare `Rem` are all comments, measured against vb6.exe against a reference that says a space is required. Guarded by REMTAIL so `RemX = 5` stays an assignment. A trailing `_` extends the remark onto the next line, which is faithful (vb6.exe reports "Expected End Sub" when it swallows one). Remaining divergence: `Rem` is a STATEMENT in VB6 and so needs a separator after a preceding statement, while HexIDE accepts `x = 1 Rem foo` — a missing error, not a wrong value. | VB6.g4 |
| `Select` | Partial | Heads selectCaseStmt with a full visitor (StatementExecutor.cs). Restriction is in the exact-match comparison - see the Select Case row: equality is type-first, so a declared-type selector… | StatementExecutor.cs |
| `Step` | Partial | by forNextStmt (VB6.g4) and read by VisitForNextStmt; a missing Step defaults to 1. Integral values only - the visitor truncation-checks all three bounds and throws: `throw new… | StatementExecutor.cs |
| `While` | Partial | Two roles, both implemented: as the Do modifier (`Do While` / `Loop While`, StatementExecutor.cs/:483) and as the head of While...Wend (VisitWhileWendStmt, :1612). Tests: DoLoopTests (14… | StatementExecutor.cs |
| `With` | Partial | VisitWithStmt pushes the evaluated target onto a per-activation withTargets stack and pops it in a finally; leading-dot members resolve against the innermost entry (StatementExecutor.cs… | StatementExecutor.cs |
| `Friend` | No-op | as one alternative of the `visibility` rule and then deliberately ignored - only PRIVATE is read. Verbatim comment above the check: "// A module-level procedure is Public unless explicitly… | PrePass.cs, ProcedureModel.cs |
| `ByVal (call-site modifier)` | Supported | For a user procedure a call-site `ByVal` suppresses aliasing: `int? location = arg.BYVAL() != null ? null : TryGetArgLocation(arg.valueStmt());` — "A call-site ByVal keyword (or a… | ExpressionExecutor.cs |
| `Case Else` | Supported | `if (cond is VB6Parser.CaseCondElseContext) return await Visit(@case.block())`. The grammar puts ELSE first in sC_Cond specifically so it is not mis-parsed as a variable call (VB6.g4… | StatementExecutor.cs |
| `Else` | Supported | Block form: ifElseBlockStmt visited when no If/ElseIf condition matched (StatementExecutor.cs). Single-line form: `context.blockStmt(1)` (:783). Measured both: `If 1 = 2 Then Debug.Print 1… | StatementExecutor.cs |
| `ElseIf` | Supported | VisitBlockIfThenElse loops `foreach (var elseIf in context.ifElseIfBlockStmt())`, evaluating each condition and returning on the first true one. Measured: n=2 with `If n = 1 / ElseIf n = 2… | StatementExecutor.cs |
| `End If` | Supported | A single END_IF lexer token closing blockIfThenElse (VB6.g4); purely structural, consumed by the parser. Exercised by every block-If test. | VB6.g4 |
| `End Select` | Supported | END_SELECT closes selectCaseStmt (VB6.g4); structural only. Exercised by StatementTests.SelectCaseTests. | VB6.g4 |
| `End With` | Supported | END_WITH closes withStmt (VB6.g4). The withTargets pop is in a `finally`, so the target is released even if the body exits abnormally (StatementExecutor.cs). | StatementExecutor.cs |
| `Local` | Supported | Its only VB6 use is `On Local Error`. The grammar folds it into a single ON_LOCAL_ERROR token accepted alongside ON_ERROR (VB6.g4) and VisitOnErrorStmt treats the two identically. Measured… | StatementExecutor.cs |
| `Loop` | Supported | Closes all three doLoopStmt alternatives (VB6.g4) and carries the post-test condition in the `Do ... Loop While\|Until` form (VisitDoBlockWhileLoop, StatementExecutor.cs). Measured: `Do / n… | StatementExecutor.cs |
| `Optional` | Supported | Three cases are distinguished, all oracle-verified per the code comment: a default expression -> that value (IsMissing False); a declared type with no default -> that type's zero (IsMissing… | BasicInterpreter.cs |
| `Public` | Supported | Public is the default for a module-level procedure (IsPrivate is true only for an explicit PRIVATE) and is what cross-module resolution looks for. `Public` on a variable, a Const, a Type… | PrePass.cs, BasicInterpreter.cs |
| `Then` | Supported | Required by both ifThenElseStmt alternatives (VB6.g4, :337) and consumed structurally; no separate execution. Both If forms dispatch on the condition value, not on the token. | VB6.g4 |
| `To` | Supported | Three roles, all working: For...Next bounds (VB6.g4 -> StatementExecutor.cs), `Case a To b` (sC_CondExpr caseCondExprTo -> StatementExecutor.cs, uses TryCompareTo so it compares across… | StatementExecutor.cs |
| `To (range)` | Supported | Three sites, all working. Array bounds: `ExtractDimensions` reads a two-`valueStmt` subscript as (lower, upper) and a one-`valueStmt` subscript as (`currentModule.PrePass.ArrayBase`, upper)… | StatementExecutor.cs |
| `Until` | Supported | Detected as `context.UNTIL() != null` in both pre-tested and post-tested Do visitors, inverting the loop test (StatementExecutor.cs, :484). Measured: `Do / n = n + 1 / Loop Until n = 3` ->… | StatementExecutor.cs |
| `Wend` | Supported | Closes whileWendStmt (VB6.g4); structural only. WhileWendTests covers accumulate, zero-iteration and Exit-Sub-from-inside cases. | VB6.g4 |
| `WithEvents` | Supported | PrePass records each WithEvents name as an event sink and hoists the slot to Nothing; the runtime VisitVariableStmt re-seeds it per instance at New; the Set path detects a WithEvents field… | PrePass.cs, StatementExecutor.cs |

### Literals, types and suffixes — 50 names (10 absent, 17 partial, 23 supported)

| Name | Status | Detail | Source |
|---|---|---|---|
| `DExponentLiteral` | **Won't load** | `DOUBLELITERAL` and `INTEGERLITERAL` accept only an `e`/`E` exponent marker, never `D`. Measured: `Debug.Print 1.5D2` -> "Compile error: mismatched input 'D2' expecting <EOF>". | VB6.g4 |
| `Line number` | Supported | **Fixed** — numeric line labels now parse, and work as `GoTo`, `On Error GoTo` and `Resume` targets. Ported from the LSP grammar, which already had the rule | VB6.g4, StatementExecutor.cs |
| `LineNumber` | Supported | **Fixed** — numeric line labels now parse, and work as `GoTo`, `On Error GoTo` and `Resume` targets. Ported from the LSP grammar, which already had the rule | VB6.g4, StatementExecutor.cs |
| `#` | **Dies** | and it is the one piece of this batch that fails even outside a file statement. `#1` lexes as its own token, `FILENUMBER : HASH LETTERORDIGIT+` (Grammar/VB6.g4), and FILENUMBER is an… | ExpressionExecutor.cs |
| `#n` | **Dies** | `FILENUMBER : HASH LETTERORDIGIT +` is a lexer token listed in the `literal` rule — then throws at run time, because `VisitVsLiteralCore` has no FILENUMBER branch and falls into `throw new… | ExpressionExecutor.cs |
| `$` | **Dies** | There is NO literal form for `$` at all (a String literal carries no suffix), so every occurrence is an identifier or function type hint and every one throws. Measured: `Debug.Print s$` ->… | VB6BuiltIns.Strings.cs |
| `,` | **Dies** | The 14-column print-zone comma is grammar-level only: `outputList : outputList_Expression (WS? (SEMICOLON \| COMMA) WS? outputList_Expression?)* \| outputList_Expression? (WS? (SEMICOLON \|… | StatementExecutor.cs |
| `:= (named argument)` | **Dies** | throws `"Assign is not implemented"` — `implicitCallStmt_InStmt WS? ASSIGN WS? valueStmt # vsAssign` with `ASSIGN : ':='` — then throws at run time: `public override Task<object?>… | ExpressionExecutor.cs |
| `;` | **Dies** | The suppress-newline / adjacent-item semicolon is in `outputList` (Grammar/VB6.g4) and nothing consumes it - probed `Print #1, "a"; "b"` -> NotImplementedException "Print not implemented". | StatementExecutor.cs |
| `ErrObject` | **Dies** | as a complexType, throws at run time. BaseTypeMapper.Map has no ErrObject case and it is not a user class/UDT/Enum, so DeclareLocal falls to the final else. Measured verbatim:… | StatementExecutor.cs |
| `Decimal` | Partial | `Decimal` has no token in the grammar's `baseType` rule (BOOLEAN, BYTE, COLLECTION, CURRENCY, DATE, DOUBLE, INTEGER, LONG, OBJECT, SINGLE, STRING, VARIANT), so `As Decimal` parses as a… | VB6.g4 |
| `!` | Partial | Honoured on a numeric LITERAL, never on an identifier. Literal: `ClassifyIntegerLiteral` and the DOUBLELITERAL branch read the trailing suffix (`'!' => new Vb6Value(float.Parse(body))`) —… | ExpressionExecutor.cs |
| `#` | Partial | Honoured on a numeric LITERAL, never on an identifier. Literal: `_ => new Vb6Value(double.Parse(body))` for the `#` suffix — measured `5#` -> Double 5, `2.5#` -> Double… | ExpressionExecutor.cs |
| `%` | Partial | Honoured on a numeric LITERAL, never on an identifier. Literal: `'%' => new Vb6Value(int.Parse(body)) // Integer (magnitude ctor keeps it Integer in Int16 range)` — measured `5%` -> Integer… | ExpressionExecutor.cs |
| `&` | Partial | Honoured on a numeric LITERAL, never on an identifier. Literal: `'&' => new Vb6Value(long.TryParse(body, out var lp) ? lp : (long)double.Parse(body))` — measured `5&` -> Long 5… | ExpressionExecutor.cs |
| `&H` | Partial | Implemented and oracle-faithful in `ClassifyRadixLiteral`, whose comment records the measurement: "VB6 &H hex / &O octal literals are unsigned bit-patterns (verified against vb6.exe): with… | ExpressionExecutor.cs |
| `&O` | Partial | The same `ClassifyRadixLiteral` path with radix 8. Measured `&O17` = 15 Integer; a trailing `&`/`%` forces the Long/Integer reading as for hex. RESTRICTION: `OCTALLITERAL : (PLUS \| MINUS)?… | ExpressionExecutor.cs |
| `() (call / array index)` | Partial | `EvaluateProcedureOrArrayCall` resolves in the documented VB6 order: a control-array element (missing element -> Err 340, oracle-pinned), then a local ARRAY variable (multi-dimensional… | ExpressionExecutor.cs |
| `:` | Partial | Only a colon IMMEDIATELY FOLLOWED BY A SPACE separates statements: the lexer rule is `NEWLINE : WS? ('\r'? '\n' \| COLON ' ') WS?`. Measured: `a = 1: Debug.Print a` runs and prints 1; `a =… | VB6.g4 |
| `@` | Partial | Honoured on a numeric LITERAL, never on an identifier. Literal: `'@' => Vb6Value.NewCurrency(decimal.Parse(body)) // @ forces Currency` — measured `5@` -> Currency 5… | ExpressionExecutor.cs |
| `Comment` | Partial | `COMMENT : WS? ('\'' \| COLON? REM ' ') (LINE_CONTINUATION \| ~ ('\n' \| '\r'))* -> channel(HIDDEN)` — comments lex to the hidden channel and are ignored. Measured working: a full-line `'… | VB6.g4 |
| `Line label` | Partial | **Fixed** — a label is now a `lineHead` on the block, reachable only after a separator containing a real line break, so a label SHARING its line with a statement registers as a jump target. Ten of fourteen measured-legal forms used to be silently lost (`Skip: stmt`, `Later : stmt`, `Skip:: stmt`, an On Error handler target, a label split from its colon by a continuation, …), because `lineLabel` was a `blockStmt` competing for the colon the block needed as a separator. A label named for a keyword (`Error:`, `Name:`) now parses too. Remaining gap: a label sharing its line with a construct TERMINATOR (`Cont: Next i`, `Fin: End If`) parses but is not a jump target — that is the tree-walking limit, not this defect; see interpreter-gaps.md | VB6.g4, StatementExecutor.cs |
| `LineContinuation` | Partial | `LINE_CONTINUATION : ' ' '_' '\r'? '\n' -> channel(HIDDEN)` — exactly one SPACE before the underscore, and the newline must follow the underscore immediately. Measured: `Debug.Print 1 +… | VB6.g4 |
| `LineLabel` | Partial | `lineLabel : ambiguousIdentifier COLON`; the visitor is a no-op — "A label is just a jump target; the pc-driver maps its position. Executing it is a no-op." — and `ExecuteProcedureBody`… | StatementExecutor.cs |
| `Object` | Partial | **Superseded by #184:** object member chains now FOLD on the read path, so `a.Inner.V` works. The single-dot restriction described below survives only on the WRITE path (`obj.a.b = x`, `Set obj.a.b = o`). — As a DECLARED type it is absent: `BaseTypeMapper.Map` falls through to `return null; // COLLECTION / OBJECT / CURRENCY (no token yet) / anything else -> caller decides`, and `ExtractType`… | VB6Visitor.cs |
| `String * N` | Partial | `fieldLength : MULT WS? (INTEGERLITERAL \| ambiguousIdentifier)` and `asTypeClause : AS WS (NEW WS)? type (WS fieldLength)?` — then throws. As a Dim / parameter / return type: `throw new… | VB6Visitor.cs |
| `StringLiteral` | Partial | The LEXER accepts the doubled-quote escape (`STRINGLITERAL : '"' (~ ["\r\n] \| '""')* '"'`) but the VISITOR never un-doubles it — it only strips the delimiters: `var str =… | ExpressionExecutor.cs |
| `(( )) (redundant parens force ByVal)` | Supported | Falls out of the lvalue test rather than a dedicated rule: `TryGetArgLocation` returns a caller slot only for a bare `VsICSContext` with no typeHint and no dictionaryCall, so a… | ExpressionExecutor.cs |
| `() (grouping)` | Supported | `VisitVsStruct` evaluates the single inner expression and returns it. Measured `(2 + 3) * 4` = 20 against `2 + 3 * 4` = 14, and `Not (True)` = False. The multi-element form `(a, b)` — not… | ExpressionExecutor.cs |
| `, (argument separator / omitted argument)` | Supported | `ArgSlots` deliberately derives argument POSITIONS from the separators rather than from the present `argCall` nodes, so a blank slot becomes `Vb6Value.Missing` and later arguments do not… | ExpressionExecutor.cs |
| `Boolean` | Supported | `if (baseType.BOOLEAN() != null) return Vb6Value.ValueType.Boolean;`. Measured `Dim b As Boolean : b = True` -> Boolean True. | BaseTypeMapper.cs |
| `Byte` | Supported | `if (baseType.BYTE() != null) return Vb6Value.ValueType.Byte;`; the declared type is recorded on the slot so later stores run `VbNumeric.CoerceOnStore`. Measured `Dim b As Byte : b = 5` ->… | BaseTypeMapper.cs |
| `Currency` | Supported | `if (baseType.CURRENCY() != null) return Vb6Value.ValueType.Currency;` — a fixed 4-decimal-place decimal, banker's-rounded at construction, rank 4 on the widening ladder. Measured `Dim c As… | BaseTypeMapper.cs |
| `Date` | Supported | `if (baseType.DATE() != null) return Vb6Value.ValueType.Date;`; Date is special-cased throughout VbNumeric — "Date + anything -> Date (adds serial days)", "Date - Date -> Double (day… | BaseTypeMapper.cs |
| `DateLiteral` | Supported | `DATELITERAL : HASH (~ [#\r\n])* HASH`; the visitor strips the hashes and parses invariantly — "VB6 date literals are culture-independent, US month/day/year — parse invariantly." `return… | ExpressionExecutor.cs |
| `DecimalIntegerLiteral` | Supported | `ClassifyIntegerLiteral`: Integer when the value fits Int16, else Long when it fits Int32, else Double — "otherwise Integer if it fits Int16, else Long if it fits Int32, else Double."… | ExpressionExecutor.cs |
| `Double` | Supported | `if (baseType.DOUBLE() != null) return Vb6Value.ValueType.Double;`. Measured `Dim d As Double : d = 1.5` -> Double 1.5. Tests: DeclaredTypeTests. | BaseTypeMapper.cs |
| `Empty` | Supported | `EMPTY_` is a real lexer token and the visitor returns `new Vb6Value(Vb6Value.ValueType.EmptyVariant)`. The comment records why it is a keyword: "It is a keyword, not a constant: `Dim Empty… | ExpressionExecutor.cs |
| `ExponentLiteral` | Supported | An `e`/`E` exponent yields Double with or without a decimal point — "exponent without a dot -> Double (e.g. 1e5)". Measured `1.5E2` = 150#, `1.5E+2` = 150#, `1.5E-2` = 0.015#, `15e1` =… | ExpressionExecutor.cs |
| `False` | Supported | `if (literalContext.literal().FALSE() is { }) { Vb6Value val = new Vb6Value(false); return val; }`. Measured -> Boolean False. | ExpressionExecutor.cs |
| `FloatingPointLiteral` | Supported | "VB6: an unsuffixed floating-point literal (with a '.' or exponent) defaults to Double"; the type-char suffixes `!`/`#`/`&`/`@`/`%` force Single/Double/Long/Currency/Integer at… | ExpressionExecutor.cs |
| `Integer` | Supported | `if (baseType.INTEGER() != null) return Vb6Value.ValueType.Integer;`; the declared type is recorded so an assignment coerces to it and overflows raise Err 6 rather than widening — the… | BaseTypeMapper.cs |
| `Long` | Supported | `if (baseType.LONG() != null) return Vb6Value.ValueType.Long;`. Measured `Dim l As Long : l = 5` -> Long 5. | BaseTypeMapper.cs |
| `Nothing` | Supported | `return Vb6Value.Nothing; // a null object reference`. Measured: `Set o = Nothing` then `o Is Nothing` -> True; a class-typed `Dim` seeds Nothing so `c Is Nothing` is True until Set. Tests:… | ExpressionExecutor.cs |
| `Null` | Supported | `return Vb6Value.Null;`. Propagation is implemented per operator: arithmetic returns Null, `&` returns Null only when BOTH operands are Null, `Eqv` returns Null, `Imp` carries the full… | ExpressionExecutor.cs |
| `Single` | Supported | `if (baseType.SINGLE() != null) return Vb6Value.ValueType.Single;`. Measured `Dim s As Single : s = 1.5` -> Single 1.5. Single is rank 3 on the widening ladder, and `/` returns Single iff… | BaseTypeMapper.cs |
| `String` | Supported | `if (baseType.STRING() != null) return Vb6Value.ValueType.String;`; a declared String slot stringifies whatever is stored (oracle row `Dim s As String : s = 5` -> "5"). Measured `Dim s As… | BaseTypeMapper.cs |
| `True` | Supported | `if (literalContext.literal().TRUE() is { }) { Vb6Value val = new Vb6Value(true); return val; }`. Measured -> Boolean True; VB6's numeric True = -1 is honoured on the bitwise path (`if… | ExpressionExecutor.cs |
| `UserDefinedTypeName` | Supported | `Dim x As <name>` resolves the complexType in order against `interpreter.Types` (a `Type` -> a fresh UDT instance), `interpreter.Enums` (-> Long 0) and class modules (-> Nothing, with the… | StatementExecutor.cs |
| `Variant` | Supported | `if (baseType.VARIANT() != null) return Vb6Value.ValueType.EmptyVariant;`, and the declaration path deliberately records NO declared type for it — "an array, an object, a UDT and a Variant… | BaseTypeMapper.cs |

### Compiler directives and options — 47 names (17 absent, 9 partial, 8 supported)

| Name | Status | Detail | Source |
|---|---|---|---|
| `#Const` | **Won't load** | VB6.g4 has no #Const lexer token and no parser rule; the only HASH-prefixed directive tokens are MACRO_IF/MACRO_ELSEIF/MACRO_ELSE/MACRO_END_IF (VB6.g4). '#Const' instead lexes as FILENUMBER… | VB6.g4 |
| `#Else` | **Dies** | throws. macroElseBlockStmt (VB6.g4) is part of macroIfThenElseStmt and shares its fate: measured, '#If Win32 Then ... #Else ... #End If' raises VBCompileErrorException 'Conditional… | PrePass.cs |
| `#ElseIf` | **Dies** | throws. macroElseIfBlockStmt (VB6.g4) is only reachable as part of macroIfThenElseStmt, so it fails with its parent: VBCompileErrorException 'Conditional compilation (#If / #Const) is not… | PrePass.cs |
| `#End If` | **Dies** | throws, as the terminator of macroIfThenElseStmt (VB6.g4) - same two exceptions as #If (PrePass.cs at module level, StatementExecutor.cs inside a procedure). | PrePass.cs |
| `#If` | **Dies** | throws `"Conditional compilation (#If / #Const) is not supported"` — throws - with two different exceptions depending on where it sits. At module level PrePass hits it first: '' (PrePass.cs), under the comment… | PrePass.cs |
| `Mac` | **Dies** | Not defined anywhere in the interpreter. Reachable only from #If, which throws first - measured with '#If Win32 Then ... #ElseIf Mac Then ... #End If': VBCompileErrorException 'Conditional… | PrePass.cs |
| `VB_MemberFlags` | **Dies** | throws `"Attribute not implemented"` — VB6 writes this only at member level - inside a procedure body, or immediately after a module-level declaration - and both positions land in a block… | StatementExecutor.cs |
| `VB_PredeclaredId` | **Dies** | but is silently discarded - no throw, and no behaviour. Emitted in the canonical class header (ModuleFileFormat.cs) and preserved verbatim, but a repo-wide grep for a consumer across… | ModuleFileFormat.cs |
| `VB_ProcData.VB_Invoke_Func` | **Dies** | throws. The dotted name is handled by the grammar (attributeStmt takes an implicitCallStmt_InStmt, VB6.g4), so 'Attribute Foo.VB_ProcData.VB_Invoke_Func = "M14"' inside a Sub parses… | StatementExecutor.cs |
| `VB_UserMemId` | **Dies** | throws `"Attribute not implemented"` — throws. Member-level only; the statement reaches StatementExecutor.VisitAttributeStmt, which is '' (StatementExecutor.cs). Measured: 'Public Function… | StatementExecutor.cs |
| `VB_VarDescription` | **Dies** | throws `"Attribute not implemented"` — throws. VB6 writes it in the declarations section immediately after the variable it describes, which is past the contiguous header run, so it is… | StatementExecutor.cs |
| `VB_VarHelpID` | **Dies** | throws `"Attribute not implemented"` — throws. Same declarations-section position as VB_VarDescription, therefore the same block-statement path: '' (StatementExecutor.cs). | StatementExecutor.cs |
| `VB_VarMemberFlags` | **Dies** | throws. Measured: 'Public Foo As Long' followed by 'Attribute Foo.VB_VarMemberFlags = "40"' raises 'NotImplementedException: Attribute not implemented' (StatementExecutor.cs). | StatementExecutor.cs |
| `VB_VarUserMemId` | **Dies** | throws `"Attribute not implemented"` — throws. Declarations-section position, so the block path applies: '' (StatementExecutor.cs). Measured at module top (before any code) it is instead… | StatementExecutor.cs |
| `Vba6` | **Dies** | Not defined anywhere in the interpreter (repo-wide grep for Vba6/VBA6 across IDE/ and LspServer/ returned nothing). Reachable only from #If, which throws first (PrePass.cs /… | PrePass.cs |
| `Win16` | **Dies** | Not defined anywhere in the interpreter (the same repo-wide grep as Win32 returned no Win16 hits at all). Reachable only from #If, which throws first (PrePass.cs / StatementExecutor.cs). | PrePass.cs |
| `Win32` | **Dies** | Not defined anywhere in the interpreter. A repo-wide grep for a Win32 conditional-compilation constant across IDE/ and LspServer/ (.cs and .g4) returned only unrelated hits - Avalonia… | PrePass.cs |
| `Attribute` | Partial | The statement parses everywhere VB6 allows it (attributeStmt, VB6.g4; reachable as a module header element at VB6.g4 and as a block statement at VB6.g4), but only ONE position is tolerated… | StatementExecutor.cs |
| `Compare` | Partial | Exists only as the second word of the composite OPTION_COMPARE token (VB6.g4), consumed by optionCompareStmt (VB6.g4) and then discarded - PrePass.VisitOptionCompareStmt is '=> default;'… | PrePass.cs |
| `Explicit` | Partial | Exists only as the second word of the composite OPTION_EXPLICIT token (VB6.g4), consumed by optionExplicitStmt (VB6.g4) and honoured - 'RequireVariableDefinitions = true;' (PrePass.cs)… | PrePass.cs |
| `Option` | Partial | There is no standalone OPTION token. The four directives are single composite lexer tokens - OPTION_BASE, OPTION_EXPLICIT, OPTION_COMPARE, OPTION_PRIVATE_MODULE (VB6.g4) - each spelled with… | VB6.g4 |
| `Option Compare` | Partial | and is accepted, but never honoured. PrePass.VisitOptionCompareStmt is '=> default;' under the comment: 'Accepted but not honoured - HexIDE always compares strings ordinally (Option Compare… | PrePass.cs |
| `Option Explicit` | Partial | Collected by PrePass ('RequireVariableDefinitions = true;', PrePass.cs) and actually read at two sites: ExpressionExecutor.cs ('if (currentModule.PrePass.RequireVariableDefinitions \|\|… | PrePass.cs |
| `VB_Description` | Partial | Position decides the outcome, and the two positions differ. In the contiguous top-of-file header run it is accepted and ignored, and ModuleFileFormat names it as the reason header… | StatementExecutor.cs |
| `VB_HelpID` | Partial | Same position split as VB_Description. Module-level, inside the contiguous top-of-file Attribute run: accepted and dropped (measured - 'Attribute VB_HelpID = 1234' at module top parses and… | StatementExecutor.cs |
| `VB_Name` | Partial | The only VB_* attribute with any consumer, and the consumer is the serializer, not the interpreter. ModuleFileFormat.Header emits it for a new .bas/.cls (ModuleFileFormat.cs,40) and… | ModuleFileFormat.cs |
| `Alias` | No-op | Optional clause of declareStmt ('(WS ALIAS WS STRINGLITERAL)?', VB6.g4; token ALIAS at VB6.g4). Parsed and discarded with the statement (PrePass.cs). Measured in both forms - the named form… | PrePass.cs |
| `Any` | No-op | There is no ANY token in VB6.g4 (grep confirms; the type keywords are enumerated as baseType). 'As Any' parses only because 'Any' falls through complexType -> ambiguousIdentifier. Inside a… | PrePass.cs |
| `Declare` | No-op | in full (declareStmt, VB6.g4 - visibility, Sub/Function, type hints, argList and As-clause) and is then dropped whole. PrePass.VisitDeclareStmt is '=> default;' under the comment: '`Declare… | PrePass.cs |
| `Lib` | No-op | Mandatory clause of declareStmt ('... WS LIB WS STRINGLITERAL ...', VB6.g4; token LIB at VB6.g4). Parsed and then discarded with the whole statement by PrePass.VisitDeclareStmt… | PrePass.cs |
| `Module` | No-op | Exists only as the third word of the composite OPTION_PRIVATE_MODULE token (VB6.g4), consumed by optionPrivateModuleStmt (VB6.g4) and discarded - 'Accepted as a no-op - HexIDE doesn't… | PrePass.cs |
| `Option Private Module` | No-op | PrePass.VisitOptionPrivateModuleStmt is '=> default;' under the comment: 'Accepted as a no-op - HexIDE doesn't enforce cross-project module-member visibility. Skipped (not thrown) so a… | PrePass.cs |
| `Text` | No-op | A real standalone token (TEXT, VB6.g4) accepted as an Option Compare operand (VB6.g4) and then deliberately ignored: 'Accepted but not honoured - HexIDE always compares strings ordinally… | PrePass.cs |
| `VB_Base` | No-op | Header-position attribute: accepted and dropped. Measured - 'Attribute VB_Base = "0{FCFB3D2A-A0FA-1068-A738-08002B3371B5}"' at module top parses (moduleAttributes, VB6.g4) and the module… | ModuleFileFormat.cs |
| `VB_Creatable` | No-op | Emitted verbatim in the canonical class header ("Attribute VB_Creatable = True\r\n", ModuleFileFormat.cs), stripped from the editable body on load and re-emitted unchanged on save. Read by… | ModuleFileFormat.cs |
| `VB_Customizable` | No-op | Header-position attribute: accepted and dropped. Measured - 'Attribute VB_Customizable = True' at module top parses and the module runs; in a .cls it is inside the contiguous run… | ModuleFileFormat.cs |
| `VB_Exposed` | No-op | Emitted in the canonical class header ("Attribute VB_Exposed = False\r\n", ModuleFileFormat.cs), preserved verbatim on round-trip, read by nothing. docs/interpreter-gaps.md:163-164:… | ModuleFileFormat.cs |
| `VB_GlobalNameSpace` | No-op | Emitted in the canonical class header ("Attribute VB_GlobalNameSpace = False\r\n", ModuleFileFormat.cs), preserved verbatim, read by nothing. docs/interpreter-gaps.md:163-164 groups it with… | ModuleFileFormat.cs |
| `VB_TemplateDerived` | No-op | Header-position attribute: accepted and dropped. Measured - 'Attribute VB_TemplateDerived = False' at module top parses and the module runs; preserved verbatim by ModuleFileFormat's… | ModuleFileFormat.cs |
| `Base` | Supported | Exists only as the second word of the composite OPTION_BASE token (VB6.g4), consumed by optionBaseStmt (VB6.g4) and honoured at PrePass.cs. See Option Base for the full behaviour and its… | PrePass.cs |
| `Begin` | Supported | Two distinct roles, both handled. In a .frm it opens a control node: 'else if (line.StartsWith("Begin")) { var component = ParseBegin(line); ... componentStack.Push(component); }'… | VbFrmFormatDeserializer.cs |
| `BeginProperty` | Supported | Opens a nested property bag; implemented with a stack so bags nest (VbFrmFormatDeserializer.cs), written back at VbFrmFormatSerializer.cs. The stack is deliberate: 'A stack rather than… | VbFrmFormatDeserializer.cs |
| `Binary` | Supported | A real standalone token (BINARY, VB6.g4) accepted as an Option Compare operand (VB6.g4). The directive itself is discarded (PrePass.cs), but HexIDE's fixed behaviour IS ordinal/Binary… | PrePass.cs |
| `Class` | Supported | The CLASS suffix that distinguishes a .cls header from a .frm one. ModuleFileFormat.SplitHeader tests for it to decide the header shape - 'lines[i].IndexOf("CLASS"… | ModuleFileFormat.cs |
| `EndProperty` | Supported | Closes the innermost property bag and folds its verbatim lines into the parent, or onto the component when it was outermost (VbFrmFormatDeserializer.cs); written back at… | VbFrmFormatDeserializer.cs |
| `Option Base` | Supported | Collected by PrePass ('ArrayBase = int.Parse(context.INTEGERLITERAL().GetText());', PrePass.cs) and applied on both declaration paths: module-level Dim arrays (PrePass.cs) and… | PrePass.cs |
| `Version` | Supported | Handled on three independent paths. Form/control files: the deserializer skips the line ('if (line.StartsWith("VERSION")) { continue; }', VbFrmFormatDeserializer.cs) and the serializer… | VbFrmFormatDeserializer.cs |

### In-box objects — 123 names (98 absent, 8 partial, 17 supported)

| Name | Status | Detail | Source |
|---|---|---|---|
| `Cls` | **Dies** | **Fixed by #191** — was Silently wrong: it ran, did nothing and let the program continue. Now raises, naming the intrinsic | StatementExecutor.cs |
| `AmbientProperties` | **Dies** | Measured for `Dim a As AmbientProperties`: VBCompileErrorException: "User-defined type not defined: AmbientProperties" (StatementExecutor.cs). The route to a real instance is gone too:… | StatementExecutor.cs |
| `AmbientProperties.BackColor` | **Dies** | Unreachable: there is no `Ambient` and no `UserControl` global to read it from — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" and "Variable not defined… | BasicInterpreter.cs |
| `AmbientProperties.DisplayAsDefault` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | BasicInterpreter.cs |
| `AmbientProperties.DisplayName` | **Dies** | Measured directly: `Debug.Print UserControl.Ambient.DisplayName` gives VBVariableNotDefinedException: "Variable not defined (UserControl)" (ExpressionExecutor.cs). The property name appears… | BasicInterpreter.cs |
| `AmbientProperties.Font` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | BasicInterpreter.cs |
| `AmbientProperties.ForeColor` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | BasicInterpreter.cs |
| `AmbientProperties.LocaleID` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | BasicInterpreter.cs |
| `AmbientProperties.MessageReflect` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | BasicInterpreter.cs |
| `AmbientProperties.Palette` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | BasicInterpreter.cs |
| `AmbientProperties.RightToLeft` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | BasicInterpreter.cs |
| `AmbientProperties.ScaleUnits` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | BasicInterpreter.cs |
| `AmbientProperties.ShowGrabHandles` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | BasicInterpreter.cs |
| `AmbientProperties.ShowHatching` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | BasicInterpreter.cs |
| `AmbientProperties.SupportsMnemonics` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | BasicInterpreter.cs |
| `AmbientProperties.TextAlign` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | BasicInterpreter.cs |
| `AmbientProperties.UIDead` | **Dies** | Unreachable: no `Ambient`/`UserControl` global exists — measured VBVariableNotDefinedException: "Variable not defined (Ambient)" / "Variable not defined (UserControl)"… | BasicInterpreter.cs |
| `AmbientProperties.UserMode` | **Dies** | Measured directly: `Debug.Print Ambient.UserMode` gives VBVariableNotDefinedException: "Variable not defined (Ambient)" and `Debug.Print UserControl.Ambient.UserMode` gives "Variable not… | BasicInterpreter.cs |
| `App.HelpFile` | **Dies** | the name is absent from VbApp.TryGetProperty's switch, so the read falls through to ExpressionExecutor.cs. Measured verbatim: "Compile error: Method or data member not found (HelpFile in… | VbApp.cs |
| `App.LogEvent` | **Dies** | VbApp.Call has no dispatch at all - VbApp.cs is: public void Call(string method, List<Vb6Value> args) => throw new VBRunTimeException(VBStandardError.MethodOrDataMemberNotFound, "App." +… | VbApp.cs |
| `App.LogMode` | **Dies** | the name is absent from VbApp.TryGetProperty's switch, so the read falls through to ExpressionExecutor.cs. Measured verbatim: "Compile error: Method or data member not found (LogMode in… | VbApp.cs |
| `App.LogPath` | **Dies** | the name is absent from VbApp.TryGetProperty's switch, so the read falls through to ExpressionExecutor.cs. Measured verbatim: "Compile error: Method or data member not found (LogPath in… | VbApp.cs |
| `App.StartLogging` | **Dies** | VbApp.Call has no dispatch at all - VbApp.cs is: public void Call(string method, List<Vb6Value> args) => throw new VBRunTimeException(VBStandardError.MethodOrDataMemberNotFound, "App." +… | VbApp.cs |
| `App.StartMode` | **Dies** | the name is absent from VbApp.TryGetProperty's switch, so the read falls through to ExpressionExecutor.cs. Measured verbatim: "Compile error: Method or data member not found (StartMode in… | VbApp.cs |
| `App.TaskVisible` | **Dies** | the name is absent from VbApp.TryGetProperty's switch, so the read falls through to ExpressionExecutor.cs. Measured verbatim: "Compile error: Method or data member not found (TaskVisible in… | VbApp.cs |
| `AsyncProperty` | **Dies** | Measured for `Dim a As AsyncProperty`: VBCompileErrorException: "User-defined type not defined: AsyncProperty" (StatementExecutor.cs). The name appears nowhere in the repository. | StatementExecutor.cs |
| `AsyncProperty_VB5` | **Dies** | Measured for `Dim a As AsyncProperty_VB5`: VBCompileErrorException: "User-defined type not defined: AsyncProperty_VB5" (StatementExecutor.cs). | StatementExecutor.cs |
| `Circle` | **Dies** | throws `"Only single element supported"` — fails at run time - but NOT with a name-resolution error. VB6.g4 has no CIRCLE token and no circleStmt rule, so `Circle (100, 100), 50` parses as a… | ExpressionExecutor.cs |
| `Clipboard` | **Dies** | never seeded. BasicInterpreter.cs seeds only Debug, Err and App as program globals. Measured verbatim: "Compile error: Variable not defined (Clipboard)". | BasicInterpreter.cs |
| `Clipboard.Clear` | **Dies** | Measured (statement position): a VBRunTimeException from StatementExecutor.cs - "Unknown method Clear on <Right(EmptyVariant)>()" - because the Clipboard lead resolves to Empty, so neither… | StatementExecutor.cs |
| `Clipboard.GetData` | **Dies** | In expression position the member-chain lead is resolved by ExpressionExecutor.cs, which throws before the member is ever looked at. Measured verbatim for Clipboard.GetText() and… | ExpressionExecutor.cs |
| `Clipboard.GetFormat` | **Dies** | In expression position the member-chain lead is resolved by ExpressionExecutor.cs, which throws before the member is ever looked at. Measured verbatim for Clipboard.GetText() and… | ExpressionExecutor.cs |
| `Clipboard.GetText` | **Dies** | In expression position the member-chain lead is resolved by ExpressionExecutor.cs, which throws before the member is ever looked at. Measured verbatim for Clipboard.GetText() and… | ExpressionExecutor.cs |
| `Clipboard.SetData` | **Dies** | Measured verbatim: "Run-time error: Unknown method SetData on <Right(EmptyVariant)>()". | StatementExecutor.cs |
| `Clipboard.SetText` | **Dies** | Measured verbatim: "Run-time error: Unknown method SetText on <Right(EmptyVariant)>()". | StatementExecutor.cs |
| `Collection` | **Dies** | COLLECTION is a real grammar token (VB6.g4) listed in baseType (VB6.g4), so `Dim c As Collection` reaches BaseTypeMapper.Map, which returns null for it (BaseTypeMapper.cs comment:… | PrePass.cs |
| `Collection._NewEnum` | **Dies** | The name lexes as an ordinary IDENTIFIER (VB6.g4; the LETTER fragment at :2110 includes `_`), and the bracketed form `[_NewEnum]` parses too — but `_NewEnum`/`NewEnum` appears nowhere in… | Exceptions.cs |
| `Collection.Add` | **Dies** | No Collection object can exist (see Collection), and no Add handler exists on any runtime proxy. Measured with the call-statement form on a Variant holder: VBRunTimeException: "Unknown… | StatementExecutor.cs |
| `Collection.Count` | **Dies** | Measured: VBMethodOrDataMemberNotFoundException: "Method or data member not found (Count in Right(EmptyVariant))". No Collection type exists to carry it. | Exceptions.cs |
| `Collection.Item` | **Dies** | Measured explicit form: VBMethodOrDataMemberNotFoundException: "Method or data member not found (Item(1) in Right(EmptyVariant))". The implicit default-member form `c(1)` fails differently… | Exceptions.cs |
| `Collection.Remove` | **Dies** | Measured: VBRunTimeException: "Unknown method Remove on <Right(EmptyVariant)>()" (StatementExecutor.cs). | StatementExecutor.cs |
| `ContainedControls` | **Dies** | Measured for `Dim c As ContainedControls`: VBCompileErrorException: "User-defined type not defined: ContainedControls" (StatementExecutor.cs). | StatementExecutor.cs |
| `Controls` | **Dies** | a form's Controls collection does not exist. Measured verbatim: "Compile error: Variable not defined (Controls)". A form binds only its own name and "Me" (VBLoader.cs); no collection is… | VBLoader.cs |
| `Controls.Add` | **Dies** | Measured verbatim: "Run-time error: Unknown method Add on <Right(EmptyVariant)>()". | StatementExecutor.cs |
| `Controls.Count` | **Dies** | Measured verbatim for Controls.Count: "Compile error: Variable not defined (Controls)". The same applies to Controls.Item(i); the default-member form Controls(i) instead gives "Sub or… | ExpressionExecutor.cs |
| `Controls.Item` | **Dies** | Measured verbatim for Controls.Count: "Compile error: Variable not defined (Controls)". The same applies to Controls.Item(i); the default-member form Controls(i) instead gives "Sub or… | ExpressionExecutor.cs |
| `Controls.Remove` | **Dies** | Measured pattern, identical to Controls.Add: "Run-time error: Unknown method Remove on <Right(EmptyVariant)>()". | StatementExecutor.cs |
| `DataBinding` | **Dies** | Measured for `Dim d As DataBinding`: VBCompileErrorException: "User-defined type not defined: DataBinding" (StatementExecutor.cs). The name appears nowhere in the runtime. | StatementExecutor.cs |
| `DataBindings` | **Dies** | Measured for `Dim d As DataBindings`: VBCompileErrorException: "User-defined type not defined: DataBindings" (StatementExecutor.cs). The name appears nowhere in the runtime. | StatementExecutor.cs |
| `DataMembers` | **Dies** | Measured for `Dim d As DataMembers`: VBCompileErrorException: "User-defined type not defined: DataMembers" (StatementExecutor.cs). The name appears nowhere in the repository. | StatementExecutor.cs |
| `DataObject` | **Dies** | `DataObject` is not a grammar keyword, so it takes the complexType path and throws VBCompileErrorException: "User-defined type not defined: DataObject" — measured for `Dim d As DataObject`… | StatementExecutor.cs |
| `DataObject.Clear` | **Dies** | Measured: VBRunTimeException: "Unknown method Clear on <Right(EmptyVariant)>()" (StatementExecutor.cs). No DataObject type exists to own it. | StatementExecutor.cs |
| `DataObject.Files` | **Dies** | Measured: VBMethodOrDataMemberNotFoundException: "Method or data member not found (Files in Right(EmptyVariant))". | Exceptions.cs |
| `DataObject.GetData` | **Dies** | Measured: VBMethodOrDataMemberNotFoundException: "Method or data member not found (GetData(1) in Right(EmptyVariant))". | Exceptions.cs |
| `DataObject.GetFormat` | **Dies** | Measured: VBMethodOrDataMemberNotFoundException: "Method or data member not found (GetFormat(1) in Right(EmptyVariant))". | Exceptions.cs |
| `DataObject.SetData` | **Dies** | Measured: VBRunTimeException: "Unknown method SetData on <Right(EmptyVariant)>()" (StatementExecutor.cs). | StatementExecutor.cs |
| `DataObjectFiles` | **Dies** | Measured for `Dim f As DataObjectFiles`: VBCompileErrorException: "User-defined type not defined: DataObjectFiles" (StatementExecutor.cs). The name appears nowhere in the repository. | StatementExecutor.cs |
| `Debug.Assert` | **Dies** | as an ICS_B_MemberProcedureCall and throws at run time. DebugProxy.Call handles only "Print"; everything else hits `throw new Exception("No method named " + method)`. Measured verbatim:… | DebugProxy.cs |
| `Err.HelpContext` | **Dies** | TryGetProperty (VbErr.cs) has cases only for number/description/source; the fall-through in ExpressionExecutor then requires a Control and otherwise throws… | ExpressionExecutor.cs |
| `Err.HelpFile` | **Dies** | Measured: `Debug.Print Err.HelpFile` -> VBMethodOrDataMemberNotFoundException, "Method or data member not found (HelpFile in Right(CSharpProxyObject))". No case in… | VbErr.cs |
| `Err.LastDllError` | **Dies** | Measured verbatim: "Method or data member not found (LastDllError in Right(CSharpProxyObject))". | VbErr.cs |
| `EventInfo` | **Dies** | Measured for `Dim e As EventInfo`: VBCompileErrorException: "User-defined type not defined: EventInfo" (StatementExecutor.cs). The name appears nowhere in the runtime. | StatementExecutor.cs |
| `EventParameter` | **Dies** | Measured for `Dim e As EventParameter`: VBCompileErrorException: "User-defined type not defined: EventParameter" (StatementExecutor.cs). The name appears nowhere in the runtime. | StatementExecutor.cs |
| `EventParameters` | **Dies** | Measured for `Dim e As EventParameters`: VBCompileErrorException: "User-defined type not defined: EventParameters" (StatementExecutor.cs). The name appears nowhere in the repository. | StatementExecutor.cs |
| `Forms` | **Dies** | the loaded-forms collection is never built. Measured verbatim: "Compile error: Variable not defined (Forms)". BasicInterpreter.cs seeds only Debug, Err and App as program globals. | BasicInterpreter.cs |
| `Forms.Count` | **Dies** | Measured verbatim: "Compile error: Variable not defined (Forms)". | ExpressionExecutor.cs |
| `Forms.Item` | **Dies** | Measured verbatim for the default-member form Forms(0).Caption: "Compile error: Sub or Function not defined (Forms)" - a parenthesised lead is routed to EvaluateProcedureOrArrayCall, which… | ExpressionExecutor.cs |
| `Hyperlink` | **Dies** | Measured for `Dim h As Hyperlink`: VBCompileErrorException: "User-defined type not defined: Hyperlink" (StatementExecutor.cs). The name appears nowhere in the runtime. | StatementExecutor.cs |
| `LicenseInfo` | **Dies** | Measured for `Dim l As LicenseInfo`: VBCompileErrorException: "User-defined type not defined: LicenseInfo" (StatementExecutor.cs). The name appears nowhere in the repository. | StatementExecutor.cs |
| `Licenses` | **Dies** | Measured for `Debug.Print Licenses.Count`: VBVariableNotDefinedException: "Variable not defined (Licenses)" (ExpressionExecutor.cs) — the global object is not seeded; only Debug, Err and… | BasicInterpreter.cs |
| `ParentControls` | **Dies** | Measured for `Dim p As ParentControls`: VBCompileErrorException: "User-defined type not defined: ParentControls" (StatementExecutor.cs). The name appears nowhere in the runtime. | StatementExecutor.cs |
| `ParentControls.ParentControlsType` | **Dies** | Measured on a Variant holder: VBMethodOrDataMemberNotFoundException: "Method or data member not found (ParentControlsType in Right(EmptyVariant))". The owning ParentControls type does not… | Exceptions.cs |
| `Printer` | **Dies** | never seeded. BasicInterpreter.cs seeds only Debug, Err and App as program globals. All three positions measured: read -> "Compile error: Variable not defined (Printer)"; assignment ->… | BasicInterpreter.cs |
| `Printer.ColorMode` | **Dies** | because the Printer object it hangs off is never seeded. MEASURED for "Printer.Orientation = 2": "Run-time error '424': Object required Can't find variable Printer" (StatementExecutor.cs)… | BasicInterpreter.cs |
| `Printer.Duplex` | **Dies** | because the Printer object it hangs off is never seeded. MEASURED for "Printer.Orientation = 2": "Run-time error '424': Object required Can't find variable Printer" (StatementExecutor.cs)… | BasicInterpreter.cs |
| `Printer.Orientation` | **Dies** | because the Printer object it hangs off is never seeded. MEASURED for "Printer.Orientation = 2": "Run-time error '424': Object required Can't find variable Printer" (StatementExecutor.cs)… | BasicInterpreter.cs |
| `Printer.PaperBin` | **Dies** | because the Printer object it hangs off is never seeded. MEASURED for "Printer.Orientation = 2": "Run-time error '424': Object required Can't find variable Printer" (StatementExecutor.cs)… | BasicInterpreter.cs |
| `Printer.PaperSize` | **Dies** | because the Printer object it hangs off is never seeded. MEASURED for "Printer.Orientation = 2": "Run-time error '424': Object required Can't find variable Printer" (StatementExecutor.cs)… | BasicInterpreter.cs |
| `Printer.PrintQuality` | **Dies** | because the Printer object it hangs off is never seeded. MEASURED for "Printer.Orientation = 2": "Run-time error '424': Object required Can't find variable Printer" (StatementExecutor.cs)… | BasicInterpreter.cs |
| `Printers` | **Dies** | Measured verbatim for Printers(0).DeviceName: "Compile error: Sub or Function not defined (Printers)"; for Printers.Count: "Compile error: Variable not defined (Printers)"… | BasicInterpreter.cs |
| `PropertyBag` | **Dies** | Measured for `Dim p As PropertyBag`: VBCompileErrorException: "User-defined type not defined: PropertyBag" (StatementExecutor.cs); same message for `Set p = New PropertyBag`… | StatementExecutor.cs |
| `PropertyBag.Contents` | **Dies** | Measured: VBMethodOrDataMemberNotFoundException: "Method or data member not found (Contents in Right(EmptyVariant))". | Exceptions.cs |
| `PropertyBag.ReadProperty` | **Dies** | Measured on a Variant holder: VBMethodOrDataMemberNotFoundException: "Method or data member not found (ReadProperty(\"A\", 0) in Right(EmptyVariant))". Measured on a real `PropBag As… | Exceptions.cs |
| `PropertyBag.WriteProperty` | **Dies** | Measured: VBRunTimeException: "Unknown method WriteProperty on <Right(EmptyVariant)>()" (StatementExecutor.cs). | StatementExecutor.cs |
| `PropertyBag_VB5` | **Dies** | The name lexes as an ordinary IDENTIFIER (digits and `_` are both in LETTERORDIGIT, VB6.g4). Measured for `Dim p As PropertyBag_VB5` and for `Set p = New PropertyBag_VB5`:… | StatementExecutor.cs |
| `PSet` | **Dies** | throws `"Only single element supported"` — fails at run time on the coordinate pair, exactly as Circle does. VB6.g4 has no PSET token and no psetStmt rule. `PSet (10, 20), vbRed` parses as a… | ExpressionExecutor.cs |
| `Scale` | **Dies** | throws `"Only single element supported"` — fails at run time. VB6.g4 has no SCALE token and no scaleStmt rule. `Scale (0, 0)-(100, 100)` parses as a bare procedure call with one argument - a… | ExpressionExecutor.cs |
| `Screen` | **Dies** | never seeded. BasicInterpreter.cs seeds only Debug, Err and App as program globals. Measured verbatim: "Compile error: Variable not defined (Screen)"; an assignment such as… | BasicInterpreter.cs |
| `Screen.ActiveControl` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs |
| `Screen.ActiveForm` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs |
| `Screen.FontCount` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs |
| `Screen.Fonts` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs |
| `Screen.Height` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs |
| `Screen.MousePointer` | **Dies** | Measured verbatim for "Screen.MousePointer = 11": "Run-time error '424': Object required Can't find variable Screen" (StatementExecutor.cs). | StatementExecutor.cs |
| `Screen.TwipsPerPixelX` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs |
| `Screen.TwipsPerPixelY` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs |
| `Screen.Width` | **Dies** | because the Screen object it hangs off is never seeded. Measured verbatim: "Compile error: Variable not defined (Screen)". | BasicInterpreter.cs |
| `SelectedControls` | **Dies** | Measured for `Dim s As SelectedControls`: VBCompileErrorException: "User-defined type not defined: SelectedControls" (StatementExecutor.cs). | StatementExecutor.cs |
| `App.ProductName` | Partial | Resolves, but is hardwired to the empty string regardless of the project - it is not read from AppInfo at all. VbApp.cs is: case "productname": value = ""; return true; | VbApp.cs |
| `ClipBoardConstants` | Partial | The nine MEMBER constants are all registered in the built-in constant table and resolve as bare names - vbCFBitmap=2, vbCFDIB=8, vbCFEMetafile=14, vbCFFiles=15, vbCFLink=-16640… | VB6BuiltIns.cs |
| `Debug` | Partial | Seeded as a program-global CSharpProxy in every module env (BasicInterpreter.cs). DebugProxy.Call implements exactly one member: `if (method == "Print")`. Every other member falls to `throw… | DebugProxy.cs |
| `Debug.Print` | Partial | DebugProxy.Call routes to IBasicStandardLibrary.DebugPrint - the Immediate window in the IDE, a capture list under test - preserving the typed Vb6Value. A bare `Debug.Print` prints a blank… | DebugProxy.cs |
| `Err` | Partial | One program-global VbErr shared by every module env (BasicInterpreter.cs, BasicInterpreter.cs). Number/Description/Source are readable and writable through ICSharpPropertyBag; Clear and… | VbErr.cs |
| `Err.Raise` | Partial | VbErr.Call `case "raise"` forwards only the first three arguments - `Raise(ToLong(args[0]), args.Count >= 2 ? ... : null, args.Count >= 3 ? ... : null)`. Arguments 4 (HelpFile) and 5… | VbErr.cs |
| `Err.Source` | Partial | Readable and writable (VbErr.cs, VbErr.cs) and set by Err.Raise when a Source argument is supplied - measured `Err.Raise 5, "src", "desc"` -> Source "src". But Capture deliberately does NOT… | VbErr.cs |
| `Global` | Partial | Two distinct VB6 meanings; only one is implemented. (1) The DECLARATION KEYWORD works: GLOBAL is in the visibility rule (Grammar/VB6.g4, "visibility : PRIVATE \| PUBLIC \| FRIEND \|… | PrePass.cs |
| `App` | Supported | Seeded as a program-global ICSharpProxy/ICSharpPropertyBag into every module env at BasicInterpreter.cs ("SeedProgramGlobal(\"App\", () => new Vb6Value(App));"). Project identity is derived… | VbApp.cs |
| `App.Comments` | Supported | Read via VbApp.TryGetProperty. Measured: App.Comments returned the empty string cleanly (probe printed "[]"). | VbApp.cs |
| `App.CompanyName` | Supported | Read via VbApp.TryGetProperty. Measured: App.Comments returned the empty string cleanly (probe printed "[]"). | VbApp.cs |
| `App.EXEName` | Supported | Read via VbApp.TryGetProperty; the value is the .vbp file's own name without extension (AppInfo.FromProject, VbApp.cs). | VbApp.cs |
| `App.FileDescription` | Supported | Read via VbApp.TryGetProperty. Measured: App.Comments returned the empty string cleanly (probe printed "[]"). | VbApp.cs |
| `App.LegalCopyright` | Supported | Read via VbApp.TryGetProperty. Measured: App.Comments returned the empty string cleanly (probe printed "[]"). | VbApp.cs |
| `App.LegalTrademarks` | Supported | Read via VbApp.TryGetProperty. Measured: App.Comments returned the empty string cleanly (probe printed "[]"). | VbApp.cs |
| `App.Major` | Supported | Read via VbApp.TryGetProperty from the .vbp MajorVer/MinorVer/RevisionVer keys. | VbApp.cs |
| `App.Minor` | Supported | Read via VbApp.TryGetProperty from the .vbp MajorVer/MinorVer/RevisionVer keys. | VbApp.cs |
| `App.Path` | Supported | Read via VbApp.TryGetProperty; the folder containing the .vbp, no trailing separator. | VbApp.cs |
| `App.PrevInstance` | Supported | Read via VbApp.TryGetProperty; hardwired to False. | VbApp.cs |
| `App.Revision` | Supported | Read via VbApp.TryGetProperty from the .vbp MajorVer/MinorVer/RevisionVer keys. | VbApp.cs |
| `App.Title` | Supported | Read at VbApp.cs and WRITTEN at VbApp.cs - the only App member with a setter. Also fills an omitted MsgBox/InputBox caption via TitleOrNull (VbApp.cs). | VbApp.cs |
| `Err.Clear` | Supported | VbErr.Call dispatches `case "clear"` (case-insensitive on the method name) to Clear(), which zeroes Number and empties Description and Source. Covered by… | VbErr.cs |
| `Err.Description` | Supported | Read via TryGetProperty case "description" (VbErr.cs), written via TrySetProperty (VbErr.cs). A trapped runtime error populates it from VBStandardError.Description in Capture (VbErr.cs)… | VbErr.cs |
| `Err.Number` | Supported | Read as a VB6 Long (VbErr.cs comment: "VB6 Err.Number is a Long"), assignable via TrySetProperty (VbErr.cs). Populated by Capture on a trapped VBRunTimeException. Measured: `Err.Number = 42… | VbErr.cs |
| `Me` | Supported | **Superseded by #184:** object member chains now FOLD on the read path, so `a.Inner.V` works. The single-dot restriction described below survives only on the WRITE path (`obj.a.b = x`, `Set obj.a.b = o`). — Bound as an ordinary env variable named "Me" at each of the three sites that can have an instance. CLASS MODULE: BasicInterpreter.cs, "ExecutionContext.AllocVariable(callee, \"Me\", m);" -… | BasicInterpreter.cs |

## Intrinsic constants — 621 names in 89 families

Constants are half the language surface by count and would swamp a table, so they are summarised by
family. They also have almost no nuance individually: a constant either resolves to the right value or it
does not.

**The interesting gap is structural, not per-name.** In VB6 these are not flat constants — they are
members of *enums* published by the VBA and VB type libraries (`ColorConstants`, `VbMsgBoxStyle`,
`KeyCodeConstants`, `VbVarType`, and so on), which is why the Object Browser was full of them. HexIDE
implements the **values** as a flat name→value table (`TryGetBuiltInConstant`) but does **not** register
the **enum types**. So `Dim x As VbMsgBoxStyle` fails, and the type-qualified form
`ColorConstants.vbRed` fails, for every intrinsic constant.

Two corrections to the obvious readings of that. It fails **uniformly** — for the Supported rows as much
as the Partial ones — so enum qualification does not explain the Supported/Partial split and cannot be
used to predict it. And the Object Browser does not merely lack the *grouping*: it shows **no intrinsic
constant at all**, because its VBA library is built solely from the LSP `vb/builtinSymbols` response,
which enumerates `VbSignatures.GetAll()` — a 97-entry list that contains none of them.

The larger cause of Partial is a constant whose **consumer** is absent: a correct value that nothing can
use. Be careful reading these rows, though — the grading is inconsistent on exactly this point. The
raster-op family is graded Supported although `PaintPicture` does not exist, while the OLE and mouse-
pointer families are graded Partial for the same reason. The difference records which rows an adversarial
pass happened to attack, not a real distinction.

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
| `vbBack` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | ExpressionExecutor.cs |
| `vbFormFeed` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | ExpressionExecutor.cs |
| `vbNewLine` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | ExpressionExecutor.cs |
| `vbNullChar` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | ExpressionExecutor.cs |
| `vbNullString` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | ExpressionExecutor.cs |
| `vbTab` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | ExpressionExecutor.cs |
| `vbUseCompareOption` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | ExpressionExecutor.cs |
| `vbVerticalTab` | **Dies** | but the name is not in `builtInConstants` (VB6BuiltIns.cs), so identifier resolution falls through to the undeclared-variable path: under `Option Explicit` it throws… | ExpressionExecutor.cs |

## The signature-help trap

A name the IDE describes is not a name the interpreter runs. `VbSignatures.cs` in the LSP server is
**editor metadata with no execution path**: it feeds signature help and the `vb/builtinSymbols` response
that populates the Object Browser's VBA library. Eleven intrinsics are listed there, offer a full
parameter tooltip, and then fail at run time:

`StrComp`, `CVar`, `CVErr`, `Shell`, `Environ`, `IIf`, `Choose`, `Switch`, `RGB`, `QBColor`, `AscW`.

Worse than a plain gap, because the IDE vouches for the call before the user makes it. Closing the gap
between those two lists — implement it, or stop advertising it — is cheap and disproportionately improves
how the IDE feels.

**Autocomplete itself is not the culprit**, contrary to an earlier draft of this section. Completion is
served from a different list (`VbKeywords.All`), and none of the eleven appears in it. The trap is
signature help and the Object Browser, not the completion popup.

## Not yet enumerated

The sweep behind this document did not reach everything, and a coverage document that implies otherwise is
worse than one that says where it stops. Known holes, none of which have rows anywhere above:

| Area | Missing |
|---|---|
| **The entire event surface** | Not one VB6 event is listed — no `Form_Load`, `Form_Unload`, `Form_QueryUnload`, `Form_Resize`, `Form_Paint`, no `_Click`, `_Change`, `_KeyPress`, `_MouseDown`. |
| In-box classes | `Form`, `MDIForm`, `UserControl`, `Menu`, `VBControlExtender`, `StdFont`, `StdPicture` — the classes the enumerated members hang off. |
| Extender methods | `Move`, `ZOrder`, `SetFocus`, `Refresh`, `Show`, `Hide`, `PopupMenu`, `Arrange`, `ShowWhatsThis`. |
| Graphics methods | `Point`, `PaintPicture`, `TextWidth`, `TextHeight`, `ScaleX`, `ScaleY` (and `Line` appears only as a statement, not in its graphics form). |
| Printer methods | Every `Printer` row is a *property*; `Printer.Print`, `.NewPage`, `.EndDoc`, `.KillDoc`, `.Line` are absent. |
| Drag and drop | `Drag` and `OLEDrag` — the methods the enumerated drag constants are arguments to. |
| `Open` clauses | The compound lexer tokens `Lock Read`, `Lock Write`, `Lock Read Write`, `Read Write`. |

The events gap is the largest and the most consequential: event handlers are how a VB6 form program is
structured, so a coverage document that omits them is silent about the most-used surface in the language.

## Maintenance

This document was generated from a full sweep of the VB6 surface and then hand-edited. When a status
changes, edit the row. When adding a construct, add its row in the same change — a coverage document that
drifts is worse than none, because it is quoted rather than checked.

**Source citations name a file and never a line number.** That is deliberate. Line numbers here have rotted
twice: `interpreter-gaps.md`'s own audit found roughly nineteen of thirty pointing past end-of-file, and
this document's first draft had every `StatementExecutor.cs` citation off by 29 lines because a commit
moved underneath it between the sweep and the commit. The **quoted throw message** in the Detail column is
the anchor — it is greppable and it moves with the code. Please do not add line numbers back.

Two known drift hazards, both live:

1. [`interpreter-gaps.md`](interpreter-gaps.md) has its own `Partial` section describing many of the same
   constructs. It owns *classification*; this file owns *coverage*. Do not restate its reasoning here —
   link to it.
2. `IDE/HexIDE.Runtime/README.md` carries a walls table that has already gone stale at least once
   (it listed `Implements` as permanently out of scope after it had been implemented).

