## Why

The Basic provider is leaving the IDE (#3411), and its strings go with it into language packs of its own, under the
same 100% parity guard (decided by Jen on 2026-11-22). The IDE then needs to read text it doesn't ship, in every
layer it reads its own, without letting whatever supplies that text change what the IDE says. #5394 builds that
mechanism; #5395 moves the provider's 112 keys into it.

## What Changes

- **A language-pack area** is a folder of `{id}.json` files in the IDE's pack format, whose keys only its owner holds.
  It's registered through the add-in host. The localization service merges each area into the layer it has a file
  for, and its English joins the canonical fallback.
- **Who may register is denied by default.** Grants are keyed by owner in IDE source, never in a manifest, and name
  the key families each owner may supply. This is the same grant record `RegisterResources` uses (#5396).
  - **Phase 2:** the provider's grant names the Desktop composition's origin, not a first-party signature.
  - **Phase 3 switches that grant** to first-party signed plus the id `hexide.basic`, and the composition origin goes
    away (the supervisor, 2026-11-23).
- **An area is checked whole and refused whole** for any key that is outside its grant, isn't `Str.`, is held by the
  IDE or another area, or has blank English. It is also refused for a language-file key its own English lacks, or a
  translation whose placeholders differ. Each refusal is logged naming both owners.
- **The user's translations keep overrides of keys no area holds now.** They save them and place them when an area
  registers, so a translation is no longer lost at the language's next save. A tier-2 pack is installed as just such
  a file.
- **Profiles' overrides are filtered as each layer merges,** not at load, since an area registers after the profile
  loads.

## Impact

- Affected spec: `localization`.
- Code: `HexIDE.Core` (`IHexIdeHost.RegisterLanguagePacks`), `HexIDE` (the area registry, the localization service,
  the user's translations, the Translation Editor, the add-in host and its grant), the provider's entry type.
- Phase 3 must switch the provider's grant, as above.
