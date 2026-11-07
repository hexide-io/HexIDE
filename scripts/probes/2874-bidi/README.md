# Avalonia RTL trailing punctuation (#2874, round 3)

Standalone repro: one TextBlock, RTL, one string, no HexIDE code or resources.
Uses the repository's pinned Avalonia 12.0.5 packages (no new dependency).

```sh
dotnet run --project scripts/probes/2874-bidi
```

Measured on Linux with Skia and DejaVu Sans, 2026-11-04:

- Logical text: `תודה HexIDE.`
- Expected visual order, read across the screen from left to right: `.HexIDE הדות`
- Actual visual order: `HexIDE. הדות`
- Actual visual runs: `[HexIDE.]` level 2, `[תודה ]` level 1, then a
  `TextEndOfParagraph` of length 1.
- Latin `H` (index 5) starts at x=197.5546875; the dot (index 11) starts at
  x=255.765625. The dot is to the right of the Latin word.
- As a diagnostic only, passing the same string with a terminal newline yields
  `[.\n]` level 1, `[HexIDE]` level 2, `[תודה ]` level 1. The dot moves to the left
  of the Latin word. This is not a proposed translation workaround.

Source trace at the pinned release:

- [TextBlock.CreateTextLayout](https://github.com/AvaloniaUI/Avalonia/blob/12.0.5/src/Avalonia.Controls/TextBlock.cs#L658)
  passes `FlowDirection` to `GenericTextParagraphProperties`.
- [TextFormatterImpl.ShapeTextRuns](https://github.com/AvaloniaUI/Avalonia/blob/12.0.5/src/Avalonia.Base/Media/TextFormatting/TextFormatterImpl.cs#L354)
  sets `bidiData.ParagraphEmbeddingLevel` from that direction. Paragraph level is
  therefore 1 here, not inferred from the first strong character. The method also
  separately infers a resolved flow direction from the first strong character.
- [FetchTextRuns](https://github.com/AvaloniaUI/Avalonia/blob/12.0.5/src/Avalonia.Base/Media/TextFormatting/TextFormatterImpl.cs#L666)
  includes the text-end run. `ShapeTextRuns` substitutes Latin `a` for runs without
  text, including that paragraph-end marker. Inference: this synthetic L after the
  dot makes N1 resolve it as L before N2 can resolve it against paragraph end.
- [BidiAlgorithm N1/N2](https://github.com/AvaloniaUI/Avalonia/blob/12.0.5/src/Avalonia.Base/Media/TextFormatting/Unicode/BiDiAlgorithm.cs#L1132)
  uses `eos` at the end of the supplied sequence; the supplied sequence already
  contains the synthetic L. `AddLevelRun` derives `eos` from paragraph level.

The repro establishes an Avalonia fault independent of HexIDE. This round leaves
it in place: no pack strings or directional controls changed. Before and after
this round, the minimal repro's actual order remains `HexIDE. הדות`.
The issue's Windows live measurement also reports the Hebrew credits ending in
`HexIDE.` and Arabic Find ending in `'zzqqxx'.`; these full UI cases still need
supervisor verification. No claim is made about a later Avalonia release.

## Fingerprint clipping

The initial Linux About tests used the local monospace fallback. Their width
assertions passed before the fix, so that run did not reproduce the Windows clip.
A second probe used the existing installed Windows **Consolas** font with Skia
on Linux, without copying or bundling the font. The optional diagnostic mode is
still a single TextBlock; supply a file URI for a font you already have:

```sh
dotnet run --project scripts/probes/2874-bidi -- --fingerprint "$CONSOLAS_FONT_URI"
dotnet run --project scripts/probes/2874-bidi -- --fingerprint "$CONSOLAS_FONT_URI" ltr
```

Measured with the canonical first-party fingerprint from `TRUST.md`, font size 11,
wrapping at 300 DIPs:

| Layout | Desired / arranged width | First-line width excluding / including trailing space | Rightmost visible character edge |
|---|---|---|---|
| Before: RTL | 300 / 300 | 296.3447265625 / 302.392578125 | 302.392578125 |
| After: LTR | 300 / 300 | 296.3447265625 / 302.392578125 | 296.3447265625 |

The wrapping formatter permits the trailing space to overhang. In RTL, that space
is reordered to the left and pushes the visible `7929` group to the right, past
the block's edge by 2.392578125 DIPs. In LTR only invisible trailing whitespace
overhangs; every visible character fits. Explicit LTR fixes both group reordering
and this clipping mechanism, without widening the dialog or changing the data.

The headless About cases check desired width and every non-whitespace character's
bounds, rather than treating invisible trailing-space overflow as clipped text.
For the fingerprint cases the container width is also chosen from the active
monospace digit advance (49.5 advances), covering an additional wrap width on Linux
without requiring a Windows font. Five new cases failed on direction before the
view change. Restoring inherited direction and default alignment also failed all
five final cases; their clipping assertions still passed with Linux’s fallback.
The independent Consolas probe above supplies the before/after clipping evidence.

The supervisor should still inspect both keys with the expander open under
Hebrew and Arabic on Windows, including at 150% display scaling, and confirm
canonical order, complete last glyphs, and the mirrored-side alignment of Latin
text. The punctuation defect above remains unchanged.
