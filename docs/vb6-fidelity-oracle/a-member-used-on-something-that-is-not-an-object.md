## A member used on something that is not an object (2026-10-27, #257)

**The question.** A run-time error in HexIDE for `v.Foo`, with `v` an Empty Variant, names a builtin the program never
called and carries no number. What does vb6.exe raise when a member is called, read or assigned on a Variant holding
Empty, Null, a number, a string or an array? And how does that differ from Nothing, and from an object that lacks the
member?

**The probe.** One form's program, each statement under `On Error Resume Next`, logging `Err.Number` and
`Err.Description`. Three more projects each held one construct that might not compile. Native and p-code agreed on
every row.

| Receiver, and what is done with it | vb6.exe |
|---|---|
| An Empty Variant: `v.Foo`, `v.Foo = 1`, `x = v.Foo`, `x = v.Foo(1)`, `Call v.Foo`, `Set x = v.Foo`, `x = v.Count` | **424 "Object required"**, each |
| A Variant holding Null: `v.Foo`, `x = v.Foo` | 424 |
| A Variant holding 5: `v.Foo`, `x = v.Foo` | 424 |
| A Variant holding "s": `x = v.Len` | 424 |
| A Variant holding `Array(1, 2)`: `x = v.Count` | 424 |
| A Variant holding Nothing: `v.Foo`, `x = v.Foo` | **91** "Object variable or With block variable not set" |
| `Dim o As Object`, Nothing: `o.Foo` | 91 |
| `Dim c As Collection`, Nothing: `x = c.Count`, `x = c.Foo` | 91 |
| A Collection, in a Variant or `As Collection`: `x = c.Foo`, `c.Foo = 1`, `v.Foo` | **438** "Object doesn't support this property or method" |
| The form, through an Object variable: `x = o.NoSuch`, `o.NoSuch = 1` | 438 |
| `Dim n As Integer`: `n.Foo` | does not compile: **"Invalid qualifier"** |
| `Dim s As String`: `x = s.Len` | does not compile: "Invalid qualifier" |

So three errors, by what the receiver holds:
- **424** when it holds no object at all, whatever the value: Empty, Null, a number, a string or an array.
- **91** when it is an object reference that is Nothing.
- **438** when it is an object without that member.

The call's form does not change the number: a read, an assignment, a call, a `Set` and an argument list are the same.
A variable declared with a non-object type is refused before the program runs.

**`x = c.Foo` on a variable declared `As Collection` compiles.** It raises 438 at run time, or 91 while `c` is Nothing.
