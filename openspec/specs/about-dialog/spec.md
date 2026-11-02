# About dialog

## Purpose

Describe the credits shown in About and preserve their attribution when a downstream brand adds its own
credits. The append-only policy is Jen’s decision in [#2746](https://github.com/hexide-io/HexIDE/issues/2746).

## Requirements

### Requirement: About credits are append-only

The About dialog SHALL show localized thanks to HexIDE’s human contributors, Claude by Anthropic
(including the Claude Code sessions writing, reviewing and merging work), and Codex by OpenAI
(including the lanes implementing queued fixes). The dialog SHALL offer a Credits expander alongside
its signing-key section, with Credits expanded when the dialog opens. About options SHALL allow brand
credits to be appended after HexIDE’s thanks, without an option to remove, replace, reorder or separately
hide the original thanks. The assembled credits SHALL be read-only and independent of later changes to
the supplied additions.

#### Scenario: A branded About dialog retains the original credits

- **GIVEN** a brand supplies its own title, subtitle, copyright and additional credits
- **WHEN** the About dialog opens
- **THEN** HexIDE’s thanks, including Claude and Codex, appear first
- **AND** the brand’s additions follow in their supplied order

#### Scenario: No brand additions

- **WHEN** About opens without additional credits
- **THEN** the Credits expander is expanded and contains HexIDE’s original thanks
