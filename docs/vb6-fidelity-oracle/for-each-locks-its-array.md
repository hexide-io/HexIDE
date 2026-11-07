## For Each locks its array (#1460)

**Measurement supplied by #1460:** `vb6.exe /make`, en-GB, native and p-code identical. Each row uses
`On Error Resume Next` and `Exit For` immediately after the attempted mutation. The Long array starts with
`ReDim a(2)`; the Variant array starts with `ReDim w(1)`.

| Inside the loop | Err.Number | Bounds afterwards |
|---|---|---|
| `ReDim Preserve a(5)` inside `For Each x In a` | 10 | 0 To 2 |
| `ReDim a(6)` inside `For Each x In a` | 10 | 0 To 2 |
| `Erase w` inside `For Each x In w` | 10 | 0 To 1 |

Error 10 is "This array is fixed or temporarily locked". What happens to remaining elements after a refused
mutation was not measured: each probe leaves immediately.

**In HexIDE:** For Each increments the same `VBArray.LockCount` as an element passed ByRef (#1455), and a
`finally` decrements it on every way out. `ForEachArrayLockTests` pins all three rows, including Preserve
(now implemented by #1459), and checks that a later ReDim succeeds after completion, Exit For, GoTo out,
Exit Sub/Function and an error leaving the procedure. Those release checks express the lock's lifetime
requirement, without claiming further VB6 measurements. Stores into the array variable (`a = b`, or `v = 5`
for a Variant holding an array) and nested For Each over the same array remain unmeasured and unchanged,
beyond the counter naturally nesting.

