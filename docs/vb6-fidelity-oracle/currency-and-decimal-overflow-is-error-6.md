## Currency and Decimal overflow is error 6 (2026-11-16, #434)

**The question.** #434 found that HexIDE throws a .NET `OverflowException` when a Double too large for Currency or
Decimal is stored in one, and that `On Error` can't catch it. What does vb6.exe raise, and what's left in the target?

**The probe.** VB6 SP6, `/make`, native code and p-code, which logged the same lines. Each row ran under
`On Error Resume Next` and logged `Err.Number`, then the target, before `Err.Clear`.

| Code | vb6.exe |
|---|---|
| `Dim c As Currency`, `c = 5`, then `c = 1E+30` | **6**, and `c` stays 5 |
| `c = 7`, then `c = CDbl(-1E+30)` | 6, and `c` stays 7 |
| `c = 900000000000000@`, then `c = c * 100` | 6, and `c` stays 900000000000000 |
| a Variant holding "kept", then `x = CCur(1E+30)` | 6, and `x` still holds the String "kept" |
| the same with `x = CDec(1E+30)` | 6, and `x` still holds "kept" |
| `x = CDec(1)`, then `x = x * 1E+30` | 6, and `x` stays Decimal 1 |
| `x = CCur(1)`, then `x = x * 1E+30` | **no error**: `x` is a Double, 1E+30 |
| `c = -1E+30` under `On Error GoTo` | the handler runs, with 6, "Overflow" |

**Readings:**
- **A value outside Currency's or Decimal's range is error 6, "Overflow", and it's trappable,** whether it comes from a
  store, a conversion function or arithmetic. The target keeps its old value.
- **Currency times a Double is a Double,** so it doesn't overflow at Currency's range.
