### Fixed

- **Deep nesting no longer stops a module loading** (#3210). The interpreter refused a module whose `If`
  blocks nested more than 58 deep, or whose parentheses nested more than 291 deep, as "nesting too deep",
  although vb6.exe builds 7,227 and 7,643. Parsing now runs on its own 64 MiB stack and accepts both, and a
  module nested that deep parses in well under a second. Running code nested several hundred levels deep can
  still raise Error 28, Out of stack space, before VB6 would (#3228). A syntax error's message now quotes only
  the line the error is on, not the module's text before it.
