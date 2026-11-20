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

### Requirement: Form-hosted Ambient reads

A running form-hosted UserControl SHALL read its container's BackColor, ForeColor and Font object,
its instance DisplayName, and the measured constants from [#4149](https://github.com/hexide-io/HexIDE/issues/4149).
UserMode SHALL be True and ShowHatching False. Palette, RightToLeft on a right-to-left form and non-form
containers SHALL refuse explicitly as not implemented.
Design-time UserControl execution remains unavailable.

#### Scenario: Reading the form font during startup
- **WHEN** a UserControl reads Ambient.Font in ReadProperties on a form with Arial 12 Bold
- **THEN** the object reads Name Arial, Size 12, Bold True and Italic False

#### Scenario: A refusal while the form loads its controls
- **WHEN** a UserControl's Initialize, ReadProperties or Resize reads a member HexIDE refuses
- **THEN** the refusal is reported as one in any event procedure is, rather than ending the run unreported

### Requirement: Ambient.LocaleID SHALL answer only where it was measured

A form-hosted UserControl's Ambient.LocaleID SHALL read 1033 whatever the user locale where Windows' system
locale and interface languages are US English, as measured (#4266). Which of those it follows is unmeasured, so
it SHALL refuse explicitly where any of them differs, and off Windows.

#### Scenario: An en-GB user locale on US English Windows
- **WHEN** a UserControl reads Ambient.LocaleID with the user locale en-GB and the system locale and interface
  languages US English
- **THEN** it reads 1033

### Requirement: A loaded UserControl array element SHALL run its own code

For templates with saved custom properties, loaded array elements SHALL attach their own module
instance and run Initialize, Resize and ReadProperties inside Load, never InitProperties. They SHALL copy the template geometry and start
hidden. Show SHALL be deferred, and form teardown SHALL include loaded elements in index order.
Measured: [#4337](https://github.com/hexide-io/HexIDE/issues/4337), native and p-code (oracle PR #4341).

#### Scenario: Loading a hidden array element
- **WHEN** a program loads a new UserControl array element from a template with saved custom properties
- **THEN** its own Initialize, Resize with extender Width zero, and ReadProperties run before Load returns
- **AND** ReadProperties receives the same PropertyBag surface as a design-time element, with operations still unimplemented
- **AND** the resulting element has the template's saved Width, Height and Top and Visible False

#### Scenario: Showing loaded elements before the form appears
- **WHEN** loaded elements are made visible before the form shows
- **THEN** Show runs in index order with the other elements before Form_Activate

#### Scenario: Showing a loaded element on a visible form
- **WHEN** the program sets the loaded element's Visible to True on a showing form
- **THEN** Show waits until that statement has returned

#### Scenario: Unloading the form with loaded elements
- **WHEN** a program executes Unload Me
- **THEN** Form_Unload runs before each element's Hide and Terminate, in index order

### Requirement: Form ambient changes reach placed UserControls

The runtime SHALL raise UserControl_AmbientChanged synchronously on each UserControl, in file order, when
its form's colour changes or font is assigned. Unchanged colours SHALL be silent; unchanged font assignments SHALL notify.
PictureBox- and Frame-hosted controls SHALL read the form's ambients. Notifications SHALL run during Form_Load
without Paint. Other, unmeasured containers SHALL retain their explicit refusal.

#### Scenario: Colour assignments

- **WHEN** a form changes BackColor or ForeColor
- **THEN** each UserControl receives the corresponding property name before the next statement, reading the new value
- **AND** assigning the current colour raises nothing

#### Scenario: Font assignments

- **WHEN** the form's FontName, FontSize or FontItalic is assigned, even its current value, or Font.Bold is set True,
  or Set Font assigns a shared font, or that font is subsequently mutated
- **THEN** each UserControl receives "Font" before the next statement
- **AND** Caption, PictureBox and Frame colour/font changes raise no ambient event

#### Scenario: PictureBox containment

- **GIVEN** UC2 inside Picture1 precedes UC11 in the form file
- **WHEN** the form changes an ambient during Form_Load or later
- **THEN** UC2 receives the event before UC11 and both read the form's new values, regardless of Picture1's values

#### Scenario: Frame containment

- **GIVEN** a UserControl inside a Frame
- **WHEN** the form changes an ambient, or the control reads one
- **THEN** the control is notified with the others and reads the form's values, regardless of the Frame's

Measured native and p-code in [#4448](https://github.com/hexide-io/HexIDE/issues/4448) (oracle PR #4630).
