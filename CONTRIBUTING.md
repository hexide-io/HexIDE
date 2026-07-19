# Contributing to HexIDE

Thanks for your interest! HexIDE is a cross-platform recreation of the Visual Basic 6 IDE in C# /
Avalonia. Contributions — bug reports, fixes, features, translations — are welcome.

By participating you agree to abide by our [Code of Conduct](CODE_OF_CONDUCT.md).

## Licensing of contributions (read first)

HexIDE is a **monorepo with two independently-licensed halves**, and the split is deliberate — a process
boundary keeps GPL out of the MIT code:

| Path | License | Your contribution is licensed as |
|------|---------|----------------------------------|
| `IDE/**` | **MIT** | MIT |
| `LspServer/**` | **GPLv3** | GPLv3 |

By submitting a change you certify that it's your own work (or compatibly licensed) and you license it
under the license of the half it touches. A few hard rules:

- **Do not** copy Microsoft/VB6 **artwork, icons, fonts, or verbatim text** into the tree. HexIDE's
  guiding principle is *reproduce VB6's behaviour, not its assets* — icons are original vector geometries,
  error/UI strings are written clean-room. A PR that pastes Microsoft-derived content will be declined.
- **Do not** move GPL-licensed code (e.g. anything derived from the Rubberduck grammar in `LspServer/`)
  into the MIT `IDE/` half. Keep the boundary intact.

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

- **`Nullable` is enabled and warnings are errors** across every project — don't introduce nullable or
  build warnings.
- **AXAML uses compiled bindings.** Set `x:DataType` on every view root and use `{CompiledBinding}`.
- **No literal colours in IDE chrome** — reference theme resource keys. (`Transparent` is the only
  exception.)
- **Any new user-facing string is a localization key**, never a hardcoded literal — add it to the English
  pack (`IDE/HexIDE/Localization/Packs/en.json`) and translate it into the shipped packs.
- **Tests** assert with **AwesomeAssertions** (`value.Should().Be(...)`) and mock with **NSubstitute** —
  not xUnit `Assert.*` and not Moq.

## Making a change

1. Open an issue first for anything non-trivial, so we can agree on the approach.
2. Branch off the default branch; keep each PR focused and its commits tidy.
3. Make sure the build is green and the relevant tests pass locally. CI runs the IDE and LSP builds/tests.
4. Describe *what* and *why* in the PR. Screenshots help for any UI change.

## Reporting bugs & security issues

- **Bugs / features:** open a GitHub issue with steps to reproduce and your OS + .NET SDK version.
- **Security vulnerabilities:** please **do not** open a public issue — follow [SECURITY.md](SECURITY.md).
