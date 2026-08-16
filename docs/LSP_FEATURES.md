# HexIDE — Language Service Features

This document describes every LSP (Language Server Protocol) capability that HexIDE exposes in the code editor, how deeply each is currently implemented, and what is planned for the future.

The HexIDE language server is an out-of-process C# executable (`HexIDE.VbLspServer`) that communicates with the IDE over stdio using standard LSP JSON-RPC. All features listed here are wired end-to-end — server handler → JSON-RPC protocol → IDE client → editor UI. The server can be upgraded in isolation; the IDE never needs to change to gain better analysis quality.

---

## Development philosophy

HexIDE follows an **infrastructure-first** approach to language services:

> *Correct LSP wiring matters more than response depth. A trivial but protocol-correct response today gives us a clear upgrade path tomorrow — with zero client changes required.*

Depth ratings use the following scale:

| Rating | Meaning |
|--------|---------|
| ✅ **Full** | Production quality — unlikely to need significant rework |
| 🔶 **Partial** | Works for common cases; known gaps documented |
| 🔧 **Wired** | Infrastructure in place; server response is intentionally trivial |
| 📋 **Planned** | Not yet started; design considered |
| 💡 **Future** | Requires deep analysis work or architectural prerequisites |

---

## Implemented features

### 1. Error diagnostics (squiggles)

**LSP method:** `textDocument/publishDiagnostics` (server-push notification)

The server parses your VB6 source using the full ANTLR4 VBA grammar on every document change. Syntax errors are reported as red squiggles in the editor with a hover tooltip showing the error message.

**What works:**
- Red wavy underlines for syntax errors (unexpected tokens, missing keywords, etc.)
- `Option Explicit` enforcement — undeclared variables reported as orange warnings (severity 2), covering all `simpleNameExpr` nodes (assignments, RHS expressions, `If` conditions, function arguments, `For` loop variables)
- Error spans match the offending token precisely
- Squiggles clear immediately when the file is closed
- ANTLR error messages are prettified by `VbErrorMessages.Prettify()` into VB6-style text (e.g. "Syntax error: unexpected X" instead of raw "mismatched input X expecting {…}")

**Known gaps:**
- No cross-file scope — variables from other modules in the project are not visible
- Scope analysis is skipped when syntax errors are present (avoids cascading false positives from incomplete parse trees)

**Depth: 🔶 Partial** — genuine ANTLR-backed analysis with expression-level undeclared variable checking; cross-file scope not yet implemented

---

### 2. Hover tooltips

**LSP method:** `textDocument/hover`

Hovering over code for 400 ms shows a tooltip. The server returns type information for declared identifiers and/or diagnostic messages at the hovered position.

**What works:**
- Type annotations shown for declared variables (e.g. `x As Integer`) — sourced from `VbScopeAnalyzer.GetDeclaredTypes()`
- Untyped declarations show just the variable name
- Diagnostic error/warning messages shown at the hovered position
- Type info and diagnostics combined when both apply (separated by blank line)
- 400 ms delay + 4-pixel jitter threshold prevents unwanted flicker
- Tooltip dismissed when pointer leaves the editor

**Known gaps:**
- Type info is declaration-based only — no type inference for expressions or function return values
- No documentation or description text for built-in functions
- Cross-file type resolution not available

**Depth: 🔶 Partial** — shows declared type annotations and diagnostics; deeper type inference requires scope analyzer expansion

---

### 3. Document symbols & procedure navigation

**LSP method:** `textDocument/documentSymbol`

The server walks the ANTLR parse tree to extract all named constructs. The IDE uses this to drive the two combo boxes at the top of the code editor (matching classic VB6 behaviour).

**What works:**
- Left combo: lists all form controls plus `(General)`
- Right combo: context-sensitive — shows all procedures under `(General)`, or the event list for a selected control
- Clicking a procedure name in the right combo moves the caret to that procedure
- Missing event stubs are generated automatically when selected from the combo

**Symbol kinds extracted:**
- `Sub`, `Function` → Method / Function
- `Property Get`, `Property Let`, `Property Set` → Property (with accessor label)
- `Enum` → Enum
- `Type` (UDT) → Struct

**Known gaps:**
- Does not include variable/field declarations (by design — combos show procedures, not all symbols)
- No hierarchical nesting in the symbol list

**Depth: ✅ Full** — ANTLR-backed; accurate ranges; matches VB6 IDE behaviour

---

### 4. Code folding

**LSP method:** `textDocument/foldingRange`

The server scans the source and returns collapsible regions. The editor applies these via AvaloniaEdit's native folding manager.

**Supported block types (12):**
`Sub` / `Function` / `Property Get|Let|Set` / multiline `If…End If` / `For…Next` / `Do…Loop` / `While…Wend` / `With…End With` / `Select Case…End Select` / `Enum…End Enum` / `Type…End Type` / `#If…#End If`

**What works:**
- All 12 block types fold correctly
- Single-line `If` statements are correctly excluded (not foldable)
- Windows (`\r\n`) and Unix (`\n`) line endings both handled
- Collapse label shows the opening line trimmed

**Known gaps:**
- Regex/stack-based scanner (not ANTLR) — complex or unusual nesting may misbehave in edge cases
- Minimum fold size is 2 lines (opening line + closing keyword)

**Depth: 🔶 Partial** — robust for typical code; ANTLR-backed replacement planned (zero client changes required)

---

### 5. Code completion (IntelliSense)

**LSP method:** `textDocument/completion`

Triggered automatically as you type or manually with `Ctrl+Space`. The server returns a combined list of keywords and names declared in the current file.

**What works:**
- ~100 VB6 keywords (`Sub`, `Function`, `Dim`, `For`, `If`, `Select Case`, etc.)
- All procedures, variables, constants, parameters, enum members, and UDT type names declared in the current file
- Case-insensitive matching
- List is filtered client-side as you type

**Known gaps:**
- No position awareness — full list returned regardless of cursor context
- No member access completion (`obj.` does nothing useful yet)
- No type-aware filtering (e.g., only showing string functions when a `String` variable is expected)
- User-defined procedures from other project files are not included

**Depth: 🔧 Wired** — infrastructure complete; quality improves as analysis depth increases

---

### 6. Signature help

**LSP method:** `textDocument/signatureHelp`

When you type `(` or `,` inside a function call, a popup shows the function's signature. The active parameter is highlighted as you move between arguments.

**What works:**
- ~90 VB6 built-in functions covered (full list below)
- Active parameter tracks comma position correctly
- Multiple overloads supported where applicable
- Call context detection handles nested parentheses
- Document flushed before requesting — no race condition with the 300 ms change debounce

**Built-in functions covered:**
Strings (`Len`, `Left`, `Right`, `Mid`, `InStr`, `InStrRev`, `Replace`, `Split`, `Join`, `Trim`, `LTrim`, `RTrim`, `UCase`, `LCase`, `StrComp`, `StrConv`, `String`, `Space`, `Format`), type conversions (`CStr`, `CInt`, `CLng`, `CDbl`, `CSng`, `CBool`, `CByte`, `CCur`, `CDate`, `CVar`), math (`Abs`, `Int`, `Fix`, `Sqr`, `Rnd`, `Sgn`, `Log`, `Exp`, `Sin`, `Cos`, `Tan`, `Atn`), arrays (`Array`, `UBound`, `LBound`), character/ASCII (`Chr`, `Asc`, `ChrW`, `AscW`), date/time (`Now`, `Date`, `Time`, `Timer`, `DateAdd`, `DateDiff`, `DatePart`, `DateSerial`, `TimeSerial`, `Day`, `Month`, `Year`, `Hour`, `Minute`, `Second`, `Weekday`), inspection (`TypeName`, `VarType`, `IsNull`, `IsEmpty`, `IsObject`, `IsArray`, `IsDate`, `IsNumeric`, `IsMissing`), I/O (`MsgBox`, `InputBox`), object/misc (`CreateObject`, `GetObject`, `Shell`, `Environ`, `RGB`, `QBColor`, `IIf`, `Choose`, `Switch`, `Randomize`, `Err`, `CVErr`)

**Known gaps:**
- User-defined procedure signatures not shown — only built-ins
- No type-aware parameter validation
- Signatures are manually authored (no automatic inference)

**Depth: 🔧 Wired** — call context detection is robust; coverage limited to built-ins until type analysis is available

---

### 7. Go to definition

**LSP method:** `textDocument/definition`

Press `F12` with the caret on any identifier to jump to its declaration. The server looks up the symbol in its cached symbol table (from `textDocument/documentSymbol`) and returns the definition location.

**What works:**
- Same-file go-to-definition for procedures (`Sub`, `Function`, `Property`), `Enum`, and `Type` declarations
- Caret moves to the definition line when found
- Returns null gracefully when no definition exists (built-in functions, keywords, etc.)

**Known gaps:**
- Cross-file navigation is a stub (`NavigateToUri()` placeholder) — requires a workspace model
- Does not resolve variable declarations (only procedure/type-level symbols)
- No support for member-access resolution (`obj.Method` → method definition)

**Depth: 🔧 Wired** — same-file works for procedure-level symbols; cross-file and variable-level resolution require workspace model

---

### 8. Document highlights

**LSP method:** `textDocument/documentHighlight`

Resting the caret on an identifier for 500 ms highlights all other occurrences of that name in the current file with a semi-transparent blue background.

**What works:**
- All occurrences highlighted via `DocumentHighlightRenderer` (`IBackgroundRenderer` at `KnownLayer.Background`)
- Case-insensitive, whole-word matching via `FindAllOccurrences` helper
- 500 ms debounce on caret movement to avoid flicker during navigation
- Highlights clear when caret moves to a non-identifier position

**Known gaps:**
- Text-based matching (not semantic) — highlights all lexical matches, not just references to the same symbol
- No distinction between read/write highlights (LSP supports `DocumentHighlightKind`)

**Depth: 🔧 Wired** — lexical matching in place; semantic resolution would improve accuracy

---

### 9. Rename symbol

**LSP method:** `textDocument/rename`

Press `F2` with the caret on any identifier. An InputBox prompts for the new name, then all occurrences in the file are replaced in a single atomic edit.

**What works:**
- F2 keybinding extracts the word under the caret and pre-fills the rename dialog
- Server reuses `FindAllOccurrences` (case-insensitive whole-word matching) to build a `WorkspaceEdit`
- All replacements applied as a single `BeginUpdate`/`EndUpdate` block — editor undo restores the whole rename in one step
- Returns null gracefully when cursor is on whitespace or no matches found

**Known gaps:**
- Lexical (text-based) matching, not semantic — will rename all lexical matches including unrelated symbols with the same name
- Single-file only — cross-file rename requires workspace model
- No `prepareRename` support — always shows the dialog (no pre-validation that the position is renameable)

**Depth: 🔧 Wired** — lexical rename in place; semantic resolution would improve accuracy

---

### 10. Code formatting

**LSP method:** `textDocument/formatting`

Press `Shift+Alt+F` to auto-format the current document. The server normalises keyword casing and applies canonical indentation.

**Format on save:** Pressing `Ctrl+S` (or `Ctrl+Shift+S` for Save As) automatically requests formatting from the LSP server before writing the file. This gives VB6-style keyword case correction on every save — if the server is not running, the save proceeds without formatting.

**What works:**
- **Keyword casing:** 166+ VB6 keywords and common built-ins normalised to PascalCase (e.g. `dim x as integer` → `Dim x As Integer`)
- **Auto-indentation:** 4-space indent per block depth for all VB6 block constructs: `Sub`/`Function`/`Property`/`If…End If`/`For…Next`/`Do…Loop`/`While…Wend`/`With…End With`/`Select Case…End Select`/`Enum…End Enum`/`Type…End Type`
- **Mid-block handling:** `Else`/`ElseIf`/`Case` correctly dedent then re-indent
- **Preserves content:** String literals and comments are never modified
- **Efficient:** Returns empty edit array when source is already correctly formatted (no-op)
- **Undo-friendly:** Single whole-document TextEdit — one Ctrl+Z to undo

**Known gaps:**
- No range formatting — always formats the entire document
- No configurable indent size (hardcoded to 4 spaces)
- Line continuation (`_`) not handled specially — continued lines receive block-level indent only
- Spacing normalisation within statements not implemented (e.g., `x=1` stays as-is, not `x = 1`)

**Depth: 🔶 Partial** — keyword casing and block indentation are robust; statement-level spacing is future work

---

## Planned features

These features are fully designed and compatible with the current architecture. They require server-side work only.

### Find all references

**LSP method:** `textDocument/references`

List every location where a symbol is used.

- Requires identifier resolution across the parse tree
- Cross-file references need the workspace model

**Depth: 📋 Planned** — depends on type analysis and workspace model

---

### Code actions / quick fixes

**LSP method:** `textDocument/codeAction`

Context-sensitive actions: add `Option Explicit`, auto-declare undeclared variables, generate procedure stubs, etc.

- Infrastructure for detecting undeclared variables already exists (`Option Explicit` diagnostics)
- Quick-fix "Add `Dim x`" is a natural first action

**Depth: 📋 Planned**

---

## Future features

These require deeper analysis infrastructure (multi-file workspace model, type resolution) before they can be meaningfully implemented.

### Semantic tokens

**LSP method:** `textDocument/semanticTokens`

Richer syntax highlighting driven by semantic information — distinguish a local variable from a procedure name, a type name from a keyword, etc.

Currently AvaloniaEdit handles syntax colouring via regex-based rules. Semantic tokens would replace this with analysis-backed token classification.

**Depth: 💡 Future** — requires full symbol resolution pass

---

### Inlay hints

**LSP method:** `textDocument/inlayHint`

Inline type annotations shown as ghost text — e.g., show inferred types next to `Dim x` declarations.

**Depth: 💡 Future** — requires type inference

---

### Call hierarchy

**LSP method:** `textDocument/prepareCallHierarchy`

Show which procedures call a given procedure, and which procedures it calls.

**Depth: 💡 Future** — requires cross-file reference tracking

---

### Workspace symbols

**LSP method:** `workspace/symbol`

Search for any symbol across all files in the project by name.

**Depth: 💡 Future** — requires workspace model

---

## Summary table

| Feature | LSP Method | Status | Depth |
|---------|-----------|--------|-------|
| Error diagnostics | `publishDiagnostics` | ✅ Live | 🔶 Partial |
| Hover tooltip | `textDocument/hover` | ✅ Live | 🔶 Partial |
| Document symbols + procedure nav | `textDocument/documentSymbol` | ✅ Live | ✅ Full |
| Code folding | `textDocument/foldingRange` | ✅ Live | 🔶 Partial |
| Code completion | `textDocument/completion` | ✅ Live | 🔧 Wired |
| Signature help | `textDocument/signatureHelp` | ✅ Live | 🔧 Wired |
| Go to definition | `textDocument/definition` | ✅ Live | 🔧 Wired |
| Document highlights | `textDocument/documentHighlight` | ✅ Live | 🔧 Wired |
| Rename symbol | `textDocument/rename` | ✅ Live | 🔧 Wired |
| Code formatting | `textDocument/formatting` | ✅ Live | 🔶 Partial |
| Find all references | `textDocument/references` | 📋 Planned | — |
| Code actions / quick fixes | `textDocument/codeAction` | 📋 Planned | — |
| Semantic tokens | `textDocument/semanticTokens` | 💡 Future | — |
| Inlay hints | `textDocument/inlayHint` | 💡 Future | — |
| Call hierarchy | `textDocument/callHierarchy` | 💡 Future | — |
| Workspace symbols | `workspace/symbol` | 💡 Future | — |

---

*Last updated: April 2026. The current behaviour contracts live under [`openspec/specs/`](../openspec/specs/).*
