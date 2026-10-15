## MODIFIED Requirements

### Requirement: Unimplemented language surface SHALL fail clearly
Where the engine does not implement part of the language, it SHALL fail in a way that identifies what is
missing, and SHALL NOT silently substitute different behaviour. The failure SHALL reach the developer, and an
error handler in the program SHALL NOT be able to trap it. A form holding a control the engine cannot create
SHALL be refused the same way, before any of its controls is placed, naming each control it cannot create.

The engine is deliberately incomplete, so meeting its edge is a normal outcome rather than an exceptional
one. A clear stop tells the developer they have found the boundary and lets them decide what to do; a silent
approximation tells them the feature works and lets them build on an answer that is wrong. Where an
approximation genuinely is the best available option, it is documented as one rather than presented as
fidelity.

A handler must not see the failure, because all a handler could do with it is carry on past a statement that
never ran, which is the silent substitution this requirement forbids. The failure is a fact about the engine,
not about the program, so it carries no VB6 error number for the program to branch on. And it has to reach the
person running the program: a failure that only the engine's log records is, at the keyboard, a statement that
silently did nothing (hexide-io/HexIDE#754).

A control the engine cannot create is the same edge reached through a form rather than a statement. Leaving it out
runs a different program, whether or not the code names it, so the form is refused rather than built without it
(hexide-io/HexIDE#857). A control nested inside one is named too, even of a kind the engine creates elsewhere,
because nothing inside an uncreatable control is read.

#### Scenario: Using a feature the engine does not implement
- **WHEN** a program uses a language feature the engine does not implement
- **THEN** it stops with an error identifying the unimplemented feature

#### Scenario: Under an error handler
- **WHEN** a program that has asked to continue at the next statement on error, or that has a handler active,
  uses a language feature the engine does not implement
- **THEN** it stops just the same, neither skipping the statement nor entering the handler

#### Scenario: In a running IDE
- **WHEN** a program running in the IDE reaches an unimplemented feature, in an event procedure or in Sub Main
- **THEN** the run ends and the developer is shown that the engine does not implement what the program reached

#### Scenario: A deliberate approximation
- **WHEN** the engine answers with an approximation because the real behaviour is unavailable to it
- **THEN** that approximation is documented at the point of implementation rather than presented as faithful

#### Scenario: A form holding a control the engine cannot create
- **WHEN** a form holding a control the engine cannot create is built to run, as the startup form or because the
  program loads it, under an error handler or not
- **THEN** it is refused before any of its controls is placed, naming every control it cannot create, nested ones
  included
