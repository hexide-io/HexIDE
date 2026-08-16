# Design: vector geometries instead of an icon service

## What was proposed, and why it was dropped

The earlier `runtime-icon-loading` design specified an injected icon *service* that would resolve an icon key
to a bitmap at runtime, with theme packs shipping their own raster icon sets so a dark theme could supply dark
icons. It also proposed importing an existing open-source icon set for the Object Browser's member kinds.

None of it was built, and on reflection each part was solving a problem that a different choice removes:

- **A resolution service exists to pick between variants.** With vector geometries tinted from a themed brush
  there are no variants to pick between — the same geometry is correct in every theme. The service had nothing
  left to decide.
- **Per-theme icon sets multiply the artwork by the number of themes.** Three shipped themes meant three sets
  to draw and keep in sync, and a fourth theme would mean a fourth. Tinting makes theme support free.
- **Importing an icon set reintroduces the problem being solved.** The whole point of the exercise was to stop
  shipping artwork that is not ours. Swapping one project's bitmaps for another's would have left the same
  class of dependency, with a licence to track.

Retiring that design is therefore not a deferral. It is a decision that the problem it addressed does not
exist under the approach that shipped.

## Geometries, keyed once

Each icon is a path geometry declared once in a resource dictionary under a `Geo.`-prefixed key. AXAML
references it directly; there is no indirection to configure and nothing to register at startup.

Vectors also remove the high-DPI question entirely. There is no 16px asset and no 32px asset to choose
between and no scaling artefact at fractional scale factors — the same geometry renders at whatever size the
surface asks for.

## Tinting, not recolouring

Icons carry no colour of their own. They are filled from a single themed brush, defined once per theme
variant, so switching theme re-tints every icon in the application without touching an icon definition.

The exception is deliberate: run, break and stop keep their own colours. Green-for-run and red-for-stop are
not decoration — the colour *is* the signal, and a developer scanning the toolbar reads it before the shape.
Those three take semantic brushes instead of the neutral ink.

## A factory for the call sites that need one

Most icons are referenced from AXAML, where a resource lookup is natural. A minority are needed from C# —
building a menu at runtime, or mapping a value to an icon. Rather than teaching those call sites to reach into
resource dictionaries, a small factory exposes exactly two operations: give me this geometry tinted with the
theme ink, or give me this geometry in a specific brush. That is the whole surface, and it keeps the resource
lookup in one place.

## What this does not cover

The Object Browser still renders member kinds as text glyphs. That gap was previously to be closed by importing
an icon set, which this change's reasoning rules out. Closing it now means drawing those kind icons as
geometries like every other icon — a straightforward but separate piece of work, tracked on the issue tracker
rather than carried here.
