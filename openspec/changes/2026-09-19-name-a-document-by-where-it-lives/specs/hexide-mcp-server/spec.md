## ADDED Requirements

### Requirement: A tool that names a document SHALL name its project, and count lines from the top of the file
A tool that accepts or reports a form, module or class SHALL identify it by its project and its name, SHALL
find it in any loaded project, and SHALL match both without regard to case. Where a reply also carries the name
a language server knows the document by, it SHALL say so. Every line number a tool accepts or reports SHALL be
counted from the top of the file, header included. Reading a document's content SHALL return its whole file
whether or not it is open. Writing content SHALL NOT be refused for being in the shape reading returned — the
whole file with its header unchanged, or the code alone — which leaves the existing refusal of a document the
IDE will not save in force for its own reason.

A caller that has never seen the IDE cannot know which of a document's several names a bare `uri` field holds,
and a name that is only unique within a project is ambiguous the moment a group is open. A read whose result
the matching write refuses breaks the most basic loop an automation client runs.

#### Scenario: Setting a breakpoint by name in the wrong case
- **WHEN** a client sets a breakpoint in `form1` of a project containing `Form1`
- **THEN** it is shown in `Form1`'s gutter, pushed to the next run, and saved with the project

#### Scenario: Reading and writing back a form
- **WHEN** a client reads the content of a form the IDE will save, and writes the same text back
- **THEN** the write succeeds and the form is unchanged
