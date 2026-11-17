### Fixed

- **Objects held by module variables and Static locals terminate when a Sub Main program returns** (#1490), in reverse creation order before default form instances. `End` continues to raise no termination events.
