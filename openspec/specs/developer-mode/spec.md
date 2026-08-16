# developer-mode Specification

## Purpose
Define how capabilities that are useful to a developer and dangerous to a user are gated.

There is exactly one such capability today — loading add-in packages that are not signed — and it is a good
illustration of the problem. It has to exist, because somebody writing an add-in must be able to run it
before it is signed. It also completely defeats the add-in trust model, so the question is not whether to
have it but how to make it unreachable by anyone who did not deliberately go and get it.

The threat being designed against is social engineering, not a technical attack. "Tick this box and
restart" is how a person is talked into disabling their own protections, and it works because the box is
right there and the person asking sounds like they know what they are doing. Everything below follows from
taking that seriously.

## Requirements
### Requirement: Dangerous capabilities SHALL NOT exist in a distributed build
The code paths that enable developer capabilities SHALL be excluded from release builds, so a distributed
binary has no way to reach them at all.

This is the layer that does not depend on the user making a good decision. A setting can be argued past, a
warning can be dismissed, and a flag can be typed by someone following instructions from a stranger — but a
build with the capability compiled out cannot be talked into anything. It reduces the attack from a
conversation to "install a toolchain, clone the source, and build it yourself", which is no longer social
engineering.

#### Scenario: A user on a released build
- **WHEN** a user runs a distributed build
- **THEN** there is no input, setting or sequence that enables a developer capability

### Requirement: Developer mode SHALL be entered only by a per-launch command-line flag
Developer mode SHALL be activated only by a flag supplied when the IDE is launched, SHALL NOT persist
across launches, and SHALL NOT be enableable from any user interface or by automation.

A persisted setting is the exact shape of the attack: enable it once, and it stays enabled long after the
conversation that caused it is forgotten. Requiring a launch flag means the dangerous state ends when the
IDE closes, and means enabling it is not something that can be done by clicking where somebody points.
Excluding automation matters for the same reason — a remote agent that could turn it on would be a way
around every other layer.

#### Scenario: Enabling developer mode
- **WHEN** the developer supplies the flag at launch
- **THEN** developer mode is active for that session only

#### Scenario: Restarting normally
- **WHEN** the IDE is next started without the flag
- **THEN** developer mode is off, with nothing carried over

#### Scenario: Looking for a way to enable it
- **WHEN** a user looks through the IDE's settings
- **THEN** there is no control that enables developer mode

### Requirement: Active developer mode SHALL be impossible to miss
While developer mode is active the IDE SHALL indicate it prominently and persistently, and the settings it
unlocks SHALL be presented behind an explicit warning.

The state is dangerous while it lasts, so the user should never be in it without knowing. A prominent,
permanent marker also protects against the case where someone else set it up — a machine handed over with
the flag in a shortcut announces itself rather than looking like an ordinary IDE.

#### Scenario: Working in developer mode
- **WHEN** developer mode is active
- **THEN** the IDE shows it prominently for the whole session

#### Scenario: Reaching the developer settings
- **WHEN** the developer opens the settings that developer mode unlocks
- **THEN** they are presented with an explicit warning about what they do

### Requirement: A dangerous capability SHALL require its own opt-in as well as developer mode
Developer mode SHALL unlock the ability to choose a dangerous capability, and each such capability SHALL
additionally require its own setting to be turned on before it takes effect.

Developer mode is entered for all sorts of ordinary reasons, and it should not follow that every dangerous
thing is now switched on. Separating "I am developing" from "I specifically want unsigned add-ins to load"
means the blast radius of the flag is the ability to make a further choice, rather than the choices
themselves.

#### Scenario: Developer mode without the capability enabled
- **WHEN** developer mode is active but the capability's own setting is off
- **THEN** the capability does not take effect

#### Scenario: The capability enabled without developer mode
- **WHEN** the capability's setting is on but the session was started without the flag
- **THEN** the capability does not take effect
