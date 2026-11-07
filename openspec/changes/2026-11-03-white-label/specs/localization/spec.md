## ADDED Requirements

### Requirement: Host product references use localized brand templates
Localized host text that names the product SHALL obtain the display name from installation identity
through a template argument. Brand substitution SHALL retain locale fallback, mnemonic and placeholder
validation, and MUST NOT globally replace text in documents, program messages or protocol identifiers.

#### Scenario: Switching interface language in a branded installation
- **WHEN** the user changes interface language
- **THEN** host templates use that language's fallback chain and the same installation display name
- **AND** a brand author need not replace every shipped language pack to change the product name

### Requirement: Translation overrides cannot suppress upstream credits
The host SHALL resolve its protected upstream-credit block from shipped translations with shipped
fallback, independently of profile, package and user override tiers. Attempts by those tiers to remove,
replace or blank the base credits SHALL be ignored with source attribution; other valid overrides
SHALL continue to apply. Additional localized adopter credits SHALL follow the base block.

#### Scenario: User and profile overrides target base credits
- **WHEN** either tier overrides the base About credits, including `Str.Dialog.About.Credits.Thanks`
- **THEN** shipped upstream credits remain visible and the rejected entry is diagnosed
- **AND** valid unrelated overrides and append-only adopter credits still apply
