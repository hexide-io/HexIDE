# Tasks

## 1. Load
- [x] 1.1 Populate `MenuComponentClass.SubItemsProperty` from nested `Begin VB.Menu` blocks in `FormDeserializer`
- [x] 1.2 Keep every menu in the flat `Components` list as well, so existing consumers are unaffected
- [x] 1.3 Record the depth of non-menu nesting separately from menu nesting

## 2. Save
- [x] 2.1 Walk `SubItemsProperty` in `FormSerializer` and emit nested `Begin VB.Menu` blocks
- [x] 2.2 Indent each level to match VB6, verified against the corpus rather than assumed
- [x] 2.3 Emit each menu exactly once — a menu reachable from both the flat list and a parent must not double-write

## 3. Gate
- [x] 3.1 Narrow the unfaithful-save condition so menu-only nesting no longer marks a form read-only
- [x] 3.2 Confirm a form with a populated container still marks read-only (#84 is not fixed by this)

## 4. Verify
- [ ] 4.1 Round-trip the six menu templates from VB6's `VB98\Template` tree and diff against the originals
- [ ] 4.2 Update the corpus baseline in `SerializationCorpusTests`
- [ ] 4.3 Confirm a menu still renders and still fires its Click handler at runtime after a load
