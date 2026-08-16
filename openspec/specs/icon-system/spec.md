# icon-system Specification

## Purpose
Define how HexIDE draws its iconography: original vector geometries authored for this project, resolved
through a single factory and tinted from the active theme, with no bitmap artwork in the tree.

Every icon in the IDE is drawn from geometry HexIDE owns. This is a licensing boundary before it is a
visual one — the upstream project carried icons extracted from a Microsoft binary, and none of that
artwork may exist in an MIT-licensed distribution. Vector geometry also re-tints per theme, which bitmaps
could not, so the constraint and the better behaviour point the same way.

## Requirements
### Requirement: IDE icons SHALL be original vector geometries
Every icon in the IDE chrome SHALL be an original vector geometry declared as a keyed resource, and the product SHALL NOT ship artwork extracted from another application.

Icons are drawn rather than imported. A geometry is defined once and referenced by key wherever it appears, so an icon has a single definition however many surfaces use it, and it renders at any size without a scaled-bitmap artefact.

#### Scenario: Referencing an icon from a view
- **WHEN** a view needs an icon
- **THEN** it references the geometry by its key rather than pointing at an image file

#### Scenario: Rendering on a high-DPI display
- **WHEN** the IDE runs at a fractional display scale
- **THEN** icons render cleanly at that scale, because there is no fixed-size bitmap to resample

### Requirement: Icons SHALL follow the active theme without per-theme artwork
Icons SHALL be tinted from a themed brush rather than carrying their own colour, so that changing the theme re-tints the whole icon set with no additional artwork.

A per-theme icon set would multiply the drawing work by the number of themes and leave them to drift apart. Tinting a single geometry set makes theme support free, and means a new theme needs colours only.

#### Scenario: Switching to a dark theme
- **WHEN** the developer selects a theme whose variant is dark
- **THEN** every icon re-tints to the dark ink colour, with no separate dark icon set involved

#### Scenario: Adding a new theme
- **WHEN** a new theme is added
- **THEN** it supplies colours only, and inherits the existing icon set unchanged

### Requirement: Icons whose colour carries meaning SHALL keep it
Run, break and stop SHALL take their own semantic colours rather than the neutral theme ink.

For most icons the shape carries the meaning and the colour is incidental. For the transport controls the colour *is* the signal — a developer scanning the toolbar reads green-for-run before they read the glyph — so flattening them to the theme ink would remove information.

#### Scenario: Rendering the transport controls
- **WHEN** the run, break and stop commands are shown on a toolbar or menu
- **THEN** each is drawn in its own semantic colour rather than the neutral ink

### Requirement: Code SHALL obtain icons through a single factory
Call sites that cannot reference a geometry resource directly SHALL obtain icons from a factory exposing a theme-tinted lookup and a caller-coloured lookup, and SHALL NOT load image files themselves.

Most icons are referenced declaratively from views. A minority are needed from code — building a menu at runtime, or mapping a value to an icon. Confining those to one factory keeps resource lookup in a single place and stops bitmap loading reappearing.

#### Scenario: Getting an icon from code
- **WHEN** code needs an icon for a runtime-constructed surface
- **THEN** it asks the factory for that geometry key and receives an image tinted with the theme ink

#### Scenario: Getting an icon in a specific colour
- **WHEN** code needs an icon in a colour other than the theme ink
- **THEN** it asks the factory for that geometry key with the brush to use

