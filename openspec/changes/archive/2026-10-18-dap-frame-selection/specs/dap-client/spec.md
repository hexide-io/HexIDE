## ADDED Requirements

### Requirement: Selected external frame inspection
The IDE SHALL select the top frame at every new external stop and SHALL allow selection of another frame
from that stop in Call Stack. Locals SHALL clear immediately and asynchronously display the selected frame's
non-expensive scopes. Late results from an earlier selection or expired stop SHALL NOT replace current
locals. Unrelated session notifications SHALL preserve selection.

#### Scenario: Switch frames before locals arrive
- **WHEN** a caller's variables request is pending and the user selects the top frame again
- **THEN** only the top frame's variables may populate Locals, even if the caller's response arrives last

#### Scenario: Select a caller without moving execution
- **WHEN** the user selects a caller in the external Call Stack
- **THEN** its loaded source is revealed and its locals are inspected
- **AND** the execution highlight and Show Next Statement still identify the actual top frame

#### Scenario: Resume and stop again
- **WHEN** a selected caller's inspection overlaps a resume and a new stop
- **THEN** the old selection and inspection expire and the new stop selects its top frame
