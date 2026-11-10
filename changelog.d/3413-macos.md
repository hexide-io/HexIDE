### Added

- **The repack tool now handles macOS builds** (#3413). `HexIDE.Repack` turns `HexIDE.app` into a bundle with a brand's
  name, identifier and icon, from the same `brand.json`. Its programs keep their ad-hoc signatures, and the adopter
  signs and notarizes the result.
