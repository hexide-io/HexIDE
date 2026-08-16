# type-library-metadata Specification

## Purpose
Define reading the contents of a referenced component library: the classes, members, signatures and
constants a project can call into.

Real VB6 projects are built on referenced libraries — data access, controls, the host application's own
object model — and most of the code a developer writes is calls into them. Without this, the IDE can list
what a project references but cannot say what any of it contains, which is the difference between a
reference list and something a developer can explore.

## Requirements
### Requirement: The IDE SHALL read member metadata from a referenced library
The IDE SHALL be able to enumerate a referenced library's classes and each class's members, with the
signature, type and any documentation the library carries.

Signatures are the point. A member name tells the developer something exists; the signature tells them how
to call it, which is what they came for and what they would otherwise be looking up elsewhere. Taking the
documentation too costs nothing, since the library already carries it.

#### Scenario: Exploring a referenced library
- **WHEN** the developer inspects a referenced library
- **THEN** its classes and their members are listed with signatures and any documentation the library carries

#### Scenario: Constants defined by a library
- **WHEN** a library defines named constants
- **THEN** they are listed with their values

### Requirement: Library reading SHALL be confined to the platform that supports it
Reading component libraries SHALL be attempted only on the platform where that component technology exists,
and SHALL be compiled so that unsupported platforms carry no such code path.

The technology is Windows-only and always will be — there is nothing to read on another platform because the
libraries are not there. Guarding it explicitly rather than letting it fail at runtime keeps the failure
predictable, and keeps a platform-specific dependency from leaking into builds that cannot use it.

#### Scenario: Running on Windows
- **WHEN** the IDE runs on Windows and a library is inspected
- **THEN** its metadata is read

#### Scenario: Running elsewhere
- **WHEN** the IDE runs on a platform without this component technology
- **THEN** no attempt is made to read a library

### Requirement: Unavailable metadata SHALL degrade, not fail
Where a library's metadata cannot be read — because the platform does not support it, the library is
missing, or it cannot be parsed — the IDE SHALL report that it has nothing to show and SHALL continue
working.

A referenced library that cannot be read is normal rather than exceptional: the project may have been
authored on another machine, the component may not be installed, and on a non-Windows platform none of them
can be read at all. Treating any of those as an error would make a cross-platform IDE unusable with real
projects, when the correct response is to carry on with less information.

#### Scenario: A referenced library that is not installed
- **WHEN** a project references a library that cannot be found
- **THEN** the project still opens and the IDE reports that the library's contents are unavailable

#### Scenario: Inspecting libraries on a platform that cannot read them
- **WHEN** the developer explores references on such a platform
- **THEN** the references are still listed, with their contents reported as unavailable
