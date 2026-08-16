# addin-system Specification

## Purpose
Define how third-party code extends the IDE: what an add-in is, what the IDE offers it, and what happens
when one misbehaves.

VB6 had a community-extensible IDE, and the tooling built on that surface is part of why people stayed with
it. Reproducing the original mechanism is not possible — it was built on a component technology that does
not exist on the platforms HexIDE targets — so this is a deliberate replacement rather than a port: the same
idea of a community-extensible IDE, expressed in terms the host platform can actually honour.

The trust decisions that gate loading — signing, consent, revocation — are a separate capability
(`addin-trust`). This one covers what an add-in can do once it is allowed to run.

## Requirements
### Requirement: Add-ins SHALL extend the IDE through a curated host surface
An add-in SHALL receive a single host object at initialization and SHALL reach every IDE capability through
it, and the IDE SHALL NOT require an add-in to reference its internal assemblies.

The surface is deliberately narrow rather than complete. A facade can be kept stable across releases and
reasoned about; direct access to internals would make every refactor a breaking change for the ecosystem
and would leave add-in authors depending on things that were never meant to be contracts.

#### Scenario: Initializing an add-in
- **WHEN** an add-in is loaded
- **THEN** it is given the host object once
- **AND** everything it can do is reachable from that object

#### Scenario: Reaching a capability the host does not expose
- **WHEN** an add-in needs a capability the host surface does not offer
- **THEN** the capability is added to the host surface rather than the add-in reaching past it

### Requirement: Contributions SHALL be removable by the contributor
Registering a menu item, tool window, command, control, or project template SHALL return a handle that
removes the contribution when disposed.

Add-ins are loaded and unloaded, enabled and disabled, and may contribute conditionally. Without a removal
handle the only way to withdraw a contribution is to restart the IDE, and every contribution point would
need its own bespoke unregister call.

#### Scenario: Withdrawing a contribution
- **WHEN** an add-in disposes the handle returned by a registration
- **THEN** the contributed item disappears from the surface it was added to

#### Scenario: Shutting down
- **WHEN** the IDE shuts down
- **THEN** every loaded add-in is disposed

### Requirement: The IDE SHALL offer menu, tool window, command, control and template contribution
The host surface SHALL let an add-in add menu items at a named location, register a dockable tool window
from a factory, register a named command, register a custom VB6 control into the toolbox, and register a
project template.

These are the extension points the original add-in ecosystem actually used. Controls and project templates
matter disproportionately: a custom control that appears in the toolbox and a project type that appears in
the New Project dialog are what make an add-in feel like part of the product rather than a bolted-on panel.

#### Scenario: Contributing a menu item
- **WHEN** an add-in adds a menu item at a named parent location
- **THEN** it appears there, visually separated from the IDE's own items
- **AND** it can supply a predicate that greys the item out when unavailable

#### Scenario: Contributing a tool window
- **WHEN** an add-in registers a tool window with a factory
- **THEN** the window docks, floats and hides like a built-in one
- **AND** its content is constructed on first display rather than at registration

#### Scenario: Contributing a control
- **WHEN** an add-in registers a custom VB6 control
- **THEN** the control appears in the toolbox and can be placed on a form

### Requirement: Add-ins SHALL be able to observe IDE lifecycle events
The host surface SHALL raise events for project load and unload, file open and close, and run start and
stop, delivered on the UI thread.

An add-in that reacts to what the developer is doing needs to know when it happens; polling is the only
alternative and it is worse in every dimension. Delivering on the UI thread means handlers can touch their
own UI without marshalling, which is what add-in authors will do whether or not it is safe.

#### Scenario: Reacting to a project opening
- **WHEN** a project is loaded
- **THEN** subscribed add-ins are notified on the UI thread with the project's identity

### Requirement: Add-ins SHALL be able to inspect and modify editor and project state
The host surface SHALL expose the active document, the current selection, the project's file list, and
current diagnostics, and SHALL allow navigation and content modification. Positions SHALL be expressed with
the first line and column numbered one. Operations that modify content SHALL be awaitable.

The numbering is not arbitrary: VB6 counts lines from one everywhere it shows them, and an add-in surface
that counted from zero would produce off-by-one bugs in every add-in that displayed a position to a
developer. Awaitable writes matter for automation — a caller that modifies a document and then reads it
back must be able to wait for the edit to land rather than guess.

#### Scenario: Reading the active document
- **WHEN** an add-in asks for the active document
- **THEN** it receives the file's name, path, content and kind, or nothing when no document is active

#### Scenario: Modifying a document and continuing
- **WHEN** an add-in changes a document's content
- **THEN** it can await completion before performing the next operation

#### Scenario: Reading diagnostics
- **WHEN** an add-in asks for diagnostics
- **THEN** it receives them for the whole project or for one file, with severity and position

### Requirement: A failing add-in SHALL NOT affect the IDE or other add-ins
Loading and initializing each add-in SHALL be isolated so that a failure is recorded against that add-in
alone, and the IDE and every other add-in SHALL continue to load.

Third-party code fails, and it fails at startup where the damage is greatest. If one add-in throwing
prevented the IDE from starting, the user's only recovery would be to find and delete files by hand — from
an IDE they cannot open.

#### Scenario: An add-in throws during initialization
- **WHEN** an add-in throws while initializing
- **THEN** it is recorded as failed with the reason
- **AND** the IDE and the remaining add-ins load normally

### Requirement: Each add-in SHALL load in its own isolated context
Each add-in SHALL be loaded into its own unloadable context, and its private dependencies SHALL resolve
only from the files its package declares. Types the host shares with add-ins SHALL resolve from the host so
that a shared type has one identity.

Two add-ins shipping different versions of the same library must not fight, which is what a single shared
load context guarantees. Resolving dependencies only from declared files closes a real hole — otherwise a
dependency could be introduced by dropping a file into the folder, bypassing whatever verified the package.
Making contexts unloadable is what a future live-unload capability would need.

#### Scenario: Two add-ins depending on different versions of one library
- **WHEN** two add-ins each ship a different version of the same dependency
- **THEN** each resolves its own version

#### Scenario: An undeclared assembly in the package folder
- **WHEN** an assembly that the package does not declare is present in the add-in's folder
- **THEN** it is never loaded from that folder

### Requirement: Add-ins SHALL be independently enabled and disabled
The IDE SHALL let a user disable an add-in without removing it, SHALL persist that choice, and SHALL skip
disabled add-ins at load. Enablement SHALL be independent of whether the user has consented to the add-in.

These are different questions — "do I trust this?" and "do I want it running right now?" — and collapsing
them makes both worse. A user disabling an add-in to isolate a problem should not have to re-make a trust
decision to turn it back on.

#### Scenario: Disabling an add-in
- **WHEN** a user disables an add-in
- **THEN** it does not load on the next start, and the choice survives restart

#### Scenario: Re-enabling a previously consented add-in
- **WHEN** a user re-enables an add-in they had already allowed
- **THEN** it loads without asking for consent again
