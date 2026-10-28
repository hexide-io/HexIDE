## A standard module's Property procedures (2026-10-27, #977)

**The question.** #975 measured a standard module's `Property Get PG() As Long()` read as `x = PG`. HexIDE doesn't reach
a standard module's Property Get by name at all: `PS` reads Empty, and `PS()` is "Sub or Function not defined". What does
vb6.exe do with a scalar Property Get read bare and with parentheses, with a Property Let and Set, with a parameter,
through the module's name, and as a statement?

**The probe.** Module2 is `Option Explicit` and has a `Private m As Long` and these:
- `Property Get PG() As Long`, returning `m + 1`, and `Property Let PG(ByVal v As Long)`, storing `v`;
- `Property Get PI(ByVal i As Integer) As Long`, returning `i * 10 + m`, and its Let, storing `v - i`;
- `Property Get PO() As Object` and `Property Set PO`;
- a read-only `Property Get RO() As String`, returning "ro";
- a `Private Property Get PP() As Long`, returning 42;
- `Property Get PV()`, with no As clause, returning `Array(1, 2, 3)`.

The form, also `Option Explicit`, used each of them. Eight more projects held one construct each that might not compile.
Native and p-code agreed on every row.

| From the form | vb6.exe |
|---|---|
| `PG`, and `PG()` | Long 1, each |
| `PG = 5`, then `PG` and `Module2.PG` | 6 and 6: the Let ran |
| `Module2.PG = 7`, then `PG` | 8 |
| `PI(3)` and `Module2.PI(3)` | 37 each |
| `PI(3) = 100`, then `PG` and `PI(0)` | 98 and 97: the Let took `i` = 3 and `v` = 100 |
| `Set PO = New Collection`, then `TypeName(PO)`, then `PO.Add 1: PO.Count` | "Collection", then 1 |
| `RO`, `RO()` | "ro" each |
| `PV(1)`, `PV()(2)`, `TypeName(PV)` | 2, 3, "Variant()": the argument indexes the returned array |
| Inside Module2: `PP`, `PG`, `PG()` | 42, 98, 98 |
| `PG + PG()` | 196 |
| `PG = "x"` | **13**, at run time |
| `PO = New Collection`: PO has a Get and a Set, but no Let | **91**, at run time |

**Refused, each a compile error:**

| Construct | vb6.exe |
|---|---|
| `RO = "x"`, with no Property Let | "Can't assign to read-only property" |
| `PG`, `Call PG` or `PG 5`, as a statement | "Invalid use of property" |
| `Set PG = Nothing`, where PG has no Property Set | "Invalid use of property" |
| `x = PG(1)`, for a Get with no parameter | "Wrong number of arguments or invalid property assignment" |
| `x = PP` from the form, PP being Private to Module2 | "Variable not defined" |

So a standard module's Property procedures are reached by their bare name, as a Function is, with or without empty
parentheses and through the module's name. Assigning to the name calls the Let, and `Set` calls the Set. The 91 for `PO =`
with no Let is consistent with the Get being read and its Nothing given the value. That reading is ours, not measured.
