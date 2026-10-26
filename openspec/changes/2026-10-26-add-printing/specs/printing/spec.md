## ADDED Requirements

### Requirement: Shared code scope and entry points
The IDE SHALL open a shared localized scope dialog before platform destination selection from File →
Print and Project Explorer → Print. The latter SHALL supply its selected module or project explicitly.

#### Scenario: Explorer Print opens shared scope
- **WHEN** Print is invoked on an Explorer project
- **THEN** the shared scope dialog receives that project's context

### Requirement: Available code ranges
Range SHALL offer Selection, Current Module and Current Project. Selection SHALL require a non-empty
stream selection captured before editor focus is lost; rectangular selections SHALL remain unavailable.

#### Scenario: Rectangular selection
- **WHEN** the editor selection is rectangular
- **THEN** Selection is unavailable

#### Scenario: A selection is retained across the dialog
- **WHEN** a user invokes Print with a non-empty stream selection and chooses Selection
- **THEN** the job contains that captured selection even after the dialog takes focus

### Requirement: Module and project context
Current Module SHALL resolve an active code document or the code associated with the contextual designer
or Explorer module, and SHALL be disabled without an identifiable code module. Current Project SHALL
require one selected project, including in a project group.

#### Scenario: A project group has a selected project
- **WHEN** Current Project is chosen in a project group
- **THEN** only the selected project supplies the job

#### Scenario: No printable module context exists
- **WHEN** Print opens without an identifiable code module or stream selection
- **THEN** Current Module and Selection are disabled

### Requirement: Print content and Setup access
Print What SHALL enable Code only and show
Form Image and Form As Text as unavailable with an explanation. Setup SHALL be accessible from this
dialog and File → Print Setup without submitting a job.

#### Scenario: Unsupported print content
- **WHEN** the scope dialog opens
- **THEN** Code is the only enabled Print What choice and the other two choices explain their unavailability

### Requirement: Immutable editable-code snapshot
The print job SHALL use one immutable snapshot of the selected source, including unsaved editor text.
Current Project SHALL include code-bearing modules in project model order, exclude carried files and
serialized form layout, start each module on a new page and identify its name. The snapshot SHALL print
editable code without hidden serialization attributes. Printing SHALL NOT modify source or project files.

#### Scenario: An unsaved project is printed
- **WHEN** Current Project is chosen with edited modules and carried files present
- **THEN** output contains the unsaved code in project order with module page breaks and names
- **AND** carried files and serialized form layout are absent and source files remain unchanged

### Requirement: Session page setup and defaults
Setup SHALL offer supported paper sizes, portrait/landscape, editable margins, font family and point
size, and optional line numbers.

#### Scenario: Setup fields
- **WHEN** Setup opens
- **THEN** paper, orientation, margins, font and line-number controls are available

### Requirement: Initial page settings
Initial paper and orientation SHALL come from the selected printer's
reported defaults. Initial margins SHALL be 12.7 mm, visibly clamped to hardware minimums. The initial
font SHALL be the editor's configured family and size, with black text on white paper; line numbers
SHALL initially be off.

#### Scenario: Default line numbers
- **WHEN** no settings have been changed in this session
- **THEN** line numbers are off

#### Scenario: First Setup in a session
- **WHEN** a printer is selected before any Setup changes
- **THEN** its default paper and orientation are used with 12.7 mm margins subject to visible hardware
  clamping, the editor font, black on white and no line numbers

### Requirement: Setup transactions and validation
Accept SHALL commit settings for this session only; Cancel SHALL discard edits.
Setup SHALL NOT submit a job. Invalid font size or settings leaving no printable body SHALL be rejected.

#### Scenario: Setup is cancelled
- **WHEN** a user changes Setup values and cancels
- **THEN** prior session settings remain and no job is submitted

#### Scenario: Setup leaves no body
- **WHEN** margins or font size make the configuration invalid
- **THEN** the IDE rejects that configuration before submission

### Requirement: Shared pagination and final printer geometry
All platforms SHALL use the same snapshot and paginator with physical geometry in points (72 per inch).

#### Scenario: Shared page geometry
- **WHEN** the same snapshot and geometry are laid out on different platforms
- **THEN** the shared paginator supplies their page layout in points

### Requirement: Text layout
Layout SHALL use actual resolved fonts and explicit glyph fallback, preserve indentation and tabs using
editor tab width, and wrap long lines without dropping characters. Layout SHALL normalise line endings
without editing documents.

#### Scenario: Tabs and long lines
- **WHEN** code includes tabs and lines wider than the body
- **THEN** wrapping preserves characters and indentation using editor tab width

### Requirement: Printed line numbers
Enabled line numbers SHALL use original module numbers, including selections;
wrapped continuation rows SHALL NOT acquire new numbers. The number gutter SHALL be included in wrapping.

#### Scenario: Number gutter
- **WHEN** line numbers are enabled
- **THEN** wrapping accounts for the number gutter

#### Scenario: A numbered selection wraps
- **WHEN** a selection starting partway through a module is printed with line numbers
- **THEN** original module line numbers appear and wrapped rows preserve every character without new numbers

### Requirement: Final page geometry
Final paper, orientation and hardware bounds SHALL be resolved before submission, with repagination after
changes. Printing SHALL NOT silently scale or crop stale pages. Page-number ranges SHALL address the
final selected source's pages. Pagination and rendering SHALL avoid allocating all raster pages at once.

#### Scenario: Paper changes in a destination dialog
- **WHEN** final printer settings change the page geometry
- **THEN** the job is repaginated against those settings before submission rather than scaling stale pages

### Requirement: Windows native printing
On Windows the IDE SHALL use the system extended print dialog for destination, copies and driver
properties and the system page setup dialog for paper, orientation and margins, alongside shared font
and line-number settings.

#### Scenario: Windows destination selection
- **WHEN** the user continues from shared scope on Windows
- **THEN** the system extended print dialog offers destination, copies and driver properties

### Requirement: Windows page delivery
Pages SHALL reach the selected printer device context using its reported
printable bounds and resolution, with one raster page rendered at a time at device resolution within a
bounded allocation. Rendering allocation failure SHALL be reported rather than silently lowering quality.
A failed document SHALL be aborted and native resources SHALL be released.

#### Scenario: Windows document output fails
- **WHEN** page rendering or device-context submission fails after a document starts
- **THEN** the document is aborted, resources are released and the failure is reported

### Requirement: macOS native printing
On macOS the IDE SHALL use the system print panel through a PDF document print operation and the system
page layout panel with private session print settings. Output SHALL use actual size with automatic
rotation disabled. Final panel settings SHALL be reconciled with pagination so the operation consumes a
regenerated PDF when geometry changes. If reconciliation requires returning to shared Setup, the IDE
SHALL obtain confirmation, regenerate and reopen the native panel before submission.

#### Scenario: macOS panel changes orientation
- **WHEN** the user changes orientation in the print panel
- **THEN** the print operation uses a PDF paginated for the final orientation with actual-size output
  and no automatic rotation

### Requirement: Linux host printing tools
On Linux the IDE SHALL provide its own destination dialog over the host printing client tools without
a second UI toolkit. It SHALL discover queues, defaults and supported media, generate PDF and submit it
with explicit destination, media and copy options.

#### Scenario: Explicit Linux job options
- **WHEN** a Linux job is submitted
- **THEN** its destination, media and copy count are explicit

#### Scenario: Linux submission succeeds
- **WHEN** the host client accepts the PDF job for an explicit destination, media and copy count
- **THEN** the IDE retains its job identifier and reports submission

### Requirement: Linux process boundary
Processes SHALL use argument lists without a shell,
bounded execution and captured diagnostics; discovery SHALL use structured option tokens rather than
translated prose or guessed media. The IDE SHALL retain the accepted job identifier.

#### Scenario: Linux client times out
- **WHEN** a printing client exceeds its execution bound
- **THEN** execution ends with captured diagnostics available for the failure

### Requirement: Unavailable Linux printing
Missing tools,
unreachable printing services or no destinations SHALL explain unavailability and offer retry or
cancellation. The IDE SHALL NOT install tools or start a daemon automatically.

#### Scenario: Linux has no usable destinations
- **WHEN** destination discovery finds no usable printer or cannot reach the printing service
- **THEN** the dialog explains unavailability and offers retry or cancellation without submitting a job

### Requirement: Cancellation, errors and temporary output
Cancellation in shared or native dialogs SHALL submit nothing. The IDE SHALL propagate platform
failures as actionable localized errors and release native resources and temporary output on error or
cancellation. Temporary files SHALL be private and uniquely named per job and deleted after backend
consumption. Code text and generated PDFs SHALL NOT be logged or retained by default. Spool acceptance
SHALL be described as submitted rather than physically printed.

#### Scenario: A destination dialog is cancelled
- **WHEN** a user cancels the platform destination dialog
- **THEN** no job is submitted and any generated temporary output is removed

### Requirement: Code-only scope boundary
This capability SHALL cover desktop IDE code printing only. It SHALL NOT implement form-image printing,
Form As Text, rectangular selection printing, runtime printer objects or runtime common-dialog printer
operations. It SHALL NOT infer unmeasured VB6 defaults or serialization semantics from these HexIDE choices.

#### Scenario: A runtime program requests printer support
- **WHEN** runtime printer functionality is requested
- **THEN** this IDE printing capability does not supply runtime support

## Purpose

Define code selection, module and project printing on the three desktop platforms, with shared page
setup and pagination. These are the accepted HexIDE contracts from issue #45 and docs/printing.md;
VB6 defaults, pagination, hidden attributes and Form As Text semantics remain unmeasured. This delta
records future behaviour and does not assert that today's stub commands implement it.
