## ADDED Requirements

### Requirement: A tool that writes a file SHALL report whether it was written
Where a tool asks the IDE to write a file, it SHALL report the outcome the IDE actually reached. A write
the IDE refused SHALL NOT be reported as a success, and the report SHALL say the file on disk is unchanged.

An automation client has no dialog to read. Everything a developer would learn from a warning — that the
file was left alone, and why — reaches an agent only through the tool's own answer, so an answer that says
"written" when nothing was written is not a cosmetic inaccuracy: it is the only signal there was, and it
was wrong. The agent then proceeds on the belief that its change is on disk, and every step after that is
built on it.

This matters most where the IDE is behaving correctly. A refusal is the IDE working as designed, protecting
a file it cannot reproduce; reporting it as success converts a safe outcome into a misleading one at the
surface, which is the one place the protection cannot be seen.

#### Scenario: A write the IDE refuses
- **WHEN** a tool asks the IDE to write a file the IDE will not reproduce
- **THEN** the tool reports that it was not written, and that the copy on disk is unchanged

#### Scenario: A write that succeeds
- **WHEN** a tool asks the IDE to write a file it can reproduce
- **THEN** the tool reports success, as before
