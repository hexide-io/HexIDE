## RENAMED Requirements

- FROM: `### Requirement: IDE icons SHALL be original vector geometries`
- TO: `### Requirement: IDE icons SHALL be keyed vector geometries`

## MODIFIED Requirements

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
