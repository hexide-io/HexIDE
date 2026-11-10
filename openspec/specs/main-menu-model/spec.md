# Main menu model

## Purpose

Describe the host-owned main-menu declaration introduced by #3510. The IDE renders its main menu from this
embedded data. Profiles cannot author or expose new placements
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
providers. Add-ins retains its static Manager child; Panel Layouts' renderer separator is an opening-time
sentinel rather than declared menu content.

#### Scenario: Brand Help links

Help's brand home and support commands retain their dynamic caption bindings and localization keys,
precede the existing repository web entry, and record the `HasBrandHome` and `HasBrandSupport`
visibility bindings used by the host.

### Requirement: Command arguments have registered types

Command arguments SHALL carry explicit string, boolean or integer types and match the registered command
signature in both names and value types. Today's menu command references have no arguments.

#### Scenario: An argument is supplied to a parameterless command

The declaration is refused for that placement rather than discarding or coercing the argument.

### Requirement: Invalid declarations are refused before use

The declaration validator SHALL reject unknown commands, providers, states, label bindings and visibility bindings, duplicate
placements, invalid argument signatures or values, unknown JSON members and invalid item shapes. Errors
SHALL identify the source and offending placement, or the JSON path when parsing fails. Toggle states
SHALL name host-owned getters and setters over the five existing toolbar and minimap settings.

#### Scenario: Unknown boolean state

A toggle naming an unregistered state is refused with the declaration source and placement id.

### Requirement: Tests guard the declaration and installed menu

Ordinary tests SHALL pin placement ids independently, verify localized keys and host command
registrations, and check the installed menu recursively against the declaration in every shipped
language, including after language changes. MainView SHALL contain no handwritten main-menu items.

#### Scenario: The menu is installed

MainView builds its menu from the validated declaration when it receives its view model. Existing named
menu items remain findable, automation menu paths remain resolvable, profile visibility targets retain
their bindings, and routed commands retain their keyboard routes and live gesture text.

### Requirement: The replacement renderer preserves host behavior

The renderer SHALL build Avalonia menus from validated declarations, preserving localized mnemonic
captions, host-bound dynamic labels, icons, commands, typed argument maps, separators, visibility bindings,
two-way settings toggles and disabled placeholders. Routed-command gestures SHALL follow the existing
keymap refresh mechanism. It SHALL retain live resource bindings when the language changes.

#### Scenario: Host label and visibility bindings

A declared `labelBinding` supplies the header from the host view model and follows language changes.
A declared `visibilityBinding` supplies `IsVisible` from the host view model, including the brand Home
and Support Help entries.

#### Scenario: Dynamic providers

Recent Projects and Recently Closed bind the host collections. Start With and Run With use the host run
collections, including radio state, and refresh when Run opens. Panel Layouts uses the existing opening-time
builder. Add-ins and Tools append host contributions in order and remove their collection subscriptions
when the renderer is disposed.

#### Scenario: The view model changes

MainView disposes its previous renderer before installing the new menu, removing the old collection
subscriptions. With no view model, no main menu is installed.
