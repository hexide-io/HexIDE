# Main menu model

## Purpose

Describe the host-owned main-menu declaration introduced by #3510. It is embedded data alongside the
existing AXAML menu; the AXAML remains the renderer. Profiles cannot author or expose new placements
through this component yet.

## Requirements

### Requirement: The declaration models the existing menu

The host SHALL embed the whole main menu as an ordered recursive declaration of submenus, separators,
command references, named dynamic providers, boolean toggles and disabled placeholders. Each item SHALL
have a unique stable placement id. Every labelled item SHALL carry an existing localization key;
separators have no label. A host-bound dynamic caption SHALL retain its named label binding and fallback
localization key.

#### Scenario: Disabled unfinished entries

View → Last Position, View → Property Pages and Window → Split are placeholders with no action, state,
provider or children.

#### Scenario: Host-populated menus

Recent Projects, Start With, Run With, Recently Closed, Panel Layouts and Add-ins name registered host
providers. Add-ins retains its static Manager child; Panel Layouts' AXAML separator is an opening-time
sentinel rather than declared menu content.

#### Scenario: Brand Help links

Help's brand home and support commands retain their dynamic caption bindings and localization keys,
precede the existing repository web entry, and record the `HasBrandHome` and `HasBrandSupport`
visibility bindings used by AXAML.

### Requirement: Command arguments have registered types

Command arguments SHALL carry explicit string, boolean or integer types and match the registered command
signature in both names and value types. Today's menu command references have no arguments.

#### Scenario: An argument is supplied to a parameterless command

The declaration is refused for that placement rather than discarding or coercing the argument.

### Requirement: Invalid declarations are refused before use

The declaration validator SHALL reject unknown commands, providers, states and label bindings, duplicate
placements, invalid argument signatures or values, unknown JSON members and invalid item shapes. Errors
SHALL identify the source and offending placement, or the JSON path when parsing fails. Toggle states
SHALL name host-owned getters and setters over the same five settings as the AXAML bindings.

#### Scenario: Unknown boolean state

A toggle naming an unregistered state is refused with the declaration source and placement id.

### Requirement: Tests guard the migration boundary

Ordinary tests SHALL compare the embedded menu recursively against the real AXAML for item order,
structure, command bindings, label keys or host label bindings, toggle state bindings, providers and
disabled placeholders. Tests SHALL pin placement ids independently and verify localized keys and host
command registrations. Rendering, shortcut derivation, focus routing and expanded profile visibility
remain outside this slice.

#### Scenario: A menu command changes only in AXAML

The parity test fails for the corresponding placement until the host declaration is updated deliberately.
