## Why

The icon-system spec required every icon to be an *original* vector geometry. The shipped icons are Fluent
System Icons: MIT-licensed, recorded with attribution, and exactly what the tree's licence rule permits. So
the spec described a constraint the product has never met, and does not need to (#420). What the rule is
really for is keeping artwork extracted from another application out of an MIT distribution.

## What Changes

- Rename the requirement "IDE icons SHALL be original vector geometries" to "IDE icons SHALL be keyed vector
  geometries".
- Permit permissively licensed third-party icon sets whose licence and attribution are recorded, and keep
  the prohibition on artwork extracted from another application.
- State what an icon is: a single keyed path geometry with no fill, whose fill the host supplies, and that
  JSON can reference an existing geometry but cannot supply new path data.
- `IconGeometryTests` guards the shipped geometries against fills and multiple paths.

## Impact

- Affected spec: `icon-system`.
- No behaviour changes; the spec now describes what ships.
