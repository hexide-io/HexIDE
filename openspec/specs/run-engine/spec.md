# Run engine

## Purpose
How the IDE runs a project and debugs it without depending on the interpreter that runs it: F5 goes through a run engine,
the debugger's front end through a contract in Core, and the facts a run reports in Core's own types. These are among the
seams by which the interpreter and the run-time forms engine can move out to an optional provider (#3411), and they let
another engine stand behind F5. Which parts move is decided on #3411, not here. What the automation tools say when no
engine runs a project is specified in `hexide-mcp-server`, and the IDE's own prompts in `localization`.

## Requirements

### Requirement: F5 SHALL run a project through a run engine
When F5 starts a project, the project runner SHALL claim the run and set the debugger up, and a run engine (`IRunEngine`)
SHALL build the program and run it (#3412). The engine SHALL say first whether it runs the project, and no start SHALL
reach it when it doesn't. It SHALL say why it refuses to start, if it does, before the run is claimed. A failure to build
SHALL come back as a failed start, and SHALL NOT be thrown.

#### Scenario: A project with no startup object
- **WHEN** F5 starts a project the engine refuses, because it has no startup object
- **THEN** the refusal is shown, and no run is claimed and nothing is torn down

#### Scenario: A project no engine runs
- **WHEN** F5 starts a project the engine says it doesn't run
- **THEN** no start reaches the engine

### Requirement: Only the run engine SHALL name the interpreter
The built-in engine SHALL run a project exactly when its type offers running it (`project-file-format`). Within the IDE's
product code, only the engine SHALL name the interpreter's `BasicInterpreter` and `VBProgram` (#3412).

#### Scenario: A project an add-in read
- **WHEN** the startup project's type offers no run
- **THEN** the built-in engine says it doesn't run it

### Requirement: A project no engine runs SHALL be run another way
An add-in's debug launch SHALL run a project through the External Debug Session, and SHALL NOT reach a run engine (#3186).
The run and debug tools SHALL say when no run engine runs the startup project, and SHALL name what can run it
(`hexide-mcp-server`, #3514).

#### Scenario: An add-in's launch
- **WHEN** a project's add-in offers a debug launch and the user starts it
- **THEN** it runs through the External Debug Session, and the run engine isn't asked

### Requirement: The debugger's front end SHALL use a contract in Core
The IDE's debug views and the automation debug tools SHALL drive a run through `IDebugController` in Core, which SHALL name
nothing of any one engine's (#3499). It SHALL model the Debug Adapter Protocol's concepts, without its wire format.
What the interpreter calls at each statement SHALL extend that contract in the interpreter's own assembly.

#### Scenario: A breakpoint from the code window
- **WHEN** the user sets a breakpoint and the program reaches it
- **THEN** the debug views hear of the stop through the contract in Core, with its reason and place

### Requirement: A run's error SHALL reach the IDE in Core's types
A running program's error SHALL reach the IDE as a failure in Core's own type (`RunFailure`), carrying the error's text and
the module and line it was raised at (`ErrorLocation`). The IDE SHALL word the error from it, and SHALL NOT read an
interpreter exception (#3498, #3670).

#### Scenario: A run-time error
- **WHEN** a program raises an error nothing handles
- **THEN** the IDE's error text and `get_last_runtime_error` give the module and line from the failure

### Requirement: A program's Shell launches SHALL be recorded in Core
The processes a program starts with `Shell` SHALL be recorded in Core (`ShellLaunches`), and `get_process_windows` SHALL
read only those (#3566).

#### Scenario: A process the program didn't start
- **WHEN** a process has the ID that a `Shell` call returned, but started after that launch
- **THEN** `get_process_windows` doesn't read it

### Requirement: The user's regional settings SHALL be recorded in Core
The host SHALL record the user's regional settings in Core (`UserRegion`) as it starts, before it pins every thread's
culture to the invariant one. A running program SHALL format and read text from that record (#3567).

#### Scenario: A program formats a date
- **WHEN** a running program formats a date with `Format$`
- **THEN** it uses the user's regional settings, as recorded at start, not the invariant culture the IDE's threads run in
