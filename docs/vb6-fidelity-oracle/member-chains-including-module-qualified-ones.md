## Member chains, including module-qualified ones (2026-09-02, #173)

| probe | measured |
|---|---|
| `p.In1.Z` (nested UDT field, unqualified) | `99` |
| `Module1.n` (module-qualified scalar) | `5` |
| `Module1.p.X` (qualified, then a field) | `7` |
| `Module1.p.In1.Z` (qualified, then **two** fields — four levels) | `42` |

No depth limit and no special case: a module qualifier is simply the first step of an ordinary chain, and
everything after it resolves exactly as it would unqualified. `Module1.p.In1.Z` is `Module1` → `p` → `In1`
→ `Z`, evaluated left to right.

Worth stating because HexIDE refused the qualified form with *"Multi-level qualified member access is not
supported"* while already folding the unqualified `p.In1.Z` correctly — the two were treated as different
problems when only the first step differs in kind (a namespace rather than a value).

