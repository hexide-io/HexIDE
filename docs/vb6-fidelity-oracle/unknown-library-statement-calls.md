## Unknown library statement calls (#867)

Provenance: hexide-io/HexIDE#867 reports that `VBA.Frobnicate 1` is a compile error in VB6.
The issue supplies no diagnostic number or exact compiler wording; neither is pinned here. HexIDE translates
this refusal to an untrappable error when the statement executes. At #867, its unsupported `VBA.CallByName`
statement likewise stopped rather than allowing `On Error Resume Next` to skip the missing effect. That refusal
recorded HexIDE's unsupported-feature policy, not a measurement of CallByName's VB6 result.

Since #2519, [the first CallByName slice](callbyname.md) runs Collection Add/Item and Err.Number calls.
Unimplemented member dispatch still stops; measured run-time errors such as Nothing's 5 are trappable.

