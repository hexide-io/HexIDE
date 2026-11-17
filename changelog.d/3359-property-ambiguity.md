### Fixed

- **A Public variable beside another standard module's Property is ambiguous when read bare** (#3359).
  Get and Let-only properties now raise "Ambiguous name detected" at the bare use, as measured in VB6;
  qualified reads and each module's own names keep their precedence.
