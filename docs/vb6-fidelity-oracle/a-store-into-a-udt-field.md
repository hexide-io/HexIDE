## A store into a UDT field (2026-10-22, #221)

**The question.** HexIDE stored a value into a Type's field as it was given. So `u.l = 5`, with `l As Long`, read back as
an Integer, which eleven corpus cases had measured as a Long and sat recorded as known divergences. What does VB6 do at the
edges: an overflow, a String that is no number, a nested Type, `With`, and a field passed ByRef?

**The probe.** A Sub Main, under `On Error Resume Next`, stores into each field of `Private Type T` (Integer, Long, Double,
String, Boolean, Date, Currency, `String * 5`, Variant, a nested Type with an Integer, and an Integer array of three), and
logs `TypeName`, the value and `Err.Number` after each. Each store follows the ones above it on the same `u`, so a failed
store shows what the field kept. Made native and p-code under en-GB; every line was identical.

| Store | VB6 |
|---|---|
| `u.i = 5` / `u.l = 5` / `u.d = 5` | Integer 5 / Long 5 / Double 5 |
| `u.l = "12"`, then `u.l = "abc"` | Long 12, then 13, and `u.l` keeps 12 |
| `u.i = 5`, then `u.i = 40000`, then `u.i = 2.5` | 6, and `u.i` keeps 5; then Integer 2 |
| `u.s = 5` | String "5" |
| `u.b = 5`, then `u.b = "abc"` | Boolean True, then 13, and `u.b` keeps True |
| `u.dt = "1/2/2003"` / `u.dt = 1` | Date 01/02/2003 / 31/12/1899 |
| `u.c = 1.23456` | Currency 1.2346 |
| `u.f = "abcdefg"` / `u.f = 5`, `f As String * 5` | String "abcde", Len 5 / String "5    ", Len 5 |
| `u.v = 5`, `v As Variant` | Integer 5 |
| `u.inner.x = "7"`, then `u.inner.x = 40000` | Integer 7, then 6, and it keeps 7 |
| `u.arr(1) = "12"`, then `u.arr(1) = 40000` | Integer 12, then 6, and it keeps 12 |
| `a(1).l = 5` / `a(1).s = 2.5`, `a` an array of `T` | Long 5 / String "2.5" |
| `With u` / `.l = 7` / `.s = True` / `End With` | Long 7, String "True" |
| `PL u.l`, where `Sub PL(x As Long)` stores `x = "9"` | Long 9: the field is passed by reference |

**What it shows:** a store into a field converts as a store into a variable of the field's type does, through every way
of reaching the field, and a value that will not convert raises 13 or 6 before the store, so the field keeps what it held.

### In HexIDE (#221)

A Type's typed fields carry their declared types, and `VbUdt.TrySet`, the one store into a field, converts through
`VbNumeric.CoerceOnStore`, as a store into a variable does. Every row above matches and is pinned in `UdtFieldStoreTests`,
except these, which HexIDE does not reach yet:
- a `String * 5` field, which HexIDE refuses as not implemented (#179);
- an array field, which it refuses the same way, and an array of a Type, whose element is not found (#178);
- `PL u.l` left the field at 7 until #1451, because a field argument was copied and never written back (*A UDT field
  passed ByRef*).

The eleven corpus cases recorded as `DECLARED-TYPE-ON-STORE` now match `vb6.exe` and are gated, and the four
`ModuleScopeTests` that pinned the Integer read a Long.

