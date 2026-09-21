# Contributing to HexIDE

Thanks for your interest! HexIDE is a cross-platform recreation of the Visual Basic 6 IDE in C# /
Avalonia. Contributions — bug reports, fixes, features, translations — are welcome.

By participating you agree to abide by our [Code of Conduct](CODE_OF_CONDUCT.md).

> **HexIDE is changing quickly, and something you want to work on may already be in progress.** Before
> you start anything non-trivial, find or open its issue and comment to ask for it. A maintainer will
> assign it to you. See [Making a change](#making-a-change).

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
> Everything else builds and runs. To work on an add-in anyway, see
> [Add-ins without the signing key](#add-ins-without-the-signing-key).

That is all most changes need. Some areas need more, and
[Advanced setup](#advanced-setup) covers them: checking VB6's actual behaviour, the third-party language
servers the test suite drives, and reproducing CI's Linux failures on Windows.

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
  pack (`IDE/HexIDE/Localization/Packs/en.json`). **You do not need to translate it.** The maintainer adds
  the other languages when merging. On your PR, CI reports the missing translations as a warning that
  names the packs, and the warning does not fail the build. Ticking *Allow edits by maintainers* on
  the PR lets the translations go straight onto your branch. If you do speak one of the languages, a
  translation from you is welcome.
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

1. For anything non-trivial, find or open an issue first, so we can agree on the approach, and **ask to
   be assigned before you start**. A comment on the issue is enough. Contributors can't assign
   themselves on GitHub, but a maintainer can assign anyone who has commented. An issue that is already
   assigned is being worked on. If one has been assigned for a while with no visible activity, ask on
   it, because it may be free. If you set work aside, say so on the issue so someone else can pick it up.
2. Branch off the default branch; keep each PR focused and its commits tidy.
3. Make sure the build is green and the relevant tests pass locally. CI runs the IDE and LSP builds/tests.
4. Describe *what* and *why* in the PR. Screenshots help for any UI change.

## Advanced setup

None of this is needed to build, run or send a PR. Each part applies only to a certain kind of change, and
each one says which.

### The VB6 fidelity oracle — for interpreter behaviour

**When you need it:** you are changing what the interpreter computes, meaning a result type, an error
number, a rounding rule, a coercion or a `Format` edge. HexIDE decides those by running the real VB6
compiler, not by reading documentation. The documentation has been wrong often enough that a test's
expected value has to come from `vb6.exe`. [`docs/vb6-fidelity-oracle.md`](docs/vb6-fidelity-oracle.md)
records every behaviour measured so far, so check it first. Your question may already be answered there.

**What it needs:** Windows and **your own licensed copy of Visual Basic 6**, installed on the machine you
work on. HexIDE neither ships VB6 nor depends on it at run time. The oracle is a development check only.

- HexIDE finds `C:\Program Files (x86)\Microsoft Visual Studio\VB98\VB6.EXE` without any setup. Set
  `VB6_EXE` if yours is somewhere else.
- `scripts/vb6-oracle.ps1 -Local` runs the whole loop for you. It compiles a small program with
  `VB6.EXE /make`, runs it, and prints `value | TypeName` (or `ERR<n>`) for each expression you give it:

  ```powershell
  .\scripts\vb6-oracle.ps1 -Local -Expression 'CByte(100) + CByte(100)', 'CByte(200) + CByte(100)'
  ```

  Those two expressions make a good first run, because both results are already recorded
  (`200 | Byte`, then `ERR6`). If they come back as recorded, you can trust the setup. Without `-Local`
  the script runs inside a Hyper-V guest instead, which is how the maintainer runs it; the oracle doc
  explains that setup.
- **Record what you measure** in the oracle doc as part of your PR: the expression, the value and the
  type. Nobody maintains this behaviour any more, so that file is one of the main things this project
  produces.

**The serialization round-trip corpus** follows the same idea for `.frm`/`.vbp` files. The round-trip tests
look in VB6's own `VB98\Template` folder by default (`VB6_TEMPLATES` overrides it). Set
`HEXIDE_ROUNDTRIP_CORPUS` to a `;`-separated list of folders to test against other real VB6 projects you
have. Without either, the tests only see HexIDE's own `demo/` files and pass without proving anything.
**Do not commit any corpus.** Microsoft's templates cannot be redistributed, and your own projects are
yours. [`docs/TEST_PROJECTS.md`](docs/TEST_PROJECTS.md) explains the report the run writes.

### Third-party language servers — for LSP client changes

**When it matters:** you are changing `HexIDE.Lsp` or anything that talks to a language server. Part of
`HexIDE.Tests` drives language servers HexIDE did not write (rumdl, texlab, clangd, ruff and the VS Code
JSON server). A client tested only against its own server ends up agreeing with that server rather than
with the specification. [`docs/foreign-language-servers.md`](docs/foreign-language-servers.md) gives the
reasoning.

Usually there is **nothing to set up**. The first test run downloads each server at a pinned version,
checks its SHA-256 before running it, and caches it in the gitignored `artifacts/foreign-lsp/`.

- **Node on `PATH`** is needed for the three JSON-server tests. Without Node they skip and say so.
- To use your own build of a server, set `HEXIDE_MARKDOWN_LSP`, `HEXIDE_LATEX_LSP`, `HEXIDE_JSON_LSP`,
  `HEXIDE_CPP_LSP` or `HEXIDE_PYTHON_LSP` to its executable, or put it on `PATH`.
- `HEXIDE_FOREIGN_LSP_DOWNLOAD=0` works offline. The affected tests skip and report it.
- `HEXIDE_REQUIRE_FOREIGN_LSP=1` turns a skip into a failure, and CI sets it. Set it locally to be sure
  none of these tests was skipped.

The coverage table in `docs/lsp-client.md` is checked against the LSP specification's `metaModel.json`,
which is downloaded the same way into `artifacts/lsp-metamodel/`. `HEXIDE_LSP_METAMODEL` points the test
at a local copy.

### Reproducing CI's Linux failures on Windows

**When it matters:** you touch code that reads or writes `.vbp`/`.vbg`/`.frm` files, or a test that works
on your machine fails on CI. CI builds and tests on Ubuntu, and one kind of bug can only show up there.
VB6 files always use backslash paths, and on Linux `System.IO.Path` treats a backslash as an ordinary
character. Run the same suites under WSL, against the same working copy:

```sh
wsl -e bash -lc 'sudo apt-get update && sudo apt-get install -y dotnet-sdk-10.0'
wsl -e bash -lc 'cd /mnt/c/path/to/HexIDE/IDE && \
  dotnet test HexIDE.Runtime.Tests/ --artifacts-path /mnt/c/path/to/HexIDE/artifacts/linux'
```

`--artifacts-path` is required, and it must point to a folder inside the repository. Without it the Linux
build overwrites your Windows `bin/`/`obj/`. With a folder outside the repository, a test that locates the
repository root fails. Keep `apt-get update` in the first command. The SDK packages change most months,
and an out-of-date package list asks for files that have been removed, which fails with a wall of 404s.

WSL runs a newer Ubuntu than CI does. If CI fails and WSL does not, try CI's exact version (24.04) in a
container with [Podman](https://podman.io/). Podman is free, whereas Docker Desktop needs a paid licence in
larger organisations. Run this from PowerShell; in Git Bash, MSYS rewrites `/repo/IDE` into a Windows path:

```powershell
podman run --rm -v "C:\path\to\HexIDE:/repo" -v hexide-nuget:/root/.nuget -w /repo/IDE `
  mcr.microsoft.com/dotnet/sdk:10.0-noble `
  dotnet test HexIDE.Tests/ --artifacts-path /repo/artifacts/container
```

The container has no Node, so the three JSON-server tests skip there.

### Add-ins without the signing key

Only the maintainer can sign **first-party** add-ins. The signing key is a release secret, and a clone
cannot rebuild the signed packages. That does not stop you working on an add-in. A Debug build loads
**unsigned** add-ins when you enable two settings:

1. Start the IDE with `--developer-mode`. This only works in a Debug build.
2. Tick **Options → Developer → Load unsigned add-in packages**, then restart.

The add-in then loads and is marked as unsigned. This is the setting for testing your own changes. It is
not a way to ship an add-in. A **third-party** add-in is signed with its author's own key, and
[TRUST.md](TRUST.md) explains how users check who published one.

### Driving the IDE from an AI coding agent

A Debug build started with `--server-port 5123` runs an MCP server on localhost. It lets an agent open
forms, take screenshots, walk the control tree and click through dialogs. `.mcp.json` at the repository
root connects Claude Code to it automatically. A Release build does not include the server.
[`docs/command-line.md`](docs/command-line.md) lists every launch option, and
[`docs/mcp-server-gaps.md`](docs/mcp-server-gaps.md) lists what the server cannot do yet.

## Reporting bugs & security issues

- **Bugs / features:** open a GitHub issue with steps to reproduce and your OS + .NET SDK version.
- **Security vulnerabilities:** please **do not** open a public issue — follow [SECURITY.md](SECURITY.md).
- **Labels:** you don't need to pick any — a maintainer labels at triage. They're worth knowing for
  *finding* work, though: issues carry an area, a T-shirt size and sometimes a state such as `blocked`
  or `needs-oracle`. [`docs/issue-labels.md`](docs/issue-labels.md) says what each one means.
