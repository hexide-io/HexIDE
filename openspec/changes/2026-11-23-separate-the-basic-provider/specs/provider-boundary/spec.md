## Purpose
Keep the boundary between the IDE and the Basic provider visible and shrinking while the provider moves out
of the tree (#3411).

## ADDED Requirements

### Requirement: Product references into the provider are listed
The IDE's product assemblies SHALL reference the Basic provider's assemblies only where a committed list in
the repository names the reference. The list SHALL name each referenced provider type, each assembly
reference, each project reference and each internals grant that crosses the boundary. The repository's tests
SHALL fail when a product assembly has a reference into the provider that the list doesn't name, and the
failure SHALL name the reference.

#### Scenario: A new reference into the provider
- **WHEN** a change makes a product assembly reference a provider type that the list doesn't name
- **THEN** the tests fail, naming the product assembly and the provider type
- **AND** the failure says to move the code into the provider or behind a shared contract

#### Scenario: A project added to the tree
- **WHEN** a project file is added that the guard doesn't classify
- **THEN** the tests fail, naming the project

### Requirement: The list can only shrink
The list SHALL name only references that exist. The repository's tests SHALL fail when the list names a
reference that no product assembly makes any more, and the failure SHALL name the row to delete. The tests
SHALL NOT write the list themselves.

#### Scenario: A slice removes a reference
- **WHEN** a change moves code so that a listed reference no longer exists
- **THEN** the tests fail until the change also deletes that row from the list

### Requirement: Provider registrations only add
A provider SHALL contribute to the IDE only through registrations that add. A registration naming a key,
resource, command, component or project type that the IDE or another provider already holds SHALL be refused,
and the refusal SHALL be logged, naming both owners.

#### Scenario: A second registration of an existing key
- **WHEN** a provider registers a resource whose key the IDE already holds
- **THEN** the registration is refused and logged, naming the IDE and the provider
- **AND** the IDE's own value is unchanged

### Requirement: A provider's strings come only from its own pack area
A provider's resource dictionaries SHALL NOT supply a localization string key or a theme key. Its strings SHALL
come only through its own language pack area, which SHALL be refused any key the IDE or another provider holds.
Nothing a provider registers SHALL replace a resource that the add-in consent and trust dialogs read.

#### Scenario: A provider's strings name a consent dialog's string
- **WHEN** a provider's language pack area or resources include a key that a consent or trust dialog reads
- **THEN** that key is refused
- **AND** the dialog shows the IDE's own text

#### Scenario: A provider's own strings
- **WHEN** a provider's language pack area supplies keys that no one else holds
- **THEN** the IDE shows the provider's text for them

### Requirement: A bundled provider registers as any add-in does
A provider's registrations SHALL go through the same host interface that an add-in receives once it has passed
consent. No registration point SHALL exist outside that interface. A first-party or bundled provider SHALL use the
same calls, and meet the same checks, as a third-party one. The language editing registration and the run
engine's internal registration are the two exceptions, while only the composed provider registers a language's
editing helpers or an engine (#5403, #5616).

#### Scenario: The bundled Basic provider starts
- **WHEN** the IDE starts the bundled Basic provider
- **THEN** the provider registers its contributions through the host interface an add-in receives
- **AND** each registration meets the same checks a third-party add-in's would

### Requirement: A registration beyond a plain control needs the provider's grant
The IDE SHALL keep, in its own source, a grant naming the owners that may register resources, a language pack area, or a
component whose role is not `Control` or that is a container. A registration of any of these from an owner the grant
doesn't name SHALL be refused, and the refusal SHALL be logged, naming the owner and what it tried to register. While the
provider is composed from `HexIDE.Desktop`, the grant SHALL name that composition and nothing else.

#### Scenario: An add-in registers a form component
- **WHEN** an add-in the grant doesn't name registers a component whose role is `Form`
- **THEN** the registration is refused and logged, naming the add-in and the component
- **AND** the designer offers no such component

#### Scenario: An add-in registers a container
- **WHEN** an add-in the grant doesn't name registers a component that is a container
- **THEN** the registration is refused and logged

#### Scenario: The bundled provider registers its form component
- **WHEN** the provider, composed from `HexIDE.Desktop`, registers a component whose role is `Form`
- **THEN** the registration succeeds

### Requirement: The designer's calls into a component class are contained
The form designer SHALL ask a registered component class for a designer root's title-bar icon only when that class is
the root's, and SHALL never ask a control placed on the root. A throw, or a missing answer, from a class's designer
contract SHALL be logged and SHALL answer as a class without the contract does, and the designer SHALL stay usable
(#5633).

#### Scenario: A root window's class throws for its caption
- **WHEN** the designer asks a root window's class for its caption and the class throws
- **THEN** the title bar shows the generic window glyph
- **AND** the throw is logged as a warning the first time and at debug after that

#### Scenario: A control on the form answers the caption contract
- **WHEN** a control on the form has a class that answers the caption contract
- **THEN** the designer never asks it

### Requirement: A double-clicked control goes only where the designer can place and save it
Where a component class gives the bounds its control takes when its tool is double-clicked, the form designer SHALL
place the control there only if the bounds are finite, have no negative width or height, and each fit a whole number
of twips. Otherwise, or when the class throws, the designer SHALL log it and SHALL centre the control at its added
size, as it does for a class without the contract (#5681).

#### Scenario: A double-clicked control's class gives bounds the designer can't use
- **WHEN** a control's tool is double-clicked and its class gives bounds with a negative width
- **THEN** the control is centred on the form at its added size, as a class without the contract's is
- **AND** the class's failure is logged as a warning the first time and at debug after that

### Requirement: A container's pages are switched only with its class's check
The form designer and automation SHALL ask for pages only through the paged-container contract and only of a
container's class, and SHALL contain each call: a throw SHALL be logged and SHALL answer as a container without
pages does. Automation SHALL refuse a page switch whose check or page count throws, changing nothing, and SHALL NOT save
the form when the switch itself throws (#5742).

#### Scenario: A container's class throws checking a page switch
- **WHEN** automation is asked to switch a container's page and its class throws saying whether the page can be switched
- **THEN** the switch is refused and nothing is changed
- **AND** the throw is logged as a warning the first time and at debug after that

#### Scenario: A container's class throws while switching the page
- **WHEN** the class throws while switching the page automation asked for
- **THEN** the reply says the switch failed, and the form isn't saved

#### Scenario: A container's class throws loading its pages
- **WHEN** the designer opens a form and a container's class throws loading its pages
- **THEN** the form opens, and a click on the container still asks the class for the page under it

#### Scenario: A plain control's class implements the paged-container contract
- **WHEN** a form holds a control whose class implements the contract but isn't a container
- **THEN** neither the designer nor automation asks it for pages

### Requirement: A form declares only a library registered as offering the control's class
Where a control's class says which control library the control is in, the form designer SHALL write that library's
header line into the form only when the registry lists the library as offering the class. Any other answer SHALL be
logged and SHALL declare nothing. A throw from the class SHALL be logged, and the control SHALL be placed as for a class
without the contract (#5749).

#### Scenario: A class names a library that doesn't offer it
- **WHEN** a control is placed and its class names a library the registry doesn't list as offering that class
- **THEN** the control is placed and the form's header gains no line
- **AND** the answer is logged as a warning the first time and at debug after that

#### Scenario: A class throws saying which library its control is in
- **WHEN** a control is placed and its class throws saying which library the control is in
- **THEN** the control is placed and the form's header gains no line

### Requirement: A granted registration still only adds
A registration from an owner the grant names SHALL still only add: it SHALL be refused, and logged naming both
owners, where any key or component it carries is already held by the IDE or another owner, and no grant SHALL
cover a key the consent or trust views read. The detailed rules arrive with the add-in system's delta (#5415) and
the localization delta (#5453).

#### Scenario: The bundled provider registers a key the IDE holds
- **WHEN** the provider, composed from `HexIDE.Desktop`, registers a resource key the IDE already holds
- **THEN** the registration is refused and logged, naming the IDE as the holder

### Requirement: A language's editing helpers register only through the owned host
A language's editing helpers SHALL be registered only through the host an add-in receives, by an owner the grant names
for them, and the IDE's own host SHALL refuse them. While only the composed provider registers them, the registration
MAY be internal to HexIDE's assemblies instead of a member of the host interface, and it SHALL then meet the same checks
(#5403).

#### Scenario: An owner the grant doesn't name registers editing helpers
- **WHEN** an add-in the grant doesn't name registers editing helpers, through the internal registration
- **THEN** the registration is refused and logged, and its helpers are never asked

### Requirement: A language's editing helpers only add
A registration naming a language id another owner already serves SHALL be refused whole and logged, naming both owners.

#### Scenario: A second owner registers a language that is already served
- **WHEN** an owner registers editing helpers for a language id another owner serves
- **THEN** the registration is refused and logged, naming both owners
- **AND** none of the languages it named is served by it

### Requirement: A language with no editing helpers gets none of them
The code window, the archive view, the Object Browser and the Watch dialog SHALL ask for a document's editing helpers by
its language id. A language with none registered SHALL get no greyed `#If` branches, no coloured brackets and no
declarations, as a plain-text file has. Every VB-family project's members SHALL be asked for as `vb6`.

#### Scenario: A VBA project's module is opened
- **WHEN** a module of a VBA or RDCore project is opened in the code window
- **THEN** its brackets and greying come from the helpers registered for `vb6`

#### Scenario: Nothing is registered for the language
- **WHEN** the Watch dialog lists a module's procedures and nothing is registered for `vb6`
- **THEN** it lists none for that module

### Requirement: An editing helper's throw answers as no helper
A throw from a registered editing helper SHALL be logged, naming its owner, and that call SHALL answer as though no
helper were registered. The registration SHALL stay. The first throw from each helper SHALL be logged as a warning and
later ones at debug.

#### Scenario: The bracket reader throws on an edit
- **WHEN** the registered bracket reader throws while the code window colours a line
- **THEN** the throw is logged, and that line's brackets are drawn uncoloured
- **AND** the next call asks the reader again

### Requirement: Withdrawing editing helpers doesn't wait on the UI thread
Withdrawing a language's editing helpers SHALL NOT wait on the UI thread, since the add-in registry withdraws them under
its lock. An open code window SHALL read its brackets and greying again in a job posted to the UI thread after the
withdrawal returns.

#### Scenario: The owner is disabled from another thread while a code window is open
- **WHEN** the helpers' owner is withdrawn from a thread other than the UI thread, with a code window open
- **THEN** the withdrawal returns without waiting on the UI thread
- **AND** the posted refresh then draws the window's brackets uncoloured and its branches ungreyed

### Requirement: The run engine registers only through the owned host
The engine that runs projects SHALL be registered only through the host an add-in receives, by an owner the grant names
for it. The IDE's own host SHALL refuse it, and a second engine SHALL be refused while one is registered, logged naming
both owners. While only the composed provider registers an engine, the registration MAY be internal to HexIDE's
assemblies instead of a member of the host interface, and it SHALL then meet the same checks (#5616, #5619).

#### Scenario: An owner the grant doesn't name registers an engine
- **WHEN** an add-in the grant doesn't name registers a run engine, through the internal registration
- **THEN** the registration is refused and logged, and the add-in's factory never runs

### Requirement: A run engine's throw answers as no engine
A throw from the run engine SHALL be logged, naming its owner, and that call SHALL answer as the IDE answers with no
engine. The engine SHALL stay registered.

#### Scenario: A start the engine throws on
- **WHEN** the engine throws as a project starts
- **THEN** the throw is logged, and the IDE is not left in Run mode

### Requirement: The designer asks only the registered run engine
The form designer SHALL preview a form, and SHALL learn which carried controls run, only by asking the registered run
engine through the IDE's forwarder, with its containment. With no engine, one being withdrawn, or one that doesn't
answer, it SHALL preview nothing and SHALL show every carried control as unsupported. A throw SHALL be logged naming the
engine's owner, and a preview's throw SHALL be shown in the box a refused preview is shown in (#5633).

#### Scenario: The engine throws as a form is previewed
- **WHEN** the engine throws as the designer asks it to preview a form
- **THEN** no window opens, the throw is logged naming the engine's owner, and its message is shown

### Requirement: The designer's previews and labels follow the engine
An engine SHALL close the previews it opened when it's withdrawn, and the designer SHALL ask again which carried
controls run once an engine comes or goes (#5633).

#### Scenario: The engine is withdrawn while a preview is open
- **WHEN** the engine is withdrawn with one of its previews open
- **THEN** the preview closes
- **AND** each carried control the engine ran is shown as unsupported

### Requirement: Withdrawing the run engine doesn't wait on the UI thread
Withdrawing the engine SHALL NOT wait on the UI thread, since the add-in registry withdraws it under its lock. A program
the engine is running SHALL be ended as End ends it, on the UI thread, after the withdrawal returns.

#### Scenario: The owner is disabled while its program is paused at a breakpoint
- **WHEN** the engine's owner is disabled from another thread while a program is paused at a breakpoint
- **THEN** the disable returns without waiting on the UI thread
- **AND** the program then ends, and the IDE leaves Run mode

### Requirement: A reserved project type takes its identity from the grant
The grant SHALL be able to reserve a project type: an identity, its project and group files, and its member formats.
Only the grant's owner, registering exactly those files, SHALL get that identity, and its projects SHALL carry it. Any
other registration of those files or that identity SHALL be refused and logged, whatever the load order. The identity
SHALL NOT change who is trusted: disabling, consent and launches SHALL go by the owner (#5625).

#### Scenario: The provider registers VB6's type
- **WHEN** the provider, composed from `HexIDE.Desktop`, registers a type for exactly `*.vbp` and `*.vbg`, with a format
- **THEN** VB6's projects carry `hexide.vb6` and "Visual Basic 6.0", as before
- **AND** the type's owner stays `hexide.basic`

#### Scenario: The provider registers part of VB6's files
- **WHEN** the provider registers a type for `*.vbp` alone, or for VB6's files and others
- **THEN** the registration is refused and logged, naming the provider and VB6's identity

#### Scenario: An add-in takes VB6's identity
- **WHEN** an add-in whose id is `hexide.vb6` registers a project type for any files
- **THEN** the registration is refused and logged

#### Scenario: The IDE starts without VB6
- **WHEN** the IDE starts with `--no-vb6`
- **THEN** the provider's registration of VB6's type registers nothing, and its other registrations go ahead

### Requirement: A project type's maker registers only through the owned host
What makes a project type's programs, File > Make's and Make with VB6's, SHALL be registered only through the host an
add-in receives, and only for a project type the owner's grant names. The IDE's own host SHALL refuse it, and a second
maker for a type one already serves SHALL be refused, logged naming both owners. While only the composed provider
registers a maker, the registration MAY be internal to HexIDE's assemblies, and it SHALL then meet the same checks
(#5621).

#### Scenario: A second maker for VB6's projects
- **WHEN** an owner whose grant names VB6's type registers a maker while another owner's maker serves it
- **THEN** the registration is refused and logged, naming both owners, and the first maker stands

#### Scenario: No maker for a project's type
- **WHEN** no maker is registered for the focused project's type
- **THEN** File > Make, Make Project Group, Make with VB6 and Start with VB6 are unavailable

### Requirement: A maker's throw fails the make
A throw from a maker or its toolchain SHALL be logged, naming its owner. A make it throws in SHALL be reported as failed,
and no program SHALL be reported as made. A question the IDE asks it, such as whether its toolchain is installed, SHALL
answer as with no maker. The IDE SHALL stay usable, and the maker SHALL stay registered.

#### Scenario: A maker throws while writing the program
- **WHEN** the maker throws while File > Make writes the program
- **THEN** Make reports that it failed, the files it repointed are put back, and nothing is reported as made

### Requirement: Withdrawing a maker doesn't wait on the UI thread
Withdrawing a maker SHALL NOT wait on the UI thread. From the moment its handle is disposed it SHALL be asked nothing
more, and the commands that make a program SHALL be asked again on the UI thread.

#### Scenario: The provider is disabled from another thread
- **WHEN** the add-in registry withdraws the provider's maker under its lock, from a thread other than the UI thread
- **THEN** the withdrawal returns at once, and File > Make is unavailable once the UI thread has run

### Requirement: The compiler Make with VB6 starts comes from the user's machine
The executable Make with VB6 and Start with VB6 start SHALL come from the user's own setting, `VB6_EXE`, or else VB6's
install folders, and never from anything a project names.

#### Scenario: A project names another program
- **WHEN** a project's file names another program in any line
- **THEN** Make with VB6 starts the compiler the user's machine names, and nothing the project named
