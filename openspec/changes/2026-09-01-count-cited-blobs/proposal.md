# Count the citations the .frm makes, not the blobs the companion yielded

## Why

A `.frm` cites its companion by offset — `Icon = "Form1.frx":0000`. Open that form with the `.frx` moved
aside and HexIDE shows no banner, logs nothing, and saves on Ctrl+S with **every citation stripped**. The
`.frx` is left untouched and now orphaned. This is #146, and unlike #143 it damages the developer's own
file, at its original path, with no Save As and no unusual gesture.

The gate's only companion-aware detector asked the companion how much it held:

```csharp
var loadedBlobCount = frxBlobs?.Count ?? 0;
if (form.HasUnmodelledBinaryProperties || capturedBlobs.Count < loadedBlobCount)
```

With no companion, `LoadCompanionBlobs` returns null, so the test is `0 < 0` — false, forever. The check
was structurally unable to detect the absence of the thing it was counting.

**The other arm was dead code.** `FormDeserializer.cs:118` tested the reconstructed subtree lines for a
citation, but `ReconstructRawSubtree` strips every FRX-citing property group *before* returning — so the
condition could never be true. It had never fired since it was written. The count was carrying the entire
requirement alone, and the count could not see this case.

And it launders exactly as #143 did: reopen the damaged `.frm` and `CanSaveFaithfully` is **true**, because
the citations that would have flagged it are precisely what was removed.

`Icon` is on essentially every form VB6 ever wrote, so this is the common shape, not an exotic one.

## What Changes

- The expected blob count is derived from **what the `.frm` cites** (`FrxDeserializer.CitedOffsets`,
  de-duplicated), not from what the companion happened to yield. The citations live in the `.frm`, so the
  check no longer depends on the file that may be the missing one.
- The dead detector is repointed at the component's own raw properties, where the citations actually are,
  so the unmodelled-owner case is caught by its own flag again rather than resting on the count.
- This also closes a variant nobody had noticed: a **truncated** companion. An out-of-range offset is
  filtered out of the blob dictionary *and* fails to extract, so under the old count both sides fell to the
  same lower number and agreed.

## Impact

- Modifies `serialization-round-trip`: the *modelled property* scenario asserted the file is **not** held
  read-only, which was only ever true when the citation could be honoured. It is restated with that
  condition, and a scenario is added for a companion that is absent or short.
- No new localization keys — the refusal reuses the existing message and surfaces through the existing gate.
- **Widens the refusal**, as the requirement's own rationale says it should: forms that used to open freely
  and save lossily now open read-only. The corpus is unaffected — 21/22 VB6-authored forms still round-trip,
  because a form that cites nothing is untouched by a citation count.
- **Not a fix for the underlying loss.** A form whose `.frx` is genuinely gone still cannot be saved; it is
  now refused instead of silently stripped. #60 is what would make such a form savable.
