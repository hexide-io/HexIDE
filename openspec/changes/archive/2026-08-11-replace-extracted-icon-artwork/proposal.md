# Replace the extracted icon artwork with themeable vector geometries

> Reconstructed at conversion time (2026-08-11) from the shipped code, when HexIDE's specs were
> migrated to the OpenSpec format. This work shipped without a spec of its own; the spec that
> existed (`runtime-icon-loading`) described a different, abandoned design and is retired by this
> change. See `design.md` for why that design was dropped.

## Why

HexIDE inherited its icon set from the upstream project, which had extracted the bitmaps from the original VB6 executable. That is a copyright problem, and no amount of scope discussion changes it: the artwork cannot ship. Replacing it is a compliance requirement rather than a design preference, and it gates any public release.

The replacement is also an opportunity. The extracted icons were 22px raster GIFs designed for a 1998 96-DPI screen. They do not scale on a high-DPI display, and they cannot follow a theme — which is why the dark theme packs previously rendered dark-coloured icons designed for a light background.

## What Changes

- Every extracted raster icon is deleted and replaced with an original vector geometry, drawn to a consistent visual language.
- Icons are declared once as geometry resources and referenced by key, so a given icon has a single definition regardless of how many surfaces use it.
- Icons are tinted from a themed brush rather than carrying their own colour, so they follow the active theme automatically instead of needing a per-theme icon set.
- A small factory provides icons to C# call sites that cannot use a XAML resource reference directly.
- Commands with semantic colour — run, break, stop — keep distinct colours rather than taking the neutral ink, because their colour carries meaning.

## Impact

- New capability: `icon-system`.
- Retires the `runtime-icon-loading` capability, which specified an icon *service* and per-theme raster icon sets. Neither was built; see `design.md`.
- Removes the last extracted third-party artwork from the tree, clearing a release gate.
- Object Browser member-kind icons are **not** addressed here and remain placeholder text glyphs; the approach that spec proposed for them is no longer available.
