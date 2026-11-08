# Era skins: design draft for review (#1138)

Status: draft, 2026-10-29. This document proposes a vocabulary and records questions; it approves no
implementation or policy change. No prototype results are claimed.

Source: [#1138](https://github.com/hexide-io/HexIDE/issues/1138), the design part of
[#1137](https://github.com/hexide-io/HexIDE/issues/1137). Jen's 2026-10-26 comment calls for one prototype
before the full design is settled. The supervisor's 2026-10-29 comment scopes this run to a reviewable
draft with options and trade-offs, no decisions and no product code. The epic's full text is not reproduced
here; this draft covers the brief recorded in #1138. Prototype evidence and review are still needed.

## Current implementation

- [ThemePackRecord](../../IDE/HexIDE/Themes/ThemePack.cs) contains a name, description, light/dark variant
  and colour overrides. It has no skin selectors. Its mapping mixes system brush keys and IDE-specific keys.
- [ThemeService](../../IDE/HexIDE/Themes/ThemeService.cs) applies Classic, Dark or Abyss, pins the app variant
  to Light or Dark, and replaces a resource dictionary. It does not implement the proposed three layers.
- [IconFactory](../../IDE/HexIDE/Utils/IconFactory.cs) constructs a single geometry drawing with neutral or
  caller-supplied ink. [icon-system](../../openspec/specs/icon-system/spec.md) requires single paths and
  theme tinting without per-theme artwork; runtime packs cannot import path data.
- [theme-packs](../../openspec/specs/theme-packs/spec.md) describes colour-only packs and a light form
  canvas. [CLAUDE.md](../../CLAUDE.md) requires SimpleTheme, deterministic IDE variants, and the canvas's
  Light scope; it forbids reloading ClassicTheme. An opt-in skin needs a reviewed revision of those rules.
- [The package manifest](../../scripts/package-licences.tsv) and
  [licence check](../../scripts/check-licences.sh) enumerate permitted package licences without OFL.
  That package inventory is not an inventory of bundled font files.

These remain today's contracts. The following model is the issue's requested direction, not a description
of a feature already shipped or a claim about measured VB6 behaviour.

## Three layers to evaluate

| Layer | Responsibility proposed by the brief | Boundary to preserve in a prototype |
|---|---|---|
| Platform skin | Control and frame appearance, platform metrics and system-colour table | Running forms and the form preview resolve system colours from this layer, independently of IDE colour preferences. |
| Application skin | IDE chrome composition: toolbars, docking headers, document framing and icon presentation | Application presentation does not rewrite a program's saved properties. |
| Colour scheme | Named colours for editor and IDE surfaces | Scheme changes do not implicitly select a different platform for a running program. |

**Which layer owns overlapping metrics and colours?** One option gives platform defaults to all controls
and lets the application skin override only IDE chrome; this shares templates but needs explicit resource
scopes. Another gives IDE controls a separate application metric and colour namespace; this isolates the
runtime but duplicates mappings. The prototype needs to expose a toolbar, a dock header and a running form
at once to show which boundary is workable.

**How are explicit form colours distinguished from system colours?** Resolving system-colour references
through the platform table while retaining explicit colours preserves the conceptual distinction in the
brief. Resolving all colours through a scheme would be simpler but could alter a program's appearance.
The representation, existing serialization paths and measured expectations need review before any change;
this draft supplies no VB6 colour values or conversion rules.

**Does the designer preview the selected runtime skin or a separately selected target skin?** A shared
selection keeps preview and execution aligned. A separate preview target helps design for another era but
needs a visible indication and a rule for F5. Designer adornments, selection and hidden-control masks also
need an explicit IDE scope so they do not become simulated platform controls.

## Parameterised skins

The brief asks for parameters over shared templates, with a skin-specific template only where parameters
cannot express the appearance. The candidate parameter families are:

| Family | Candidate content | Question and options |
|---|---|---|
| Bevel style | Edge construction and pressed/raised states | Fixed named styles are easier to validate; configurable edge recipes cover more appearances but enlarge the renderer contract. Which does the prototype require? |
| Corner radius | Control and frame radii | A shared radius is compact; per-control/state radii avoid exceptions but increase the parameter count. Which differences are necessary? |
| Gradient stops | Ordered stops and direction | Named gradient roles constrain combinations; arbitrary stops are more expressive but need bounds and validation. What is the smallest useful set? |
| Metrics | Insets, edge widths, caption/button dimensions and spacing | Shared metrics reduce drift; control-specific metrics improve fidelity but need scaling and minimum-size rules. Which are independent? |
| Colour table | Platform system roles and skin defaults | A complete table is predictable; inheritance reduces repetition but needs a stable parent and cycle rules. Which roles must be explicit? |
| Frame layout | Caption, buttons, borders and content placement | Named layouts are easy to test; declarative slots accommodate more frames but become a larger layout language. What cannot shared layouts express? |

**What justifies a template exception?** Requiring a prototype demonstration of an inexpressible structure
limits duplication. Allowing an exception whenever parameters become awkward makes drawing faster but risks
one complete template set per era. Review needs an example, its attempted parameterisation and the reason
for any exception; no threshold or template schema is settled here.

**Which skin is the first prototype, and how much does it cover?** One era across a small set of controls
provides a coherent visual comparison; one difficult control across several eras exercises parameter
reuse more directly. The former matches the recorded instruction to prototype one skin first. The exact
era, control set and acceptance criteria remain questions for review. No dimensions, gradients, font
metrics or historical palette values in this draft are measurements.

## Theme packs remain data

The requested direction is a pack that selects platform/application skins and supplies colour data,
without executable code. This does not yet authorise runtime templates, scripts or imported artwork.

**What can a pack select or override?** Selecting registered skin identifiers only gives a small, safe
contract but makes new structures a product contribution. Allowing validated parameter overrides supports
more user-created variations but needs versioned schemas, value bounds and compatibility rules. Allowing
new templates would conflict with the data-only boundary unless a separate declarative format were
explicitly designed and approved.

**How do existing colour-only packs migrate?** Implicit default selectors preserve existing files but
make their appearance depend on the chosen defaults. An explicit format version and migration makes intent
clear but requires upgrade and rollback handling. Unknown skin identifiers could reject the whole pack
with a useful diagnostic or use a documented fallback; the latter preserves usability but may hide an
incorrect preview. Which outcome is acceptable?

**Which values win?** Skin defaults followed by scheme overrides are flexible; restricting overrides to
IDE roles protects platform simulation. The choice must define system-table ownership, accent precedence,
partial overrides and light/dark variant consistency. It must also say whether changing a pack changes
runtime selection or only IDE presentation, and whether open windows update live or at the next run.

## Layered icons

The brief asks to retain original vector geometries, add named layers and skin shape replacements, and
keep a single-colour set for system-following and high contrast. Existing permissively licensed third-party
artwork remains subject to the current attribution rules; extracted application artwork remains forbidden.

**How does an icon declare layers?** An ordered list of geometry keys with brush-role keys is explicit
and reuses existing resources. A keyed drawing group is closer to Avalonia's rendering model but needs a
clear boundary between compiled artwork and pack data. Either needs common bounds, ordering and a rule
for missing layers; neither representation is chosen here.

**How are layer brushes named?** Semantic roles such as neutral ink, accent and warning reduce palette
size and support accessibility. Icon-specific roles give drawing control but multiply pack keys. A hybrid
can share defaults with exceptions but needs precedence rules. Example role names here are illustrative,
not new public resource keys. Run, break and stop already have semantic colour requirements to retain.

**How does a skin replace one shape?** Replacing an entire icon by its stable key is straightforward but
duplicates unaffected layers. Replacing a named layer keeps other shapes shared but makes layer names a
compatibility contract. Should overrides live only in registered compiled skins, or may data select from
a registered shape catalogue? Accepting new path data would require a separate decision beyond today's
pack parser. Call sites should be assessed through both declarative resources and the existing factory.

**How is monochrome selected?** A dedicated single-colour geometry per icon gives predictable silhouettes
but requires more artwork. Flattening layers into one brush shares shapes but can lose distinctions where
layers overlap. Review must choose the system-following/high-contrast selection and missing-override
behaviour, including how existing semantic transport colours remain accessible.

## Host-native running programs

Host-native is the default requested in the brief. The proposed input is Avalonia platform settings for
accent colour and light/dark preference, combined with each skin's table. This does not establish that
Avalonia supplies every historical system colour, metric or native frame detail.

**What does host-native mean on each supported desktop?** A shared renderer using host preferences is
portable and predictable but approximates native appearance. Host-specific templates can fit more closely
but increase maintenance and require verification on each host. Native OS frames preserve host behaviour
but may not combine with simulated client controls. The name, scope and limits need an explicit decision.

**How are unavailable settings and live changes handled?** A documented skin fallback is deterministic;
retaining the last known settings avoids abrupt changes but needs startup and persistence rules. Immediate
updates track the host; applying at the next run keeps a running program visually stable. High contrast,
missing accent data, multiple windows and a host preference that conflicts with an explicit era selection
need agreed precedence. The current IDE's deterministic theme variant must not be weakened accidentally.

## Clean-room drawing process — proposed checklist

This is a process draft for approval, not evidence that a drawing has passed review.

- [ ] Write a description of the intended appearance and behaviour before drawing. Record the author,
  date and factual sources; omit copied paths, assets and tracing instructions.
- [ ] Draw from that description without viewing reference artwork during drawing. Keep the description
  and original editable drawing together so the work can be reviewed.
- [ ] Record provenance: author, date, source description, tools, original geometry files, and any
  separately licensed material with its licence and attribution. Do not extract another application's art.
- [ ] Have someone other than the artist compare the result to references only to check that it is not
  a copy. Record reviewer, date and outcome. A rejected drawing returns to an independent drawing pass.

**Where does provenance live?** A sidecar per asset is easy to audit but adds files; one manifest is easy
to search but can conflict across contributions. **What separation is required between describer and
artist?** Different people provide stronger separation but cost availability; one person using a written
brief is easier to organise but needs a clearer exposure record. **How are failed comparisons handled?**
A fresh description and redraw avoids reference-guided edits; constrained feedback is quicker but may
turn review into copying guidance. These choices need review; the checklist grants no licence clearance.

## Font policy question

**May openly licensed OFL fonts be bundled, and under what checks?** Keeping today's permitted set leaves
fonts to the host and avoids a new redistribution policy, but yields variable metrics and availability.
A font-specific exception could permit reviewed font files with recorded source/version, licence text,
attribution and any applicable naming/modification conditions; it needs an asset inventory and a check for
packaged outputs. Broadening the NuGet allowlist alone is simpler but neither inventories loose fonts nor
captures their individual obligations. Which approach is approved, and who reviews each font's actual
licence? No font, legal conclusion, OFL exception or allowlist change is included in this draft.

## Revisions after prototype and review

| Document | Revision to consider after decisions | Outstanding dependency |
|---|---|---|
| `openspec/specs/icon-system/spec.md` | Replace single-path/single-colour-only and no-per-theme-artwork rules with layered geometries, brush roles, shape replacement and monochrome fallback; retain originality/licence rules and factory access. | Layer schema, replacement granularity, accessibility behaviour. |
| `openspec/specs/theme-packs/spec.md` | Describe skin selectors alongside colours, resolution order and migration; replace the always-light canvas rule with the approved preview/platform boundary. | Pack schema, defaults, runtime/designer selection and update policy. |
| `CLAUDE.md` | Describe Win98 chrome as removed as a default and returning as an opt-in skin when implemented; reconcile deterministic IDE variants, canvas scope and template restrictions. | Prototype evidence and approval; no implied restoration of old dependency controls. |
| `scripts/package-licences.tsv` and licence guards | Record an approved font policy at its actual enforcement boundary, if an exception is accepted. | Font redistribution decision and inventory design. |

Future live-spec requirements belong inside each spec's `## Requirements` section. This draft does not
revise those contracts in advance of approval. Feature and language inventories retain their current
statuses and counts because this run changes no behaviour.

Review still needs the first prototype's drawings and provenance, a record of which parameters worked
and which required template exceptions, answers to the questions above, and corresponding test plans.
Useful later checks include two independent IDE/runtime selections, preview/execution agreement, host
preference changes, old pack loading, one icon shape override, high contrast, fractional display scaling
and installed-font absence. These are proposed checks, not tests run or evidence of fidelity.
