# Changelog

All notable changes to HexIDE are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). The leading `0.` is the stability promise:
there isn't one yet. Anything may change between 0.x releases.

## [Unreleased]

## [0.1.0] — unreleased

The first versioned build. Before this, nothing in the tree carried a version at all, so a shipped
binary could not be identified.

### Added

- **A cross-platform Visual Basic 6 workbench** — form designer, toolbox, project explorer, properties
  window, menu editor and code editor, running natively on Windows, macOS and Linux.
- **Native VB6 file format support.** Reads and writes `.vbp`, `.vbg`, `.frm`, `.cls`, `.bas`, `.ctl`,
  `.pag` and their binary companions. Content HexIDE does not recognise is carried through verbatim, so
  a project opened here still opens in VB6. Saves are atomic, so a crash cannot truncate your work.
- **A VB6 interpreter** for running code in the IDE — classes, `Property Get`/`Let`/`Set`, `WithEvents`,
  user-defined types, `Enum`, object arrays, error handling, and around eighty intrinsics including the
  full `Format` mask language. Its behaviour is checked against the real 1998 compiler rather than
  against assumptions about it. It is a demonstrator with a deliberate ceiling — see the Scope page.
- **Language tooling** over an out-of-process LSP server: syntax diagnostics, document symbols, and
  keyword and declared-name completion.
- **Thirty languages**, four of them right to left, covering the whole interface.
- **A signed add-in system** with offline verification, per-add-in load contexts and a first-load
  consent gate, plus a bundled AI Chat add-in (bring your own key).
- **A bridge to the real VB6 compiler on Windows**, so Run and Make produce genuine native executables
  when VB6 is installed.

### Fixed

- **Every VB6 option button crashed the IDE on render.** `Classic.Avalonia.Theme 11.3.0.3` is compiled
  against Avalonia 11 and called a `StreamGeometryContext.ArcTo` overload that Avalonia 12 replaced, so
  drawing a radio bevel threw on the render thread and took the process down — in the designer and at
  runtime alike, with nothing in the log.
- **Every VB6 check box rendered ticked**, whatever its value, because the tick's visibility rules
  targeted a part name the template does not use.
- **Dark themes made the code editor unreadable.** Syntax colours were hardcoded for a light background,
  leaving keywords at roughly 1.1:1 contrast on both shipped dark packs.
- **Release builds contained the wrong platforms.** Publishing without a runtime identifier bundled
  native payloads for every platform SkiaSharp ships, so the Windows archive carried Linux binaries and
  the download was six times larger than it needed to be.

[Unreleased]: https://github.com/hexide-io/HexIDE/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/hexide-io/HexIDE/releases/tag/v0.1.0
