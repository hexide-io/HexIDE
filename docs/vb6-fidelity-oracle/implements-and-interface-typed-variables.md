## Implements, and interface-typed variables (2026-09-02, #186)

Measured with `scripts/vb6-oracle.ps1 -Classes`, added for this: the harness could only ever compile one
`.bas`, so **the entire object model was unmeasurable**. Implements, `As New`, `Set` type-enforcement,
default members and parameterized properties all need at least one class module and several need two.

| probe | measured |
|---|---|
| `Dim x As IFoo : Set x = New Bar : x.Area()` | **7** — dispatched to `IFoo_Area` |
| `TypeName(x)` where `x As IFoo` holds a `Bar` | **`Bar`** — the concrete class, not the interface |
| `TypeName(New Bar)` | `Bar` |
| `TypeOf b Is IFoo`, `b` implements it | **True** |
| `TypeOf b Is IFoo`, `b` does **not** | **False** — not an error |
| `IFoo_Draw` declared **Public** rather than Private | **accepted** — the Private convention is not enforced |
| `x.Own` where `x As IFoo` and `Own` is `Bar`'s own member | **compile error**, *"Method or data member not found"* |
| a class declaring `Implements IFoo` but omitting a member | **compile error**, see below |

### The two compile errors, verbatim

An interface-typed variable exposes **only the interface's members**. Reaching for the concrete class's own
member fails, in the module that does it:

```
Compile Error in File 'Module1.bas', Line 19 : Method or data member not found
```

And a class that claims an interface must supply all of it — reported against the *class*, naming both the
missing member and the interface:

```
Compile Error in File 'Partial.cls', Line 0 : Object module needs to implement 'Erase2' for interface 'IFoo'
```

That second message is the one HexIDE should reproduce. Per the approximation rule in `CLAUDE.md` and
`interpreter-core:40-42`, it is raised **when the class is first instantiated** rather than before the
program runs — the conformance data (both member tables) is already collected by `PrePass`, so the check
itself needs no binding. A class never instantiated is never checked, which is the accepted
error-on-a-path-never-taken divergence.

### What a class-typed slot refuses

A slot declared `As <class>` enforces that name on every `Set`. Measured with `On Error GoTo` (see the
harness note below), the error is the same in both directions and carries no interface-specific message:

| probe | measured |
|---|---|
| `Dim c As Bar : Set c = New Baz` (unrelated classes) | **Err 13**, `Type mismatch` |
| `Dim x As IFoo : Set x = New Baz` (`Baz` does not implement `IFoo`) | **Err 13**, `Type mismatch` |
| `Set v = New Bar` (`v As Variant`), then `Set x = v` (`x As IFoo`) | **accepted** |

The third row is the interesting one: routing a reference through a Variant does not launder it, and does not
break it either. The check reads the OBJECT, not the declared type of wherever the reference came from — a
Variant has no declared type to check against, and the `Set` into `x` still succeeds because `Bar` really does
implement `IFoo`.

### Harness note: `On Error Resume Next` does not trap inside a `-Declarations` function

Found while measuring the above, and it silently corrupts results, so it is worth stating plainly. A probe
function of the form

```vb
Function P()
    On Error Resume Next
    ' ...something that errors...
    P = "n=" & CStr(Err.Number)
End Function
```

returns **nothing**, and the error surfaces on the harness's own outer trap instead. A control probe that
merely does `Err.Raise 5` behaves the same way, so this is not specific to the error being measured. The same
function written with `On Error GoTo H` works correctly and returns the number and description.

**Use `On Error GoTo` in `-Declarations` probe functions.** `On Error Resume Next` remains fine in the
expression-level harness, which is where the rest of this document's error numbers came from. The cause has
not been established, so this is recorded as a measured harness behaviour rather than explained.

