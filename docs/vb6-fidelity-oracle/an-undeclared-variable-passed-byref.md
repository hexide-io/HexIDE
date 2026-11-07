## An undeclared variable passed ByRef (2026-10-23, #1505)

Measured on vb6.exe, native and p-code identical, under en-GB, with no `Option Explicit`. Each program
`ByRefUndeclaredTests` pins was made as written.

A name nothing claims, passed as an argument, is declared where it is passed, as a Let would declare it. A ByRef
parameter then writes into it.

| probe | result |
|---|---|
| `Bump a12` twice, with `Sub Bump(x)` doing `x = x + 1` | `a12` is an Integer 2 |
| `Fill b12`, with `Sub Fill(ByRef s)` doing `s = "filled"` | String "filled" |
| `Fill s:=c12`, the same by name | String "filled" |
| `r = BumpF(d12)`, a Function that adds 1 to `x` and returns `x * 10` | `d12` an Integer 1, `r` 10 |
| `Bump (f12)` | Empty: the parentheses pass a copy |
| `Keep h12`, with `Sub Keep(ByVal x)` doing `x = 9` | Empty |
| `Bump Seven`, with `Function Seven()` returning 7 | `Seven` is still 7: a Function's name is called, not declared |
| `BumpI i12` twice under `DefInt I`, with `Sub BumpI(x As Integer)` | an Integer 2 |
| `k.Bump e12` twice, a class's `Sub Bump(x)` | an Integer 2 |

**The obvious guess, and HexIDE had made it.** A read of an undeclared name is Empty, so an argument would be only a
value. It is not: VB6 declares the variable at the argument, so the procedure's write lands in it.

### In HexIDE (#1505)

`ExpressionExecutor.ResolveArg` passes a ByRef parameter the caller's slot when the argument is a variable
(`TryGetArgLocation`). A name never declared had no slot, because the read path deliberately declares nothing, so that
a watch expression cannot create a variable (#171). The argument was therefore passed as a copy.

The read now records the bare name it found unclaimed (`unclaimedRead`). When that is the argument being resolved,
`DeclareUnclaimed` declares it with `StatementExecutor.AllocImplicitVariable`, the same helper a store uses, so a DefType
still applies. A watch or Immediate-window evaluation still declares nothing: it suppresses implicit declaration, so the
read refuses before it records anything, and it refuses user procedure calls anyway.

