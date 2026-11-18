## What an Enum member's value and a Type's array bounds may name (2026-11-18, #4594, #4595, SP6)

**The question.** A module-level Const and a module-level array's bounds may name in-box constants, the module's own
Consts and other modules' Public ones (the oracle's "What a module-level array's bound may name"). Does an Enum member's
value follow the same rules? And can an array field inside a `Type` take its bounds from a Const or an Enum member, as
`Dim` can?

**The probe.** One project per row, each a `Sub Main` in `Module1` that writes its results to a file, under
`On Error Resume Next`. Rows that name another module add `Module2`, holding `Public Const PC As Long = 40` and
`Public PV As Long` unless the row says otherwise. Every project was built with `VB6.EXE /make` as native code and as
p-code, on the Win10 guest. **Native and p-code agreed on every row,** in what they compiled and in what they printed.
A compile error is quoted as `/make` wrote it to its log, without the file's path.

### An Enum member's value

| Member, in `Enum E` in `Module1` | Result |
|---|---|
| `M = VBA.vbBinaryCompare` | 0, a Long |
| `M = vbObjectError Or 127` | -2147221377, a Long |
| `M = vbRed` | 255, a Long |
| `M = VBRUN.vbRed` | 255, a Long |
| `M = VBA.vbRed` | **"Method or data member not found"**, a compile error |
| `M = K + 1`, with `Const K As Long = 5` above the Enum | 6, a Long |
| `M = K`, with that Const below the Enum | "Constant expression required" |
| `M = Module2.PC + 1` | 41 |
| `M = PC * 2`, bare | 80 |

For comparison, a module-level `Const C As Long = VBA.vbRed` is the same compile error, "Method or data member not
found".

**When the project also declares the name.** `M = vbRed` again, with a declaration of `vbRed` somewhere in the project:

| The project's `vbRed` | Result |
|---|---|
| `Public Const vbRed As Long = 3` in `Module2` | **3** |
| `Const vbRed As Long = 3` above the Enum | **3** |
| `Const vbRed As Long = 3` **below** the Enum | **"Constant expression required"** |
| `Dim vbRed As Long` above the Enum | "Constant expression required" |
| `Public vbRed As Long` in `Module2`, read as `vbRed + 0` | "Constant expression required" |

**What it shows:**
- **An Enum member's value follows a module-level Const's rules,** in every row the two share: in-box constants bare or
  qualified by their own library, Consts above it, and other modules' Public Consts bare or qualified.
- **A qualifier must name the library that defines the constant.** `vbRed` is VBRUN's: `VBRUN.vbRed` is 255 and
  `VBA.vbRed` doesn't compile, in an Enum and in a Const alike.
- **The project's name beats the library's, even where the project's can't be read.** The obvious guess for a Const
  `vbRed` below the Enum was that the forward reference would fall back to the library's 255. It doesn't: the name is
  the project's, it isn't a constant yet, and the Enum doesn't compile. A variable of that name is refused the same
  way.

#4594 also records `VBA.VbCompareMethod.vbDatabaseCompare` in an Enum member as 2, measured separately.

### An Enum member naming a library constant the project also declares (2026-11-18, #4762)

**The probe.**
- **Build:** vb6.exe 6.00.9782 with MSVBVM60 6.00.9848, `/make` native and p-code in session 0. The two builds gave identical results in all seven projects.
- **One project per row,** since a row may be a compile error.
- **`Module1`** holds `Enum E` with `M = …`, above every procedure. Its `Sub Main` logs `M` and `TypeName(M)`.
- **The logger** is in a module of its own.
- **The row's other declaration** sits where the row says.

| `M = …` | The project also has | VB6 |
|---|---|---|
| `VBA.vbBinaryCompare` | a module named `VBA` | the project won't load: "Name conflicts with existing module, project, or object library", with no file or line |
| `VBRUN.vbRed` | `Public Enum VBRUN` in `Module2` | "Name conflicts with existing module, project, or object library", `Module2` line 1 |
| `VbCompareMethod.vbDatabaseCompare` | a module named `VbCompareMethod` | "Method or data member not found", `Module1` line 2: the qualifier names the module |
| `vbRed` | `Sub vbRed()` in `Module1` | "Expected Function or variable" |
| `vbRed` | `Public Sub vbRed()` in `Module2` | "Expected Function or variable" |
| `vbRed` | `Public Declare Sub vbRed Lib "x" ()` in `Module2` | "Expected Function or variable" |
| `vbRed` | `Public Enum O` with `vbRed = 3` in `Module2` | **builds: `M` is 3, a Long** |

- **A project can't name a module or an Enum `VBA` or `VBRUN`:** those are library names. So an Enum member qualified by one always means the library.
- **A module may be named after a library Enum,** such as `VbCompareMethod`. A qualifier of that name then means the module, not the library's Enum.
- **The project's name hides the library's constant.** A Sub or a Declare of that name is "Expected Function or variable", in the Enum's own module or another. Another module's Enum member of that name gives that member's value.
- **The obvious guess was that a name the project declares as a Sub would be passed over,** so the library's 255 would be used. It isn't: the project's Sub is found and refused.

**Not measured:** `Sub vbRed` declared `Private` in another module; another Enum's member `vbRed` in the Enum's own module.

### A Type's array bounds

| Field, in `Type T` in `Module1` | `LBound`, `UBound` |
|---|---|
| `a(1 To N) As Long`, with `Const N = 3` above the Type | 1, 3, and `x.a(3) = 7` reads back 7 |
| `a(0 To MAXS) As Integer`, with `Enum S: MAXS = 4` above | 0, 4 |
| `a(N + 1) As Long` | 0, 4 |
| `a(1 To Module2.PC) As Long` | 1, 40 |
| `b(PC \ 10, 1 To 2) As Integer` | 0 to 4, then 1 to 2 |
| `g(PlayerGrid1 To PlayerGrid2) As Long`, members of `Module2`'s `Public Enum PG` (1 and 2) | 1, 2 |
| `g(Module2.PlayerGrid1 To PG.PlayerGrid2) As Long` | 1, 2 |
| `g(PlayerGrid1 To PlayerGrid2) As Long`, the Enum a class's `Public Enum` | 1, 2 |
| `a(1 To N) As Long`, with `Const N = 3` **below** the Type | "Constant expression required" |
| `a(1 To PV) As Long`, `PV` another module's Public variable | "Constant expression required" |

**In a class module.** `Class1` declares `Private Const N As Byte = 3`, `Private Enum CE` with `cMax = 5`, and then
`Private Type T` with `a(1 To N) As Long`, `b(0 To cMax) As Integer` and `c(1 To PC) As Long`, `PC` being `Module2`'s
`Public Const PC As Long = 40`. A module-level `Private this As T` and a method's local `Dim y As T` both use it:

| Read, in a method of `Class1` | Result |
|---|---|
| `LBound(this.a)`, `UBound(this.a)` | 1, 3 |
| `UBound(this.b)`, `UBound(this.c)` | 5, 40 |
| `UBound(y.a)`, the local | 3 |
| `this.a(3) = 7`, then `this.a(3)` | 7 |

The same class with its `Private Const N` below the `Private Type` is "Constant expression required".

**What it shows:** a Type's array bounds may name what a module-level `Dim`'s may, in a standard module and in a class,
and a Const below the Type is a forward reference, refused as it is for a `Dim`. In a class they read the class's own
Consts and Enums, which are the same for every instance.

### In HexIDE

At `86dcc6c50`, in `HexIDE.Runtime.Tests` probes:
- every Enum row is the compile error "Constant expression required (Enum E, member …)", the valid ones included. The
  pre-pass folds a member's value against Enum members only (#4594);
- every Type row is refused: "Nonliteral array bounds for field 'a' inside Type 'T' are not implemented (#178)" (#4595);
- a module-level Const reads `vbObjectError Or 127`, `VBA.vbBinaryCompare` and `VBRUN.vbRed` as VB6 does, and refuses
  `VBA.vbRed` as unread rather than giving VB6's compile error.
