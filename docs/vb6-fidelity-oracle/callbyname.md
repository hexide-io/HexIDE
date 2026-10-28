# CallByName

Provenance: hexide-io/HexIDE#2519's supplied probe results, measured on vb6.exe SP6 on an en-GB VM.
The brief reports identical logs from native and p-code builds. These measurements were supplied with the
issue; this implementation run did not repeat the Windows probe.

The first implementation slice relies on these rows:

| Probe | Observed value/type or error |
|---|---|
| `CallByName k, "Add", VbMethod, "a", "ka"` | `k.Count` becomes 1 |
| `CallByName(k, "Item", VbMethod, "ka")` after that Add | String `a` |
| `CallByName(k, "Count", VbGet)` | Err 438 |
| `CallByName(k, "Item", VbGet, 1)` | Err 438 |
| `CallByName(Err, "Number", VbGet)` with Err clear | Long 0 |
| `CallByName(o, "x", VbGet)` with `o As Object` holding Nothing | Err 5 |
| `CallByName(five, "x", VbGet)` with a Variant holding 5 | Err 424 |
| A Variant holding Null used as ProcName | Err 94 |

The probe also establishes case-insensitive member names, VBA qualification as the same function, and
VbMethod = 1, VbGet = 2, VbLet = 4, VbSet = 8. Collection Count and Item are methods in its type library;
Get does not select them. Nothing's 5 is distinct from ordinary member access's 91.

The same issue supplies the class measurements used by the second slice. Its class declares an Integer
field x, Variant property P, Object property O, indexed String Property Get Item(Integer), Long Function
F(Integer, Integer), G(Optional Variant a, Optional Integer b = 7), H(ParamArray rest()), Sub S(Variant)
and Sub Twice(ByRef Integer). These are supplied SP6 native/p-code results, not new probes from this run:

| Probe | Observed value/type or error |
|---|---|
| Let x to 5; Get x, including uppercase X and VBA qualification | Integer 5 |
| Let P to "hello"; Get P | String `hello` |
| Set O to Collection k; read O with Set | Object, Collection; `O Is k` is True |
| Read that Collection result without Set | Err 450 |
| Get Item with index 3 | String `item 3` |
| Method F with 2, 3 | Long 5 |
| Method F with "2", 3.6 | Long 6 |
| Method F with one argument / three arguments | Err 449 / 450 |
| Get F with 2, 3 | Err 450 |
| Method G with no arguments | IsMissing(a) True, b 7 |
| Method G with 1, 2 | IsMissing(a) False, b 2 |
| Method H with 1, 2, 3 / no arguments | String `count 3` / `count 0` |
| Method S as a statement with "s1" | Logs S s1, succeeds |
| Method S as a value with "s2" | Logs S s2; result Empty |
| Method Twice with Integer n holding 4 | Caller n becomes 8 |
| Unknown class member with Get or Method; empty name | Err 438 |
| Get x with combined call type 3 | Integer 5 |
| x with call type 0 or 16 | Err 438 |
| TypeName of Method F with 1, 1 | String `Long` |

Named or omitted argument slots and conversions of non-String names or call types other than
Integer/Long are not measured here. Neither are other call-type combinations, unavailable accessors,
private members, non-scalar fields, field Set/indexing or forms/controls; these remain explicit
implementation refusals. Collection object results remain refused by this slice; the issue measures class object results, not a Collection Item
object round trip.
