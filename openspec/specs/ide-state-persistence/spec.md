# ide-state-persistence Specification

## Purpose
Define what the IDE remembers about itself between sessions: window geometry and the arrangement of tool
windows.

The default layout is a guess, and it is wrong for most people — a window sized for the smallest reasonable
screen, panels at proportions nobody chose. Anyone who works in the IDE for an afternoon adjusts it, and an
IDE that discards that adjustment every launch is asking them to do it again daily until they stop
bothering.

## Requirements
### Requirement: Window geometry SHALL be restored
The IDE SHALL record its window's size, position and maximised state, and restore them on the next launch.

This is the adjustment everyone makes first and notices most. Restoring the maximised state separately from
the size matters: a window restored to its pre-maximised bounds by a developer who always works maximised
looks like the setting did not take.

#### Scenario: Reopening after resizing
- **WHEN** the IDE is resized or moved and then restarted
- **THEN** it reopens with the same geometry

#### Scenario: Reopening after working maximised
- **WHEN** the IDE was maximised when it closed
- **THEN** it reopens maximised

### Requirement: The tool window arrangement SHALL be restored
The IDE SHALL record which tool windows are open, where they are docked, and their sizes, and restore that
arrangement on the next launch.

Which panels a developer keeps open is a working style, not a preference to re-express each morning —
someone who closed the toolbox because they work in code did so deliberately. Sizes matter as much as
visibility: a properties panel restored to a default width is one the developer has to widen again before
they can read it.

#### Scenario: Toolbox home
- **WHEN** the IDE starts without recorded state or resets its window layout
- **THEN** the Toolbox is open in the left region
- **AND** closing and reopening it returns it to that region and records it as open for restart
- **AND** the recreated left dock is nonempty and receives visible layout space

#### Scenario: Showing a closed tool again
- **WHEN** a resized tool is closed and shown again, including after restarting the IDE
- **THEN** its last recorded region and dock proportion are restored
- **AND** repeated close/show cycles preserve the allocated size
- **AND** showing a closed member of a split, including after restarting the IDE, restores its own dock in that split

#### Scenario: Reopening after rearranging panels
- **WHEN** tool windows are docked, resized, opened or closed and the IDE is restarted
- **THEN** the same arrangement is restored

#### Scenario: Reopening split docks
- **WHEN** tool docks are arranged side by side or stacked, including nested splits, and the IDE restarts
- **THEN** their split orientations, proportions, tab groups and active tool tabs are restored
- **AND** older manifests without a split tree still restore through their recorded regions
- **AND** a tool that registers after layout initialization receives its saved dock and ancestor proportions when it registers

#### Scenario: A live tool in an unrecognized dock arrangement
- **WHEN** a tool is still in the live layout tree but its dock cannot be mapped to a persisted region
- **THEN** it SHALL be recorded as open in its home region
- **AND** a tool absent from the live tree is still recorded as closed, even if its owner reference remains

### Requirement: Restoration SHALL degrade to a usable layout
Where recorded state cannot be applied — because it is missing, unreadable, or describes an arrangement that
no longer makes sense — the IDE SHALL fall back to its default layout and start normally.

Layout state is the kind of thing that goes stale: a panel removed from the product, a geometry describing a
monitor no longer attached, a file truncated by a crash. None of those is a reason to fail to start, and a
developer locked out of the IDE by its own remembered layout has no way to reset it from inside the IDE.

#### Scenario: Unreadable state
- **WHEN** the recorded layout cannot be read
- **THEN** the IDE starts with its default layout

#### Scenario: A layout referring to something that no longer exists
- **WHEN** recorded state describes a tool window the IDE no longer has
- **THEN** the rest of the layout is applied and the unknown entry is ignored
- **AND** each unknown tool key is logged once per restore, even if it appears more than once

#### Scenario: A window positioned off-screen
- **WHEN** the recorded position is not visible on any attached display
- **THEN** the window is placed somewhere visible

### Requirement: IDE state SHALL be stored per user, not with the project
Window and layout state SHALL be recorded in the user's own configuration location rather than alongside any
project.

This state is about the IDE, not about a project — a developer's preferred window size does not change
because they opened a different project. Storing it per user also means it survives having no project open
at all, which is the state the IDE starts in.

#### Scenario: Opening a different project
- **WHEN** the developer opens a different project
- **THEN** the window geometry and panel arrangement are unchanged
