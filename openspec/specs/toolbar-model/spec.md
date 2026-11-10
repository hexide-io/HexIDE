# Toolbar model

## Purpose

Describe the host-owned toolbar declaration introduced by #3621. This component models the existing
AXAML; the IDE still renders the handwritten toolbars. Profiles cannot author placements through it.

## Requirements

### Requirement: Bands and placements describe the current toolbars

The host SHALL embed the Standard, Edit, Debug and Form Editor bands as ordered data beside the main
menu declaration. Bands and placements, including static flyout menu children, SHALL have unique,
stable host-owned ids. Interactive placements SHALL carry localization keys for both tooltip and
automation name, and the existing automation id. Separators and unlabelled host readouts SHALL have no
invented tooltip or automation name.

#### Scenario: Existing placement kinds

The declaration contains command buttons, separators, command toggles, flyouts and named providers.
Add Form has static menu children; Add Project names the host's `add-project-templates` provider.

#### Scenario: Selection readouts

The `selection-readout` provider stands for both icons, both text blocks and both spacers at the end
of Standard. It does not expose a widget, code or AXAML authoring escape hatch. The readout's units
remain unchanged; #1641 is outside this component.

### Requirement: Toolbars share host command and visibility registrations

Toolbar commands and static flyout children SHALL resolve through the main menu's command registry,
using its typed argument signatures and value validation. Visibility bindings SHALL resolve through
the same host visibility registry. A command toggle SHALL name a registered checked binding as well
as its command; the Lock Controls binding retains its one-way mode and false null fallback.

#### Scenario: Distinct shell commands

Standard and Debug Break and End retain their shell bindings, rather than being replaced by the
similarly named routed commands used by the main menu.

### Requirement: Invalid declarations are attributed and refused

The validator SHALL refuse unknown commands, toolbar providers, checked and visibility bindings,
duplicate ids, invalid item shapes, missing interactive labels, invalid argument signatures or values,
unknown JSON members and malformed declarations. Errors SHALL identify the source and offending
placement, or the JSON path for parsing failures.

#### Scenario: Nested duplicate

A static flyout child duplicating a band or another placement is refused in the same id namespace.

### Requirement: Tests guard the AXAML migration boundary

Ordinary tests SHALL independently pin ids and localized keys and walk the real MainView toolbar
AXAML in order, checking structure, command, tooltip, automation name, visibility, toggle binding and
flyout contents. The selection-readout run SHALL be checked in full, including spacing. No renderer
SHALL consume this declaration in this slice.

#### Scenario: A toolbar drifts

Changing a command binding, tooltip key, band visibility, static flyout child or selection readout
without updating the matching contract fails parity.
