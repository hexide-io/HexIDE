## Five constructs VB6 builds that HexIDE's grammars refused (2026-11-28, #5831 to #5835, SP6)

**The obvious guess, overturned five times:** that a construct HexIDE's grammars refuse is one VB6 refuses too. The
corpus pilot found each of these in real programs, and VB6 builds every one. HexIDE at `603e079ce` refused each in both of
its grammars, the interpreter's and the bundled language server's. That is the corpus pilot's measurement as each issue
records it, not repeated here.

**Measured on vb6.exe 6.00.9782 (SP6)** in the oracle VM on 2026-11-28. Each probe is a Standard EXE with
`Startup="Sub Main"` and a reference to OLE Automation. Its modules were written to the VM with CRLF line endings, and it
was built with `/make` twice: as native code (`CompilationType=0`) and as p-code (`CompilationType=-1`). The probes were
built, not run: the question is whether VB6 accepts the text. The corpus pilot had built each one natively, and these rows
repeat that and add p-code.

| Issue | Construct | Native | P-code |
|---|---|---|---|
| #5831 | An Enum member `[_Sentinel] = 2` | builds | builds |
| control | The same member unbracketed, `_Sentinel = 2` | **refused**: "Line 4 : Invalid character" | **refused**, the same |
| #5832 | Inside `With x`, `Module2.Foo .Objs(0).Value, 1` | builds | builds |
| #5832 | Inside `With x`, `Foo .Items(0), 1` | builds | builds |
| #5832 | Inside `With x`, `Module2.Foo .Items(0), 1` | builds | builds |
| #5832 | Inside `With x`, `Foo .Objs(0).Value, 1` | builds | builds |
| #5833 | An Enum member `[_] = 5` | builds | builds |
| #5833 | An Enum member `[_]`, with no value | builds | builds |
| #5834 | `Append s`, a call without parentheses to the class's own `Sub Append` | builds | builds |
| #5835 | `40` alone on the line before `End Sub` | builds | builds |

Every build that succeeded wrote "Build of 'p.exe' succeeded." to its `/out` log and left `p.exe`. A failed build
wrote "Compile Error in File '…\Module1.bas', Line 4 : Invalid character" and "Build of 'p.exe' failed.", and left
no `p.exe`. Its `Line 4` is the 0-based code-view count described in
[What `/make` writes to `/out`](what-make-writes-to-out-and-what-line-n-counts.md#what-make-writes-to-out-and-what-line-n-counts-2026-09-19):
the file's sixth line, `_Sentinel = 2`, with the one `Attribute` line left out.

### What each row shows

- **#5831:** the brackets are what make a name that starts with an underscore legal as an Enum member. The same
  line without them is "Invalid character", in both builds.
- **#5832:** VB6 builds the combination and each part on its own. HexIDE's grammars accept the three variants and
  refuse only the combination. That needs two parts together: a module-qualified call without parentheses
  (`Module2.Foo`), and a first argument that is a With-relative chain, indexed and then member-accessed
  (`.Objs(0).Value`).
- **#5833:** a lone underscore in brackets is a legal Enum member name, with a value and without one.
- **#5834:** `Append` is also the file-mode keyword of `Open … For Append`, and VB6 still builds `Append s` as a call
  to a class's own `Sub Append`.
- **#5835:** VB6 accepts a line holding only a line number as the last line of a procedure. The closing-line rows of
  [A line number on a closing or middle line of a block](a-line-number-on-a-closing-or-middle-line-of-a-block.md#a-line-number-on-a-closing-or-middle-line-of-a-block-2026-10-28-2529)
  numbered `End Sub` itself (`90 End Sub`). Here the number is on a line of its own, before it.

### The probes

The modules are each issue's example, verbatim. Where an issue describes the rest of its project rather than giving
it, these were written for the probe.

#5832's project, around its `ModD`. Each variant replaces the one call inside `With x`:

```vb
Attribute VB_Name = "Module2"
Option Explicit

Public Type T1
    Items(1) As Long
    Objs(1) As Class1
End Type

Public Sub Foo(ByVal a As Long, ByVal b As Long)
End Sub

Sub Main()
    Bar4
End Sub
```

```vb
VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
END
Attribute VB_Name = "Class1"
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = False
Attribute VB_Exposed = False
Option Explicit

Public Value As Long
```

#5833's project adds a `Module1` holding an empty `Sub Main`. #5834's `Module1` holds:

```vb
Sub Main()
    Dim o As New Class2
    o.Twice "x"
End Sub
```
