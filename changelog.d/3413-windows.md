### Added

- **A tool that makes a published Windows build another product** (#3413). `HexIDE.Repack` takes a stock build and the
  same `brand.json`, and writes a new directory with the executable's name, icon and version details rewritten, and
  the inputs for an installer that goes alongside HexIDE. The output is unsigned, and the adopter signs it. A branded
  build also gives Windows its own taskbar identity.
