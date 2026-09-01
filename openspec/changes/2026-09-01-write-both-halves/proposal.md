# A save writes both halves of a designer file, or neither

## Why

A `.frm` and its `.frx` are not two files that happen to sit together — they are one artifact split in two.
`FrxSerializer` gives each record the offset of wherever it lands, and the `.frm` cites those offsets, so
the citations are only meaningful against the exact companion produced alongside them.

`SerializeFormToFile` wrote the text **unconditionally** and then let `WriteCompanionBinary` decide, on a
measure of its own, whether to write the companion. When that refusal fired, the `.frm` went to disk citing
freshly renumbered offsets beside a companion still holding the **old** partition. And because a form's
faithfulness is re-derived from its own citations, the damaged pair reopened as **faithful** — no banner,
freely editable, the mapping wrong. This is #148, and it needed no unusual gesture: dropping a
blob-bearing control and pressing Ctrl+S was enough.

The two measures could never agree, because they answered different questions with different instruments:

| | measures | using |
|---|---|---|
| the load gate | what the file cites against what reached the model | the citations, from the `.frm` |
| the save refusal | a flat length-prefix walk of the produced companion against a load-time constant | a reader documented as wrong for `List` and `ItemData` |

That second reader called VB6's own `ODBC Log In` unsavable on **every** save while it reproduced its
companion byte for byte — a standing false positive, and the seed of the corruption.

## What Changes

- **One ruler, asked once, before either half is written.** Whether HexIDE can reproduce a file is a
  question about the file, so it is answered where the file is read — `CanSaveFaithfully`. The save path's
  only remaining job is to keep the two halves in step. The companion write loses its second, independent
  refusal.
- **Companion first.** Two `File.Move` calls cannot be made atomic, so the order decides what an
  interruption leaves. Text first leaves a new `.frm` whose renumbered citations all resolve inside the
  larger stale companion — to the wrong records, silently. Companion first leaves the old `.frm`, whose
  citations may now overrun the file and be refused at load. A crash should fail towards the outcome the
  developer is told about.
- **A companion nothing cites is not deleted.** A form citing nothing produces no blobs on every save, and
  reading that as "the developer cleared the last picture" destroyed a file the form never referenced. The
  old count comparison blocked this by accident; the guard is now stated in terms of what was cited.
- **One record cited twice is written once.** The writer collected blobs per property while the offset map
  is keyed by reference, so two controls sharing an image wrote the bytes twice and left both citations
  pointing at the second copy — a file larger than VB6 wrote it, no longer round-tripping.

## Impact

- Modifies `serialization-round-trip`: the previous-version guarantee is restated over the **pair** rather
  than the single file, and gains scenarios for the interrupted pair, the uncited companion, and the shared
  record.
- **No new localization keys.** The refusal reuses the existing message, whose text — "Its file on disk is
  unchanged" — is now literally true on every path, which it was not while the text half was written anyway.
- Removes a live false positive: `ODBC Log In` was denied its companion write on every save.
- Corpus unchanged at 21/22 VB6-authored forms.
- **Not covered, and now more visible:** `SaveModule` still has no `CanSaveFaithfully` gate above its own
  picker (#147), and a UserControl's read-only banner is not wired at all — `IsReadOnly` binds to a form
  definition that is null for a module.
