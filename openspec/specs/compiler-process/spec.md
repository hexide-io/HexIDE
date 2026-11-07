# compiler-process Specification

## Purpose
Start the compiler without the stock IDE or its native host, find its root endpoint and key, keep it from outliving HexIDE, and end it cleanly, as measured on BETA 1024. Tracks #1689.

## Requirements

### Requirement: Compiler mode launch
The adapter SHALL start the compiler as the install's `bin\STUB.exe --compiler=0`, with `bin\` as its working directory, no window and standard input closed, and SHALL NOT need the stock IDE or its native host. It SHALL NOT start it as `--ide=PID`, which is the page server of the IDE's front end.

#### Scenario: Started without the IDE
- **WHEN** the adapter starts a compiler from an install folder
- **THEN** no process of the stock IDE is started

### Requirement: Endpoint key
The adapter SHALL read the first 42 bytes of the compiler's standard output as a little-endian 32-bit port followed by a 38-character braced GUID. It SHALL open the root socket as `ws://127.0.0.1:PORT/KEY/root` with that GUID as the key, unaltered, and SHALL NOT put the key in an exception or a log.

#### Scenario: Endpoint record
- **WHEN** the compiler writes its 42-byte record
- **THEN** the adapter takes the port and the key from it
- **AND** a port outside 1 to 65535, or a key that is not a braced GUID, fails the start without repeating the key

#### Scenario: Root opened with the key
- **WHEN** the adapter connects to a compiler it started
- **THEN** the root upgrades and pre-initialization answers with the process id it started (measured, BETA 1024)

### Requirement: Stub chosen from the project
The adapter SHALL choose the install's 32-bit or 64-bit compiler executable. It SHALL append `_noDEP` unless the project's `Settings` set `project.depAware` to `true`, and SHALL append `_LAA` on 32-bit only where they set `project.largeAddressAware` to `true`. A project with no `Settings` SHALL get `_noDEP`.

#### Scenario: Project that is not DEP-aware
- **WHEN** the project's `Settings` do not set `project.depAware` to `true`
- **THEN** the adapter starts the 32-bit executable whose name ends `_noDEP`
- **AND** loading the project does not answer -4

### Requirement: Compiler lifetime
The adapter SHALL hold the compiler in a job object that ends it when the job closes, so that the compiler never outlives HexIDE, because nothing watches the parent in compiler mode. Disposing the process SHALL end the compiler and wait for it. A shutdown request SHALL be command 9 with an empty payload on the root's next request id. It is not answered, and the compiler SHALL be awaited to its exit.

#### Scenario: Clean shutdown
- **WHEN** a prepared project asks its compiler to shut down
- **THEN** the compiler exits with code 0 (measured, BETA 1024)

#### Scenario: HexIDE ends first
- **WHEN** the job object closes with the compiler still running
- **THEN** the compiler ends

### Requirement: Install package list
The package list sent at initialization SHALL be the names of the folders under the install's `packages\`, in ordinal order. It SHALL NOT be the path of that folder, which does not complete project preparation on BETA 1024.

#### Scenario: Packages asked for during preparation
- **WHEN** preparation asks for a package
- **THEN** the identifier names a folder under the install's `packages\`, and its `package.twinproj` answers the request

### Requirement: Root exchange unchanged from BETA1011
On BETA 1024 the adapter SHALL send the root requests in the encodings recorded on BETA1011, since BETA 1024 accepted every one: pre-initialize (command 22, `96` then `0,1,10000,0,`), initialize (command 0, flags 0), load (1), projects loaded (3), the answer to a settings request (24, four zero bytes), and the answer to a package request (4). Shutdown (9) is new to this adapter.

#### Scenario: Recorded initialization replays
- **WHEN** the compiler side of the BETA 1024 recording `Recordings/beta-1024/root-initialization.jsonl` is played against the root session
- **THEN** the root session sends pre-initialize, initialize and shutdown byte for byte as recorded

### Requirement: Language data required
Initialization SHALL carry the install's language pack. On BETA 1024 a one-entry object does not complete it, as an empty one does not on BETA1011 (#1200).

#### Scenario: One-entry language object
- **WHEN** initialization carries a language object with a single entry
- **THEN** no initialize reply arrives, and initialization times out (measured, BETA 1024)