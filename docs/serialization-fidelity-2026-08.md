# VB6 serialization fidelity — red-team findings, 2026-08-11

Produced by a 25-agent red team (12 hunting lanes, each adversarially verified, plus synthesis) against a
corpus of **Microsoft-authored VB6 files shipped with VB6 itself** (`VB98\Template`). 66 findings survived
verification: 6 BLOCKING, 17 CORRUPTING, 17 LOSSY, 26 COSMETIC.

The method matters: `vb6.exe` cannot author files from the command line (`/make` only compiles), so this is
**not** a diff against VB6's output. It does not need to be — VB6 wrote the corpus, so any byte that changes
on load+save is a defect by definition. Questions that genuinely need the running VB6 IDE are isolated in §6.

## Corrections to the agents' evidence (verified by hand before publishing)

The mechanisms below were confirmed; several **evidence counts were not**. Treat the counts here, not in
the body:

| Claim in the body | Actual, verified |
|---|---|
| `VB98\Common\Tools\APE\Source` as a corpus root | **Does not exist** on this install. Nothing under `VB98` matches `APE*`. |
| C2 affects "28/28 `.cls` files" | The install has **2** `.cls` files. Both are affected — so 2/2, and the defect is real: `Complex Data Consumer.cls` loses `DataBindingBehavior = 2 'vbComplexBound` and `Data Source.cls` loses `DataSourceBehavior = 1 'vbDataSource`; both have `VB_Exposed` flipped True→False. |
| C8 affects "11 files" with non-ASCII | **1** file in the whole `VB98` tree (`Web Browser.frm`), confirmed byte-level. The *mechanism* is real and verified — `ProjectService` passes no `Encoding` to any read or write — so any non-English project is exposed; the corpus simply barely exercises it. |
| L1 "790→12" and B2 "deletes the .frx" | **Confirmed empirically.** Loading `Button ListBox.frm` reads 5 blobs from its 2122-byte `.frx`, and `FormSerializer` returns `null` binary — which the write path treats as "delete the companion". `Splash Screen.frx` goes 790→12 bytes. |

Diff *counts* in the body (105, 121, 200, 272 differing lines) come from a line-by-line comparison with no
alignment, so a single insertion cascades. They indicate "badly wrong", not a magnitude — the harness note
in §7.5 says the same.

The harness is `IDE/HexIDE.Runtime.Tests/SerializationCorpusTests.cs`. It now scores VB6-authored and
HexIDE-authored files separately, because the original "3/25 pass" was **entirely** HexIDE's own `demo/`
output passing against its own defects. The true figure is **0/22 VB6-authored files round-trip**.

---

# HexIDE VB6 Serialization — Remediation Plan

## 1. The headline

HexIDE's `.frm`/`.ctl` writer is a *regenerator*, not a round-tripper: it rebuilds every designer line from a partial in-memory model, so **0 of 22 Microsoft-authored corpus files survive a save byte-for-byte** (the 3 "OK" results are HexIDE's own `demo/*/Form1.frm` output, `SerializationCorpusTests.cs:41-46`). Three of those 22 cannot be opened at all — a single `Object = "{GUID}#2.0#0"; "x.ocx"` header line throws `InvalidOperationException("Stack empty.")` at `VbFrmFormatDeserializer.cs:103/105`, which takes out every VB6 form hosting an ActiveX control. Most of the remaining damage is cosmetic whitespace that a maintainer could ignore, but **five defects change or destroy user data silently** — a `Ctrl+S` deletes `Button ListBox.frx` (2122 bytes) and `Mover ListBox.frx` (516 bytes) outright (`ProjectService.cs:857-864`), truncates `Splash Screen.frx` from 790 to 12 bytes, rewrites `ScaleWidth` on a Pixel-ScaleMode UserControl by a factor of 15, resets `VB_Exposed`/`VB_Creatable`/`MultiUse` on 28 of 28 corpus `.cls` files, and flattens every menu tree and container into a flat sibling list.

---

## 2. Severity table

Effort: **S** ≤ ~2h, **M** ~½–2 days, **L** > 2 days / needs design.

### BLOCKING — file cannot be opened, or a save destroys a file on disk

| # | Finding | Root cause (one line) | Corpus | Fix |
|---|---------|----------------------|--------|-----|
| B1 | `Object = "{GUID}#v#l"; "file.ocx"` header throws `"Stack empty."` | `VbFrmFormatDeserializer.cs:103` and `:105` both `componentStack.Peek()` before any root `Begin` has been read | 3/25 (100% of parse failures) | **S** (guard) + **S** (`FormDefinition.HeaderLines` + emit between `FormSerializer.cs:48` and `:52`) |
| B2 | Saving a form whose blobs are all unmodelled **deletes** the `.frx` | `FormSerializer.cs:40-43` leaves `frxContent` null at 0 blobs; `ProjectService.cs:860-864` reads that as "delete the companion" | 2 destroyed today (2122 B + 516 B), 2 more once B1 lands | **S** for the guard; **M** for pass-through |
| B3 | Nested `BeginProperty` (OCX property bags) → `NullReferenceException` | `VbFrmFormatDeserializer.cs:16-19` tracks one open block in 3 scalar fields; the inner `EndProperty` nulls them, the outer `:61` dereferences null | 2/25, both masked behind B1 | **M** (replace 3 fields with a `Stack<(name, lines, dict)>`) |

### CORRUPTING — bytes VB6 authored are replaced with different, wrong bytes

| # | Finding | Root cause | Corpus | Fix |
|---|---------|-----------|--------|-----|
| C1 | Nested `Begin` blocks flattened — menu trees and container membership destroyed | `FormDeserializer.cs:95-96` + `:268-269` push every descendant into one flat `List<ComponentInstance>`; `ComponentInstance` has no parent field; `FormSerializer.cs:59-67` emits a flat loop | 12/22 (9 of 19 parseable); all 6 menu templates | **L** |
| C2 | Class-module header regenerated from a hardcoded literal | `ModuleFileFormat.cs:69-70`/`:76-77` skip `BEGIN..END` and all attributes; `:25-38` re-emits a fixed string | **28/28** `.cls` — `VB_Exposed` True→False ×19, `VB_Creatable` False→True ×12, `MultiUse` ×3, `DataBindingBehavior`/`DataSourceBehavior` ×1 each, spurious `MTSTransactionMode` ×18 | **M** |
| C3 | `Client*` aliased onto `Left/Top/Width/Height` — client rect overwritten by outer rect | `FormDeserializer.cs:101-108` renames onto the same `PropertyClass`; `FormSerializer.cs:82-102` regenerates both families from one number | 1 value-corrupted (`Dialog.frm`: 3195→3600, 6030→6150, 3750→3405, 2760→2700); 18 gain fabricated outer rects | **L** |
| C4 | `ScaleWidth`/`ScaleHeight` discarded on load, fabricated from the client rect in twips | `FormDeserializer.cs:109-110` `continue`; `FormSerializer.cs:87/:92` invent replacements | 3 root-level (Colorful Control.ctl **15×**: 320×240 → 4800×3600 with `ScaleMode = 3 'Pixel` preserved; About Dialog +45%; Log in Dialog) | **M** (verbatim) / **L** (modelled) |
| C5 | Font `Charset` hardcoded to `2` (SYMBOL) on write | `VbFrmFormatSerializer.cs:109` literal `2`; `VBFont` has no Charset field | 13/13 corpus Charset lines are `0` (ANSI) | **S** (with C6) |
| C6 | Fractional font sizes truncated — `VBFont.Size` is `int` | `VBFont.cs:10`; `FormDeserializer.cs:239-249` `Convert.ToInt32` | 10 lines: 9.6→10 ×4, 8.25→8 ×4, 15.75→16, 32.25→32 | **S** (fold into C5) |
| C7 | Binary props of unknown controls dropped **and** blob destroyed on `.frx` rewrite | `FormDeserializer.cs:298-299` `continue` (no `errorSink`); blob absent from `CollectBlobs` | Splash Screen: `Picture = "Splash Screen.frx":000C` line gone + 774 bytes destroyed | **M** |
| C8 | ANSI source bytes read as UTF-8 → U+FFFD → written as `EF BF BD` | `ProjectService.cs:255/:322/:488/:900` — no `Encoding` argument anywhere | **11 files**: all 10 APE `.vbp` (`©`, `®`), `Web Browser.frm:102` (`0xAF 0xAF` in a control value) | **S** (+ migration story) |
| C9 | Unknown controls re-parented to the form root | `FormDeserializer.cs:89` hardcoded `ReconstructRawSubtree(…, 1)`; form-level `UnknownChildSubtreeTexts` | 2 today (`imgLogo` leaves Frame1, keeping frame-relative `Left=360/Top=795`), 3 more after B1 | subsumed by C1 (**L**) |
| C10 | Menus never reach the Menu Editor; a Menu Editor session appends a **second, disjoint** menu set | `TopLevelMenu` (`FormEditViewModel.cs:66`) written only by `EditMenu()`'s OK path; originals stay in `AllComponents` and are re-emitted by the write-back at `:113-119` | 6/6 menu templates (71 menu instances rendered as 0-sized canvas items) | **M**, gated on C1 |
| C11 | `ReloadFormFromDisk` never syncs `UnknownChildSubtreeTexts` | `ProjectService.cs:491-497` adopts only `Code` + `Components` | IDE path; resurrects externally-deleted controls | **S** |

### LOSSY — information is gone after a save, but nothing on disk is *wrong*

| # | Finding | Root cause | Corpus | Fix |
|---|---------|-----------|--------|-----|
| L1 | Only `Form.Icon` + `PictureBox.Picture` are modelled as blobs — `DragIcon`, `ItemData`, `List`, `CommandButton.Picture`, `MouseIcon` are logged and dropped | `FormDeserializer.cs:260-264` `continue`; `grep DragIcon` over the repo = **0 hits** | 8 dropped lines across 3 files; `.frx` shrinks 790→12 and 16→12 | **L** |
| L2 | `Attribute VB_Description` and any 6th+ header attribute deleted | `ModuleFileFormat.cs:76-77` eats every contiguous `Attribute ` line; `:25-38` emits only 5 | **16/28** `.cls` | **S** |
| L3 | OCX `Object` declarations have no model slot — write side missing | No header field on `VBSerializedComponent`/`FormDefinition`; `VbFrmFormatSerializer.cs:16-24` emits only `VERSION 5.00` | same 3 as B1 | **S**, lands with B1 |
| L4 | `.frx` is a heterogeneous format — `List`/`ItemData` use a **2-byte** count, not a 4-byte length | `FrxDeserializer.cs:8-9` asserts uniformity; `:24-38` is a blind linear walk | `GROUP.FRX` breaks at `pos=1090` leaving 18 bytes unread; `ODBC Log In.frx` manufactures a phantom key at 12 and can never key `0x000E` | **M**, blocks L1 |
| L5 | `.vbp` `Name=` / `Startup=` lose their quotes | `ProjectSerializer.cs:39/:74` don't re-quote what `ProjectDeserializer.cs:63-64` stripped | 17/17 for `Name=`; **5/17** produce `Startup=Sub Main` (unquoted, contains a space) | **S** |
| L6 | `\` → `/` in `.vbp` item paths on Linux/macOS | `ProjectSerializer.cs:46/:54` use `Path.GetRelativePath`, which emits `Path.DirectorySeparatorChar` | **9/17** `.vbp` (all APE projects share `..\AEInclud`) | **M** |
| L7 | `ComboBox`/`ListBox` `List` dropped with **no diagnostic** — modelled name, unmapped CLR type | `FormDeserializer.cs:126-251` has no `List<string>` arm and no terminal `else`; `BuildKnownPropertyNames` still calls it "known" so the verbatim path skips it too | 1 line, but the harness logged **1** error for that file, not 2 — that count is the proof | **S** (+ mandatory `List<string>` arm in `VbFrmFormatSerializer` or saves throw at `:89`) |
| L8 | `LockControls = 0` dropped (special-cased out of verbatim, written back only when true) | `FormDeserializer.cs:113-119` + `FormSerializer.cs:55-56` | 0/25 — latent | **S** |
| L9 | `VERSION` header discarded on read, hardcoded `VERSION 5.00` on write | `VbFrmFormatDeserializer.cs:40-43`; `VbFrmFormatSerializer.cs:22` | 0/25 (all 28 files say 5.00) | **S**, but **oracle-gated** |
| L10 | Binary lines inside unsupported-control subtrees dropped **silently** | `ReconstructRawSubtree` (`FormDeserializer.cs:290`) takes no `IDeserializeErrorSink` | 1 today | **S** (log) |

### COSMETIC — byte differences with no change of meaning

| # | Finding | Root cause | Corpus | Fix |
|---|---------|-----------|--------|-----|
| K1 | Property names not padded to VB6's 16-column field | `VbFrmFormatSerializer.cs:59/:66/:70/:72/:76/:80/:85/:127` all emit one space | **~607 emitted lines**; rule verified 1268/1268 (`indent + (Object.?7:0) + max(16,len)`, always 3 spaces after `=`) | **S** |
| K2 | `Begin`/`BeginProperty` lines lose VB6's trailing space | `VbFrmFormatSerializer.cs:34`, `:104`, `FormDeserializer.cs:294` | **215/215** such lines in the harnessed corpus, 0 counterexamples; the line-2 diff on all 19 emitting files | **S** |
| K3 | No `'True`/`'False`/enum comment on bool + enum values | `VbFrmFormatSerializer.cs:68-86` — no comment parameter exists | **91 lines / 13 files** (36 of them from the hardcoded Font block) | **S** for bools; **M** for enums (needs K4) |
| K4 | No VB6 enum display-name table; C# names diverge — `FillStyles` is **transposed** (0=`Transparent` in code, `0 'Solid` in the corpus) | `VBAlign.cs`, `VBStartupPosition.cs`, `VBTextAlignment.cs`, `FillStyles.cs:5-6` — no display data anywhere | blocks 14 of the 91 K3 lines | **M** — `Enum.GetName` would emit the **wrong constant's name** |
| K5 | Property order is C# declaration order, not VB6's alphabetical | `FormSerializer.cs:107` iterates `instance.BaseClass.Properties`; source order is captured at `VBSerializedComponent.cs:15` and thrown away at `FormDeserializer.cs:255-266` | all 19 emitting files | **M** |
| K6 | Alphabetical is intrinsic-only — OCX bags **and** `StdFont` blocks use fixed persistence order | 12 corpus `Font` blocks are `Name\|Size\|Charset\|Weight\|Underline\|Italic\|Strikethrough`; `VbFrmFormatSerializer.cs:107-113` already gets this right | constraint on K5 — a naive sort **regresses working code** | — |
| K7 | Unknown props relocated to a trailing block | `FormSerializer.cs:149-150` after the known loop closes at `:147` | all 19 | subsumed by K5 |
| K8 | `Client*`/`Scale*` hoisted past the property block in a hand-rolled order | `FormSerializer.cs:57` + `:82-102` (Width pair, Height pair, Top, Left) | all 19 | subsumed by K5 |
| K9 | `LockControls` has no ordered slot | `FormSerializer.cs:55-56` sits between the property block and the measurements | 0/25 — position happens to match | subsumed by K5 |
| K10 | Verbatim-replayed lines never re-indented → one block at two indent levels | `WriteVerbatimLine` (`VbFrmFormatSerializer.cs:130-133`) skips `WriteIndent()` | 12 have deep enough nesting; 9 observable | subsumed by C1 |
| K11 | Reconstructed unknown `Begin` lines lose the trailing space + wrong indent | `FormDeserializer.cs:292-294` synthesises from parsed tokens | 5 | **S** (store `RawBeginLine`) |
| K12 | Unknown subtree emitted at hardcoded depth 1 at the **end** of children | `FormDeserializer.cs:89`; `FormSerializer.cs:69-73` | 2 | subsumed by C1 |
| K13 | Twips → pixel-double × reciprocal → float noise | `FormDeserializer.cs:172-177` `1.0/15`; `FormSerializer.cs:126` | **2 of 192** distinct values (6684→6683.999999999999, 4056→4055.9999999999995); 9 lines / 5 files; drifts once more on save 2 | **M** |
| K14 | `.vbp` item order rebuilt from a fixed template; `Reference=` moved to the end | forms/modules stored in disjoint lists at `ProjectDeserializer.cs:78` vs `:85` — order lost at **parse** time | 17/17 | **M** |
| K15 | `PreservedItemLines` written without advancing `knownKeyCount` | `ProjectSerializer.cs:67-68` vs `ProjectDeserializer.cs:107-108` | 2/17 (`UserDocument=`) | **S** (one line) |
| K16 | `.vbp` extension tail loses its final CRLF | `ProjectDeserializer.cs:165` `.TrimEnd()`; `ProjectSerializer.cs:94` `Write` not `WriteLine` | 1/17 | **S** |
| K17 | `rawLine.Trim()` eats authored whitespace in `.vbp` values | `ProjectDeserializer.cs:43/:60` | 1/17, 1 line | **S** |
| K18 | Culture-dependent numeric formatting (latent) | `StringWriter` at `VbFrmFormatSerializer.cs:10` captures `CurrentCulture`; reader parses Invariant with `AllowThousands` | **0/25** — masked by `App.axaml.cs:28` | **S** — `new StringWriter(CultureInfo.InvariantCulture)` |
| K19 | `FrxImageHelper` sniffs the StdPicture stream header as image magic | `FrxImageHelper.cs:29-31` reads byte 0; real image magic is at **+8**, or **+24** with a CLSID | 2 rendering (the two files that *do* round-trip render blank) | **S** |
| K20 | `.vbp` `Name=` synthesised from the `.vbp` entry, never the file's `VB_Name` | `ProjectService.cs:347`/`:974` | **0/87** module entries mismatch — but the **harness** feeds the filename (`SerializationCorpusTests.cs:196`), producing 20 false `.bas` failures | **S** (harness) |
| K21 | Shared blob array written twice, both refs point at the second copy | `FrxSerializer.cs:20-23` `ReferenceEqualityComparer` + last-write-wins | 0 corpus; HexIDE-authored only (paste path `FormEditViewModel.cs:613`) | **S** |
| K22 | Shortcut/NegotiatePosition/Index/HelpContextID unmodelled on `VB.Menu` — lost via **clipboard**, not via save | `MenuComponentClass.cs:9`; `DesignerClipboard.cs:9-21` carries only `PropertyClass` values | 9 shortcut lines; 0 lost on plain save | **M** |
| K23 | `"phase 1"` in the error message should say Phase 3 | `FormDeserializer.cs:262` vs `openspec/specs/serialization-round-trip/spec.md:192` | 8 error lines misdirected | **S** — 1 grep hit, no test |

---

## 3. The cheap wins

The corpus score is gated by **two whitespace defects that dirty line 2 and line 3 of every file**. Nothing else can move the number until they land.

**Tier 1 — the two that unlock everything (≈2h total, near-zero test risk):**

1. **K2, trailing space on `Begin`/`BeginProperty`** — three string literals: `VbFrmFormatSerializer.cs:34`, `:104`, `FormDeserializer.cs:294`. Every existing assertion is substring-based (`SerializationRoundTripTests.cs:171/:249/:593`, `UserControlSaveRoundTripTests.cs:105`) and survives.
2. **K1, 16-column name padding** — route the 7 `WriteSimpleType` branches + `WriteRawProperty` through `name.PadRight(16)`. Verified against 1268/1268 corpus lines. No test asserts serializer output for exact equality.

These two alone take **no file** from DIFF to OK (line 4+ still differs everywhere), but they remove the noise floor that currently makes every diff unreadable.

**Tier 2 — the smallest set that actually flips files (≈1–1.5 days):**

3. **K3 booleans** — `'True`/`'False` with the verified 4-char value field. Covers ~77 of the 91 lines with no enum table needed.
4. **K5 + K7 + K8 + K9, source property order** — one change: carry `VBSerializedComponent.OrderedRawProperties` (already captured at `VbFrmFormatDeserializer.cs:62/:101-103`, thrown away at `FormDeserializer.cs:255-266`) onto `ComponentInstance` as a `SourcePropertyOrder`, and emit one ordered stream instead of the known/unknown split. This is the **single highest-leverage change in the plan** — it closes four findings and is a precondition for `Client*`/`Scale*` landing in the right place. Do **not** sort alphabetically: K6 proves OCX bags and `StdFont` blocks use fixed persistence order, and `VbFrmFormatSerializer.cs:107-113` is already correct.
5. **C5 + C6, one Font commit** — `VBFont.Size` `int`→`float`, add `Charset`, both read at `FormDeserializer.cs:226-231` and written at `VbFrmFormatSerializer.cs:108-109`.
6. **K13, round measurements to integer before writing** — 9 lines, 5 files.

**Expected new ratio.** Be honest about the ceiling here:

- After Tier 1 + Tier 2: the **6 menu templates** still fail on C1 (flattening), the **3 OCX files** still fail on B1, `Dialog.frm` still fails on C3, `Colorful Control.ctl`/`About Dialog`/`Log in Dialog` still fail on C4, `Splash Screen`/`About Dialog` still fail on C9/C1. That leaves roughly **`Control Events.ctl`, `Projects/Form1.frm`, `FRMDATEN.FRM`, `ADDIN.FRM`, `Button ListBox.frm`, `Tip of the Day.frm`** as the plausible flips — **3/25 → ~9/25**, i.e. 6 of 22 VB6-authored files, up from 0.
- Add **B1+L3** (the `Object` guard + header slot, **S+S**) and 1 more file parses cleanly (`Treeview Listview Splitter.frm` has no `BeginProperty` at all — verified `grep -c` = 0); the other two trade `"Stack empty."` for B3's NRE. → **~10/25**.
- Add **C1** (nesting, **L**) and the 6 menu templates plus `Splash Screen` come into range → **~16/25**. That is the realistic ceiling without C3/C4.

**Free wins to take regardless** (zero risk, no ratio movement): K23 (1 grep hit, no test), K15 (one line), K18 (`new StringWriter(CultureInfo.InvariantCulture)` — one line, fixes 5 sites), K16, L10 (add the error-sink log so the loss stops being silent), C11, K20's harness half (**turns the `.bas` lane from 20 failures to 20 passes — verified 20/20 — and stops it masking the real `.cls` defects**).

---

## 4. The hard core

### Needs design (do not attempt in three weeks)

- **C1 / C9 / C10 / K10 / K12 — the parenting model.** `FormDefinition.Components` is flat by construction (`FormDefinition.cs:43`) and `ComponentInstance` has no parent field (confirmed: the only other `partial class ComponentInstance` declaration is that same file). The flat list is load-bearing for `VBLoader.SpawnComponents` (`VBLoader.cs:35-80`), `FormEditViewModel.Initialize/ReloadFromModel`, `PasteControls`, and the save write-back at `FormEditViewModel.cs:113-119`. The correct shape — keep the flat list as the enumeration surface, add **only** a back-pointer consumed by the serializer — is known, but C10 shows the designer consequences ripple: a canvas filter that excludes menus must be a *separate filtered collection*, because `Components` is also the ordering key for the save write-back, and removing menus from it would be a **worse, LOSSY** regression.
- **C3 — the two rectangles.** `Left/Top/Width/Height` and `ClientLeft/ClientTop/ClientWidth/ClientHeight` are genuinely different rects (`Dialog.frm` proves the deltas: +405 height, +120 width, and the frame delta depends on `BorderStyle`/`ControlBox`, **neither of which is modelled** — `FormComponentClass.cs:11-20`). Giving the root its own client slots means re-pointing `FormComponentClass.cs:39-46`, which sizes and positions the *running* window from those slots. Runtime and integration tests asserting form geometry will shift.
- **C4 / L1 / L4 — the `.frx` model.** `FrxDeserializer`'s "flat sequence of 4-byte-length blobs" (`FrxDeserializer.cs:8-9`) is provably wrong, and the fix is not a patch: the reader must become **reference-driven** — parse the `.frm` first, collect every `"x.frx":HHHH` offset with the property that cited it, decode each record at its stated offset with the width that property implies. Three call sites move together (`ProjectService.cs:485`, `:888`, `SerializationCorpusTests.cs:134`), and the integer-overflow guard at `FrxDeserializer.cs:29-33` must be carried across. **L1's cheap-looking fix (declare `ItemData`/`List` as `byte[]` properties) does not work** — the reader cannot address offset `0x000E` at all.

### Needs a lot of mechanical work (tractable, just long)

- **K5's ordered stream** across every writer path, including the synthesized `Client*`/`Scale*` (`FormSerializer.cs:82-102`) which have no stored value to order by.
- **C2 + L2 — module header parsing.** Straightforward but touches 4 test sites (`ModuleFileFormatTests.cs:9-22/:24-32/:34-45`, `UnsavedChangesPromptTests.cs:155-161`). Must preserve the original comment text (`0   'False` vs `0  'NotPersistable`) and the absence of `MTSTransactionMode`, not just the values.
- **K4 — the enum display-name table.** 42 distinct property names carry comments; 20 distinct display strings observed. `Enum.GetName` is **not** an option: `FillStyles.cs:5-6` declares `Transparent = 0`, but `Colorful Control.ctl:16` reads `FillStyle = 0 'Solid` — reflection would emit a different VB6 constant's name. Table keyed on enum **type**, not property name (`BorderStyle` is three different enumerations). On a table miss, emit **no** comment rather than a fabricated one.
- **L6 — verbatim `.vbp` item paths** threaded through `ProjectService`'s three load passes (`:261-299`, `:304-336`, `:339-376`).

### Needs `vb6.exe` to settle a question first

- **K2 + K1's severity.** The corpus proves VB6 always *writes* the trailing space and the 16-column field; it says nothing about what VB6 *reads*. If either is required on load, both jump COSMETIC → **BLOCKING**. Circumstantial evidence for "accepts": `demo/neon-aurora` ships a HexIDE-written unpadded `Form1.frm` alongside a real vb6.exe-built `.exe`.
- **C1's severity.** After flattening, `mnuEditUndo` carries `Shortcut = ^Z` at top level. VB6's Menu Editor forbids shortcuts on top-level items. If the `.frm` loader enforces that, the saved file is **rejected**, not merely wrong.
- **Finding 17's severity.** Whether vb6.exe *rejects* a `.ctl` whose `VB.UserControl` block carries `Left/Top/Width/Height` (which HexIDE invents on both `Userctls/*.ctl`). If it rejects, those 2 files are BLOCKING.
- **L9 must not be fixed without the oracle** — if vb6.exe rewrites `VERSION 4.00` → `5.00` on its own save, echoing the original *is* the divergence.

---

## 5. Launch call

### Must fix before any public release

The criterion is **silent data loss on a user's own files**. Five findings meet it, and three are unambiguous:

| Must-fix | Why it's non-negotiable |
|---|---|
| **B2** | `Ctrl+S` **deletes** a `.frx` from disk. 2122 bytes on `Button ListBox`, 516 on `Mover ListBox`, verified un-gated (`ProjectService.cs:691-694` → `:466` → `:857-864`, no `IsDirty` check on the chain). This is destruction of a user's authored asset by an ordinary Save. **S** for the guard alone — gate the delete on whether a companion existed at load; `LoadCompanionBlobs` (`ProjectService.cs:880-889`) already records it in `baselineStore`. |
| **C2** | One File → Save Project rewrites **every** `.cls` in the project, edited or not (`ProjectService.cs:691-694`, no dirty gate), resetting `VB_Exposed`, `VB_Creatable`, `MultiUse`, `DataBindingBehavior`, `DataSourceBehavior`. These are VB6 **Instancing** settings. 28/28 corpus files. Silent, universal, semantic. **M**. |
| **C8** | Every non-ASCII byte becomes `EF BF BD`. Hits **every non-English-locale VB6 project**. `Encoding.Latin1` on the reads/writes in `ProjectService` is one afternoon; the migration story for files HexIDE already wrote in UTF-8 is the only real work. **S+**. |
| **B1 + L3** | Not data loss — the file won't open — but "open any VB6 project" is the headline promise, and *any* project using a single OCX fails it. The `.vbp` side already solves this (`SerializationRoundTripTests.cs:405-418` asserts `Object={831FDD16-…}` survives verbatim, with the comment "…so a project depending on an uninstalled OCX is not corrupted on save"). The `.frm` reader crashing instead is an **asymmetry inside one codebase**, and it is embarrassing to ship. **S+S**. |
| **L7** | `List` on a ComboBox/ListBox is dropped **with no diagnostic at all** — the harness logged 1 error for `ODBC Log In.frm`, not 2. Add the terminal `else { InvalidValue(); }` at `FormDeserializer.cs:126-251` so an unmapped property type can never fail silently again. **S** — but note the mandatory second half: a `List<string>` arm in `VbFrmFormatSerializer`, or the moment `ListProperty` holds a value the save **throws** at `:89`. |

Plus the free wins: K23, K15, K18, K16, L10, C11, K20-harness.

### Ship as documented limitation

- **All of COSMETIC.** Nobody's data is harmed by an unpadded property name. Fix K1/K2/K3 if the two hours are there (they read as sloppiness in a diff), but they are not a gate.
- **C1 / C9 / C10 (nesting + menus).** Large, and the damage is confined to *re-saving a VB6-authored form*. Mitigate instead: **make loading a form with nested `Begin` blocks or an unknown control class open it read-only, or show a "this form will not save faithfully" banner.** That converts a CORRUPTING defect into a refused operation, which is the correct posture for alpha.
- **C3 / C4 / L1 / L4.** Same treatment — the read-only gate covers them all. C4's 15× `ScaleWidth` error on `Colorful Control.ctl` is genuinely bad, but it only fires on save.
- **L5, L6, K14** — `.vbp` shape defects. Document; none loses a value except L5's 5 unquoted `Startup=Sub Main` lines, which are **oracle-gated** (§6, Q6).

### Alpha vs beta

**Call it alpha.** Three independent reasons, in order of weight:

1. **Beta implies "your data is safe."** Until B2 and C2 land, an ordinary Save deletes binary assets and rewrites class Instancing settings on files the user did not touch. That is disqualifying for beta regardless of the corpus ratio.
2. **The guarantee under test is currently 0/22 on Microsoft's own files.** Even after the cheap wins, ~9/25. Beta on a round-trip promise with a two-thirds failure rate against the reference corpus is not defensible.
3. **The oracle questions are unresolved.** Three COSMETIC findings would become BLOCKING if vb6.exe turns out to require the trailing space or the 16-column field. Shipping beta while three severities are undetermined is a bet, not a decision.

Alpha with an explicit, honest limitation note — "HexIDE can open and run VB6 projects; saving is not yet round-trip faithful and is gated read-only for forms it cannot reproduce" — is a strong, defensible position. It is also the only framing under which the read-only gate reads as *engineering discipline* rather than *a missing feature*.

**Three-week plan:** week 1 the five must-fixes + the free wins; week 2 the read-only gate + K1/K2/K3/K5; week 3 the oracle session (§6) and re-baselining the harness (§7). C1 and the `.frx` model are post-launch, and the plan should say so out loud.

---

## 6. Open questions for the oracle

Every one is a sub-five-minute experiment in the VB6 IDE at `C:\Program Files (x86)\Microsoft Visual Studio\VB98\VB6.EXE`. Record results in `docs/vb6-fidelity-oracle.md`.

| Q | Experiment | Decides |
|---|---|---|
| **Q1** | Take a HexIDE-saved `.frm` (unpadded names, no trailing space after `Begin`). Open it in VB6. Does the form load? | **K1 + K2: COSMETIC or BLOCKING.** Highest-value question in the list — three findings hang on it. |
| **Q2** | Hand-edit a `.frm` so `mnuFileNew` is a top-level `Begin VB.Menu` **retaining** `Shortcut = ^N`. Open in VB6. | **C1: CORRUPTING or BLOCKING.** If the loader enforces "no shortcut on a top-level item", the flattened file is rejected. |
| **Q3** | Hand-edit `Userctls\Colorful Control.ctl` to add `Left = 0 / Top = 0 / Width = 4800 / Height = 3600` to the `Begin VB.UserControl` block. Open in VB6. | Whether HexIDE's invented outer rect on `.ctl` roots is **BLOCKING** (2 files) rather than merely wrong. |
| **Q4** | Open `Forms\Dialog.frm` (the one file with both rects), note the on-screen size. Edit so `Height = ClientHeight` and `Width = ClientWidth`, reopen. Does the client area visibly shrink by ~405×120 twips? | **C3**: which family vb6.exe honours, and whether HexIDE's `Height == ClientHeight` output is geometrically absurd or ignored. |
| **Q5** | Set `ClientWidth = 6683.999999999999` in a `.frm` and open it. | **K13**: LOSSY (accepted) or BLOCKING (rejected). Fractional `ScaleWidth` is legal (`About Dialog.frm:15 = 5380.766`); fractional `ClientWidth` appears nowhere in the corpus. |
| **Q6** | Edit a `.vbp` to `Startup=Sub Main` (no quotes). Open the project, check Project Properties → Startup Object. | **L5**: 5 files LOSSY or CORRUPTING. |
| **Q7** | Save a `.vbp` from VB6 on a Mac-style path (`Form=..\Sub\F.frm` → `../Sub/F.frm`). Does VB6 open it? | **L6**'s consequence. Do **not** repeat "unopenable in VB6" until this is run — Win32 accepts `/`. |
| **Q8** | New Standard EXE. Toggle Lock Controls **on** then **off**. Save. Does the `.frm` contain `LockControls = 0`, or is the line absent? | **L8**: reachable hole or unreachable theory. |
| **Q9** | Drop a ComboBox, add 3 design-time `List` items and non-zero `ItemData`. Save. Hex-dump the `.frx`. | **L4**: settles the 2-byte record layout (is `09 00 / 09 00` byte-size + char-count, or entry-length + string-length?). Unblocks L1. Also add a `Text` > 255 chars on a multiline TextBox in the same form to test for a third record shape. |
| **Q10** | Set a form's `Picture` to a WMF, an EMF, a GIF and a JPEG in four separate forms. Dump each `.frx`. | **K19**: the full set of CLSID prefixes and whether `6C 74 00 00` is a fixed signature or a variable field. |
| **Q11** | Open a VB4/VB5-era `.frm` (`VERSION 4.00`) in VB6 and save. Does the header become `5.00`? | **L9**. If yes, "fixing" the VERSION echo would *introduce* a defect. **Do not fix L9 until this is answered.** |
| **Q12** | In one form, set `Appearance = 1`, `MousePointer = 5`, a CheckBox `Value = 2`, `Shape` BorderStyle 2–5, `FillStyle` 2–7. Save and read the comment text. | **K4**: fills the display-name table for the values the corpus never exercises. |
| **Q13** | Author a form with an OCX. Reorder the `Object =` lines relative to `VERSION`. Does VB6 open it, and does it rewrite them on save? | **B1/L3**: whether verbatim echo is the correct target or VB6 regenerates from the `.vbp` component list. |
| **Q14** | Copy/paste a PictureBox with a picture. Save. Does the `.frx` contain the blob once or twice? | **K21**: whether dedup matches VB6. |
| **Q15** | Reorder the properties inside an OCX `BeginProperty Buttons {66833FE8-…}` bag. Open in VB6. | **K5/K6**: whether `IPersistPropertyBag::Load` is order-sensitive — i.e. whether "preserve source order" is merely nice or mandatory. |
| **Q16** | Author a class in VB6 as `Instancing = PublicNotCreatable` (`VB_Creatable = False`, `VB_Exposed = True`). Hand-edit to HexIDE's output (`MultiUse = -1`, `VB_Creatable = True`, `VB_Exposed = False`). Open the project. | **C2**: does VB6 reject, silently repair, or honour the combination HexIDE writes? |

---

## 7. Harness gaps

The current gate gives **false confidence in four distinct ways**, and two of them are outright bugs in the harness itself.

### Harness bugs to fix first

1. **The `.bas` lane fails on a harness bug, not a product defect.** `SerializationCorpusTests.cs:196-197` feeds `Path.GetFileNameWithoutExtension(path)` as the module name, producing `Attribute VB_Name = "Load Resources"` — a space, not a legal VB6 identifier. Simulating the real path with the name read from `VB_Name` gives **20/20 byte-identical**. All 20 reported `.bas` failures are artifacts, and they are masking the real `.cls` defects. **One-line fix.**
2. **The corpus root misses 26 of the 28 `.cls` files.** `CorpusRoots()` at `SerializationCorpusTests.cs:41-43` hardcodes `VB98\Template` and never learns about `VB98\Common\Tools\APE\Source`. C2 (28/28 header corruption) and L2 (16/28 attribute deletion) are almost entirely invisible to the gate as written.

### Coverage the corpus does not have

3. **Not one `.frx` byte is ever compared.** `SerializationCorpusTests.cs:148` discards the binary output: `var (rendered, _) = new FormSerializer().Serialize(...)`. Nothing asserts the companion still exists. So B2 (deletion), C7, L1 (790→12) and K21 are all invisible, and the three "OK" files (`demo/`) contain **zero** `.frx`/`.ctx`/`.pgx` — verified. **Add: compare `frxContent` against the on-disk companion, and assert the companion is not deleted.** This turns 3/25 into roughly 2/9 on the binary axis — it makes an already-red test redder, it does not break a green one. It is the single highest-value harness change.
4. **The diff is decoded with the same lossy decoder as the product.** `SerializationCorpusTests.cs:125` and `:190` use `File.ReadAllText` with no encoding, so `original` and `rendered` both carry U+FFFD. **C8 can never appear in the report.** Needs a byte-level comparison, not a string one.
5. **`Differences()` (`SerializationCorpusTests.cs:91-95`) compares line *i* to line *i* with no alignment.** One insertion cascades through the rest of the file, so the diff counts (105, 121, 200, 272) **cannot be attributed to any single cause**. Do not use them to prioritise. Add a proper alignment diff, or at minimum stop reporting the raw counts as if they meant something.
6. **The three passes are HexIDE's own output.** `SerializationCorpusTests.cs:44-46` adds `demo/` as a corpus root; all 3 OK files are `demo/{neon-aurora,neon-vertigo,wild-ecstasy}/Form1.frm`. **They pass because HexIDE wrote them.** Once K2 lands they will flip to failures — which is the correct outcome and needs regenerating, not reverting. Consider reporting VB6-authored and HexIDE-authored files as **separate scores** so this can never flatter the number again.

### File kinds and constructs entirely absent

| Missing | Why it matters |
|---|---|
| **`.dob` / `.dsr`** (6 files exist in `VB98\Template`) | `CorpusFiles(".frm", ".ctl")` never opens them — 112 property lines unexamined. `DATARPT.DSR:11` carries `_DesignerVersion=   100683782`, a 16-char name that exercises K1's boundary. |
| **`.pag` (PropertyPage) + `.ctx`/`.pgx` companions** | The `.ctx`/`.pgx` delete branch at `ProjectService.cs:957-968` is **code-derived only** — the corpus's two `.ctl` files have no `.ctx`. the maintainers' launch-readiness triage already records this exact bug class as previously-fixed for `.ctx`; it is untested. |
| **The `ProjectService` load → save path** | The harness calls `FormSerializer` directly. `docs/TEST_PROJECTS.md` already notes "Nothing exercises the real ProjectService load → save path" — which is exactly where B2's `File.Delete` lives. |
| **Control arrays** | `VBLoader.TryParseControlArrayIndex` (`VBLoader.cs:99-113`) re-parses `Index` out of `UnknownRawPropertyLines` — a **de-facto property store** that the clipboard silently empties (K22) and that K22's own fix would break. No corpus file covers it. |
| **A `BeginProperty` at depth ≥ 2** | `grep -rn BeginProperty IDE/HexIDE.Runtime.Tests/` returns only `SerializationRoundTripTests.cs:530` — a **depth-1** fixture. B3's NRE has no test. Add a depth-2 fixture next to it. |
| **Non-ASCII content** | 11 corpus files carry high bytes and the harness cannot see any of them (gap 4). |
| **A design-time-populated ListBox/ComboBox** | L4's 2-byte record layout, L7's silent drop, L1's `ItemData`. `GROUP.FRX` (in `VB98\Wizards\PDWizard\Setup1\`) exercises it and is **outside the corpus roots** — add that tree. |
| **A second save (idempotence)** | K13's float noise drifts **again** on save 2 (bits `…647` → `…646`, verified). Only `UserControlSaveRoundTripTests.cs:122` tests idempotence, and only for `.ctx` size. Add save→save→compare to the corpus harness. |
| **VB4/VB5-era `.frm`** | All 28 corpus files say `VERSION 5.00`, so L9 has zero evidence either way. |
| **A form with both `LockControls` and a later-sorting unknown property** (e.g. `ScaleMode`) | K9's only failure mode. Not present in `VB98\Template`; needs a wider corpus or a hand-authored fixture. |