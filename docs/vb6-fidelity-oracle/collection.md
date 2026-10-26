## Collection (2026-10-22, #1471)

**The obvious guess is that a key which isn't a String raises 13, "Type mismatch". It does only when the key names no
item.**
- `c.Add x, 5` raises 457, "This key is already associated with an element of this collection", when the Collection
  holds five items or more. It raises 13 when there are fewer.
- A key that isn't a String is first looked up as `Item` would look up an index, and finding an item counts as a
  duplicate.
- This holds for every numeric type, a Date, a Variant holding a number, and through `Key:=` and a late-bound call.

**The second guess is that For Each walks a copy. It walks the Collection itself, with a cursor.**
- Remove the current item and the loop goes on to the next one.
- Add an item at the end and the loop reaches it. Add one before the current item and the loop doesn't.
- Point the variable at another Collection and the loop ends.

**The third: keys are not merely case-insensitive.** They compare as `StrComp(a, b, vbTextCompare)` compares, so
"straße" and "strasse" are one key, as are "ae" and "æ".

**How the first was found, because it nearly went down as something else.**
- The same statement, `d.Add "bad", 5, , 0`, gave 13 in one probe and 457 in a test's program.
- The first reading was that a duplicate-key error on the line before had left something behind. That was the
  difference between the two programs that was easiest to see.
- Taking the test's statements out one at a time disproved it. Only the statements that changed Count changed the
  answer.
- A matrix then gave the rule: the count of items, ten key values, two integer types, four key names, and where the
  keyed item sat. 457 came exactly when the key was from 1 to Count.

**The question.** HexIDE had no Collection. `Dim c As Collection` declared Nothing, and `New Collection` stopped the
run. #1471 asked for:
- `Add`, `Item`, `Remove` and `Count`;
- For Each and `!`;
- VB6's errors for each.

**The probe.**
- **82 projects**, each made native and p-code under en-GB. These were Sub Main programs logging each statement's
  outcome under `On Error Resume Next`, plus one-statement projects for the compile errors.
- **The 19 programs `CollectionTests` runs.** These were made the same way, as written. They logged exactly what the
  tests pin, and gave exactly the compile errors the tests expect.
- Native code and p-code agreed on every line but one, a one-line For Each over Nothing (below).

### Item, and what an index is

`c` holds "apple" keyed "a", then "pear".

| Expression | VB6 |
|---|---|
| `c(1)`, `c(2)`, `c("a")`, `c("A")` | apple, pear, apple, apple |
| `c.Item(1)`, `c.Item(Index:=2)`, `c!a`, `c![A]` | apple, pear, apple, apple |
| `c(0)`, `c(3)`, `c(-1)` | 9 |
| `c("zz")`, `c("2")` | **5**: a String is always a key, even one that reads as a number |
| `c(1.5)`, `c(2.5)` | pear, pear: half to even |
| `c(0.5)`, `c(Empty)`, `c(True)` | 9 |
| `c(Null)`, `c(2147483648#)` | 13 |
| `c(CByte(1))`, `c(CCur(2))`, `c(#1/1/1900#)` | apple, pear, pear |

### Keys

| `c.Add`, on the Collection above | VB6 |
|---|---|
| key "a" again; key "A" | 457 |
| key "1" | accepted, and distinct from position 1: `c(1)` is apple, `c("1")` the new item |
| key "" | accepted; `c("")` finds it |
| key "a " | accepted, distinct from "a" |
| key Null, key Empty, key `#1/2/2003#` | 13 |
| key "é", then key "É" | 457 |
| key "straße", then key "strasse" | 457 |

**Key equality is `StrComp(a, b, vbTextCompare) = 0`.** Each of 14 pairs was added as two keys and compared with
`StrComp`, and the two agreed on every pair. **The same:** a/A, ae/æ, AE/Æ, ss/ß, 1/¹, oe/œ, th/þ, and "a" & `Chr(0)`
against "a". **Different:** e/é, co-op/coop, a/ª, o/ö, `''`/`'`, and "x" & `Chr(160)` against "x ". #1479 has the
table beside .NET's ordinal and ICU compares, neither of which agrees.

### A key that isn't a String

At Count 3, three unkeyed items:

| Key | VB6 |
|---|---|
| 1, 3, 1.5, `CByte(2)`, `CCur(2)`, `CSng(2)`, `CDate(2)`, `CDec(2)`, a Variant holding 2, `Key:=2` | **457** |
| 4, 0, -1, 3.5, 0.5, True, False, 2147483648# | 13 |
| an array, a Collection | 13 |
| key 2 with Before and After both given | 5 |
| key 2 with Before 0, and with Before a missing key | 457 |
| key 9 with Before 0, and with Before a missing key | 13 |
| `o.Add "bad", 2` late-bound; `o.Add "bad", 9` | 457; 13 |
| key 1 or 0 on an empty Collection | 13 |

The matrix ran 0 to 8 unkeyed items around one keyed item, with keys "kk", "a", "zz" and "x", the keyed item first or
last, and Integer and Long keys 0 to 9. In every one of its 108 rows, key `k` gave 457 exactly when 1 ≤ `k` ≤ Count,
and 13 otherwise.

### Add's place, and the order it checks things in

`d` holds "x", "y".

| Statement | VB6 |
|---|---|
| `d.Add "b0", , 1`; `d.Add "a9", , , 3`; `d.Add "bk", "kk", 2`; `d.Add "ak", , "kk"`; `d.Add "ak2", , , "kk"` | order b0 ak bk ak2 x y a9 |
| Before 0, Before Count+1, After 0, After Count+1 | 9 |
| Before and After both | 5 |
| Before or After a key that isn't there | 5 |
| Before 1.5 | accepted, rounded half to even |
| Before Null; Before Empty | 13; 9 |
| After Count | accepted, at the end |
| `d.Add Item:="n1", Key:="nk"`; `d.Add "n2", Before:=1`; `d.Add After:=2, Item:="n3"` | as positionally |

**The order is:**
1. Before and After both given (5).
2. The key: a non-String key gives 13 or 457 as above, and a duplicate key 457.
3. The position: 9, or 5 for a missing key.

| Statement, on a Collection of two | VB6 |
|---|---|
| a duplicate key with Before 0, and with After 0 | 457 |
| key 5 with Before 0, and with After 0 | 13 |
| a duplicate key, or key 5, with Before and After | 5 |
| key Null with Before a missing key | 13 |
| a duplicate key with Before a missing key | 457 |
| Before 0 and After 0; Before a missing key and After 0 | 5 |

### Remove

| Statement | VB6 |
|---|---|
| `Remove 1`; `Remove "nk"`; `Remove Index:=1`; `Remove "KK"` for key "kk" | removed |
| `Remove 1.5` | removes item 2 |
| `Remove "nk"` again; `Remove ""` | 5 |
| `Remove 0`, `Remove Count+1`, `Remove Empty`, `Remove True` | 9 |
| `Remove Null`, `Remove 2147483648#` | 13 |

### The object itself

- `TypeName` is "Collection", `VarType` 9, and `IsObject` True.
- `TypeOf c Is Collection`, `Is Object` and `Is VBA.Collection` are all True. A Class1 instance `Is Collection` is
  False.
- `New VBA.Collection`, `Call c.Add(…)`, `c.Add (5)`, and `.Add`, `.Count`, `.Item` and `!nine` inside `With c` all work.
- `Set e = d` shares the Collection: `e.Add` shows in `d.Count`, and `Set d = Nothing` leaves `e` holding it.
- An item is a copy of what was added. A String changed after `Add` is unchanged in the Collection, and so is an array
  (its item's `TypeName` is "Variant()").
- A Collection can hold itself: `c(c.Count) Is c` is True.
- Passed ByRef or ByVal, the callee adds to the caller's Collection either way. `Set c = New Collection` in the callee
  replaces the caller's only when it was passed ByRef.
- Items keep their types: Integer, Byte (`VarType` 17), String, Single, Null, Empty and Date.

**What it refuses, measured:**
- **Assigning an item**, whether `c(1) = "x"`, `Set c(1) = New Collection`, `c!k = "y"` or `c.Item(1) = 5`, is 424, and
  the item is unchanged. That is for an item that is there: the index is looked up first (#1492, below).
- **Through Nothing**, `e.Count`, `e(1)` and `e.Add 1` are 91.
- **`c.Foo`** compiles and raises 438.
- **`v = c` and `v = New Collection`** are 450, and so is `v = o` with `o` an Object holding one.
- **`"x" & c` and `c + 1`** don't compile: "Argument not optional".
- **`c = 5`**, with `c As Collection`, doesn't compile: "Argument not optional".
- **`o = 5`**, with `o As Object` holding a Collection, is 438, and `o` still holds the Collection.
- **`w = 5`**, with `w` a Variant holding a Collection, simply makes `w` an Integer.

**The compile errors, through a variable declared As Collection:**

| Statement | VB6's compile error |
|---|---|
| `c.Add`; `v = c.Item`; `c.Remove` | Argument not optional |
| `c.Add 1, , , , 5`; `v = c(1, 2)`; `v = c.Count(1)`; `c.Remove 1, 2` | Wrong number of arguments or invalid property assignment |
| `c.Add 1, Kay:="a"` | Named argument not found |
| `c.Count = 5` | Function call on left-hand side of assignment must return Variant or Object |
| `v = c!1` | Syntax error |
| `For Each y In c`, `y As Long`; the same with `y As String` | For Each control variable must be Variant or Object |

**The same mistakes late-bound**, through an Object or a Variant, are run-time errors:

| Statement | VB6 |
|---|---|
| `o.Add`; `o.Add 1, , , , 5`; `o.Add Key:="b"`, no Item | 450 |
| `o.Item`; `o.Remove`; `o.Remove 1, 2`; `o()`; `o(1, 2)` | 450 |
| `o.Add 1, Item:=2`; `o.Add 1, Kay:="z"` | 448 |
| `o.Count(1)` | **451**, "Property let procedure not defined and property get procedure did not return an object" |
| `o.Foo` | 438 |

A Variant holding one gives the same: `w.Add 1, Kay:="z"` is 448, and `w.Add` 450.

### An empty Collection, and assigning an item (#1492)

**An index into an empty Collection is 5, whatever it is.** A position, a key, Null and Empty are all 5 there, where a
Collection with items gives 9, 5, 13 and 9. This was found while measuring #435, after the tables above had pinned 9 for
a position without ever asking about an empty Collection.

`e` is an empty Collection:

| Statement | VB6 |
|---|---|
| `v = e(1)`, `e(0)`, `e(2)`, `e(-1)`, `e("k")`, `e(Null)`, `e(Empty)`, `e.Item(Index:=1)` | 5 |
| `e.Remove 1`, `e.Remove 0`, `e.Remove "k"` | 5 |
| `e.Add "x", , 1`, `e.Add "x", , 0`, `e.Add "x", , , 1`, `e.Add "x", , "k"` | 5 |
| `e.Add "x", 1`, a key that isn't a String | 13, as the key rule above gives |
| `v = o(1)` and `o.Remove 1`, late-bound | 5 |
| `v = c(1)`, after `c.Remove 1` emptied it | 5 |

**Assigning an item looks the index up first.** So the 424 above holds only for an item that is there, and late-bound
the forms differ. `c` holds "a" keyed "k", then "b"; `o` is an Object and `w` a Variant, each holding `c`.

| Statement | VB6 |
|---|---|
| `c(1) = 5`, `c("k") = 5`, `c!k = 5`, `c.Item(1) = 5`, `Set c(1) = Nothing` | 424 |
| `c(3) = 5`, `c(0) = 5`, `c.Item(3) = 5`, `Set c(3) = Nothing` | 9 |
| `c("zz") = 5`, `c!zz = 5` | 5 |
| `e(1) = 5`, `e("k") = 5`, `e!k = 5`, `Set e(1) = Nothing`, on an empty `e` | 5 |
| `o(1) = 5`, `o.Item(1) = 5`, `o!k = 5`, `w!k = 5`, `w.Item(1) = 5` | **451** |
| `o(3) = 5`, `w.Item(3) = 5`; `w!zz = 5` | 9; 5 |
| `w(1) = 5`, `w(3) = 5`, `Set w(1) = Nothing`, `Set o(1) = Nothing` | **438**, with no look at the index |

The item is unchanged in every row.

**What resisted explanation:** why late-bound `Set o(1) = x` and `w(1) = x` skip the look, when `o(1) = x` makes it.

**In HexIDE (#1492):** `VbCollection.Position` gives 5 first in an empty Collection. `ItemAssigned` makes the look and
chooses 424, 451 or 438 by the form and by whether the reference is early-bound.

### For Each

Each loop prints its variable, starting from a Collection holding "a", "b", "c" unless the row says otherwise.

| Loop | VB6 prints |
|---|---|
| plain | a b c, and afterwards `x` is Empty |
| `f.Remove 1` while on "a" | a **b** c: the loop goes on to what now follows the current place |
| `f.Remove 2` while on "b", over b c d | b **d**: the removed item is never reached |
| `f.Add "z"` on the first pass, over b d | b d **z**: an item added at the end is reached |
| `f.Add "first", , 1` on the first pass, over b d z | b d z: one added before is not |
| `f.Remove 1` twice while on "a", over a b c d | a **c** d |
| `f.Remove 1` three times while on "b" | a b, then the loop ends, with Count 0 |
| `Exit For` on "b" | `x` keeps "b" |
| a loop over `f` inside a loop over `f` | each keeps its own place: aa ab ac … |
| `Set f = New Collection: f.Add "new"` while on "a", over a b | **a**, then the loop ends; `f(1)` is "new" |
| over an empty Collection, `x` "unchanged" | nothing, and afterwards `x` is **Empty** |
| `x = x * 10` in the body, over 1 2 | the items are unchanged |

- **When a loop finishes, a Variant variable is left Empty and an object variable Nothing.**
- **An object loop variable over an item that isn't an object** raises 424. Under Resume Next, over "s" then 2, the body
  runs once with the variable Nothing, and the second item's 424 ends the loop. A Collection-typed loop variable does
  the same.
- **A class-typed loop variable** compiles. A Long or String one is "For Each control variable must be Variant or
  Object".

**Over Nothing**, For Each raises 424, which a handler sees as 424. Under Resume Next:
- the body runs once, with `Err` 424 and the variable unchanged;
- then `Next` raises 92, "For loop not initialized";
- and the variable is left Empty.

That is the same through `w As Collection` and `w As Object`, written on one line or on several, with or without a
body. **The one exception is p-code's alone: `For Each v In w: Next`, a one-line loop with an empty body.** There p-code
raises 424, skips the `Next`, runs anything after it on the line, and leaves `v` unchanged. Native code gives 92 and
Empty, as for every other form (#1486).

The complete #1486 table, measured with `w As Collection` and again with `w As Object`, with identical
results. `w` is Nothing, `v` starts as `"unchanged"`, and `o` is declared `As Object`. Each loop runs under
`On Error Resume Next`, followed on a new line by logging `Err.Number` and `TypeName(v)`; `L` logs its argument.

| Loop | Native | P-code / IDE (HexIDE target) |
|---|---|---|
| `For Each v In w: Next` | 92, v Empty | 424, v String |
| `For Each v In w: Next: L "after"` | after runs; 92, v Empty | after runs; 424, v String |
| `For Each v In w: L "body": Next` | body once; 92, v Empty | body once; 92, v Empty |
| `For Each v In w` / `Next` | 92, v Empty | 92, v Empty |
| `For Each v In w` / `L "body"` / `Next` | body once; 92, v Empty | body once; 92, v Empty |
| `For Each o In w: L "body": Next` | body once; 92, v String | body once; 92, v String |

`ForEachTests.Nothing_ResumeNext_MeasuredTable` pins every row for both declarations (#1486).

### An object held by a Collection

`Class1` prints "terminate" and its Name when it ends.

- **An item is released when its last reference goes:**
  - on `Remove`, by position or key;
  - on `Set c = Nothing`;
  - on `Set c = New Collection`, the old items first to last;
  - when a procedure's local Collection goes out of scope;
  - when a Function's returned Collection is discarded at the end of the statement, as in `MakeC.Add 5`;
  - when the object holding the Collection in a field terminates (its own `Class_Terminate` first, then the items);
  - on `Erase` of an array of Collections.
- **Removing an item another variable still holds** terminates nothing. It ends when that variable lets go.
- **An object added twice** terminates on its second removal, not its first.
- **For Each's variable holds the current item.** Removed inside the loop, the item terminates as the loop moves past
  it. After `Exit For`, removing it terminates nothing until the variable is cleared with `v = Empty`.
- **A Collection-typed variable refuses a class instance**, 13, and a Class1 variable refuses a Collection, 13. The
  variable stays Nothing, and the refused `New` object terminates at the end of the statement.
- `IsObject(c(1))` is True and `TypeName(c(1))` is "Class1". `c(1).Hello`, `c("one").Name = "uno"` and `c!one.Name`
  reach the object.

### A Let into a Variant from a temporary object (#1485)

This is general, not the Collection's own, but it was measured here. A Let into a Variant from a temporary object
leaves the Variant **Empty** when the object's default value can't be fetched. The temporary may be `New X`, or the
object a Function returns. From a variable, the Variant keeps its value.

| Statement, `v` holding "-" | Error | `v` after |
|---|---|---|
| `v = New Collection`; `v = MakeC()`; `v = MakeV()` | 450 | **Empty** |
| `v = c`; `v = o`; `v = w`, each holding a Collection | 450 | "-" |
| `v = New Class1`; `v = MakeK()`, Class1 having no default member | 438 | **Empty** |
| `v = k`, a Class1 variable | 438 | "-" |
| `v = 5`, then `v = New Collection` | 450 | **Empty** |
| `arr(0) = New Collection`, `arr` an array of Variant | 450 | **Empty** |
| `s = MakeV()`, `s As String` | 450 | "-" |

### `As New`, for #435

`Dim d As New Collection`; `Set e = d`; `e.Add` ten items; `Set d = Nothing`. Then `d.Count` is 0, a new Collection made
on the next use of `d`, and `e.Count` is still 10.

### What this overturned

- **That a key which isn't a String is always 13.** That was HexIDE's first model, built from a probe on a Collection
  of two. At five items or more the same statement is 457.
- **That the 457 followed the error before it.** It was the first reading of the transcript that contradicted the
  probe. Taking statements out one at a time showed the earlier error was irrelevant, and that Count decided.
- **That `Debug.Print c` could be measured in a made program.** Debug.Print is compiled out of an exe, so the row
  measured nothing and was dropped.

### What resisted explanation

- **Why `Add` looks a non-String key up as a position at all.** Sharing `Item`'s lookup would explain it, but that is
  a guess at the implementation. What is recorded is the rule the 108 matrix rows and the edges agree on.
- **Why p-code alone skips the `Next` of a one-line empty For Each.**

**VB6's own texts**, where HexIDE's are reworded under `Exceptions.cs`'s policy:
- 457 is "This key is already associated with an element of this collection";
- 451 is "Property let procedure not defined and property get procedure did not return an object".

**Every run-time error above gave `Err.Source` "Probe"**, the project's name (#1483).

**Not measured:**
- what VB6's Locals window shows for a Collection;
- `c.[_NewEnum]` called directly;
- a Collection's keys under a locale other than en-GB.

### In HexIDE (#1471)

**The object.**
- `VbCollection` is an in-box object reached through the proxy path, as `Err` and `App` are.
- It carries the reference count that class instances carry. Its items go through the same `AddRef` and `ReleaseRef`
  at every store, and a `New Collection` or a Function's returned Collection is held by the statement until it is
  stored, as a class instance is.
- `TypeName`, `TypeOf`, a Collection-typed variable's refusal of a class instance, and the 450 a Collection gives as a
  value all follow the tables above.

**The calls.**
- `Add`, `Item`, `Remove` and `Count` take positional and named arguments, through the `INamedParameterSource` hook
  that #1474 can use for other objects.
- Too few or too many arguments raise VB6's compile errors as the statement runs.
- Reached through an Object or a Variant, those compile errors become VB6's late-bound 448, 450 and 451.
  `VbCollection.IsEarlyBound` decides which, from the name the reference was read through.

**Keys.** These compare as `OrdinalIgnoreCase`, which Split and Filter's text compare also uses (#1479). That is wrong
on 7 of the 14 pairs above: "straße" and "strasse" are two keys in HexIDE and one in VB6. The difference can only give
a missing 457 or a failed lookup (5), never another item's value. A non-String key is looked up as a position first,
as measured.

**For Each and `!`.**
- For Each keeps a cursor on the Collection. `Add` and `Remove` move it as VB6's moves.
- `c!key`, `c![a key]` and `!key` inside `With` are the default member with the key as a String. `!` on any other
  object still stops the run.

**The debugger.** Locals and Watch open a Collection to "Item 1", "Item 2" and so on, each its own node, so an object
item opens to its fields. That is HexIDE's choice: VB6's Locals window was not measured.

**Not yet:**
- `Dim c As New Collection` (#435);
- an array `As Collection` or `As Object` (#1482);
- `Err.Source` (#1483);
- the emptied Variant (#1485);
- the one-line For Each (#1486);
- `c.[_NewEnum]` called directly.

**`CollectionTests`.** It pins seven programs and twelve compile errors, each made with vb6.exe as written.

