# Contributing to HexIDE

Thanks for your interest! HexIDE is a cross-platform recreation of the Visual Basic 6 IDE in C# /
Avalonia. Contributions — bug reports, fixes, features, translations — are welcome.

By participating you agree to abide by our [Code of Conduct](CODE_OF_CONDUCT.md).

## Licensing of contributions (read first)

HexIDE is **MIT-licensed throughout** — both halves of the monorepo. The VB6 language server runs as a
separate subprocess for crash isolation and a replaceable backend, not for any licensing reason:

| Path | License | Your contribution is licensed as |
|------|---------|----------------------------------|
| `IDE/**` | **MIT** | MIT |
| `LspServer/**` | **MIT** | MIT |

By submitting a change you certify that it's your own work (or compatibly licensed) and you license it
under **MIT**. A few hard rules:

- **Do not** copy Microsoft/VB6 **artwork, icons, fonts, or verbatim text** into the tree. HexIDE's
  guiding principle is *reproduce VB6's behaviour, not its assets* — icons are original vector geometries,
  error/UI strings are written clean-room. A PR that pastes Microsoft-derived content will be declined.
- **Do not** import GPL- or otherwise copyleft-licensed code anywhere into the tree — the whole
  repository is MIT and must stay that way. Some neighbouring projects in this space are copyleft-licensed;
  their code cannot come in, however convenient the fit. Keep it 100% permissive.

## Getting set up

Requires the **.NET 10 SDK** (no Java needed — the ANTLR tool is bundled). From the repo root:

```sh
cd IDE && dotnet build HexIDE.Desktop/HexIDE.Desktop.csproj   # build the IDE
cd IDE && dotnet run  --project HexIDE.Desktop/               # run it
```

Tests:

```sh
cd IDE       && dotnet test HexIDE.Runtime.Tests/        # VB6 interpreter
cd IDE       && dotnet test HexIDE.Tests/                # IDE view-models
cd IDE       && dotnet test HexIDE.Integration.Tests/    # headless Avalonia UI
cd LspServer && dotnet test HexIDE.VbLspServer.Tests/    # LSP server
```

> **First-party add-ins:** the Desktop build only signs/bundles the bundled add-ins when a first-party
> signing key is present; a normal clone has none, so the build simply skips that step and prints a note.
> Everything else builds and runs.

## Conventions

- **Architecture boundary — CST, not AST.** HexIDE's own in-process language tooling is *syntactic*: it
  parses to a concrete syntax tree and offers syntax diagnostics, structural symbols, keyword completion,
  and a tree-walking interpreter that demonstrates VB6 by **executing** it (runtime scopes + runtime types
  are fine — that's execution, not analysis). It does **not** build a semantic/bound AST or perform static
  semantic analysis — cross-file binding, type inference, semantic diagnostics beyond syntax, semantic
  rename, find-all-references, workspace symbols, or a refactoring engine. That work belongs to a full
  language engine behind the replaceable LSP/backend seam; PRs that build it into HexIDE itself will be
  redirected there.
- **`Nullable` is enabled and warnings are errors** across every project — don't introduce nullable or
  build warnings.
- **AXAML uses compiled bindings.** Set `x:DataType` on every view root and use `{CompiledBinding}`.
- **No literal colours in IDE chrome** — reference theme resource keys. (`Transparent` is the only
  exception.)
- **Any new user-facing string is a localization key**, never a hardcoded literal — add it to the English
  pack (`IDE/HexIDE/Localization/Packs/en.json`) and translate it into the shipped packs.
- **Tests** assert with **AwesomeAssertions** (`value.Should().Be(...)`) and mock with **NSubstitute** —
  not xUnit `Assert.*` and not Moq.

## Specs — how design is recorded

HexIDE keeps its design in [OpenSpec](https://github.com/Fission-AI/OpenSpec) format under `openspec/`:

- **`openspec/specs/<capability>/spec.md`** — how the system behaves *today*, written as requirements and
  scenarios in the present tense. This is the source of truth; read it to understand what something does and
  why it does it that way.
- **`openspec/changes/<change-id>/`** — work in flight: a `proposal.md` (why and what), an optional
  `design.md` (the technical approach and the roads not taken), a `tasks.md` checklist, and spec deltas.
- **`openspec/changes/archive/`** — completed changes, kept as the historical record of *why*.

There is no status field anywhere. Status is structural: in `specs/` it is built, in `changes/` it is in
flight, in `archive/` it is done.

**A proposal is welcome but not required.** If you want to think a design through in the open before writing
code, `openspec` gives you the scaffolding and we will engage with it. If you would rather just send a PR with
a clear description, that is equally fine — we would rather have the contribution than the ceremony.

**Keeping `specs/` true is the maintainer's job, not yours.** When a PR changes observable behaviour, the
maintainer updates the relevant spec as part of merging it. You are welcome to include that update yourself,
but nobody will block your PR for omitting it.

If you do use the tooling: `npm install -g @fission-ai/openspec`, then `openspec validate --specs --strict`.
Note that it reports anonymous usage stats by default — `openspec config set telemetry.enabled false` turns
that off. That setting is per-machine, so it does not travel with this repo; HexIDE itself makes no network
calls of any kind.

## Making a change

1. Open an issue first for anything non-trivial, so we can agree on the approach.
2. Branch off the default branch; keep each PR focused and its commits tidy.
3. Make sure the build is green and the relevant tests pass locally. CI runs the IDE and LSP builds/tests.
4. Describe *what* and *why* in the PR. Screenshots help for any UI change.

## Reporting bugs & security issues

- **Bugs / features:** open a GitHub issue with steps to reproduce and your OS + .NET SDK version.
- **Security vulnerabilities:** please **do not** open a public issue — follow [SECURITY.md](SECURITY.md).
