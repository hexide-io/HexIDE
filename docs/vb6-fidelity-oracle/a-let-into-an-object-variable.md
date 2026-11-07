## A Let into an object variable (2026-10-23, #1493)

**The obvious guess is that `k = 5`, with `k As C1` holding an instance, replaces the object with 5. It assigns the
default member of the object `k` holds.** A class with none raises 438, and `k` keeps the object. Holding Nothing,
the same Let raises 91. A Variant is different: it takes the value and drops the object.

**The question.** HexIDE released the object at `k = 5`, so its Terminate ran there, and stored 5. The program ran on
with the object gone, and `k.Name` on the next line failed to compile. #1493 asked which targets are object variables
for a Let, and what each does.

**The probe.**
- Two Sub Main programs, made native and p-code under en-GB.
- The 3 programs `LetIntoObjectTests` runs, made the same way. They logged exactly what the tests pin.

Native code and p-code agreed on every line. `C1` logs its Initialize and Terminate.

### Object variables

| Target, holding an instance of `C1`, which has no default member | VB6 |
|---|---|
| `Dim k As C1`: `k = 5`, and `k = "x"` | 438 both times; `k` still the instance |
| `Dim o As Object`: `o = 5` | 438; `o` still the instance |
| `Dim an As New C1`, untouched: `an = 5` | the instance is made, then 438 (#435) |
| a ByRef parameter `x As C1`: `x = 6` | 438; the caller's variable still the instance |
| a ByRef or ByVal parameter `x As Object`, and a ByVal `x As C1` | 438 |
| `Function F() As C1`, holding an instance: `F = 7` | 438; the Function returns the instance |
| `Function FObject() As Object`, the same | 438; it returns the instance |
| a class's `Public o As C1`: `h.o = 8`, and `.o = 9` in `With h` | 438; the field still the instance |
| a class's `Private mo As C1`, from inside: `mo = 5` | 438 |

| Target, holding Nothing | VB6 |
|---|---|
| `Dim n As C1`: `n = 5` | 91; `n` still Nothing |
| `Dim o2 As Object`: `o2 = 5` | 91 |

### Variants

| Target, holding an instance | VB6 |
|---|---|
| `Dim v As Variant`: `v = 5` | no error. The instance terminates at the Let, and `v` is 5, an Integer |
| an untyped `u`, and a class's `Public v As Variant` | the same |
| a ByRef `x As Variant`, and a ByVal untyped `x` | the same |
| `Function FVariant() As Variant` and an untyped Function, holding an instance | the same, and the Function returns the Integer |

### A default member

`d = 11`, with `d` holding an instance of a class whose `Property Get Value` carries `Attribute Value.VB_UserMemId = 0`,
calls its `Property Let Value` with 11. There is no error, and `d.Value` is 11 afterwards.

### Aside: a variable that hides a procedure

The first probe declared a local `r As C1` in Sub Main while the module had a Sub `R` that logs. Every `R "label"` then
named the variable. It compiled, and under `On Error Resume Next` it raised and logged nothing. A `Dim L As Long` in
the same position is a compile error ("Expected Sub, Function, or Property", #1491's probes). So an object variable
standing where a procedure call should be compiles, as a late call of its default member, and a Long does not.

### What this overturned

- **That a Let into an object variable replaces what it holds.** Only a Variant's does. Any declared object variable
  hands the value to its object's default member, and keeps the object whatever happens.
- **That `As Object` is a Variant for this purpose.** It reads late, as a Variant does (#1005), but a Let into it is
  438, as into a class-typed variable.

### In HexIDE (#1493)

**What counts as an object variable.** A Let target is one when it is any of:
- declared as a class, `As Object` or `As Collection`;
- an `As Object` parameter (`ExecutionEnvironment.ObjectParameters`, new);
- any other slot that holds an object and isn't a Variant's.

**What a Let into one does.** It refuses before anything is released or stored:
- 438 for a class with no default member, and for a Collection;
- 91 for Nothing;
- a refusal naming #174 for a class with a default member;
- a refusal naming #790 for a control.

**Where it was hard to tell a Variant apart.** An untyped or `As Variant` Function's own name takes the value. Its
slot has no Variant mark, so the executor is told (`VariantReturn`). `ProcedureInfo.ReturnsObject` and
`ParamInfo.DeclaredObject` now separate `As Object` from Variant, where `ReturnsVariant` and `ReadsAsVariant` had
counted it as one for reading.

**Not covered:**
- a Type's field declared as a class;
- an element of an object array (#1482).

**`LetIntoObjectTests`** pins three programs and the default-member refusal.

**#169 found one more.** An object Function's return name started Empty before any Set. So `F = 5` in `F() As Object`
took the value where vb6.exe raises 91, and an unset `As Object` or `As Collection` Function returned Empty. It starts
Nothing now, and `LetIntoObjectTests` pins a fourth program.

