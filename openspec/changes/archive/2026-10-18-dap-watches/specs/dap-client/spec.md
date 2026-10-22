## ADDED Requirements

### Requirement: External expression watches
The IDE SHALL keep a separate external expression-watch list for the IDE session and SHALL evaluate it
in the selected stopped external frame using DAP context `watch`. It SHALL preserve result text, type and
expandable variable references, display refusals per expression, and discard replies from obsolete stops,
frames or watch definitions. Resume and End SHALL clear remote values. Stop, frame selection, watch edits
and Immediate completion SHALL refresh values. External watch definitions SHALL NOT alter interpreter watches.

#### Scenario: Select another frame
- **WHEN** the user selects another external call-stack frame
- **THEN** old values clear and expressions are evaluated in that frame, including frame ID zero

#### Scenario: Adapter rejects an expression
- **WHEN** an adapter refuses one watch expression
- **THEN** its row displays the error while other expressions remain evaluable

#### Scenario: Expand a watch
- **WHEN** a watch result has a nonzero variables reference and the user expands it
- **THEN** children are fetched asynchronously for the same stop

#### Scenario: Break-condition types
- **WHEN** an external watch is added or edited
- **THEN** only expression watches are offered; interpreter break-condition watches remain unchanged
