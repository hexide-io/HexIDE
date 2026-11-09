## ADDED Requirements

### Requirement: Modern is an opt-in bundle
The IDE SHALL offer a Modern profile alongside `vb6`, `vbaode` and `vba`. Omitted profile selection
SHALL retain Classic through `vb6`. Modern SHALL enable the palette, searchable settings presentation
and OS light/dark following through declarative profile data, preserving explicit saved user choices.
Profiles SHALL NOT change runtime language behaviour.

#### Scenario: A default launch
- **WHEN** the user starts without a profile selector
- **THEN** Classic remains selected with its existing surfaces and defaults

#### Scenario: Opting into Modern
- **WHEN** the user explicitly selects the Modern bundle
- **THEN** the three headline patterns are available using shared command, settings and theme services
- **AND** other Modern patterns are not implied by this selection
