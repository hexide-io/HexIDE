# toolbars Specification

## Purpose
Define the toolbars: which ones exist, how they are shown and hidden, and what state survives a restart.

Toolbars are pure muscle memory. A VB6 developer knows where the Run button is without looking, and knows
that the Edit toolbar is the one they turn on when they are working in code. Getting the set and the
defaults right costs little and is noticed immediately when wrong.

## Requirements
### Requirement: The IDE SHALL provide the four toolbars VB6 provided
The IDE SHALL provide Standard, Edit, Debug and Form Editor toolbars, each carrying the commands its VB6
counterpart carried.

Four toolbars with familiar contents is the whole of the muscle-memory claim here. Consolidating them into
one, or redistributing the commands more logically, would be a defensible design and would break the thing
this surface exists to preserve.

#### Scenario: The available toolbars
- **WHEN** the developer looks at the list of toolbars
- **THEN** Standard, Edit, Debug and Form Editor are offered

### Requirement: Only the Standard toolbar SHALL be shown by default
On a fresh installation the Standard toolbar SHALL be visible and the other three SHALL be hidden.

That is VB6's default, and the reason it is right is unchanged: showing all four costs several rows of
vertical space before the developer has done anything, and most sessions need one of them. The others are
there when the work calls for them.

#### Scenario: First run
- **WHEN** the IDE is started with no saved preferences
- **THEN** the Standard toolbar is visible and the others are not

### Requirement: Toolbar visibility SHALL persist across sessions
A toolbar shown or hidden by the developer SHALL be in that state the next time the IDE starts.

A developer who turns on the Debug toolbar is not making a decision about the current five minutes; they are
saying that is how they want the IDE. Resetting it on restart means making the same decision every session
until they give up and stop using the feature.

#### Scenario: Restarting after changing visibility
- **WHEN** the developer shows or hides a toolbar and restarts the IDE
- **THEN** that toolbar is in the state they left it

### Requirement: Toolbars SHALL be toggled from the menu and from the toolbar area itself
The developer SHALL be able to toggle each toolbar from the View menu, and from a context menu raised by
right-clicking the menu bar or toolbar area — including empty space in that band. Both SHALL show which
toolbars are currently visible.

The right-click is where the gesture lives in every application with toolbars, and it is what a developer
tries first because their pointer is already there. Including empty space matters: with only Standard
visible most of the band *is* empty space, which is exactly when someone is reaching for another toolbar.

#### Scenario: Toggling from the toolbar area
- **WHEN** the developer right-clicks the toolbar band, including where no toolbar is drawn
- **THEN** a menu appears listing the four toolbars with their current visibility, and selecting one toggles it

#### Scenario: Toggling from the View menu
- **WHEN** the developer opens the toolbars list in the View menu
- **THEN** the same four are listed with their current visibility

### Requirement: A toolbar button SHALL be unavailable when its command cannot run
Each toolbar button SHALL reflect whether its command can currently execute.

A button that looks available and does nothing is worse than one that is greyed out, because the developer
concludes the feature is broken rather than that the prerequisite is missing. It also carries information
for free: a greyed Run button says the project is not in a state to run.

#### Scenario: A command whose prerequisites are unmet
- **WHEN** a toolbar button's command cannot execute
- **THEN** the button is shown as unavailable
