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

## 1. Error diagnostics (squiggles)

**`textDocument/publishDiagnostics`** (server-push)

The server parses VB6 source with the full ANTLR4 grammar on every change. Syntax errors surface as red
wavy underlines with a hover tooltip.

**What works:** precise error spans on the offending token; ANTLR messages prettified into VB6-style text
by `VbErrorMessages.Prettify()`; squiggles cleared on close.

**`Option Explicit` undeclared-variable checking does not run.**
`VbDiagnosticsProvider.EnableUndeclaredVariableCheck` is a `const bool = false`, so the check is compiled
out, not merely disabled at runtime. The analyzer and its tests exist and work; nothing reaches a user.
(The document these replaced listed this under *what works*, describing the severity and the node kinds it
covers. It has never shipped switched on.)

That is the boundary doing its job rather than an oversight: the check needs a symbol table, and a symbol
table is the thing being declined. Turning it on would mean either accepting false positives on every
identifier declared elsewhere, or building the binding pass that is out of scope.

- ◐ Scope analysis is skipped when syntax errors are present, avoiding cascading false positives from an
  incomplete parse tree.
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

- ◐ The scanner is regex/stack-based rather than ANTLR, so unusual nesting can misbehave. An ANTLR-backed
  replacement needs no client change.

**Depth: 🔶 Partial**

---

## 5. Code completion

**`textDocument/completion`**

Returns a static list of 88 keywords and 42 common built-ins, plus every procedure, variable, constant,
parameter, enum member and UDT name declared in the current file. Case-insensitive; filtered client-side as
you type.

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
parentheses, and the document is flushed before the request so there is no race with the change debounce.

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
using case-insensitive whole-word matching.

- ■ Lexical, not semantic: it highlights every textual match, not references to the same symbol. Telling
  those apart is binding.
- ◐ No read/write distinction, though `DocumentHighlightKind` allows one.

**Depth: 🔧 Wired**

---

## 9. Rename symbol

**`textDocument/rename`**

`F2` prompts for a new name and applies all replacements as one atomic edit — a single undo restores the
whole rename.

- ■ Lexical, single-file. It will rename unrelated symbols that share a name, and cross-file rename needs a
  workspace model. **Semantic rename is explicitly outside the limit**, so this will not grow into it.
- ◐ No `prepareRename`, so the dialog always appears without pre-validating the position.

**Depth: 🔧 Wired**

---

## 10. Code formatting

**`textDocument/formatting`**

`Shift+Alt+F` formats the document; `Ctrl+S` requests formatting before writing, giving VB6-style case
correction on every save. If the server is not running the save proceeds unformatted.

**What works:** 123 keywords normalised to PascalCase; 4-space block indentation across every VB6 block
construct; `Else`/`ElseIf`/`Case` dedent then re-indent; string literals and comments never touched; an
empty edit array when already correct; a single whole-document edit, so one `Ctrl+Z` undoes it.

- ◐ No range formatting, indent size hardcoded to 4, line continuations get block-level indent only, and
  statement-level spacing (`x=1` → `x = 1`) is not normalised. All ordinary work.

**Depth: 🔶 Partial**

---

## Summary

| Feature | Method | Depth | Principal limit |
|---|---|---|---|
| Error diagnostics | `publishDiagnostics` | 🔶 Partial | ■ cross-file scope |
| Hover | `textDocument/hover` | 🔶 Partial | ■ type inference |
| Document symbols | `textDocument/documentSymbol` | ✅ Full | ◐ nesting |
| Code folding | `textDocument/foldingRange` | 🔶 Partial | ◐ regex scanner |
| Completion | `textDocument/completion` | 🔧 Wired | ■ member access, ◐ position |
| Signature help | `textDocument/signatureHelp` | 🔧 Wired | ■ user-defined signatures |
| Go to definition | `textDocument/definition` | 🔧 Wired | ■ cross-file |
| Document highlights | `textDocument/documentHighlight` | 🔧 Wired | ■ semantic resolution |
| Rename | `textDocument/rename` | 🔧 Wired | ■ semantic rename |
| Formatting | `textDocument/formatting` | 🔶 Partial | ◐ spacing, range |

Legend: ◐ depth (outstanding work) · ■ boundary (belongs to a language engine).

Every ■ in that table is answered the same way — by a backend that builds a real AST, attached over the
seam. See [`lsp-client.md`](./lsp-client.md) for what HexIDE would consume from one.

---

*The behaviour contracts live under [`openspec/specs/`](../openspec/specs/).*
