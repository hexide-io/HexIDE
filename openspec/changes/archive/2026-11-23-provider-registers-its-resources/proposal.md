## Why

Slice 2.7 of the move to HexIDE.Basic (#3411) takes the IDE's `App.axaml` off the Basic provider's control themes, which it merged by
`avares://HexIDE.Runtime/…` (#5396). The provider has to add them itself, through the route phase 3's bundled add-in will use, so the
IDE no longer names it. That route is new add-in API on the trust boundary, and any add-in could reach it once it ships.

## What Changes

- `IHexIdeHost.RegisterResources(Uri)` adds a resource dictionary from the caller's own assembly.
- **Deny by default.** Only an owner HexIDE grants it to may register, and only keys in the families its grant names
  (`RegistrationGrants`, in HexIDE's source). The same table carries #5394's language-pack families.
- **It only adds.** Every key, in merged and theme dictionaries too, is read before anything merges, and one refused key
  refuses the whole dictionary. Refused:
  - a `Str.*` key;
  - a key the IDE or another owner holds;
  - a type key outside the grant or the caller's assembly;
  - any other kind of key;
  - a root that isn't a resource dictionary.

  Each refusal is logged naming the key and both owners.
- **Only through an owned host.** The IDE's own host refuses the call. An add-in's owned host passes its origin and its calling assembly.
- **The provider's entry type,** `BasicProvider : IAddin`, registers its three dictionaries. `HexIDE.Desktop` composes it at startup with an
  owned host under a composed origin (`AddinOrigin.IsComposed`). That isn't first-party, which means signed by the first-party key.
- **Phase 3 switch.**
  - When the provider ships as a bundled add-in, its grant changes from `Composed: true` to a first-party signed package with the id
    `hexide.basic`.
  - The composed origin and `BasicProviderComposition` go.
  - `RegistrationGrants.For` already matches a non-composed grant only for a first-party origin.

## Impact

- Affected spec: `addin-system`.
- Code:
  - `IHexIdeHost`, with a `PublicAPI.Unshipped.txt` line;
  - `HexIdeHost`, `AddinOwnedHost` and `AddinOrigin`;
  - the new `AddinResourceService` and `RegistrationGrants`;
  - `DISetup` and `App.axaml`;
  - `HexIDE.Runtime/BasicProvider.cs`;
  - `HexIDE.Desktop/BasicProviderComposition.cs`, `DesktopStartup.cs` and its project reference to `HexIDE.Runtime`.
- #5388's list loses `HexIDE`'s rows for the three dictionaries, and gains the `HexIDE.Desktop → HexIDE.Runtime` row, which hexide-int agreed to.
