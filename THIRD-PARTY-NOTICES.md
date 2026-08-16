# Third-Party Notices

HexIDE incorporates third-party components. This file supplements the credits in
[README.md](README.md). The whole repository — both the `IDE/` and `LspServer/` halves — is
**MIT-licensed**.

## Grammars

- **proleap-vb6-parser** — `IDE/HexIDE.Runtime/Interpreter/Grammar/VB6.g4`
  Copyright (C) 2017 Ulrich Wolffgang. MIT License.
  <https://github.com/uwol/proleap-vb6-parser>
  Compiled into the VB6 interpreter (MIT half).

- **proleap / grammars-v4 VB6 grammar** — `LspServer/HexIDE.VbLspServer/Grammar/VisualBasic6Lexer.g4`, `VisualBasic6Parser.g4`
  Copyright (C) 2017 Ulrich Wolffgang; maintained in [antlr/grammars-v4](https://github.com/antlr/grammars-v4/tree/master/vb6). MIT License.
  The MIT VB6 language server parses with this grammar (carrying HexIDE's clean-room fixes). It runs as
  a **separate process** for crash isolation + a replaceable backend — not for any licensing reason.

## Libraries

HexIDE redistributes the NuGet packages below. Except where noted they are under the **MIT License**;
see each project for its authors and full license text.

**MIT**
- **Avalonia UI** and its `Desktop`, `Skia`, `Browser`, `Themes.Simple`, `Controls.ColorPicker`,
  `Controls.DataGrid` packages — © The Avalonia Project
- **AvaloniaEdit** (a port of ICSharpCode AvalonEdit) — © the AvaloniaUI community
- **Avalonia.Labs** — the experimental `RoutedCommand`/`CommandManager` **vendored** into
  `IDE/HexIDE/AvaloniaLabs.CommandManager/` — © The Avalonia Project; the MIT notice ships in that
  folder's `LICENSE`. <https://github.com/AvaloniaUI/Avalonia.Labs>
- **Dock** (`Avalonia`, `Model`, `Model.Avalonia`, `Model.MVVM`, `Serializer`, `Themes.Simple`) — © Wiesław Šoltés
- **Classic.Avalonia** (Theme, CommonControls) — © the Avalonia community
- **CommunityToolkit.Mvvm** — © .NET Foundation and Contributors
- **R3** — © Cysharp, Inc.
- **LanguageExt.Core** — © Paul Louth
- **StreamJsonRpc** — © Microsoft Corporation
- **EmmyLua.LanguageServer.Framework** — the JSON-RPC + LSP protocol shell for the MIT VB6 language
  server — © CppCXY (the EmmyLua project). <https://github.com/CppCXY/LanguageServer.Framework>
- **MessagePack** (MessagePack-CSharp) — © Yoshifumi Kawai / Cysharp; **Nerdbank.MessagePack** — © Andrew Arnott
- **Microsoft.Extensions.Logging.Abstractions** — © .NET Foundation and Contributors
- **Pure.DI** — © Nikolay Pianikov; **PropertyChanged.SourceGenerator** — © Antony Male *(compile-time source generators)*

**Apache License 2.0** — requires the license text on binary redistribution:
- **Serilog**, **Serilog.Sinks.File**, **Serilog.Extensions.Logging** — © Serilog Contributors.
  <https://www.apache.org/licenses/LICENSE-2.0>

**BSD-3-Clause**:
- **Antlr4.Runtime.Standard** — the ANTLR 4 runtime, © 2012–2022 The ANTLR Project. Compiled into
  both the VB6 interpreter and the LSP server (both MIT); redistribution permitted under
  the 3-clause BSD license with the standard warranty disclaimer.

Upstream fork: HexIDE is a derived work of **AvaloniaVisualBasic6** by Bartosz Korczyński — MIT.
<https://github.com/BAndysc/AvaloniaVisualBasic6>. Icons: see **Icons** below.

*Build- and test-time only (not redistributed in the app):* Antlr4BuildTasks, Svg.Skia, xUnit,
NSubstitute, AwesomeAssertions, coverlet, Microsoft.NET.Test.Sdk, Avalonia.Headless, Avalonia.Diagnostics.
The MCP automation server (`ModelContextProtocol.AspNetCore`) is compiled in for **Debug only** and is
excluded from Release binaries.

## Icons

- **Fluent UI System Icons** — Copyright (c) Microsoft Corporation. MIT License.
  <https://github.com/microsoft/fluentui-system-icons>
  Icon geometries are extracted from the 16px SVGs and embedded in
  `IDE/HexIDE/Themes/IconGeometry.axaml` (and one inline in `HexIDE.Runtime/BuiltinControls/VBTimer.axaml`),
  rendered as theme-tinted vector paths. These **fully replace** the original VB6-derived artwork —
  every toolbar, menu, tool-window, toolbox, project-tree and dialog icon is now a Fluent geometry.
  All VB6.exe-extracted GIFs and the VB6 app icon have been removed from the source tree.

## Fonts

HexIDE bundles **no proprietary UI font**. VB6's default form font, **MS Sans Serif**, is not
redistributable and has been **removed** from the source tree. VB6 form text is drawn with the bundled
libre substitute when present, otherwise the platform's default sans-serif — the IDE chrome already uses
the system UI font.

- **Liberation Sans** — SIL Open Font License 1.1. The designated substitute for VB6 form text
  (metric-compatible with Arial, so VB6's fixed-pixel form layouts stay stable); the `VBFont` → Avalonia
  `FontFamily` mapping falls back to it. Placing `LiberationSans-*.ttf` in `IDE/HexIDE/Resources/`
  activates it — the SIL OFL 1.1 text must ship alongside the font files (an OFL requirement).
  © the Liberation Fonts authors. <https://github.com/liberationfonts/liberation-fonts>

*(The VB6-derived icon set was likewise fully replaced with Fluent geometries — see **Icons** above.)*
