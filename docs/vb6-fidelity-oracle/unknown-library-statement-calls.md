## Unknown library statement calls (#867)

Provenance: hexide-io/HexIDE#867 reports that `VBA.Frobnicate 1` is a compile error in VB6.
The issue supplies no diagnostic number or exact compiler wording; neither is pinned here. HexIDE translates
this refusal to an untrappable error when the statement executes. Its unsupported `VBA.CallByName` statement
likewise stops rather than allowing `On Error Resume Next` to skip the missing effect. That last refusal is
HexIDE's unsupported-feature policy, not a measurement of CallByName's VB6 result.

