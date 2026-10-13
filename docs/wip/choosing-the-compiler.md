# One Build command, many compilers

> **Work in progress.** Captured 2026-10-13, building on #399 (raised 2026-09-12). Nothing here is built. The
> companion document, [Building with the real VB6](vb6-native-build.md), covers what the VB6 toolchain itself
> could offer once it is chosen.

## The problem

HexIDE has two ways to build and run a project, and shows both as menu items: **Make** and **Start** use
HexIDE's own interpreter, while **Make with VB6** and **Start with VB6** hand the project to a real `vb6.exe`.
That was the natural first step, and it does not scale:

- **It doubles the menu** for something most users will never configure, since `vb6.exe` exists only on a
  Windows machine with VB6 installed.
- **It asks the question at the wrong moment.** Which compiler builds a project is a property of the project,
  not of each keystroke, and a second pair of commands invites picking the wrong one by habit.
- **It does not grow.** HexIDE is meant to host more than one backend: the interpreter, real `vb6.exe`, and later
  others over the replaceable backend seam. A menu item per compiler per action is not a design.

## The end state

- **The menu has Build, Rebuild, Clean and Start,** and nothing that names a compiler (#399).
- **The project says which toolchain builds it,** and Build and Start follow it.
- **A one-off choice is possible** without changing the project: a drop-down beside Build, or Build With… on
  the project's context menu.
- **The status bar says which toolchain the last build used,** so the choice is never invisible.
- **If the chosen toolchain is unavailable,** for example VB6 on a machine without it, HexIDE says so and
  offers the alternatives. It never switches silently, because a build that quietly used a different compiler
  is a result nobody asked for.

The order of precedence is: the one-off choice, then the project's setting, then the IDE's default, which is
the interpreter.

## Where the setting lives

In the `.vbp` itself, in a section of HexIDE's own:

```ini
[HexIDE]
Toolchain=vb6
```

- **It travels with the project,** through source control and onto other machines, which a setting in HexIDE's
  user profile would not.
- **HexIDE already preserves it.** Everything from a `.vbp`'s first `[Section]` line to the end of the file is
  kept verbatim today, which is how `[VBCompiler]` and add-in sections such as `[vbAdvance]` survive a save.
  Writing the setting, rather than only preserving it, needs that tail to become section-aware, which is the
  same work [Building with the real VB6](vb6-native-build.md) needs for `[VBCompiler]`.
- **VB6 tolerates sections it does not know,** since add-ins have always written their own. Whether the VB6 IDE
  *keeps* an unknown section when it saves the project is not yet measured. If it drops it, a project saved in
  VB6 falls back to the default, which is safe but worth saying in the documentation.
- **A project group** (`.vbg`) has no setting of its own; each project's applies.

## Toolchains as providers

Each toolchain is a provider registered with the IDE, not a pair of commands. A provider declares:

- its name, as shown in the drop-down and the status bar;
- whether it is available here, and **why not** when it is not ("VB6 is not installed", "Windows only");
- which kinds of project it can build;
- what Build, Rebuild, Clean and Start do;
- what it prints, for the Output window;
- how to read its diagnostics, for the error list (#451);
- optionally, a debugger, which is the same question #891 asks for add-ins.

The interpreter and `vb6.exe` are the first two providers, and the existing `Vb6ToolchainService` becomes the
second one's implementation. A later backend, or an add-in, adds a provider rather than menu items. An add-in
that contributes one runs a compiler on the user's behalf, so it falls under the same trust rules as any other
add-in.

## What other IDEs do with polyglot repositories

**VS Code has no project model of its own.** Its unit is a folder, or a `.code-workspace` file listing several.
Everything else belongs to extensions:

- **Language support** starts when a file of that language opens. The extension, usually through a language
  server, finds its own project file: `.csproj` for C#, `Cargo.toml` for Rust, `compile_commands.json` for
  clangd. The editor never reads them.
- **Building** is `.vscode/tasks.json`, a list of commands, plus tasks that extensions detect, such as npm
  scripts. Ctrl+Shift+B runs the task marked as the default build, and offers a list when none is marked: a
  saved default with a choice on the fly.
- **Build output** is turned into the Problems list by *problem matchers*, patterns attached to each task. The
  built-in `$msCompile` reads the Visual C++ format, which is the format `C2.EXE` and `LINK.EXE` print.
- **Running and debugging** is `.vscode/launch.json`, one entry per configuration, each served by a debug
  adapter over the Debug Adapter Protocol.

**Visual Studio's Open Folder mode** works the same way, with `tasks.vs.json` and `launch.vs.json`.
**JetBrains IDEs** keep a project directory (`.idea`) with a module per language and named run configurations.

What works in VS Code's approach: the core stays small, and any toolchain fits without the editor knowing
about it. What does not: `tasks.json` belongs to the folder, not to a project, so a repository with several
projects ends up with a hand-written list and no clear default, and none of it is discoverable until you go
looking. Someone used to VB6, where a project is opened and F5 runs it, finds that unfriendly.

### What HexIDE should take, and where it should differ

- **Take:** providers instead of menu items; problem matchers instead of one parser per tool; a saved default
  with a choice on the fly.
- **Differ:** keep the project file as the unit. HexIDE can open a folder, but it should then list every
  project file it finds (`.vbp`, `.vbg`, and the project files of any other backend a user attaches) as nodes
  in the Project Explorer, each with its own toolchain. A VB6 user keeps "open the project, press F5"; a mixed repository still
  works.
- **Already there:** HexIDE routes each file to a language server by its extension (`lsp-servers.json`, see
  [`language-servers.md`](../language-servers.md)), which is VS Code's model for language support. Build and run
  are the two surfaces that still have menu items instead of providers.

## Moving there from today

- Retire `MakeWithVb6Command` and `RunWithVb6Command`, and their menu items. Alt+F5 is Start with VB6 today;
  it could become "Start with…", opening the one-off choice.
- Rename the Make items to Build, and add Rebuild and Clean (#399).
- The affected `Str.Menu.*` keys change meaning, so every shipped language pack needs new values rather than a
  mechanical rename.
- Update the Make and Start rows in [`MISSING_FEATURES.md`](../MISSING_FEATURES.md). One of them already gives
  the wrong shortcut: Make with VB6 has none, and Alt+F5 belongs to Start with VB6.

## Open questions

- Does the VB6 IDE keep an unknown `.vbp` section when it saves? One run with the IDE settles it.
- Is "toolchain" the right word in the UI, or is "compiler" clearer to a VB6 user even though the choice also
  decides how the program runs?
- Should a project group be able to say "build every project with VB6", or is per-project enough?
- Where does a provider's debugger fit, given the interpreter's debugger and an external one behave differently?

## Related

- #399: Build, Rebuild and Clean, and the toolchain as configuration.
- #48: the disabled Project Properties tabs, where the setting would be edited.
- #451: no error list.
- #891: an add-in cannot contribute a debug adapter.
- #455: a server that needs a generated project file at the workspace root.
