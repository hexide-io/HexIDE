# usercontrol-rendering Specification

## Purpose
Define what a project's own UserControl looks like when it has been placed on a form: how it is drawn, how
it behaves as part of the host form, and what is drawn when it cannot be rendered properly.

The forcing requirement is that the form must open at all. A form referencing a UserControl the designer
does not understand used to fail to load, leaving the developer with a project they could not edit because
of a control they had not touched — the worst possible response, since it makes one unrecognised type take
out the whole form.
## Requirements
### Requirement: A form referencing a project UserControl SHALL open, with the control in place
Loading a form that hosts a UserControl defined in the same project SHALL succeed, and the control SHALL
occupy its saved position and size.

An unrecognised control type is a reason to draw something different, never a reason to refuse the form.
Keeping the geometry matters as much as loading: a control drawn at the wrong size silently misrepresents
the layout, and any arrangement the developer does around it is done against a false picture.

#### Scenario: Opening a form that hosts a UserControl
- **WHEN** a form referencing a project-defined UserControl is opened
- **THEN** the form loads and the control appears at its saved position and size

### Requirement: A hosted UserControl SHALL be drawn from its own definition
A hosted UserControl SHALL be drawn showing the child controls its definition contains, so it appears on the
host form as it will at run time. Where its definition holds those controls inside containers, they SHALL be
drawn inside those containers.

The point of placing a composite control is that it looks like the thing it is. A generic box conveys
position and size but nothing about whether the layout around it is right, which is the question the
developer is actually asking while arranging the form.

That argument applies with more force to a UserControl built out of containers, which is what a composite
control usually is. Drawing its contents flat would not merely lose the frames — it would pile controls that
belong in different containers on top of each other near the corner, which looks less like the control than
an empty box does.

#### Scenario: A UserControl containing child controls
- **WHEN** a UserControl containing child controls is hosted on a form
- **THEN** those children are visible within its bounds on the host form

#### Scenario: The UserControl's definition changes
- **WHEN** the UserControl's own definition is changed and the host form is opened again
- **THEN** the host form shows the updated appearance

#### Scenario: A UserControl whose children are held by containers
- **WHEN** a UserControl whose definition holds controls inside a container is hosted on a form
- **THEN** those controls appear inside that container, positioned as its definition places them

### Requirement: A hosted UserControl SHALL behave as one control on the host form
On the host form the UserControl SHALL be selectable, movable and resizable as a single control, and its
children SHALL NOT be individually selectable or movable there. Opening the control itself SHALL be
available from the host form.

The children belong to the UserControl's own definition, not to the form. Allowing them to be selected on
the host would invite edits with nowhere to be saved — the host form has no place to record a change to
another file's internals. Treating the control as opaque and offering a way into its own designer keeps
every edit in the file that owns it.

#### Scenario: Clicking a hosted UserControl
- **WHEN** the developer clicks a hosted UserControl on the form
- **THEN** the whole control is selected, not one of its children

#### Scenario: Editing the control itself
- **WHEN** the developer opens the hosted control from the host form
- **THEN** the UserControl's own designer opens, where its children can be edited

### Requirement: Where a UserControl cannot be drawn from its definition, a labelled placeholder SHALL be drawn
Where the definition cannot be resolved — including a UserControl hosted inside another and a circular
reference between definitions — a placeholder SHALL be drawn at the control's position and size, identifying
the type it stands for, and the form SHALL still open.

There will always be a case the renderer cannot resolve, and a circular reference has no correct rendering
at all. A placeholder answers the two questions that survive not knowing what the control looks like — how
much room it takes and what it is — while keeping the promise that the form opens. Labelling it is what
separates a deliberate fallback from a rendering bug.

#### Scenario: A UserControl hosted inside another UserControl
- **WHEN** a UserControl contains another project UserControl
- **THEN** the inner one is drawn as a labelled placeholder and the outer one still renders

#### Scenario: Definitions that reference each other
- **WHEN** two UserControl definitions reference each other
- **THEN** the cycle is detected, a placeholder is drawn, and no form fails to load

### Requirement: Properties the designer does not model SHALL survive a save
Properties on a hosted UserControl that the designer does not recognise SHALL be preserved and written back
unchanged when the form is saved.

A hosted control carries properties belonging to the control's own type, which the designer has no model
for. Dropping what it does not recognise would mean opening a form and saving it — with no edit at all —
silently stripped the control's configuration. Preserving them verbatim costs nothing and is the difference
between a designer that is incomplete and one that is destructive.

#### Scenario: Saving a form with a hosted UserControl
- **WHEN** a form hosting a UserControl is opened and saved without being edited
- **THEN** every property of the hosted control is written back unchanged, including those the designer does not model


### Requirement: A placed UserControl SHALL expose its configured extender members
At run time a placed UserControl SHALL refuse TabIndex, TabStop, SetFocus, CausesValidation and
HelpContextID when its `.ctl` sets CanGetFocus False, except that ForwardFocus True retains TabIndex.
ForwardFocus True SHALL remove TabStop, SetFocus, CausesValidation and HelpContextID. Enabled and Data* members SHALL be absent regardless
of that setting. An early-bound refusal SHALL use the interpreter's existing method-or-data-member
compile-error path; a late-bound missing member SHALL raise 438.

InvisibleAtRuntime True SHALL leave Name, Index, Parent and Object as members; Left and Top reads SHALL
raise 393, and other members SHALL be absent. A windowed hWnd SHALL be explicitly unimplemented, with
no fabricated value; a windowless hWnd SHALL be absent, per #1631's supervisor decision. Recognised
members without runtime bindings (Parent, Object, Container, CausesValidation and HelpContextID) SHALL
be explicitly refused as unimplemented rather than reported as missing.

Parts A–C of #1631 cover member availability, focus, keys, containers, drawing and lifecycle. Designer
property editing remains separate work.

#### Scenario: A late-bound non-focusable control
- **WHEN** a program reads TabIndex or calls SetFocus through an Object holding a UserControl with CanGetFocus False and ForwardFocus False
- **THEN** the statement raises 438

#### Scenario: An invisible extender
- **WHEN** a program reads Left on a UserControl with InvisibleAtRuntime True
- **THEN** the statement raises 393
- **AND** reading Width instead is refused as a missing member

#### Scenario: Native handles are never invented
- **WHEN** a program reads hWnd on a windowed UserControl
- **THEN** HexIDE refuses the operation as not implemented
- **AND** a windowless UserControl instead refuses hWnd as a missing member


### Requirement: A placed UserControl SHALL receive and forward focus keys
Tab SHALL skip CanGetFocus False, ForwardFocus True and InvisibleAtRuntime controls, while a windowless
UserControl and a child of a ControlContainer SHALL be separate stops. Controls without TabIndex SHALL
leave holes; out-of-range file indices SHALL take free positions in form order.

AccessKeys SHALL require Alt. A normal UserControl SHALL receive AccessKeyPress with the lower-case
letter before taking focus. ForwardFocus SHALL send the key to the next focusable control in tab order;
CanGetFocus False SHALL send it to the first. A UserControl receiving another control's access key,
including a Label's, SHALL receive AccessKeyPress for that key even without AccessKeys of its own.

#### Scenario: Access key forwarding
- **WHEN** Alt+Z activates a CanGetFocus False UserControl whose AccessKeys is z
- **THEN** the first focusable control takes focus
- **AND** if that destination is a UserControl it receives AccessKeyPress 122

### Requirement: DefaultCancel SHALL route Enter and Esc to the UserControl
DefaultCancel True SHALL expose Default and Cancel, initially False. Default True SHALL make
Ambient.DisplayAsDefault True and raise AmbientChanged with DisplayAsDefault. With Default and Cancel
True, Enter from a TextBox SHALL raise AccessKeyPress 13 on the UserControl without TextBox KeyPress,
and Esc SHALL raise 27. CommandButton focus SHALL make DisplayAsDefault False; Enter SHALL click that
button. Each placed control SHALL execute its handlers on its own instance using the program's shared
interpreter and event queue.

#### Scenario: Enter in a TextBox
- **WHEN** a TextBox has focus and a DefaultCancel UserControl has Default True
- **THEN** the UserControl receives AccessKeyPress 13
- **AND** the TextBox receives no KeyPress for Enter

### Requirement: UserControl containers and drawing SHALL follow measured settings

At run time Container assignment into a ControlContainer UserControl SHALL preserve Left and Top;
assignment into a plain or InvisibleAtRuntime UserControl SHALL raise 425. ContainedControls.Count
SHALL count the placed children. Alignable SHALL expose Align initially as Integer 0 and Negotiate
reads SHALL raise 393. Align 2 SHALL span the form width at its bottom, preserving the control height.
BackStyle 0 SHALL show the underlying form rather than paint the UserControl BackColor.

#### Scenario: Moving a control into a ControlContainer
- **WHEN** a program assigns a CommandButton's Container to an eligible UserControl
- **THEN** Container identifies that UserControl and Left and Top retain their values

### Requirement: UserControl events SHALL follow the measured order

Focus entry SHALL raise EnterFocus, the form's instance GotFocus, then the control's GotFocus;
a windowless instance SHALL raise the form's GotFocus first. Leaving SHALL raise the control's
LostFocus, ExitFocus, then the form's instance LostFocus. Between UserControls, the new EnterFocus
SHALL precede the old LostFocus and ExitFocus, before the form's old LostFocus and new GotFocus and
the new control's GotFocus.

Startup SHALL raise Initialize, ReadProperties and Resize in form order with windowless instances
last. Show SHALL follow Form_Load and precede Form_Activate. Unload SHALL raise Hide then Terminate
per control. InvisibleAtRuntime instances SHALL raise only Initialize, ReadProperties and Terminate.

#### Scenario: An invisible instance loads and unloads
- **WHEN** its form starts and subsequently unloads
- **THEN** Initialize and ReadProperties run before Form_Load and Terminate runs on unload
- **AND** Resize, Show and Hide do not run
