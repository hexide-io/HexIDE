# HexIDE

**An open, cross-platform IDE for Visual Basic 6 & VBA.**

HexIDE reads and writes native `.vbp`/`.frm`/`.frx`/`.bas` files and layers on a modern,
LSP-backed editor — IntelliSense, diagnostics, code folding, rename, and formatting — over a
form designer and project manager. It runs on Windows, macOS, and Linux. No COM, no registry,
no Visual Studio required.

> **Status: Pre-alpha — early developer preview.** HexIDE is a working cross-platform VB6
> *editor and form designer*. It is **not a compiler** — the built-in interpreter runs only a
> subset of VB6, and producing a real `.exe` currently shells out to an installed copy of the
> original VB6 (Windows only). Treat it as an early preview, not a finished product.
> Contributions and feedback are very welcome.

---

## 🧭 Philosophy

HexIDE should feel *instantly familiar* to a VB6 developer — the same default keybindings, menus, and
toolbars — with the dead weight of 1998 removed or modernised and modern conveniences folded in gently,
never at the cost of familiarity. See **[PHILOSOPHY.md](PHILOSOPHY.md)** for the full statement.

---

## ✨ Features

### Form designer & project
- 🎨 **Visual form designer** — drag-and-drop controls on a Win98-style canvas
- 📦 **14 built-in controls** — CommandButton, TextBox, Label, CheckBox, OptionButton, ComboBox, ListBox, Frame, PictureBox, Timer, Shape, Menu, HScrollBar, VScrollBar
- 💾 **Native VB6 project format** — reads and writes `.vbp`, `.frm`, `.frx`, `.bas`
- 🪟 **MDI interface** with dockable tool windows

### Code editor
- 💡 **IntelliSense** — keyword and declared-name completion as you type
- 📐 **Code folding** — Sub/Function/Property/If/For/Select and 11 other block types
- 🔍 **Find & Replace** — modeless, with regex, whole word, match case, and scope options
- 🖌️ **Format code (Shift+Alt+F)** — keyword casing + auto-indentation, with format-on-save
- ⌨️ **Auto-close blocks** — type `Sub Foo()`, press Enter, and `End Sub` appears
- 🔤 **Keyword case-correction** — `public sub` becomes `Public Sub` on Enter
- ⚙️ **Tools ▸ Options** — persistent editor, grid, and environment settings

### Language intelligence (LSP)
- 🚨 **Live diagnostics** — syntax errors as red squiggles with hover tooltips
- ⚠️ **`Option Explicit` enforcement** — undeclared variables flagged as warnings
- 📝 **Document symbols** — two-combo procedure navigation (object + member)
- ✏️ **Rename symbol (F2)** — lexical rename across the current file
- 🎯 **Go to Definition (F12)** — jump to a declaration in the current file

---

## 🚀 Quick start

### Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- No Java required — the ANTLR4 grammar compiles via MSBuild

### Build and run

```sh
git clone https://github.com/hexide-io/HexIDE.git
cd HexIDE

# Build & run the desktop app
cd IDE && dotnet build HexIDE.Desktop/
dotnet run --project HexIDE.Desktop/

# Run the test suites
cd IDE       && dotnet test HexIDE.Runtime.Tests/       # VB6 interpreter
cd IDE       && dotnet test HexIDE.Tests/               # IDE view-models
cd LspServer && dotnet test HexIDE.VbLspServer.Tests/   # LSP server
```

### Open in Visual Studio

Open `HexIDE.slnx` at the repo root — all projects, in two solution folders (`IDE` and
`LspServer`). Requires Visual Studio 2022 17.10+ for `.slnx` support.

---

## 🏗️ Architecture

HexIDE is a monorepo with a clean licence boundary:

```
HexIDE/
├── IDE/                    # MIT — the IDE application
│   ├── HexIDE.Core/        #   Framework-agnostic abstractions (no Avalonia)
│   ├── HexIDE/             #   IDE shell, visual designer, code editor
│   ├── HexIDE.Runtime/     #   VB6 interpreter & built-in controls
│   ├── HexIDE.Lsp/         #   LSP client (StreamJsonRpc)
│   ├── HexIDE.Desktop/     #   Desktop entry point
│   └── HexIDE.Standalone/  #   Headless runner for published projects
│
├── LspServer/              # GPLv3 — separate process
│   └── HexIDE.VbLspServer/ #   LSP server (Rubberduck VBA grammar)
│
└── HexIDE.slnx             # Master solution
```

The **language features sit behind a standard LSP boundary** — the IDE is only a client, and the
server runs as its own subprocess. Two things follow. First, the GPLv3 grammar stays isolated:
the MIT IDE never links against GPL code. Second, **the server is a replaceable backend, not a
fixture** — the current Rubberduck-grammar server is a starting point, and because the client is
transport- and server-agnostic (it speaks only [LSP JSON-RPC](https://microsoft.github.io/language-server-protocol/)
over a duplex byte stream, stdio on the desktop), a more capable language engine can take over
language intelligence without any change to the IDE.

Under the hood, **`HexIDE.Core`** is a zero-dependency abstraction layer with no Avalonia
imports; **Pure.DI** provides source-generated, AOT-safe dependency injection; and theming is
driven by switchable theme packs (a Win98-style Classic theme ships today).

The server currently handles: `initialize`, `didOpen`/`didChange`/`didClose`, `hover`,
`completion`, `signatureHelp`, `definition`, `documentHighlight`, `documentSymbol`,
`foldingRange`, `formatting`, `rename`, and `publishDiagnostics`.

---

## 🗺️ Roadmap

HexIDE is under active development. Near-term work is client-side editor polish: richer hover
and completion (docs + icons), inline rename, signature help, more themes, and editor context
menus.

Deeper cross-file analysis (project-wide navigation and rename) and any debugging experience
(Immediate window, breakpoints, stepping) depend on a dedicated execution/analysis backend and
are further out.

Development happens in the open — see the issue tracker and Discussions to follow along or weigh in.

---

## 🙏 Standing on the shoulders of giants

HexIDE would not exist without:

| Project | Author(s) | Contribution | Licence |
|---|---|---|---|
| [AvaloniaVisualBasic6](https://github.com/BAndysc/AvaloniaVisualBasic6) | Bartosz Korczyński ([@BAndysc](https://github.com/BAndysc)) | The original IDE — form designer, component model, interpreter, project serialisation. HexIDE is a direct fork. | MIT |
| [Rubberduck](https://github.com/rubberduck-vba/Rubberduck) | The Rubberduck contributors | The VBA ANTLR4 grammar (`VBALexer.g4` + `VBAParser.g4`) powering the LSP server | GPLv3 |
| [proleap-vb6-parser](https://github.com/uwol/proleap-vb6-parser) | Ulrich Wolffgang | The VB6 ANTLR4 grammar (`VB6.g4`) powering the interpreter | MIT |
| [Avalonia UI](https://avaloniaui.net/) | Avalonia team | The cross-platform .NET UI framework | MIT |
| [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit) | Avalonia community | The code editor control (port of ICSharpCode.AvalonEdit) | MIT |
| [Classic.Avalonia](https://github.com/AvaloniaCommunity/Classic.Avalonia) | Avalonia community | Win98/Classic theme for VB6-style chrome | MIT |
| [Dock](https://github.com/wieslawsoltes/Dock) | Wiesław Šoltés | Dockable tool-window layout | MIT |
| [Pure.DI](https://github.com/DevTeam/Pure.DI) | Nikolay Pianikov | Source-generated DI (AOT-safe) | MIT |

> Much of HexIDE was built in pair-programming with **[Claude](https://claude.ai/)** (Anthropic).

"Visual Basic", "VB", and "VBA" are trademarks of **Microsoft Corporation**. HexIDE is an
independent, community-built project — **not affiliated with, endorsed by, or sponsored by
Microsoft** — and uses these names only descriptively to indicate VB6 file-format and workflow
compatibility. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for bundled-component
attributions.

---

## 📄 Licence

- **`IDE/`** — [MIT](LICENSE)
- **`LspServer/`** — [GPLv3](LspServer/LICENSE) (due to the Rubberduck grammar)

The subprocess boundary ensures GPL does not propagate to the MIT IDE.

---

## 🤝 Contributing

HexIDE is in early development and contributions are welcome — bug reports, feature ideas, or
pull requests. See [CONTRIBUTING.md](CONTRIBUTING.md) to get started. If you're from the
VB6/VBA/twinBASIC community and want to help bring the classic IDE to modern platforms, please
get in touch.
