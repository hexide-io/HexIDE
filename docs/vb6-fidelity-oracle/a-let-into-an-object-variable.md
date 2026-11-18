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


### An unset Function of an object type returns Nothing (2026-11-18, #4622, SP6)

**The probe.** A standard module in a Sub Main project held one Function per type, each with an empty body. It
logged each one's `TypeName`, `VarType` and `Is Nothing`. The project also held a form, Form2, and a class, Class1, and
referenced nothing beyond VB's own libraries. p-code and native code logged the same lines. The run was in session 0.

| `Function F() As …` | `TypeName(F())` | `VarType(F())` | `F() Is Nothing` |
|---|---|---|---|
| `Form2`, the project's form | Nothing | 9 | True |
| `Form`, and `VB.Form` | Nothing | 9 | True |
| `Control` | Nothing | 9 | True |
| `TextBox`, `VB.TextBox`, and `PictureBox` | Nothing | 9 | True |
| `Object` | Nothing | 9 | True |
| `Collection` | Nothing | 9 | True |
| `Class1`, the project's class | Nothing | 9 | True |
| `Variant` | Empty | 0 | (not asked) |

**A second run** added the OLE Automation reference, `stdole2.tlb`, to reach `OLE_COLOR` and `StdFont`:

| Declaration | `TypeName` | `VarType` | value, or `Is Nothing` |
|---|---|---|---|
| `Function F() As OLE_COLOR`, unset | Long | 3 | 0 |
| `Function F() As stdole.OLE_COLOR`, unset | Long | 3 | 0 |
| `Dim c As OLE_COLOR` | Long | 3 | 0 |
| `Function F() As StdFont`, unset | Nothing | 9 | True |
| `Dim f As StdFont` | Nothing | | True |

- **Every object type starts at Nothing,** whether VB's own, stdole's, a form of the project, or a class. Only the
  Variant starts Empty.
- **`OLE_COLOR` is a Long,** in a Function's result as in a `Dim`: it starts at 0.
- **HexIDE's own reading, found with it:** an unset Function `As Form2` or `As Form` read TypeName "Variant", where
  `As Class1` and `As Object` read "Nothing" (#4622).

**Not measured:** a referenced library's own class, a Property Get, and a Function returning an array of these.

## A default member, by kind and by target (2026-11-18, #4694)

**The question.** The section above found that `d = 11` calls the default `Property Let`. Real code reaches the same
Let through fields, `With`, ByRef parameters, a Function's own name and array elements, and a default member can
also be Get-only, a field, or a Function. What does each do?

**The probe.**
- **Build:** VB6 SP6, `/make`, native code and p-code, which logged every row identically.
- **The classes:**
  - `D`: a default `Property Get/Let Value` As Variant, whose Let logs `Let <x>`;
  - `DG`: a Get-only default;
  - `DF`: a default Public field `Value As Variant`, by `VB_VarUserMemId = 0`;
  - `DFn`: a default Function;
  - `DL`: a default Get/Let As Long;
  - `DT`: like `D`, and logs its Terminate;
  - `H`: `Public d As D`.
- **The two compile errors** were each built as a project of their own.

| Target and statement | VB6 |
|---|---|
| `Dim d As D`, holding a `D`: `d = 11` | `Let 11`; `d` still the `D`, `d.Value` 11 |
| `Dim o As Object`, holding a `D`: `o = 12` | `Let 12`, `o` still the `D` |
| `d = 13` with `keep` also holding it | `Let 13`; `d Is keep` True |
| `Dim g As DG`: `g = 14` | compile error **"Invalid use of property"** |
| `Dim f As DF`: `f = 15` | the field takes 15; `f` still the `DF` |
| `Dim fn As DFn`: `fn = 16` | compile error **"Type mismatch"** |
| `Dim l As DL`: `l = "17"`; `l = "abc"` | `l.Value` Long 17; 13 |
| a class field `h.d = 18`; `With h`: `.d = 19` | `Let 18`, `h.d` still the `D`; `Let 19` |
| a ByRef parameter `x As D`: `x = 20` | `Let 20`, on the caller's object |
| inside `Function GetD() As D`, after `Set GetD = New D`: `GetD = 21` | `Let 21`; the Function returns the `D` |
| inside `Function GetO() As Object`, the same: `GetO = 27` | `Let 27` |
| `Dim arr(1 To 2) As D`, holding a `D`: `arr(1) = 22` | `Let 22`; `arr(1)` still the `D` |
| `Dim n As D`, holding Nothing: `n = 23` | 91 |
| `Dim v As Variant`, holding a `D`: `v = 24` | `v` is Integer 24, the object dropped |
| `Dim og As Object`, holding a `DG`: `og = 25` | **451** |
| `Dim o As Object`, holding a `DF`: `o = 28`; holding a `DFn`: `o = 29` | the field takes 28; **450** |
| `Dim t As DT`: `t = 26`, then `Set t = Nothing` | `Let 26`, and Terminate at the `Set`, not at the Let |

The probe's `TypeName(d)` printed `d`, not `D`. VB6 recases an identifier project-wide to its last-typed spelling,
and the variable `d` shares the class's name. That's the probe's naming, not a finding.

**Readings:**
- **Every object-typed target hands the value to the default member, and keeps its object:** a variable, a field, a
  `With` member, a ByRef parameter, a Function's own name and an array element alike. Only a Variant takes the value.
- **A default Property Let converts the value** as a call converts its argument: `"17"` into a Long Let is 17, and
  `"abc"` is 13.
- **A default field takes the value.**
- **A default that can't take a Let** is a compile error through a class-typed variable, and a run-time error
  through `As Object`:
  - Get-only: "Invalid use of property", or 451;
  - a Function: "Type mismatch", or 450.
- **The Let doesn't release the object.** Terminate runs when the last reference goes, as before.

**Not measured:**
- a Sub as the default;
- a typed (non-Variant) field as the default;
- a UDT's object field;
- an element of an object array holding a class with no default member.
