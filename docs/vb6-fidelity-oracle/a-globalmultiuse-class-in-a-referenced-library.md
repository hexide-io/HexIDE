<<<<<<< HEAD
# A GlobalMultiUse class in a referenced library

Provenance: the measurement comment on [#1107](https://github.com/hexide-io/HexIDE/issues/1107),
reported by hexide-int and accepted by the supervisor on 2026-10-26. The comment describes vb6.exe SP6
runs of Sub Main EXEs referencing two GlobalMultiUse ActiveX DLLs, compiled both natively and as p-code.
It points to PR #2125 for the probe sources and logs; those are not present in this checkout. This entry
records the reported observations, not a new measurement by this implementation run.

- Referencing a library alone creates no instance. Each GlobalMultiUse class initializes on its own first
  member call, including when another class in that library has already been used.
- Calls share one instance per class throughout the run. A library-qualified call shares that instance
  with bare calls, and state survives between calls.
- Explicit `New` creates a separate instance with its own Initialize and Terminate.
- The implicit instances terminate after Sub Main's final statement, when Main returns. `End` suppresses
  all Terminate hooks.
- Within one library, implicit instances terminate in reverse creation order. Across libraries native
  and p-code gave different orders; this implementation's tests deliberately assert no such order.
- A project procedure wins over a bare library member and creates no library instance. Otherwise the
  first library in `.vbp` Reference order wins, without an ambiguity error or creation of the loser.
- A library's GlobalMultiUse `Trim` hides VBA's bare `Trim`. Explicit `VBA.Trim` still invokes VBA and
  `HexLibA.Trim` invokes the library.

The supervisor's implementation contract uses managed `ICSharpProxy` factories, with `New` and
`CreateObject` calling those factories. This is a host integration decision, not an additional VB6
measurement. Unknown libraries and COM hosting remain outside #1107.
=======
## A GlobalMultiUse class in a referenced library (2026-10-26, #1107)

Measured with vb6.exe SP6. Two ActiveX DLL projects were built with `/make`, which registers them:
- **`HexLibA`** has two GlobalMultiUse classes. `GlobalA` has `Bump`, which counts calls on its instance, `Who`,
  which returns `"A"`, `Trim`, which returns `"libA-trim"`, and a `Tag` property naming its instance. `GlobalA2` has
  `Ping`.
- **`HexLibB`** has one GlobalMultiUse class, `GlobalB`, whose `Who` returns `"B"`.

Every class logs its `Class_Initialize` and `Class_Terminate`, numbering its instances. Each client is a `Sub Main`
EXE that references the libraries through `Reference=` lines, built native and p-code. The two builds gave the same
log line for line, except for the terminate order noted below. GlobalMultiUse is the class header's
`VB_GlobalNameSpace = True` with `VB_Creatable = True`.

### When the implicit instance is made, and how long it lives

| Client | Log |
|---|---|
| Calls `Bump()` twice, reads `Tag`, makes `Set o = New GlobalA` and calls `o.Bump()`, releases `o`, calls `Bump()` again and `Ping()`, then `HexLibA.Bump()` | `main start` / `A init #1` / `bump1 1` / `bump2 2 tag A#1` / `A init #2` / `explicit 1 tag A#2` / `A term #2` / `after explicit released` / `bump3 3 tag A#1` / `A2 init` / `ping pong` / `qualified 4` / `main end` / `A2 term` / `A term #1` |
| References the library and calls nothing in it | `main start` / `main end`: no instance at all |
| Calls `Bump()` and `Ping()`, then `End` | `main start` / `A init #1` / `bump1 1` / `A2 init` / `ping pong` / `before End`: no terminate |

- **Made at first use, one per class.** The implicit instance is created at the first call of one of its own class's
  members, not when the program starts, and not when another class in the same library is used: `A2 init` waited for
  `Ping`.
- **One instance for the whole run.** It keeps its state between calls (`Bump` counted 1, 2, 3, 4), and a
  library-qualified call, `HexLibA.Bump()`, reaches the same instance.
- **An explicit `New` is a separate instance** (`A#2`, its own count from 1). It terminates when released, and the
  implicit one carries on.
- **It terminates when `Sub Main` has returned**, after the program's last line, and not at all after `End`. That
  matches `End` for a project's own objects ([End fires no Class_Terminate](end-fires-no-class_terminate.md)).
- **Terminate order:** within one library, the reverse of creation in both builds (`A2` before `A`). Across two
  libraries it differed by build: native terminated `A` then `B`, in creation order, while p-code terminated `B` then
  `A`. Measured, but there's no rule here I'd defend. Don't pin an order across libraries.

### Which library a bare name comes from

| Client | Log |
|---|---|
| References A then B, calls `Who()` | `A init #1` / `who A` / `A term #1` |
| References B then A, calls `Who()` | `B init` / `who B` / `B term` |
| References A then B, and has its own `Public Function Who()` returning `"own"`; calls `Who()`, then `HexLibA.Who()` and `HexLibB.Who()` | `who own` / `A init #1` / `B init` / `qualified A A B B` / then both terminate |
| References A, calls `Trim(" x ")`, `HexLibA.Trim(" x ")` and `VBA.Trim(" x ")` | `A init #1` / `trim [libA-trim]` / `qualified [libA-trim]` / `vba [x]` / `A term #1` |

- **The first `Reference=` line wins.** A bare name both libraries have comes from the one listed earlier in the
  project, with no compile error for the ambiguity. Only that library's instance is made.
- **The project's own procedure wins over every library**, and no library instance is made for it.
- **A library member hides a VBA function of the same name.** `Trim` reached `HexLibA`'s, not VBA's. Neither client
  project listed VBA in a `Reference=` line, since a `.vbp` never does. The qualifier reaches each one: `HexLibA.Trim`
  and `VBA.Trim`.

The DLLs were unregistered afterwards (`regsvr32 /u`), and their type libraries confirmed gone from the guest's
registry.
>>>>>>> upstream/main
