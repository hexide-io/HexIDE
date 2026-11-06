## Module scope for Types and Enums (2026-09-03)

Issue #180 is a cross-module question, and until now the legality harness compiled **one** module — so the
thing the issue is written about was unmeasurable, and the only alternative was to guess. The harness now
takes a list of extra modules, written as `Module2.bas`, `Module3.bas` beside `Module1` and named in a
per-case `.vbp`. A list rather than one extra, because the decisive case needs three: a user and two
exporters, so nothing local can disambiguate.

### The base rule is the one already written for procedures

| probe | verdict |
|---|---|
| two modules, both `Private Type Point` | legal — unrelated types |
| two modules, both `Public Type Point` | legal |
| local `Private` beside a foreign `Public` | legal — local wins |
| a foreign `Private` enum's members, bare | **illegal** — *Variable not defined* |
| a foreign `Private` Type, bare or qualified | **illegal** — *User-defined type not defined* |
| two foreign `Public` Types, nothing local | **illegal** — *Ambiguous name detected* |

Own module first at any visibility, then other modules' `Public` only, 2+ is ambiguous. That is
`TryResolveProcedure` verbatim — so #180's own diagnosis was right: the algorithm was written and two
declaration kinds were not using it. **Two `Private`s in different modules do NOT clash**, which is why a
collision check that ignored visibility would have refused a legal program.

### Then they diverge, and the difference is not guessable

| type position | module-qualified | project-qualified | project + module |
|---|---|---|---|
| **UDT** `Point` | **legal** | legal | legal |
| **Enum** `MyPublicEnum` | **illegal** | legal | **illegal** |

Value position accepts everything, to four levels: `Project1.Module1.MyEnum.Foo` and a bare `Project1.Foo`
both compile.

**The reading that fits all of it: a UDT's type identity is MODULE-scoped, an Enum's is PROJECT-scoped.**
So `Module.Point` names a real thing and `Module.MyEnum` does not — the module is no part of an enum's
identity *as a type*. Its **members** are still hoisted into the module's namespace, which is why the
module qualifies them as *values* though not as a type.

That also explains a result recorded earlier without an account of it: two modules may each own a `Public
Type` of one name (each belongs to its module — ambiguity only at an unqualified use), and may **not** both
export a `Public Enum` of one name (they collide in the single project namespace — refused at the
declaration, reported at line 0 with no use involved). One rule, two consequences. *The mechanism is
inferred; the twelve verdicts it accounts for are not.*

### Disambiguation, and what a prefix does not buy

| probe | verdict |
|---|---|
| `Dim p As Module2.Point`, two exporters | legal — the prefix resolves it |
| `Module2.EKind.kA`, two exporting the same `Public Enum` | **illegal** — the prefix does not help |
| `Module2.kA`, a member colliding across two enums | legal |
| `Dim p As Module2.Point` beside a **local** `Point` | legal, and reads the FOREIGN member |
| a foreign `Private` Type, qualified | **illegal** |

So the module prefix is a genuine **override**, not a tie-break — and it never defeats `Private`.

### Two harness lessons, each of which produced a wrong answer first

- **`Option Explicit` is mandatory in the probing module.** Without it an unresolvable name is an
  implicitly-declared Variant and the module compiles, so *"not found"* and *"found unambiguously"* are
  indistinguishable. The first run reported a `Private` enum's members as visible across modules — a
  confident wrong fact, of the same family as the duplicate `Sub Main` incident recorded above.
- **A probe that calls `TypeName(p)` on a UDT fails on an unrelated rule** — a UDT cannot be passed to a
  late-bound function — and so measures its own mistake. Reading a field keeps the case about scoping.

