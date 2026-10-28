# The bundled VB6 language server — what it analyses

This document describes what HexIDE's **own** language server produces: the ten live language features, how
deep each genuinely is, and — separately, because the distinction is load-bearing — which limitations are
work outstanding and which are the architecture holding a line.

Its companion, [`lsp-client.md`](./lsp-client.md), describes the protocol HexIDE speaks to *any* server. If
you are asking "will HexIDE work with my server", that is the document you want. This one is about the
server in the box.

`HexIDE.VbLspServer` is an out-of-process executable reached over stdio. It is an ordinary entry in
[`lsp-servers.json`](./language-servers.md)'s defaults rather than a special case beside them, so it can be
replaced or switched off like any other.

**Visitor hardening (#1894):** symbol, declaration and Option Explicit walks bound recursion in
`VisitChildren` at 500 levels, including left-associated operator trees. The 100,000-addition
[probe](parse-depth-probes.md) now retains statement-level declarations and symbols without crashing;
expression interiors beyond the bound are skipped. The interpreter's expression-walk overflow remains.

---

## Two limits, and only one of them is a backlog

Read the "known gaps" below with this distinction in hand, because it decides whether a gap is a promise:

**◐ Depth** — the feature works and could work better. Real outstanding effort.

**■ Boundary** — the feature is as good as it will get *here*. HexIDE's in-process tooling is syntactic
(CST) only. It does not build a bound AST and by design never will: no cross-file name binding, no type
inference, no semantic diagnostics beyond syntax, no semantic rename, no find-all-references, no workspace
symbol resolution, no code-fix engine. That work belongs to a real language engine delivered over the
replaceable LSP seam, which is the whole reason the seam exists.

A boundary item is therefore **not** a to-do. Anything phrased as "requires a workspace model" or "requires
type inference" is describing a different program's job, not a queue this one is standing in. The
[client document](./lsp-client.md) lists what HexIDE would consume from such an engine.

This document previously listed six of those as *Planned* and *Future*. That was written before the limit
was decided, and it made promises the architecture has since ruled out.

### Depth ratings

| Rating | Meaning |
|--------|---------|
| ✅ **Full** | Production quality — unlikely to need significant rework |
| 🔶 **Partial** | Works for common cases; known gaps documented |
| 🔧 **Wired** | Infrastructure in place; server response is intentionally shallow |

The philosophy behind that last rating has not changed and is worth restating:

> *Correct LSP wiring matters more than response depth. A protocol-correct but trivial response today gives
> a clear upgrade path tomorrow — with zero client changes required.*

---

## What the server is sent, and the lines it keeps off

All server positions count CR, LF and CRLF as line ends, including mixed endings in a file edited outside
VB6 (#1052). Diagnostics, symbols, folds, rename, highlight, signature help, protected regions and formatting
use those same physical lines. Formatting and protected-region blanking preserve each line's terminator.

The client sends each form, module and class as **the whole VB6 file**: a class's `VERSION` and
`BEGIN … END` block, a form's `VERSION` line, object references and designer block, the run of `Attribute`
lines that opens the code, and every member's `Attribute` lines further down. It names the document by its
`file:` URI, or `untitled:<Project>/<Name>.<ext>` when it has no file yet. Until
[#273](https://github.com/hexide-io/HexIDE/issues/273) the server was sent what the code window showed — a
module without its header, a form without its designer block — under a private `vb6://` name, so none of
its answers had ever had to know a header existed. Positions in every answer are now file lines, the same
lines the editor, the interpreter and the file on disk use. See [`lsp-client.md`](./lsp-client.md) for how
names are chosen and changed.

**The header and the members' `Attribute` runs are the IDE's to change**, and read-only in the code window.
So no answer this server gives may change them or point into them, and one helper, `VbProtectedRegions`,
decides which lines they are for all four answers that could reach them: diagnostics, formatting, rename and
highlight. Four copies of the rule would drift.

- **The header** is the top of the file through the last line of the leading `Attribute` run. A text with
  none of that has no header.
- **A member's run** is a block of logical lines below the header whose first word is `Attribute`, and it is
  protected only when it describes a member, as VB6 decides that rather than as the text reads:
  - procedure attributes directly under a procedure's declaration are that procedure's, **whatever their
    qualifiers name** — VB6 gives them by position;
  - `VB_Var…` lines under a module-level variable's or constant's declaration are that line's when they name
    what it declares, or, on a line declaring one name, when they name something nothing in the module
    declares, which is what retyping the variable's name leaves behind.
- **Anything else is an orphan**: a run under a blank line, a comment or a body statement, a run mixing the
  two kinds, a variable attribute away from its variable. An orphan is ordinary text here as it is in the
  IDE, and a damaged one is reported like any other line.

The first draft of that rule bound a run by its qualifiers: a run described a member when the line above
declared every name it qualified. `vb6.exe` overturned it the same day it was built. VB6 gives procedure
attributes to the procedure above whatever they name, binds a `VB_Var…` line by name, reads every
`:`-separated statement on a declaration line, and gives a constant a variable's run. Under the first rule,
every procedure renamed by retyping lost its run's protection.

**The rule is the IDE's, ported rather than shared.** The IDE's copy is `ReadOnlyRegions`
(`IDE/HexIDE.Runtime/Serialization/ReadOnlyRegions.cs`), and the two halves of the repository do not
reference each other, for the same reasons the two grammar copies are separate; the server only ever has
text, so it carries that file's text-only branch. **One file is the exception, and it is shared, not ported**:
the directive scanner (`IDE/HexIDE.Core/Language/ConditionalCompilation.cs`), which the server compiles by a
`<Compile Include>` link, is described under [conditional compilation](#conditional-compilation) below. `BundledServerRespectsTheIdesRegionsTests` keeps them in
step by driving the built server with the real client over four inline files, `demo/`, `corpus/designer`,
`corpus/member-attributes` and `HEXIDE_ROUNDTRIP_CORPUS` when it is set. `corpus/member-attributes` is
required, both its halves: the runs VB6's own IDE wrote, and the misplaced runs VB6 loaded. Without it the
comparison would pass over the inline files and the demos alone. The inline files stay because VB6 writes no
orphan and no rename's leftover, and each of the reader's rules is decided by one shape in them. Formatting
is checked exactly: every line is given two trailing spaces, so the lines the answer changes must be exactly
the non-empty lines outside the IDE's regions.

- ◐ A local declared anywhere in the module counts as a name the module declares, so a `Dim Count` inside
  any procedure turns a renamed variable's `Attribute Count.VB_VarDescription` from that variable's run into
  an orphan, in both copies of the rule ([#1013](https://github.com/hexide-io/HexIDE/issues/1013)).

---

## 1. Error diagnostics (squiggles)

**`textDocument/publishDiagnostics`** (server-push)

The server parses VB6 source with the full ANTLR4 grammar on every change. Syntax errors surface as red
wavy underlines. The client shows no message for one yet, on hover or anywhere else
([#451](https://github.com/hexide-io/HexIDE/issues/451)); `get_diagnostics` reads them.

**What works:** precise error spans on the offending token; ANTLR messages prettified into VB6-style text
by `VbErrorMessages.Prettify()`; squiggles cleared on close. A whole VB6 file is accepted, and nothing is
reported inside its header or a member's `Attribute` lines. A header the grammar cannot read is set aside
and the file parsed again without it, because the parser's recovery from a damaged designer block can
swallow the code after it, errors and procedures alike.

That last rule was found by a test, not predicted. Damage one designer line (`ClientHeight = = 3000`) and put
a syntax error in the code: the parse reported three errors, all in the header, and nothing for the code,
and the procedure dropped out of the outline too. Leaving the header's errors out on their own would have
published nothing at all for that form, with no reason given. So when an error lands on a protected line,
the file is parsed again with the protected lines emptied and their line breaks kept, and that parse
answers. A header the grammar can read stays in the tree, and no file in the corpus pays for a second parse.

Option lines directly before a malformed first procedure no longer make the module-level prediction
fail with an empty procedure tree (#939). That procedure and its symbol survive, with the same syntax
diagnostics as without the Option lines. Recovery inside the procedure can still hide later procedures
and errors (#938).

The grammar had to learn whole files first. When both grammars were first given every corpus file whole,
30 of 46 failed inside the header, all for one reason: VB6 writes an enumerated property value with a
comment after it (`MultiUse = -1  'True`), and the whitespace before that comment reached rules that went
straight from the value to the end of the line. Every `.cls` VB6 ever wrote failed at line 3. Fix 7 in
[`vb6-grammar-fixes.md`](./vb6-grammar-fixes.md) is the repair, and `WholeFileGrammarTests` in each half now
parses the corpus whole.

Statement-level LL recovery stops before the next newline, colon or end of file, and the listener reports
at most one syntax diagnostic per offending token (#938). A broken assignment such as `x = = 1` therefore
leaves the next line's error visible and later procedures in the tree. The SLL fast path is unchanged.
Recovery is still partial: an extraneous token that ends the block loop early (such as `List0.` before
more statements), an error between procedures, or an Option line directly before a broken procedure can
still mask later errors and procedures. `Sync` retains ANTLR's default behaviour; overriding it introduced
a false error in VB6's Tip of the Day template.

`Declare PtrSafe` is accepted in active code too (#1548), with existing types. `LongPtr`, `LongLong` and
their conversion functions remain follow-up work; this adds no external API execution.

An empty `If True Then:` line is accepted without requiring `End If` (#223); the following line is
an independent statement. Both grammars also accept leading empty colon statements and labels before
`End If`, using the measured label-name list.

Whitespace-only offending tokens are named `end of line` in unexpected-input and syntax-error-near
messages, rather than shown as empty quotes (#665). Null, empty and EOF tokens still read `end of file`.

### Conditional compilation

**Code that `vb6.exe` never compiles is never parsed** (#1112). Before parsing, the server applies the module's
`#If`, `#ElseIf`, `#Else`, `#End If` and `#Const`, and empties each directive line and every line of an inactive
branch, keeping the line breaks, so every position in every answer is still the document's own. A
`Declare PtrSafe … As LongPtr` behind `#If VBA7 Then` is no longer squiggled, and neither is a procedure whose
`#If` blocks give it two headers.

- **The scanner is the interpreter's**, so the two cannot read a directive differently. It is one BCL-only file,
  `IDE/HexIDE.Core/Language/ConditionalCompilation.cs`, which `HexIDE.VbLspServer.csproj` compiles by a
  `<Compile Include>` link. That is the only product source the two halves share, and the link is checked:
  `ci-skippable.selftest.sh` reads every linked path from the server's project files and fails if the
  classifier would skip the build for one. The file must stay BCL-only, so `LspServer/` still builds on its own.
- **The arguments come from the project that lists the document.** The server looks for a `.vbp` beside the
  document and in up to three folders above it, and reads its `CondComp` line, as Project Properties' Make tab
  writes it. That serves every client alike, and a group whose projects each have their own arguments needs
  nothing more. The `.vbp`'s item paths are backslash-separated on every host, and are resolved a segment at a
  time, never handed to `System.IO.Path` whole. The project is read again when the file on disk changes.
- **A directive's error is the one diagnostic.** `vb6.exe` refuses the module at a directive out of place
  (`#End If` with no `#If`, an `#Else` after `#Else`, an `#If` never closed) or at a second `#Const` for one
  name, so the server reports that, on that line, in `vb6.exe`'s words, and nothing else in the module.

- ◐ **The evaluator is reduced, and says so rather than guessing.** The interpreter values a directive with its
  own runtime, which the server does not have. The server's values whole-number literals, `True` and `False`,
  names, parentheses, unary minus, `Not`, `And`, `Or`, `Xor`, `Eqv`, `Imp` and the six comparisons, with VB6's
  semantics for that subset (`Not` is bitwise, so `Not 1` is `-2` and true). Anything else leaves the condition
  **undetermined**: a string, a fraction, arithmetic, a function such as `Len`, `Like`, or a name that is neither
  a `#Const` nor `Win32` or `Win16` in a document no project lists, where it might be an argument. An
  undetermined block keeps one branch for the parser, so the outline has something to show, and **reports
  nothing anywhere inside it**, from its `#If` to its `#End If`, because which of its lines `vb6.exe` compiles
  is not known. Which branch matters: this grammar's recovery from an error can swallow every error after it, so a
  kept `Declare PtrSafe` once left a module with a real error further down looking clean, found on the running
  IDE. So the first branch that might be the one is kept, and where the parse's first error falls inside an
  undetermined block the module is parsed again keeping each such block's last instead, where code written for
  several compilers keeps the oldest one's, and the parse whose first error comes later answers. That is at most
  one extra parse. None of those is a binding question, so each is depth: a fuller evaluator closes them. An
  evaluation error, such as `"Variable not defined"` for a function `vb6.exe` refuses in a directive, is not
  reported for the same reason; the interpreter reports it when the module loads.
- ◐ A document no `.vbp` lists, or that two list with different arguments, or whose project's `CondComp`
  `vb6.exe` refuses, has no arguments, only the built-ins.

**`Option Explicit` undeclared-variable checking does not run.**
`VbDiagnosticsProvider.EnableUndeclaredVariableCheck` is a `const bool = false`, so the check is compiled
out, not merely disabled at runtime. The analyzer and its tests exist and work; nothing reaches a user.
(The document these replaced listed this under *what works*, describing the severity and the node kinds it
covers. It has never shipped switched on.)

The retained analyzer has a fixed baseline (#54): designer names collected from the form header,
class/module names explicitly listed before the semicolon in the document's `.vbp`, and the existing
built-in list with `vbPixels` added. `RGB`, `Sin` and `Cos` calls already bypass its simple-variable
visitor. Project names use the same disk lookup and cache as conditional-compilation arguments; they
are supplied only when exactly one project lists the document and its arguments are valid. Unsaved
documents still get their own designer names, but no disk-project names. A header the grammar blanks
for recovery still supplies its names from the original text in the provider path.

This is collection, not binding: it does not inspect other modules' code, check accessibility, or
resolve a reference to a declaration. The remaining runtime-library names have not been added without
measurements. Cross-module variables remain invisible, so enabling the check would still produce false
positives. The baseline does not justify switching it on.

- ◐ Scope analysis is skipped when syntax errors are present, avoiding cascading false positives from an
  incomplete parse tree. Its diagnostics are filtered off the protected lines too, although the check cannot
  run, so that switching it on does not reopen the header.
- ■ No cross-file scope. Variables declared in other modules are not visible and will not become visible —
  that is whole-program name binding.

**Depth: 🔶 Partial**

---

## 2. Hover tooltips

**`textDocument/hover`**

**What works:** declared type annotations for variables (`x As Integer`) from `VbScopeAnalyzer`; diagnostic
messages at the hovered position; both combined when they apply; a 400 ms delay and 4-pixel jitter threshold
to stop flicker.

- ◐ No documentation text for built-in functions.
- ■ Declaration-based only. No inference for expressions or return values, and no cross-file resolution —
  both are type inference.

**Depth: 🔶 Partial**

---

## 3. Document symbols and procedure navigation

**`textDocument/documentSymbol`**

Drives the two combo boxes at the top of the code editor, matching classic VB6 behaviour: the left lists
form controls plus `(General)`, the right is context-sensitive. Selecting a missing event stub generates it.

Extracts `Sub`, `Function`, `Property Get/Let/Set` (with accessor label), `Enum` and `Type`.

- By design, variables and fields are excluded — the combos show procedures, as VB6's did.
- ◐ No hierarchical nesting.

**Depth: ✅ Full** — ANTLR-backed, accurate ranges, matches the original.

---

## 4. Code folding

**`textDocument/foldingRange`**

Twelve block types fold: `Sub`, `Function`, `Property Get|Let|Set`, multiline `If`, `For`, `Do`, `While`,
`With`, `Select Case`, `Enum`, `Type`, `#If`. Single-line `If` is correctly excluded. Both line-ending
conventions are handled.

**The header's fold and each member's attribute fold are not this server's**, and it sends neither. The code
window makes them itself from the file's layout, with any server or none, and merges every server's answer
around them: a server fold that nests is kept, and one that crosses one of them or starts where one starts
is dropped ([`lsp-client.md`](./lsp-client.md)). They describe the file format rather than the code, so they
are the same whichever server is attached. Checked live on `demo/bill-of-fare`, none of the server's eleven
procedure folds started in or crossed the form's 90-line header, which stayed one folded row when they
arrived. As the client reads it, a procedure's fold runs from the start of its declaration's line to
`End Sub`, so it contains its attribute run's fold, and the two nest. LSP starts that fold after the
declaration's text instead, which is where the attribute fold starts, and the client's merge would then drop
the procedure's fold ([#1022](https://github.com/hexide-io/HexIDE/issues/1022)).

- ◐ The scanner is regex/stack-based rather than ANTLR, so unusual nesting can misbehave. An ANTLR-backed
  replacement needs no client change.

**Depth: 🔶 Partial**

---

## 5. Code completion

**`textDocument/completion`**

Returns a static list of 87 keywords and 42 common built-ins, plus every procedure, variable, constant,
parameter, enum and UDT name, and enum member declared in the current file. Labels are unique
case-insensitively, with declared names taking precedence over static entries; `Date` is a Function.
Declared names carry their syntactic completion kind: Sub → Method, Function → Function, Property →
Property, Const → Constant, Enum → Enum, enum member → EnumMember, Type → Struct, and variables and
parameters → Variable. Filtered client-side as you type.

Completion bypasses the analysis worker and reads the last published declaration snapshot (#2434).
It can therefore return names from the previous revision while a change is being parsed. Once the
first open starts analysis, completion can return the static keywords and built-ins even before any
declarations have been parsed. Diagnostics still run and publish for every
change, and other requests (including formatting) still wait for that change's analysis.

- ◐ No position awareness — the full list comes back regardless of cursor context.
- ■ No member-access completion (`obj.`), no type-aware filtering, no symbols from other project files. Each
  needs binding: resolving what `obj` *is*, or what type is expected here, is the relationship between two
  symbols rather than a list of them.

**Depth: 🔧 Wired**

---

## 6. Signature help

**`textDocument/signatureHelp`**

89 VB6 built-ins are covered, spanning strings, type conversions, math, arrays, character/ASCII,
date/time, inspection, I/O and object/misc. The active parameter tracks comma position through nested
parentheses. Past the end of a signature whose last parameter is a list, as `Array`, `Choose` and `Switch` have,
that parameter stays active. Past any other signature's end, the argument's own index is sent, so no parameter
is marked for an argument VB6 would refuse (#2599).
The document is flushed before the request so there is no race with the change debounce.

- ◐ Signatures are manually authored tables.
- ■ User-defined procedure signatures are not shown. Matching a call site to its declaration is binding.

**Depth: 🔧 Wired**

---

## 7. Go to definition

**`textDocument/definition`**

`F12` jumps to a declaration found in the cached symbol table. Returns null gracefully for built-ins and
keywords.

- ◐ Same-file resolution covers procedure-level symbols (`Sub`, `Function`, `Property`, `Enum`, `Type`) but
  not variable declarations.
- ■ Cross-file navigation and member-access resolution (`obj.Method`) are binding.

**Depth: 🔧 Wired**

---

## 8. Document highlights

**`textDocument/documentHighlight`**

Resting the caret on an identifier for 500 ms highlights matching occurrences via a background renderer,
using case-insensitive whole-word matching. Nothing inside a file's header or a member's `Attribute` lines is
highlighted, and resting the caret there highlights nothing.

- ■ Lexical, not semantic: it highlights every textual match, not references to the same symbol. Telling
  those apart is binding.
- ◐ No read/write distinction, though `DocumentHighlightKind` allows one.

**Depth: 🔧 Wired**

---

## 9. Rename symbol

**`textDocument/rename`**

`F2` prompts for a new name and applies all replacements as one atomic edit — a single undo restores the
whole rename.

**Inside the protected lines it changes one thing.** No edit lands in a file's header or a member's
`Attribute` lines except the renamed member's own name where it qualifies one of that member's attribute
lines: renaming `Total` changes `Attribute Total.VB_Description` under `Total`, and nothing else on the line.
A run VB6 gives to another member keeps what it says, so `Attribute Total.VB_Description` under `Sub Halt()`
is `Halt`'s and is left alone (`VbProtectedRegions.IsOwnQualifier`). Nothing depends on a server sending the
qualifier edit. The IDE rewrites a member's qualifiers itself when it saves a document that has unsaved
changes, at the document's own Save or Save As, and when it writes the document to another file. VB6 was
measured doing the same. A project save leaves an unchanged member's file as it was read, and so did VB6's.

**It refuses with `-32803 RequestFailed` and a reason** for:

- a caret on a protected line;
- a name the designer block declares — the form's, a control's or a menu's — wherever the caret is. Its
  declaration is a `Begin` line in the header, so a rename that kept off the header would leave the code
  naming a control that no longer exists. Rename it in the Properties window, as in VB6;
- a new name that is not a VB6 name: a letter, then letters, digits and underscores, and not `Rem`, `Empty`,
  `New`, `Nothing`, `Me` or `Attribute` in any case (`VbTextHelpers.IsName`). Those six were measured against
  `vb6.exe`, each refused in every position tried though it has a name's shape; renamed `Rem`, a function's
  `Rem = mSum` would become a comment. Task 4.4 knew of the first four; `Me` (refused as a module-level array)
  and `Attribute` (refused as a parameter) were found in the oracle by the review of this document;
- a caret on a keyword (`VbKeywords.ReservedWords`), a rule older than the rest.

The code window asks the first three questions itself and says why it refuses, and also shows the server's refusal message when a request is declined. It asks the first two before it prompts for a name, and the third after
the prompt and before it sends the request. A local that shares only a property
name the layout sets, such as `Caption`, or the `Index` parameter of a control array's event handler, is
renamed as usual: its uses in the code change, and the designer lines that set `Caption` do not.

- ■ Lexical, single-file. It will rename unrelated symbols that share a name, and cross-file rename needs a
  workspace model. **Semantic rename is explicitly outside the limit**, so this will not grow into it. For
  the same reason a local that shares a control's name is declined along with the control: telling a
  reference to the control from the local is binding.
- ◐ No `prepareRename`, so the server cannot say before the prompt that a position cannot be renamed. On a
  keyword the prompt still appears, then the refusal explains why no edit was made (#1015).
- A caret with no word, or no editable occurrences, still returns `null` (no change required). Rename is
  scheduled behind prior document changes, including its refusal checks.
- ◐ Only those six words are refused as a new name, and any other keyword is let through. VB6 refuses
  more in at least one position: the oracle's label probe found 24 other keywords that VB6 will not take as a
  line label, among them `End`, `Loop`, `Next` and `Print` ([`vb6-fidelity-oracle/enums.md`](./vb6-fidelity-oracle/enums.md),
  *Which keywords may name a label*). Whether it refuses each of them as a variable or procedure name has not
  been measured, and the IDE and this server refuse only the words measured in one of those positions.

**Depth: 🔧 Wired**

---

## 10. Code formatting

**`textDocument/formatting`**

`Shift+Alt+F` formats the document; `Ctrl+S` requests formatting before writing, giving VB6-style case
correction on every save. If the server is not running the save proceeds unformatted.

**What works:** 123 keywords normalised to PascalCase; 4-space block indentation across every VB6 block
construct; `Else`/`ElseIf`/`Case` dedent then re-indent; string literals and comments never touched; an
empty edit array when already correct; one edit per run of changed lines, which the IDE applies as one
step, so one `Ctrl+Z` undoes it. No edit reaches into a file's header or a member's `Attribute` lines, and
each line keeps the line ending it had (the formatter used to join its output with `\n`, so no file VB6
wrote was ever already formatted). Protected lines are copied through without being classified, so a
property's attribute lines are neither indented nor stop the body after them being indented. Measured on a
copy of `demo/bill-of-fare`: `Ctrl+S` on the untouched form gets `[]` from the server, where it used to get
one edit spanning the file, and the saved file is byte-identical to the original.

The server used to answer with that one edit spanning the whole document, which pointed inside the header
even when its text left the header alone. The client does not rely on this server's restraint: it reduces
any server's answer to the lines it changes and drops those inside the protected lines
([`lsp-client.md`](./lsp-client.md)), so a replacement backend that rewrites the header is clipped rather
than obeyed.

- ◐ No range formatting, indent size hardcoded to 4, line continuations get block-level indent only, and
  statement-level spacing (`x=1` → `x = 1`) is not normalised. All ordinary work.

**Depth: 🔶 Partial**

---

## The server's own trace

Not a language feature, so it is absent from the table below. The trace explains an analysis to the
client; the server's debug log also separates scheduling and execution costs.

Set `trace` to `messages` or `verbose` in `initialize`, or send `$/setTrace` to a running server, and the
analysis reports itself over `$/logTrace`. At `messages` that is one line per analysis; at `verbose` the
same line arrives with detail attached in the notification's `verbose` member. Off is the default, an
unrecognised value is ignored rather than silently downgraded, and with the level off no analysis report
is allocated or sent. Debug timing logs are independent of this protocol trace setting.

```
untitled:Project1/Form1.frm: LL fallback, 41.3 ms, 1 diagnostic, 7 symbols
```

The line opens with the name the client sent: a `file:` URI for a document with a file, and
`untitled:<Project>/<Name>.<ext>` for one without. This example used to read `vb6://module/Form1`, a scheme
the client no longer sends.

**What it deliberately does not carry is the point.** A client capturing its own traffic already has every
method name, payload and elapsed time, so echoing those back would prove the notification works and teach
nothing. What only the server process knows is:

- **Which prediction stage answered.** The parser tries SLL first and falls back to a full LL(\*) re-parse
  when SLL bails — on a genuine syntax error, or on VB6's call-vs-array ambiguity. That fallback is the
  single largest determinant of how long an analysis took, and until this channel existed it was reported
  to nobody.
- **Whether the wall-clock budget expired.** When it does, the previously published diagnostics are kept.
  On the wire that is an unchanged diagnostic set, which looks identical to a file nobody edited.
- **A document refused rather than analysed** — a ranged content change against a server advertising Full
  sync is refused and the document evicted. On the wire, an empty diagnostic array; indistinguishable from
  a file with nothing wrong.

Set `HEXIDE_LOG_LEVEL=Debug` to record each scheduled method's queue wait and execution time in the
rolling server log. Completion has its own immediate dispatch path. Analysis logs include the parse's
thread-pool queue wait, the SLL duration and, if reached, the LL duration (including abandoned work that
finishes later). The diagnostics duration covers the whole budgeted analysis and cache refresh, so it
includes parsing rather than being additive to it. Method, request id and document revision identify
interleaved work; parse and diagnostics entries also carry the document URI. LL runs inline after SLL,
with no separate queue. No document content is logged. See the [#2434 measurement](completion-latency-2434.md).

Measured against five third-party servers, this channel is empty in practice: `trace: "verbose"` plus an
explicit `$/setTrace` produced zero `$/logTrace` frames between them. So the bundled server is also the
reference implementation the client half is proved against.

---

## Summary

| Feature | Method | Depth | Principal limit |
|---|---|---|---|
| Error diagnostics | `publishDiagnostics` | 🔶 Partial | ■ cross-file scope, ◐ `#If` evaluator |
| Hover | `textDocument/hover` | 🔶 Partial | ■ type inference |
| Document symbols | `textDocument/documentSymbol` | ✅ Full | ◐ nesting |
| Code folding | `textDocument/foldingRange` | 🔶 Partial | ◐ regex scanner |
| Completion | `textDocument/completion` | 🔧 Wired | ■ member access, ◐ position |
| Signature help | `textDocument/signatureHelp` | 🔧 Wired | ■ user-defined signatures |
| Go to definition | `textDocument/definition` | 🔧 Wired | ■ cross-file |
| Document highlights | `textDocument/documentHighlight` | 🔧 Wired | ■ semantic resolution |
| Rename | `textDocument/rename` | 🔧 Wired | ■ semantic rename, ◐ no prepareRename |
| Formatting | `textDocument/formatting` | 🔶 Partial | ◐ spacing, range |

Legend: ◐ depth (outstanding work) · ■ boundary (belongs to a language engine).

Every ■ in that table is answered the same way — by a backend that builds a real AST, attached over the
seam. See [`lsp-client.md`](./lsp-client.md) for what HexIDE would consume from one.

---

*The behaviour contracts live under [`openspec/specs/`](../openspec/specs/).*
