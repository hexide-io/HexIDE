# A declared array named with empty parentheses

Provenance: hexide-io/HexIDE#2474, measurements supplied in the issue and
confirmed as this run's scope by the supervisor comment of 2026-10-28.
The issue records agreement between vb6.exe native and p-code.

With `Dim arr(2) As Long`, both `S arr()` and `Call S(arr())`, calling
`Sub S(x() As Long)`, pass the whole array ByRef. Inside S, `UBound(x)`
is 2; after `x(0) = x(0) + 1`, the caller's `arr(0)` is 1.

Passing `arr()` to a ByRef `x As Variant` reports `TypeName(x)` as
`Long()` and shares the array: adding 10 to `x(0)` leaves the caller's
element at 10. A ByVal Variant receives a copy, leaving the caller's element 0.

With `Dim arr(4) As Long`, `F(arr())`, where `F(x() As Long)` returns
`UBound(x)`, returns 4. With `Dim arr(3) As Long`, `UBound(arr())` is 3.
With the original upper bound 2, `w = arr()` into a Variant copies the array;
w has type `Long()` and upper bound 2.

A Variant v holding `Array(1, 2)` does not qualify as a declared array:
`V v()` is refused at compilation with "Expected Sub, Function, or Property".
HexIDE raises that compile error when the statement executes.

The issue also records a ParamArray case, but the supervisor explicitly
defers its implementation until after #2462 merges.
