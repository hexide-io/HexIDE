# ide-visual-density Specification

## Purpose
Define how compact the IDE's chrome is, and against what.

The control library the IDE is built on is tuned for modern accessibility — larger targets, more padding,
taller rows. That is the right default for a general-purpose application and the wrong one here, where
looking like the original is a feature. The difference is not subtle in aggregate: a properties grid a few
pixels taller per row shows noticeably fewer properties, and the window stops feeling like the thing it is
reproducing.

## Requirements
### Requirement: Chrome density SHALL be measured against the original product
Chrome sizing SHALL be specified by comparison with VB6 running on a current version of Windows, at
unscaled resolution, rather than chosen by eye.

"Looks about right" is not reproducible and does not survive a change to the control library. Measurements
taken from the real product give a reference anyone can check, and make a regression something that can be
demonstrated rather than argued about.

#### Scenario: Sizing a chrome element
- **WHEN** the density of a chrome element is decided
- **THEN** it is set from a measurement of the corresponding element in the original product

### Requirement: Density adjustments SHALL be made in the theme layer
Sizing overrides SHALL live with the IDE's own styling rather than being applied at individual call sites.

Per-view padding drifts: the next view gets whatever its author chose, and nothing states what the intended
density is. Keeping the overrides together makes the target explicit in one place, and means a control type
is sized once rather than everywhere it is used.

#### Scenario: Adjusting a control's density
- **WHEN** a control type is too tall or too padded
- **THEN** it is corrected once in the styling layer rather than at each use

### Requirement: Density SHALL NOT be pursued where the framework owns the surface
Chrome controlled by the docking framework SHALL be left as the framework renders it.

Some surfaces — dock title bars, resize handles, tab strips — belong to a library rather than to this
project, and matching them would mean forking it. That cost is real and permanent, and the payoff is small:
these are the parts of the window a developer looks at least. Naming the boundary keeps it a decision rather
than an oversight someone re-opens periodically.

#### Scenario: Framework-rendered chrome that is not VB6-like
- **WHEN** a surface rendered by the docking framework does not match the original's density
- **THEN** it is accepted as-is rather than pursued by forking the framework
