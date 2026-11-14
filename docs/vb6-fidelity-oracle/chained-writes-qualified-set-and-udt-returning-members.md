## Chained writes: through a module qualifier, with Set, and through a member that returns a UDT (2026-11-14, #173)

**The question.** #173's reads were measured on 2026-09-02, in
[member chains, including module-qualified ones](member-chains-including-module-qualified-ones.md). There, a module
qualifier is just the first step of an ordinary chain. Three writes were left open:
- an assignment through a module-qualified chain;
- a chained `Set`;
- a write through an object member that returns a UDT, including whether the write reaches the object or a copy.

**The probe.** vb6.exe SP6 (6.00.9782), `/make`, on the Win10 oracle guest. The probes used `scripts/vb6-oracle.ps1`, in a
scratch copy that adds `CompilationType=-1` and extra standard modules. Each row is a function called from `Sub Main`
under `On Error Resume Next`, and its return value is what was logged. Each group that might not compile was built as a
project of its own, so a compile error couldn't hide another row. Every project was built as native code and as p-code,
and the two agreed in every row.

What each group declared:
- **A.** `Module1` has `Private Type Inner` (`Z As Long`), `Private Type Outer` (`X As Long`, `In1 As Inner`),
  `Dim p As Outer` and `Dim a(1) As Outer`. `Module2` has `Public Type InnerQ` and `OuterQ` of the same shape,
  `Public q As OuterQ`, and `ReadQ()`, which returns `q.In1.Z`.
- **B.** Classes `Leaf` (`Public Value As Long`), `Holder` (`Public Child As Leaf`) and `Outer2` (`Public H As Holder`).
  `Module1` has `Private Type InnerO` (`Obj As Leaf`), `Private Type OuterO` (`In1 As InnerO`), `Dim o As Outer2`,
  `Dim u As OuterO`, and `NewLeaf(n)`, which returns a new `Leaf` with `Value = n`. Rows B2, B5 and B6 build `o` and
  `o.H` first, unqualified.
- **C.** `Public Type TU` (`Z As Long`) is in a standard module. Class `Box` keeps `Private m As TU` and counts calls in
  `Public Gets` and `Lets`. Its members are `Friend Property Get U() As TU`, `Friend Property Let U(v As TU)`,
  `Friend Function GetU() As TU`, `Friend Sub SetZ(n)` and `Friend Function MZ()`, which returns `m.Z`. `BoxG` is the
  same with only the `Property Get`. Each row on a `Box` starts with `b.SetZ 3`; `BoxG`'s `m.Z` starts at 0.
- **D.** `Module1` has `Private Type T` (`Z As Long`), `Dim mt As T`, a module `Property Get PT() As T` and
  `Property Let PT(v As T)`, and `Function GetT() As T`, each counting its calls. Each row sets `mt.Z = 3` first.

| Row | Statement | vb6.exe | HexIDE at `e6fb3b859` |
|---|---|---|---|
| A1 | `Module1.p.X = 7`, then `p.X` | `7` | 424, "Can't find variable Module1" |
| A2 | `Module1.p.In1.Z = 42`, then `p.In1.Z` | `42` | 424, the same |
| A3 | `Module1.a(1).In1.Z = 9`, then `a(1).In1.Z` | `9` | 424, the same |
| A4 | `Module2.q.In1.Z = 5`, then `Module2.ReadQ()` | `5` | 424 |
| A5 | `Module2.q.In1.Z = 6`, then `q.In1.Z` | `6` | not run as such; the unqualified `q.In1.Z = 6` lands |
| A6 | `Module1.p.In1.Z = Module1.p.In1.Z + 1`, after A2 | `43` | 424 |
| B1 | `Set o.H.Child = NewLeaf(5)`, then `o.H.Child.Value` | `5` | "Object member chains are single-dot only" |
| B2 | `Set Module1.o.H.Child = NewLeaf(6)`, then `o.H.Child.Value` | `6` | 91, "Object variable or With block variable not set" |
| B3 | `Set u.In1.Obj = NewLeaf(7)`, then `u.In1.Obj.Value` | `7` | compile error at `Obj As Leaf` (#3924) |
| B4 | `Set Module1.u.In1.Obj = NewLeaf(8)`, then `u.In1.Obj.Value` | `8` | the same (#3924) |
| B5 | `Set Module1.o.H.Child = l`, then `o.H.Child Is l` | `True` | not run |
| B6 | `Set Module1.o.H.Child = Nothing`, then `o.H.Child Is Nothing` | `True` | "single-dot only", at the unqualified `Set` before it |
| C0 | `b.U.Z`, a read: the control | `3`, `Get` once | matches |
| C1 | `b.U.Z = 5` on `Box`, then `m.Z`, `Gets`, `Lets` | builds; `3`, `1`, `0` | "Assigning a field of a UDT reached through an object's member is not implemented" |
| C2 | `b.GetU().Z = 5`, then the same | builds; `3`, `1`, `0` | the same |
| C3 | `b.U.Z = 5` on `BoxG` (no `Let`), then `m.Z`, `Gets` | builds; `0`, `1` | not run |
| C4 | `With b.U` / `.Z = 5` / `.Z`, `m.Z`, `Gets`, `Lets` / `End With` | `5`, `3`, `1`, `0` | matches |
| D1 | `PT.Z = 5`, then `mt.Z`, `gets`, `lets` | builds; `3`, `1`, `0` | 424 |
| D2 | `Module1.PT.Z = 6`, then the same | builds; `3`, `1`, `0` | 424 |
| D3 | `GetT().Z = 7`, then `mt.Z`, `gets` | builds; `3`, `1` | matches |

The HexIDE column comes from the same programs run by the interpreter in throwaway tests that weren't committed.

**The answer.**
- **A write's module qualifier is the first step of its chain, as a read's is.** Every qualified write landed: through a
  `Private` and a `Public` UDT, through an array element, into another module's `Public` variable, and as a
  read-modify-write on both sides of one statement.
- **`Set` works through a chain of any depth**, through objects and through UDT fields, qualified or not. It stores the
  very object assigned (`Is` is `True`), and it stores `Nothing` too.
- **A write through a member that returns a UDT compiles, and is lost.** The member is called once. Its result is a
  copy, the field is written into that copy, and the copy is thrown away. A `Property Let` of the same name is never
  called. This holds for a class's `Friend Property Get`, with or without a `Let`, for a `Friend Function`, and for a
  standard module's own `Property Get` and `Function`, qualified or not. Under `With b.U` the copy lives for the block,
  so `.Z` reads back `5` inside it while the object still holds `3`.

**Overturned.** Two guesses were natural here, and both were wrong:
- `GetU().Z = 5` looks like an assignment to a function call, so a compile error seemed likely.
- A property with both `Get` and `Let` invited a Get-modify-Let round trip.

VB6 does neither. It builds the statement and writes a temporary. So a lost write is VB6's behaviour, not a defect to
reproduce differently: HexIDE should call the member once, write the copy, and keep the owner unchanged.

**What it settles for HexIDE (#173).** The qualified write and the chained `Set` are ordinary chains whose last step is
assigned. The write through a UDT-returning member is the one that must not alias: the copy must be a copy. #3924,
where a UDT field can't be typed as a project class, gates rows B3 and B4.
