## The Forms collection (2026-10-28, #2496)

VB6's global `Forms` holds the program's loaded forms. Measured on vb6.exe SP6, three projects built native and p-code,
which log the same lines in every one:
- **main** starts in Sub Main, with four forms. It loads and shows them, takes them through the three idioms programs use,
  and tries the index's edges. Run in session 0 and again in the console session, where a shown form reads `Visible`
  True. The two runs agree apart from that.
- **close-skipme** and **close-all** start in Form1, which shows Form2 and loads Form3, then unloads itself from a
  Timer. Its `Form_Unload` walks `Forms`, unloading each form other than itself in the first and every form in the second.

Every form logs Initialize, Load, QueryUnload, Unload and Terminate, with `Forms.Count`, each form's `Caption` in the
collection's order, and whether the form itself is among them. A form a loop is on is named by `Is` against each form's
name, which neither loads nor makes anything.

### What it holds, and in what order

| After | `Forms`, in order |
|---|---|
| nothing, in Sub Main | 0, empty |
| `Load Form2`, `Load Form1`, `Form3.Show` | Form2, Form1, Form3 |
| `Form4.pub = 1`, which makes Form4's instance and doesn't load it | unchanged |
| `Form1.Show`, `Form1.Hide`, `Form2.ZOrder` | unchanged |
| `Unload Form2` | Form1, Form3 |
| `Load Form2` again | Form1, Form3, **Form2**, at the end |
| `Set g = New Form2` | unchanged: made, not loaded |
| `Load g`, `g.Caption = "Form2b"` | Form1, Form3, Form2, Form2b |

- **Forms holds the loaded forms, in the order they loaded.** Showing, hiding and ZOrder don't move a form. An unload
  takes it out, and a later load puts it at the end. A hidden form is held as a shown one is.
- **An instance that isn't loaded isn't held**, whether it was made by a `Public` variable's use or by `New`. A second
  instance of a form is held beside the first, by both `Name`s "Form2".
- **A form is in `Forms` from its own Load to the end of its own Unload.** During `Form_Initialize` it isn't. During
  `Form_Load` it is, at the end, and `Forms.Count` counts it. During `Form_QueryUnload` and `Form_Unload` it still is.
  It goes once its Unload has returned.
- **A cancelled unload leaves the form held,** with no error to the `Unload` statement.

### The index, and what else it answers

| Read | Answer |
|---|---|
| `TypeName(Forms)` | `Object` |
| `TypeName(Forms.Count)` | `Integer` |
| `TypeName(Forms(0))` | the form's own class, `Form1` |
| `Forms(Forms.Count)`, `Forms(-1)` | 9 |
| `Forms(1.5)`, `Forms(2.5)` | both the form at 2: converted as CInt converts |
| `Forms("1")` | the form at 1 |
| `Forms("Form1")`, `Forms("")`, `Forms!Form1` | **13**. There are no keys: a String that isn't a number is a type mismatch, not a name |
| `Forms(Null)` | 13 |
| `Forms(70000)` | 6 |
| `Forms.Item(0)` | the form at 0, as `Forms(0)` |
| `Set o = Forms` | allowed: `TypeName(o)` is `Object`, and `o(0)` and `o.Count` answer as through `Forms` |
| `o.Add "x"`, late-bound | **424** |
| `o.Remove 0`, late-bound | 438 |
| `v = Forms`, without `Set`, into a Variant | **450**, and `v` stays Empty |

`o.Add` raised 424, Object required, where `o.Remove` raised 438. Recorded, not explained.

### The three idioms

Each starts from Form1, Form2 and Form3 loaded in that order.

**`For i = 0 To Forms.Count - 1: Unload Forms(i): Next`** unloads every other form and then fails. `i` 0 unloaded Form1,
which moved Form2 to 0. `i` 1 unloaded Form3. `i` 2 raised **9**, and **Form2 was left loaded**. The bounds are taken once,
as any For's are, while the collection shrinks under them.

**`For i = Forms.Count - 1 To 0 Step -1`** unloads all three, last first.

**`For Each f In Forms: Unload f: Next`** unloads all three, in order, with no error. After it, `f Is Nothing` is True. An
`Exit For` leaves `f` holding the form it reached.

**For Each walks the forms held when it began:**
- Form3, unloaded during the first pass, was **still handed out** on the third. `f` then held the unloaded form. This is
  not what a form's `Controls` does: its For Each passes over a control unloaded ahead of it.
- Form4, loaded during the first pass, wasn't reached.
- When Form2 cancelled its QueryUnload, the loop went on to Form3 with no error, and left Form2 loaded.

**The close-down idiom.** Form1's `Form_Unload` walking `Forms` visited Form1, Form2 and Form3 in order. Form2 and Form3
each raised QueryUnload (`UnloadMode` 1) and Unload, and each was in `Forms` during them. Form1 was still in `Forms`
throughout its own Unload. **An `Unload f` that reached Form1 itself did nothing:** no event, no error, the form still
held. So the loop with `If Not f Is Me` and the loop without it do the same. Once the Unload returned, `Forms` was empty
and the run ended, raising the Terminates.

### The Terminates at the run's end

A follow-up probe, because main's Terminates came in an order the oracle's "Several forms" says they shouldn't. The
project lists Form3, Form1, Form4 and Form2, and each form logs its `pub` in Terminate: the default instances hold 1 to
4, and `g`, a second Form2 held by a module's Public variable, holds 22.

| Made in the order | Terminates at the run's end |
|---|---|
| Form4, Form2, Form1, Form3, loaded and then unloaded, then `g`, loaded and unloaded | Form3, Form1, Form4, **Form2 22**, Form2 2 |
| `g` first, then Form4, Form2, Form1, Form3, instances only, never loaded | Form3, Form1, Form4, **Form2 2**, Form2 22 |

**The Terminates go form by form, in the order the project lists the forms.** Within one form, the instance made last
terminates first, in both rows. The order the forms were made in doesn't matter. In main, made in the order Form2, Form1,
Form3, Form4 and listed as Form1 to Form4, they terminated Form1, Form2, Form2, Form3, Form4.

**This corrects "Several forms" (#822),** which recorded the Terminates as coming "in the order the forms were first
created". Its probes had two forms each, made in the project's order, so the two rules gave the same answer there.
HexIDE follows the old row (#2497).

**Not measured:** an MDIForm and its children in `Forms`; a form loaded by another's `Form_Load` while a For Each runs, as
distinct from one loaded in the loop's body; `Forms` in the IDE (`/runexit`).
