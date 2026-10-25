## `Like` in `#If` and `#Const`, and a directive's text order (2026-10-23, #1598)

**The question.** #1112 measured that a directive compares strings as text whatever `Option Compare` says, `Like`
included, from `"ABC" Like "a*"` alone. That row cannot tell a text `Like` from a case-folding one, and HexIDE refused
`Like` in a directive until `Like` existed (#1547). And HexIDE ordered a directive's strings by `OrdinalIgnoreCase`,
which #1567 measured as wrong in 14,856 of 51,360 pairs of vb6.exe's text order. Which compare does `Like` take in a
directive, what does it refuse, and does a directive order text as running code's text compare does?

**The probe.** One project of two modules, `Module1` with `Option Compare Binary` and `Module2` with `Option Compare
Text`, each holding the same sixteen `#If` conditions. Each condition picks one of two definitions of a `Public Const`,
`"True"` or `"False"`, and `Sub Main` logs every constant. Both modules start with `#Const KLike = "abc" Like "a*"`. Then
five projects of one module each, for what might be refused, each `#If` selecting a constant `Sub Main` logs. Built
native and p-code with vb6.exe 6.00.8176 (the release build) and run on the MSVBVM60 6.00.9848 (SP6) runtime, en-GB.

**Result: native and p-code identical, and the Binary module identical to the Text one on every row.**

| Condition | In either module | Tells apart |
|---|---|---|
| `"a" Like "A"` | True | text from Binary |
| `"M" Like "[a-z]"` | True | a text range from a Binary one |
| `"_" Like "[A-z]"` | False | text from Binary, where `_` (95) lies between `Z` and `a` |
| `"abc" Like "a*"` | True | |
| `5 Like "#"` | True | a number is taken as its text |
| `"abc" Like "x*"` | False | |
| `"" Like "["` | False | a list never read is not refused, as in running code |
| `CStr("a" Like "a") = "True"` | True | `Like` gives a Boolean |
| `KLike = True`, `KLike = -1`, `CStr(KLike) = "True"` | True | a `#Const` holding `Like` holds a Boolean |
| `"_" < "a"` | True | vb6.exe's text order from `OrdinalIgnoreCase`, which says False |
| `"co-op" > "coop"` | True | the same: `OrdinalIgnoreCase` says False |
| `"it's" > "its"` | True | the same: `OrdinalIgnoreCase` says False |
| `"a" < "B"` | True | |
| `"abc" = "ABC"` | True | |

**Refused, each a compile error naming the line:**

| Condition | vb6.exe's message |
|---|---|
| `"a" Like "["` | Invalid pattern string |
| `#Const KBad = "a" Like "["`, then `#If KBad` | Invalid pattern string, at the `#Const` |
| `"b" Like "[z-a]"` | Invalid pattern string |
| `Null Like "a"` | Invalid use of Null |
| `"a" Like Null` | Invalid use of Null |

So `Like` in a directive is running code's `Like` in a Text module, error 93 and all, in every module. The Null rows are
Null reaching the `#If`, as `#If Null` is "Invalid use of Null" (#1112); a `Like` with a Null side is Null, as in
running code. The three order rows are the ones that tell vb6.exe's text order from `OrdinalIgnoreCase`, and vb6.exe's
order is the one #1567 measured.

### In HexIDE (#1598)

- **`ConstantExpression`** evaluates `Like` with `VbLike`, the interpreter's own, as text whatever the module says, and
  orders two strings with `VbTextCompare`, #1567's comparer, in place of `OrdinalIgnoreCase`. A pattern `VbLike` cannot
  read is its error 93, which a directive raises as the compile error "Invalid pattern string".
- **`ConditionalCompilationTests`** runs the probe program and must print vb6.exe's lines, and refuses each of the five
  refused projects with vb6.exe's message.
- **Divergence kept:** `Like` matches by its per-character fold under text, so a ligature or a sharp s does not stand for
  its letters, here as in running code (#1479).
- **The language server** still leaves a directive holding `Like` undecided, so both of its branches stay live there. It
  has no `Like` of its own.

