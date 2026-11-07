## `IsMissing`, and the shape of an omitted argument (2026-09-01)

`IsMissing` only means anything inside a procedure that has an `Optional` parameter, so this needed a probe
with its own procedures rather than the expression harness. No modal is involved, so it still ran headless
through `/make`.

| call | `IsMissing` | value |
|---|---|---|
| `Probe(1)` where `Optional v` | **True** | |
| `Probe(1, 2)` where `Optional v` | False | 2 |
| `Probe(1)` where `Optional v = 5` | **False** | 5 |
| `Probe(1)` where `Optional n As Integer` | **False** | 0 |
| `Probe(1, , 3)` where `Optional b, Optional c` | b **True**, c False | |
| `Probe(1)` where `a` is required | False | |

**An omitted argument is a Variant of subtype `vbError`** — `TypeName` = **`"Error"`**, `VarType` = **10**,
`IsEmpty` = False, `IsNull` = False, `IsError` = **True**. It is emphatically *not* `Empty`, and treating
it as Empty is what makes `IsMissing` impossible to express — the two are separate subtypes and VB6 tests
them separately.

**Two guesses the measurements overturned**, both of which would have shipped wrong:

- An `Optional n As Integer` is never missing. There is nowhere in an `Integer` to put a
  `vbError` Variant, so an omitted `Optional n As Integer` simply gets `0`.
- An `Optional` with a **default** is never missing either — the default supplies it, so `IsMissing` is
  False and the value is the default.

So `IsMissing` is True for an `Optional` Variant, untyped or explicitly `As Variant`, without a default,
left out by the caller (or left blank in the middle of the list — the blank-argument row ties this to #135,
where a blank argument keeps its position).

**Correction from [#866](https://github.com/hexide-io/HexIDE/issues/866):** the original account generalised
from `As Integer` to every declared type. The issue records these probes on vb6.exe, native and in the IDE;
`F` returns `IsMissing(o) & "|" & TypeName(o) & "|" & VarType(o)`:

| Declaration | Call | Result |
|---|---|---|
| `Optional o As Variant` | `F()` | `True\|Error\|10` |
| `Optional o As Variant` | `F(Empty)` | `False\|Empty\|0` |
| `Optional o` | `F()` | `True\|Error\|10` |
| `Optional o As Variant = 5` | `F()` | `False\|Integer\|2` |
| `Optional o As Integer` | `F()` | `False\|Integer\|2` |

An explicit Variant can hold Missing; its `As Variant` clause does not supply Empty. A default still supplies
its value instead.

**Measured but not implemented:** using a missing value in a string concatenation raises **Error 13, Type
mismatch**. Recorded so the next person does not have to re-derive it; HexIDE currently lets such a value
flow rather than raising.

