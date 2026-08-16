# HexIDE

**An open, cross-platform IDE for Visual Basic 6 & VBA.**

![HexIDE with a new Standard EXE project — the familiar VB6 layout (toolbox, form designer, Project Explorer, Properties) rendered with modern vector icons](screenshots/hexide-new-standard-exe.png)

HexIDE reads and writes native `.vbp`/`.frm`/`.bas`/`.cls` files and layers on a modern,
LSP-backed editor — IntelliSense, diagnostics, code folding, rename, and formatting — over a
form designer and project manager. It runs on Windows, macOS, and Linux. No COM, no registry,
no Visual Studio required.

> **Status: Pre-alpha — early developer preview.** HexIDE is a working cross-platform VB6
> *editor and form designer*. It is **not a compiler** — the built-in interpreter runs only a
> subset of VB6, and producing a real `.exe` currently shells out to an installed copy of the
> original VB6 (Windows only). Treat it as an early preview, not a finished product.
> Contributions and feedback are very welcome.
>
> **Work on a copy.** HexIDE does not yet reproduce the VB6 file format faithfully enough to be
> trusted with a project you care about — see [Opening real VB6 projects](#real-projects)
> for exactly what it does and does not preserve.

---

<a id="real-projects"></a>

## ⚠️ Opening real VB6 projects

Back it up first, or work on a copy, or at least commit before you open it.

That warning is measured, not boilerplate. The benchmark below is **the 22 form and control files
Visual Basic 6 ships inside its own `VB98\Template` folder** — Microsoft's files, written by VB6
itself. If HexIDE cannot re-emit those unchanged, it cannot re-emit yours.

| Against VB6's own 22 template forms | Today |
|---|---|
| Open, edit code, and run | **all 22** |
| Survive open-then-save **byte-for-byte** | **0** |
| Open **read-only** — HexIDE refuses to save rather than damage them | **12** |
| Will save, but will not reproduce the original file | 10 |

What changes in those 10 ranges from harmless to genuinely lossy. Property-name padding and key
ordering are noise a maintainer can ignore. But HexIDE also re-expresses form geometry — writing
`Height`/`Width` where VB6 wrote `ClientHeight`/`ClientWidth` — and on two of them
(`Button ListBox.frm`, `Mover ListBox.frm`) it drops a control property that points at an embedded
image: the `.frx` bytes survive on disk, the reference to them does not. **Treat saving a
VB6-authored form as a change you review, not one you trust.**

Standard and class modules (`.bas`, `.cls`) *do* round-trip byte-for-byte, and are covered by a
regression gate that fails the build if that stops being true.

**Why forms are gated.** HexIDE's designer model has no parent link between controls, so saving a
form with nested controls or a menu tree would flatten it into a flat sibling list. VB6 then rejects
the result outright as soon as a menu carries a shortcut or a separator — which is nearly every real
menu. A form that HexIDE cannot reproduce therefore opens **read-only**, with a banner saying so, and
Save is refused. Refusing is recoverable; silently writing a file that only fails weeks later when
you go back to VB6 is not. The reasoning is written up in
[docs/serialization-outcomes.md](docs/serialization-outcomes.md).

**Companion binaries (`.frx`, `.ctx`, `.pgx`) are preserved, never regenerated.** They hold icons,
pictures and list contents that exist nowhere else. When a form references a blob HexIDE does not
model, the save path leaves the companion file completely alone rather than rewriting it — an earlier
build regenerated them, which truncated one template's `.frx` from 790 bytes to 12 and deleted
another outright. Note the limit of that guarantee: the bytes are safe, but as above, the property
line in the `.frm` that *points* at them can still be dropped.

**There is no autosave and no crash recovery.** If HexIDE crashes, unsaved work is gone. VB6 was no
better, but that is worth knowing in advance rather than discovering.

**It is not a compiler.** The built-in interpreter covers a subset of the language — enough to run
simple projects and demos, not enough to run a real application. Producing an actual `.exe` shells
out to an installed copy of VB6 and is therefore Windows-only.

None of this is permanent. It is where a from-scratch reimplementation of a 1998 file format
honestly sits today, and the gap is tracked file-by-file in
[docs/serialization-fidelity-2026-08.md](docs/serialization-fidelity-2026-08.md).

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
- 💾 **Native VB6 project format** — reads `.vbp`, `.frm`, `.frx`, `.bas`, `.cls`; writes `.vbp`,
  `.frm`, `.bas`, `.cls` (`.frx` companions are preserved as-is, not regenerated — see
  [Opening real VB6 projects](#real-projects))
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
├── LspServer/                  # MIT — separate process (crash isolation)
│   └── HexIDE.VbLspServer/ #   LSP server (proleap grammar + EmmyLua shell)
│
└── HexIDE.slnx             # Master solution
```

The **language features sit behind a standard LSP boundary** — the IDE is only a client, and the
server runs as its own subprocess for crash isolation. And **the server is a replaceable backend,
not a fixture** — because the client is transport- and server-agnostic (it speaks only
[LSP JSON-RPC](https://microsoft.github.io/language-server-protocol/) over a duplex byte stream,
stdio on the desktop), a more capable language engine can take over language intelligence without
any change to the IDE.

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
| [proleap-vb6-parser](https://github.com/uwol/proleap-vb6-parser) / [grammars-v4](https://github.com/antlr/grammars-v4) | Ulrich Wolffgang | The VB6 ANTLR4 grammar powering **both** the interpreter and the LSP server | MIT |
| [EmmyLua.LanguageServer.Framework](https://github.com/CppCXY/LanguageServer.Framework) | CppCXY (the EmmyLua project) | The JSON-RPC + LSP protocol shell the language server is built on | MIT |
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

HexIDE is **MIT-licensed throughout** — both the `IDE/` and `LspServer/` halves. See [LICENSE](LICENSE)
and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

The language server runs as a separate subprocess for crash isolation and a replaceable backend — not
for any licensing reason.

---

## 🤝 Contributing

HexIDE is in early development and contributions are welcome — bug reports, feature ideas, or
pull requests. See [CONTRIBUTING.md](CONTRIBUTING.md) to get started. If you still work in VB6 or
VBA and want to help bring the classic IDE to modern platforms, please get in touch.
