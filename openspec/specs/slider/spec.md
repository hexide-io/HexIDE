# Slider

## Purpose

The MSComctlLib Slider object model, measured against VB6 SP6 in the fidelity oracle (#1091 A).
Measured input and drawing are implemented (#1091 B); persisted own properties load and the measured saved form round-trips byte for byte. Forms containing Sliders remain read-only until designer edits can be saved faithfully.

## Requirements

### Requirement: Measured object model

HexIDE SHALL register MSComctlLib.Slider on the in-box control foundation and implement its measured property defaults,
validation, selection, tick count and methods. Value SHALL be its default member for Let reads and writes.

#### Scenario: Value assignment

- **WHEN** code assigns Value outside Min to Max
- **THEN** Value clamps to the nearest endpoint without an error
- **AND** Change fires only if Value changed
- **AND** Scroll does not fire

#### Scenario: Range assignment

- **WHEN** code assigns Min greater than or equal to Max, or Max less than or equal to Min
- **THEN** error 380 leaves the range unchanged
- **WHEN** a valid range assignment passes Value
- **THEN** Value follows silently

#### Scenario: Selection

- **WHEN** SelectRange is False
- **THEN** SelStart reads Value and assigning SelStart moves Value without Change
- **WHEN** SelectRange is True
- **THEN** the selection is independent of Value, SelStart truncates its length at Max, and an excessive SelLength raises 380
- **AND** ClearSel sets SelStart to -1 and SelLength to 0

### Requirement: Incomplete surface

Unmeasured members and conversions SHALL refuse as not implemented. Slider own properties SHALL remain view-only
until their designer edits can be saved faithfully. The form itself SHALL remain read-only until those edits can be saved faithfully, regardless of file line endings.

#### Scenario: Native handle

- **WHEN** code reads Slider.hWnd
- **THEN** HexIDE reports that the member is not implemented

#### Scenario: Saving before modelled own-property editing

- **WHEN** a form contains a Slider whose own properties have not been edited
- **THEN** the form remains read-only with either LF or CRLF line endings
- **AND** direct serialization retains its Slider own property file values, spelling and order
- **AND** those own properties remain view-only

### Requirement: Measured input and drawing

The Slider SHALL use SmallChange for arrows and wheel input and LargeChange for paging. It SHALL draw the measured
tick sides, thumb direction, selection band and markers, single border, disabled thumb dither and dotted focus rectangle,
without pinning pixel metrics. A horizontal drag SHALL show Text above the thumb.

#### Scenario: Dragging

- **WHEN** the user drags the thumb
- **THEN** Scroll fires for every traversed value
- **AND** release raises MouseUp, Click if released over the control, Scroll and one Change
- **AND** Change fires even if Value ends where it started

#### Scenario: Channel and keyboard

- **WHEN** the user clicks or holds the channel
- **THEN** Value pages toward the pointer with Scroll at each step and one Change on release
- **WHEN** arrows or page keys are pressed and released
- **THEN** KeyDown, Scroll, KeyUp and Change fire even at an endpoint
- **WHEN** Home or End is pressed and released
- **THEN** KeyDown, Change, KeyUp and Change fire without Scroll

#### Scenario: Wheel and right click

- **WHEN** the wheel moves up or down one notch
- **THEN** Value decreases or increases by SmallChange with Scroll only
- **WHEN** the user right clicks
- **THEN** MouseDown, MouseUp and Click fire without changing Value

### Requirement: Measured file persistence

The reader SHALL load Slider properties in control order, accept Min equal to or greater than Max, clamp Value silently,
ignore Text and ignore selection when SelectRange is False. File Orientation SHALL preserve the recorded dimensions.
The writer SHALL round-trip the saved Slider corpus byte for byte, omit Text and preserve whether SelStart was explicitly read.
It SHALL carry own property lines without replacing them with the runtime interpretation. Missing extents SHALL remain absent
while the size is unchanged; resizing SHALL write extents for the new size.

#### Scenario: File range and selection

- **WHEN** a file records Min 20 with default Max 10
- **THEN** it loads Min 20, Max 10 and Value 20
- **WHEN** SelectRange is True with SelStart 8 and SelLength 10 at Max 10
- **THEN** SelLength loads as 2

#### Scenario: Explicit SelStart

- **WHEN** a saved file records Value 6 without SelStart
- **THEN** saving does not invent SelStart
- **WHEN** it also records SelStart 6
- **THEN** saving keeps that line before Value
