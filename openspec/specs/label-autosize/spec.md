# label-autosize Specification

## Purpose
Define runtime Label AutoSize: its triggers, caption measurement and placement, including the deliberate
correction to VB6's repeated centered WordWrap growth. Designer sizing remains unmeasured and unchanged.
Sources: [#922](https://github.com/hexide-io/HexIDE/issues/922), the
[oracle](../../../docs/vb6-fidelity-oracle/autosize-on-a-label-and-a-picturebox.md), and
[measuring PR #4984](https://github.com/hexide-io/HexIDE/pull/4984).

## Requirements

### Requirement: Runtime Label AutoSize responds to content changes
A runtime Label with AutoSize True SHALL recompute its dimensions on a changed Caption or Font and when AutoSize becomes True. It SHALL retain saved dimensions on load and explicit Width/Height until a trigger.

#### Scenario: A changed caption fits and an identical assignment holds
- **GIVEN** an AutoSize Label loaded at 3000 by 1000 twips with Caption Hello
- **WHEN** the program assigns Caption Hi
- **THEN** it resizes to the text and keeps the left edge for Left Justify
- **WHEN** the program assigns Width 5000 and then Caption Hi again
- **THEN** Width remains 5000

### Requirement: Caption measurement uses the Label's own font
On Windows, a runtime Label SHALL measure the drawn caption in its VBFont through GDI at 96 DPI. Elsewhere it SHALL approximate using whole-pixel glyph advances in the drawn font, without overhang. The extent SHALL include the border. CR, LF and CRLF SHALL delimit lines, including empty lines. WordWrap SHALL wrap at the old width less the border and widen only for a word needing more space.

#### Scenario: Windows measures the default font
- **GIVEN** a running AutoSize Label on Windows in MS Sans Serif 8.25 with no border and Caption x
- **WHEN** Caption becomes Hello
- **THEN** Width is 360 and Height 195 twips

### Requirement: Placement preserves the alignment edge without centered WordWrap creep
The Label SHALL round the old rectangle to whole pixels, keep the edge named by Alignment and grow downward. If the result equals the rounded old rectangle, it SHALL keep the original unsnapped values; otherwise it SHALL write all four whole-pixel values. With WordWrap and Center, content width equal to the old pixel width SHALL retain Left and Width to avoid VB6's repeated two-pixel growth.

#### Scenario: Centered WordWrap does not creep
- **GIVEN** a centered WordWrap Label on Windows in MS Sans Serif 8.25 at Left 600, Width 1000 and Height 195 twips
- **WHEN** its caption becomes the measured four-line sentence
- **THEN** Left is 600, Width 1005 and Height 780, deliberately diverging from VB6's 585 / 1035 / 780
- **AND** when content width equals the old rounded width, Center keeps its Left and Width

#### Scenario: An unchanged rectangle keeps fractional dimensions
- **GIVEN** a left-justified WordWrap Label at Width 1000 twips
- **WHEN** its new rectangle equals its rounded old rectangle
- **THEN** Width stays 1000 rather than snapping to 1005

### Requirement: Unmeasured resize triggers retain existing behaviour
Design-time AutoSize and resizing on Alignment, BorderStyle, WordWrap or UseMnemonic changes SHALL retain existing behaviour. Identical font assignments SHALL retain existing no-resize behaviour; this has not been measured in VB6.

#### Scenario: Designer editing and unmeasured assignments do not resize
- **WHEN** Alignment, BorderStyle, WordWrap or UseMnemonic changes, an identical font is assigned, or the designer edits a caption
- **THEN** AutoSize does not recompute dimensions

### Requirement: Unmeasured captions consisting only of line breaks refuse sizing
A runtime AutoSize Label given a caption consisting only of line breaks other than a single CRLF SHALL refuse with HexIDE's program-facing English not-implemented error naming #922.

#### Scenario: A line-break-only caption has no measured width
- **WHEN** a running AutoSize Label is given Caption vbLf
- **THEN** it refuses as not implemented rather than guessing a width
