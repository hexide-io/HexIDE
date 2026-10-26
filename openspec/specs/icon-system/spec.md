# icon-system Specification

## Purpose
Define how HexIDE draws its iconography: keyed vector geometries resolved through a single factory
and tinted from the active theme, with no bitmap artwork in the IDE chrome. Permissively licensed
third-party icon sets are permitted when their licence and attribution are recorded under the tree's
licence rule; artwork extracted from another application is not.

## Requirements

### Requirement: IDE icons SHALL be keyed vector geometries
Every icon in the IDE chrome SHALL be a single path geometry with no fill, declared as a keyed resource,
and the product SHALL NOT ship artwork extracted from another application. Third-party icon sets MAY be used
where their licence is permissive and recorded with attribution in `THIRD-PARTY-NOTICES.md`, per the tree's
licence rule.

The host supplies the fill brush so the same path follows the active theme (or a semantic colour).
A geometry is defined once and referenced by key wherever it appears, so an icon has a single definition
however many surfaces use it, and it renders at any size without a scaled-bitmap artefact.

Icons are compiled resources today. JSON can reference an existing geometry by string key; it cannot
supply new path data, and there is no runtime path-data parser for importing new icons.

#### Scenario: Adding an icon from a third-party set
- **WHEN** an icon from a permissively licensed icon set is added
- **THEN** its licence and attribution are recorded in `THIRD-PARTY-NOTICES.md`
- **AND** it is declared as a keyed single path geometry without a fill, so the host can tint it

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
