## 1. Areas

- [x] 1.1 A registry of areas, each checked whole and refused whole, with both owners named in the log
- [x] 1.2 Merge each area into the layer it has a file for; its English joins the canonical fallback
- [x] 1.3 Re-apply the active language when an area comes or goes, on the UI thread
- [x] 1.4 Filter profile overrides as each layer merges
- [x] 1.5 Keep, save and later place user overrides of keys no area holds now
- [x] 1.6 List the areas' keys in the Translation Editor

## 2. Host

- [x] 2.1 `IHexIdeHost.RegisterLanguagePacks`, from the caller's own assembly
- [x] 2.2 Deny by default; the provider's grant names its families and the Desktop composition's origin
- [x] 2.3 No grant's family covers a key the consent and trust dialogs read, a family their code reads by name, or a
  protected key

The provider's entry type registers its area in #5395, when the provider has one.
