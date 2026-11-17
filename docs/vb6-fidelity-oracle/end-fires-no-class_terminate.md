## `End` fires no `Class_Terminate` (interpreter-debugger reset)

Verified: a module-level object held live, then `End` — the class's `Class_Terminate` does **not** run (output is
`BEFORE-END` only; no `TERMINATE-FIRED`). VB6 `End` terminates abruptly and invokes **no** `Terminate` (nor
`Unload`/`QueryUnload`). So the debugger's reset ("Stop") must unwind the walk **without** firing `Class_Terminate`
— the interpreter suppresses it via an aborting-guard on `FireTerminate` while `IDebugGate.IsAborting` is set.

### `End` itself, from three places (2026-10-12, #775)

The section above measured one object at module level, and stated the form events without measuring them. Three
probes now cover both. Each is a Standard EXE with a `Class1` whose `Class_Terminate` logs, and forms whose
`QueryUnload`, `Unload` and `Terminate` log. Every log line is written by opening, appending and closing the file,
so nothing waits in a buffer that `End` could discard. Real `vb6.exe` 6.00.8176 (the original release), run in session 0.

| Probe | Log |
|---|---|
| `Form1_Load` creates objects at module and form level and shows `Form2`; a 300 ms Timer creates a local object, logs `tick`, then `End`, then logs `after End` | `Form1_Load`, `Form2_Load`, `tick` |
| `End` inside `Form1_Load`, with a module-level and a local object live, then logs `after End` | `Form1_Load` |
| `Sub Main` creates a module-level and a local object, shows `Form1`, then `End`, then logs `after End` | `Main`, `Form1_Load` |

In every case the exe exited with code 0, and **nothing else was logged**:
- no line after `End`;
- no `QueryUnload`, `Unload` or `Terminate` from either form;
- no `Class_Terminate` for the module-level, the form-level or the local object.

So `End` is the same wherever it is reached, and it is not an unload. HexIDE's `End` (#775) stops the program at
the statement, suppresses `Class_Terminate` through its own `Ending` flag (which also works with no debugger
attached), and in the IDE ends the run as the End command does, reporting nothing.

### When a Sub Main program returns: the order objects terminate in (2026-11-16, #1490)

**The question.** `End` fires no Terminate, as above. A program that ends by returning from Sub Main terminates what it still
holds (#1490). In what order?

**The probe.** A Sub Main program ends by returning. Each object of class `C1` appends `term <name>` from its `Class_Terminate`, and Form1's `Form_Terminate` does the same. Setup makes them in this order, then returns:

| Made | Object | Held by |
|---|---|---|
| 1 | `Module1.x` | a local, then stored in Module1's `x` only after #8 |
| 2 | `Module1.b` | Module1's `b`, declared second |
| 3 | `Module1.a` | Module1's `a`, declared first |
| 4 | `ModB.qb` | ModB's private variable |
| 5 | `ModA.pa` | ModA's **Public** variable, set from Module1 |
| 6 | `ModA.qa` | ModA's private variable |
| 7 | `ModB.pb` | ModB's Public variable, set from Module1 |
| 8 | `Static s` | a Static in a Module1 Sub |
| then | Form1 | `Load Form1`, then `Unload Form1`, so the default instance stays |

**The order it ended in:** Static s (8), ModB.pb (7), ModA.qa (6), ModA.pa (5), ModB.qb (4), Module1.a (3), Module1.b (2), Module1.x (1), and then Form1.

**The rule: objects terminate in reverse order of creation, and the form's default instance after all of them.**
- **Neither module nor the project's listing matters.** The run was repeated with ModA and ModB swapped in the `.vbp`, with the same order.
- **Neither declaration order nor kind of variable matters:** module-level, Public, private or Static.
- **What isn't a candidate rule:** reverse order of assignment into the variables. `Module1.x` was made first but stored last, and it terminated last.

Built native and p-code, each with the modules in both listing orders. All four runs ended identically.
