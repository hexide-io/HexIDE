## MODIFIED Requirements

### Requirement: A server SHALL be given the project's working directory
Each server SHALL be started with the current project's working directory as its working directory, and told
that directory as its workspace root, unless its entry states otherwise.

Servers routinely resolve their own configuration relative to where they are run. A server started somewhere
arbitrary reads none of the user's settings for it and reports subtly different results with no indication
why — a wrong answer rather than a missing one.

Where a project's working directory changes during a session, running servers SHALL be restarted so that
none continues against a root that no longer describes the project.

**Where that directory does not exist, the server SHALL still be started**, inheriting the IDE's own working
directory, and SHALL still be told the project's directory as its workspace root. The two are separate
questions — *where the process runs* and *which tree it analyses* — and they only coincide by accident. A
project that has not been saved yet has a directory that is real as an answer to the second and not yet real
as an answer to the first: it is where the project's files will be written the moment the user adds a
module, and it is the parent of every document URI the server will be sent. Starting a process there instead
fails outright, which costs the whole connection and every language feature with it.

The IDE SHALL NOT create the directory in order to launch there. Nothing reaps these directories, so one
created per server start would accumulate with no owner, and a directory that exists would then no longer
mean the project has content in it.

A working directory named by the server's **own entry** SHALL NOT be checked for existence and SHALL be used
as given. Somebody who named one meant it, and a name that does not resolve is a configuration error they
can correct and must be told about; inheriting silently there would start the server against the wrong tree,
which is a wrong answer rather than a failure.

Where a server cannot be started, the reported reason SHALL name the working directory it was to be started
in. The operating system's own message for an unusable working directory does not name it, so a report
without it points at the executable for a fault that has nothing to do with it.

#### Scenario: A server that reads its own configuration from the workspace
- **WHEN** a server is started for a project
- **THEN** its working directory and workspace root are the project's working directory

#### Scenario: A project that has not been saved yet
- **WHEN** a server is started for a project whose directory does not exist
- **THEN** the server starts, inheriting the IDE's working directory
- **AND** it is still told the project's directory as its workspace root
- **AND** the directory is not created

#### Scenario: An entry naming a working directory that is not there
- **WHEN** a server's own entry names a working directory that does not exist
- **THEN** the server does not start, and the reported reason names that directory
