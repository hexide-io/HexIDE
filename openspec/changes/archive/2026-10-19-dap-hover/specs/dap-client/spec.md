## ADDED Requirements

### Requirement: External hover data tips
While an external session is active, the editor SHALL evaluate hovered identifiers only against the selected stopped external frame, using evaluate context hover, and only when the adapter advertises supportsEvaluateForHovers as true. It SHALL NOT evaluate these tips through the interpreter. An unsuccessful evaluation SHALL display no value tip.

#### Scenario: Selected caller value
- **WHEN** the user selects a caller frame and dwells over an identifier with a hover-capable adapter
- **THEN** the editor displays the adapter result for that identifier in the selected caller frame

#### Scenario: Unsupported or unresolved hover
- **WHEN** the adapter does not advertise hover support or refuses the identifier
- **THEN** the editor displays no external value tip

### Requirement: External hover inspection lifetime
The editor MUST discard pending and displayed external tips when the pointer moves away, the editor detaches, or the external inspection changes, including frame selection, Immediate evaluation, stepping, resume, End and relaunch.

#### Scenario: Late reply after frame selection
- **WHEN** a hover reply arrives after the selected frame changes
- **THEN** the old result is not displayed

#### Scenario: Visible tip after resume
- **WHEN** execution resumes with a data tip visible
- **THEN** the tip closes and its attached text is cleared
