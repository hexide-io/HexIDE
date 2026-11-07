## `Class_Terminate` lifecycle (interpreter-advanced Phase 4.2)

Verified with a `Thing` class logging `Class_Initialize`/`Class_Terminate` to a file (via a module `Log` sub),
driven through `Set`/scope-exit/escape scenarios. **VB6 `Class_Terminate` is true last-reference-drop
(reference-counted)** — it fires exactly when the final reference to an instance goes away, never before:

| Scenario | `vb6.exe` result |
|---|---|
| `Set x = New T` then `Set x = Nothing` (sole owner) | `Terminate` fires **synchronously** at the `Set … = Nothing` statement |
| `Set y = New T` when `y` already holds an object | the **new** instance's `Initialize` fires **before** the old instance's `Terminate`; the old terminates at the reassignment (its last ref dropped) |
| Two+ locals holding objects, at `End Sub`/`End Function` | terminate in **declaration order** (`Dim a,b,c` → `Term a`, `Term b`, `Term c` — *not* reverse/LIFO; declaration order == assignment order in the probe) |
| Function returns a local object (`Set F = t`) | the local does **NOT** terminate at the function's scope exit — the return value holds a reference; it terminates when the **receiver** is later cleared |
| Local stored into a module-global (factory: `Set gThing = t`) | the local does **NOT** terminate at scope exit — the module global holds it; terminates when the global is cleared |
| Shared local (`Set b = a`) then `Set a = Nothing` | `Terminate` does **NOT** fire — `b` still references it; it fires when the **last** holder drops (here, `b` at `End Sub`) |
| `Set gLast = Nothing` (drops the last *external* ref) **inside a running method** | `Terminate` fires **only after the method returns**, not mid-method — a running method's **`Me` is a counted reference** |
| `New T` passed straight into a call (`Consume New T`), never stored — **`ByVal` and `ByRef`** | the temporary terminates **right after the call statement returns** — the temporary is a counted reference for the statement's duration |
| `With New T … End With` | terminates at **`End With`** — the `With` target is a counted reference for the block |

Every one of these is exactly what **reference counting** predicts (a reference is counted wherever it is held —
named storage, a call parameter, `Me` during a call, a `With` target, a statement temporary — and `Terminate`
fires when the count reaches zero). Phase 4.2 therefore implements **real slot-based reference counting** (runtime
execution machinery — in bounds; the walls are CST-only / no-compile-stage / no-extended-language-surface, none of which
refcounting touches), **not** a best-effort scan. Cycles leak (never reach zero) — faithful to VB6. The only
documented divergence is interface-pointer-granularity temporaries beyond call arguments (rare).

**Design consequence (recorded for Phase 4.2):** a best-effort scheme that fires `Terminate` on *every*
`Set … = Nothing` or scope-exit **without** tracking whether another reference survives would **wrongly**
terminate live objects in the return-escape, module-var-factory, and shared-ref cases — all common patterns, and
a false-fire (running `Terminate` then continuing to use the object) is worse than not firing. Faithful behaviour
needs the last-reference test — either a persistent refcount or a point-in-time reference scan. (Cycles leak in
VB6 too — never collecting them is itself faithful.)

