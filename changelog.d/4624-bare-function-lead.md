### Fixed

- **A Function can head a member chain without parentheses** (#4624). Reads such as `GetC.Count` and `GetB.Me2.Q`, and Let writes such as `GetK.X = 5`, call the Function and use its returned object.
