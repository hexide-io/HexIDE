# localization Specification

## Purpose
Define how the IDE's own text is translated: where strings come from, how a language is chosen and applied,
and the guarantees that keep a translated IDE usable even when a translation is incomplete.

VB6 solved this by shipping a different build per language, which was a distribution constraint of its era
rather than a design. One build carrying every language is the modern answer, and it is worth doing properly
here for a reason specific to this project: the people most likely to still be maintaining VB6 code are
distributed across a lot of countries, and an English-only IDE excludes them for no good reason.

Which languages ship, and how a pack is produced, are a separate capability (`language-packs`). This one is
the mechanism.

## Requirements

### Requirement: Every user-facing string SHALL be addressed by a key
Text shown to the user SHALL be referenced by a stable key resolved at display time, and SHALL NOT be
written as a literal at the point of use.

A literal is invisible to translation: nothing lists it, nothing reports it missing, and it surfaces as a
stray English word in an otherwise translated dialog. Making the key the only way to obtain text means the
set of translatable strings is enumerable, which is what every other guarantee here depends on.

#### Scenario: Adding a new piece of interface text
- **WHEN** interface text is added
- **THEN** it is added as a key with a canonical English value, not as a literal

#### Scenario: Refusing an empty or duplicate component name
- **WHEN** a control or form rename is refused because the name is empty or already used in the form
- **THEN** the Properties window and `set_control_property` use the active language's naming refusal key

#### Scenario: Project-load diagnostics cross the runtime boundary
- **WHEN** the runtime deserializer reports a form-load warning to the IDE
- **THEN** it supplies a localization key and separate arguments, and the IDE resolves them in its current language
- **AND** the project-load heading, remaining-warning count and skipped ActiveX Document notice use localization keys
- **AND** a runtime host without localization retains an English diagnostic
- **AND** warnings describe the affected content rather than internal development phases

#### Scenario: Call Stack row automation names
- **WHEN** Call Stack rows are rebuilt at a stop in either debugger
- **THEN** their automation names use the active language's row-name format with the frame name and line
- **AND** the row's string representation uses the same text for selection summaries without a container
- **AND** a language change while stopped is reflected when the rows are next rebuilt

### Requirement: Program message box buttons SHALL use the IDE language
Message boxes opened through the IDE SHALL resolve their OK, Cancel, Abort, Retry, Ignore, Yes and No
captions through the localization service when shown. The program's prompt and title SHALL remain its
own text. The standalone runner, which has no localization service, SHALL retain English button captions.

#### Scenario: A program asks a question in a French IDE
- **WHEN** a program opens a Yes/No message box in an IDE using French
- **THEN** its buttons read Oui and Non and return the same results as their English counterparts

### Requirement: English SHALL be canonical and SHALL be the fallback for every key
The English pack SHALL define every key, other packs SHALL only override keys they translate, and any key a
pack does not carry SHALL resolve to its English value.

This is what makes an incomplete translation safe. A pack that had to be complete before it could ship would
mean no pack ships until it is finished; merging over English means a half-translated language is usable
immediately and improves incrementally. The failure mode it removes is the important one — a missing key
renders as English rather than as a blank control, and a blank control is indistinguishable from a broken
IDE.

#### Scenario: A pack missing a key
- **WHEN** the active language does not define a key
- **THEN** the English value is shown

#### Scenario: A partially translated language
- **WHEN** a pack translates only some keys
- **THEN** the IDE is fully usable, showing translations where they exist and English elsewhere

### Requirement: Language selection SHALL NOT change the process culture
Choosing a language SHALL select a set of strings and nothing more, and SHALL NOT alter the culture used for
parsing or formatting numbers, dates or currency.

The interpreter's correctness depends on it. A VB6 program's source literals are read by a fixed rule, always
with a point, and the IDE pins its threads to an invariant culture so that this holds; a localization
mechanism that changed the thread's culture would alter how a running VB6 program interprets its own numeric
literals as a side effect of the IDE's menu language. What a program writes and reads at run time (`CStr`,
`Format`, `CDate`, a String stored into a number) follows the user's regional settings instead, as VB6's does.
The interpreter takes those as its own locale at startup, before the pin, rather than from the thread
(`VbLocale`, #777). So the IDE's language, the user's regional locale and the culture of the IDE's threads are
three separate things, and translating the IDE cannot change what a program computes.

#### Scenario: Running a program with a non-English IDE language
- **WHEN** the IDE language is changed and a VB6 program is run
- **THEN** the program's numeric parsing and formatting are unchanged

### Requirement: System default SHALL follow the OS UI language
The `system` language selection SHALL resolve from the OS UI culture captured before the invariant thread
culture pin at startup ([#799](https://github.com/hexide-io/HexIDE/issues/799)). Resolution SHALL try an exact neutral-language match, an exact shipped
region match, a neutral-language match on the two-letter code, then canonical `en`. The invariant pin
SHALL remain in place, and the persisted selection SHALL remain `system`.

#### Scenario: British English system language
- **WHEN** the startup UI culture is `en-GB` and System default is selected
- **THEN** the IDE uses the British English overrides, including Colour Palette and Centre in Form

#### Scenario: A region without a shipped regional entry
- **WHEN** the startup UI culture is `fr-CD` and System default is selected
- **THEN** the IDE uses the neutral French pack

### Requirement: Changing language SHALL take effect immediately
Selecting a language SHALL update the interface without restarting the IDE, including windows already open.

A restart to see a language is a restart to *evaluate* a language, which is enough friction that people stop
trying. It also makes the safety requirement below possible: a change that only took effect on restart could
not be reverted before the user was already looking at it.

#### Scenario: Switching language
- **WHEN** the user selects a different language
- **THEN** the interface changes immediately, including windows already open

### Requirement: A language change SHALL be revertible without reading the new language
After a language change the IDE SHALL offer to keep or undo it in a way that does not require reading the
new language, and SHALL undo it automatically if not confirmed within a short period.

This is the one setting that can lock the user out of the control that undoes it. Someone who selects a
language they cannot read — by accident, or to see what it looks like — must not have to navigate an
unreadable menu to get back. Presenting the choice in both languages and reverting on inaction makes the
mistake self-correcting.

#### Scenario: Selecting a language the user cannot read
- **WHEN** the user changes to a language and does not confirm
- **THEN** the IDE reverts to the previous language on its own

#### Scenario: Confirming the change
- **WHEN** the user confirms the new language
- **THEN** it is kept and persists across restarts

### Requirement: Text direction SHALL follow the pack, but the design surface SHALL NOT mirror
A pack SHALL be able to declare a right-to-left direction, which SHALL apply to the IDE's own chrome
including dialogs. The form design surface SHALL remain left-to-right regardless of the interface language.

Direction is a property of the language being read, and chrome is read. A form being designed is a different
thing: VB6 positions controls at absolute coordinates, and the form has its own right-to-left property that
belongs to the application being built. Mirroring the canvas with the IDE would move the developer's
controls to the other side of a form whose layout they did not change.

#### Scenario: A right-to-left interface language
- **WHEN** a right-to-left language is active
- **THEN** the IDE's panels, menus and dialogs mirror

#### Scenario: Designing a form under a right-to-left interface
- **WHEN** a form is open on the design surface under a right-to-left language
- **THEN** the canvas and the controls on it are not mirrored

### Requirement: A pseudo-localized mode SHALL exist to find untranslated text
The IDE SHALL provide a generated pseudo-localized mode covering every canonical key, in both directions,
available to developers and not offered to end users.

It is the completeness oracle: with every key visibly transformed, anything still rendering as plain English
is a string somebody wrote as a literal. Generating it from the canonical set rather than maintaining a file
means it can never drift out of date. Keeping it out of the end-user list matters because it is unreadable
by design — offering it as a language would be offering a way to break the IDE.

#### Scenario: Auditing for untranslated text
- **WHEN** a developer runs the IDE pseudo-localized
- **THEN** any text still appearing in plain English is a string that was not keyed

#### Scenario: The end-user language list
- **WHEN** a user chooses a language
- **THEN** the pseudo-localized modes are not among the options

### Requirement: A regional language SHALL fall back through its neutral language
Where a language is chosen with a region, resolution SHALL try the regional pack, then the neutral language,
then English.

Most regional differences are a handful of words, so requiring a full pack per region would mean either
duplicating a translation many times or offering no regions at all. Falling back through the neutral means a
region can exist to carry only what it actually changes, and can exist with no pack at all where it changes
nothing.

#### Scenario: A region that overrides some words
- **WHEN** a regional language defines only some keys
- **THEN** those keys use the regional value and the rest come from the neutral language

#### Scenario: A region with nothing to override
- **WHEN** a region carries no overrides at all
- **THEN** it is still selectable and resolves entirely through its neutral language

### Requirement: Only one language change SHALL await confirmation at a time
While a language change is awaiting confirmation, another language change SHALL be refused, and nothing SHALL
be applied by the refused change.

A change that is not confirmed reverts to the language active when it was made. A second change made while the
first is unconfirmed would record the first's unconfirmed language as the one to return to, so whichever
resolved last would decide where the IDE ended up. That could leave the IDE in a language nobody kept.

#### Scenario: A second change while the first is unconfirmed
- **WHEN** a language change is awaiting confirmation and another change is requested
- **THEN** the second is refused, the first's confirmation is still the only one open, and not confirming it returns the IDE to the language it had before either
