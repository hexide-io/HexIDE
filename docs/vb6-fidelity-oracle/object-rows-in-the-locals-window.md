## Object rows in the Locals window (2026-10-28, #2700)

**The question.** "What a Timer has at run time" and "Me in the Locals window" found object rows typed as two names with a
slash, such as `Object/Timer`. What does the Type column say for a class instance held as its class, as `Object` and in a
`Variant`; for `Nothing` in each; and for an array of objects? Asked for #2700.

**The probe.** VB6's IDE, SP6 (6.00.9782). A Standard EXE starting from `Sub Main` in Module1, which has no variables of
its own. Class1 has `Public Value As Long`, `Private mName As String` set to `"c1"` in `Class_Initialize`, and
`Public Property Get Name() As String` returning `mName`. `Sub Main` declares, in this order:

```vb
Dim objC As Class1
Dim objO As Object
Dim varO As Variant
Dim nilC As Class1
Dim nilO As Object
Dim nilV As Variant
Dim empV As Variant
Dim arrC(1 To 2) As Class1
Dim arrO(1 To 2) As Object
Dim colC As Collection
Dim colO As Object
```

It then sets `objC`, `objO` and `varO` each to a `New Class1`, with `objC.Value = 3`. It sets `nilV` to `Nothing` and
`arrC(1)` and `arrO(1)` to `objC`, and sets `colC` to a `New Collection` holding `objC`, with `colO` the same Collection.
Then it reaches `Stop`, with `empV` and the other `nil` variables never assigned. Paused there, the Locals window was
widened and `Module1`, `objC`, `varO`, `arrC`, `arrO` and `colC` expanded.

**What the window showed**, Expression, Value and Type exactly as written, in the window's order. A node with an
expander is marked "+", and an indented row is a child of the node above it.

| Expression | Value | Type |
|---|---|---|
| `Module1` + | | `Module1/Module1` |
| (one child, with no Expression) | `<No Variables>` | |
| `objC` + | | `Class1/Class1` |
| `mName`, under `objC` | `"c1"` | `String` |
| `Name`, under `objC` | `"c1"` | `String` |
| `Value`, under `objC` | `3` | `Long` |
| `objO` + | | `Object/Class1` |
| `varO` + | | `Variant/Object/Class1` |
| `mName`, `Name`, `Value`, under `varO` | `"c1"`, `"c1"`, `0` | `String`, `String`, `Long` |
| `nilC` | `Nothing` | `Class1` |
| `nilO` | `Nothing` | `Object` |
| `nilV` | `Nothing` | `Variant/Object` |
| `empV` | `Empty` | `Variant/Empty` |
| `arrC` + | | `Class1(1 to 2)` |
| `arrC(1)` +, under `arrC` | | `Class1/Class1` |
| `arrC(2)`, under `arrC` | `Nothing` | `Class1` |
| `arrO` + | | `Object(1 to 2)` |
| `arrO(1)` +, under `arrO` | | `Object/Class1` |
| `arrO(2)`, under `arrO` | `Nothing` | `Object` |
| `colC` + | | `Collection/Collection` |
| `Item 1` +, under `colC` | | `Variant/Object/Class1` |
| `colO` + | | `Object/Collection` |

So:

- **A live object is typed as its declared type, a slash, then its class**: `Class1/Class1`, `Object/Class1`,
  `Collection/Collection`, `Object/Collection`. A `Variant` holding one adds `Variant/` in front, as `Item n` does:
  `Variant/Object/Class1`.
- **`Nothing` is typed as the declared type alone**, with Value `Nothing` and no expander: `Class1` and `Object`. A
  `Variant` set to `Nothing` reads `Variant/Object`. An unassigned `Variant` reads `Empty`, typed `Variant/Empty`.
- **An array is typed as its element type and bounds**, as `Grid` was: `Class1(1 to 2)` and `Object(1 to 2)`. Each
  element follows the rules above, so `arrC(1)` is `Class1/Class1` and `arrC(2)` is `Nothing`, typed `Class1`.
- **A class instance expands to its variables and properties, sorted by name with case ignored**: the `Private`
  variable `mName`, the `Property Get` `Name` and the `Public` variable `Value`. A Collection expands to its `Item n`
  rows alone.
- **A procedure's own locals keep their declaration order**: `objC` to `colO` above are not sorted, unlike the members
  under `Me`. The procedure's module comes first, typed `Module1/Module1`, and with no variables it holds the single row
  `<No Variables>`.
