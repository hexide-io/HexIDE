# TreeView

## Purpose

Describe the runtime drawing, input and native file persistence of MSComctlLib.TreeView (#1086). ImageList binding awaits
#1083; remaining runtime members and the sample-node designer preview remain later slices.

## Requirements

### Requirement: Dispatch shared control methods

A running TreeView SHALL use the shared SetFocus, Move and ZOrder paths (#2557).
Remaining unsupported method calls, including Refresh, SHALL raise HexIDE's not-implemented
error naming the member and tracking issue #1086. This changes dispatch only; the
[measured TreeView surface](../../../docs/vb6-fidelity-oracle/in-box-controls-the-treeview.md)
remains the source for its own methods.

#### Scenario: Move or focus a running tree

- **WHEN** code calls TreeView1.SetFocus, Move or ZOrder
- **THEN** the shared control implementation handles the call

#### Scenario: Refuse a pending method

- **WHEN** code calls TreeView1.HitTest
- **THEN** the not-implemented error names HitTest and #1086

### Requirement: Draw the runtime hierarchy

The control SHALL draw visible nodes in sibling order, descending into expanded nodes, with
Indentation controlling each hierarchy level. Text SHALL reflect node Text and Bold. Model changes
SHALL invalidate the drawing. Rows SHALL start at the client origin and be 16 pixels high at 96 dpi,
with pointer hit-testing using the same row height. GetVisibleCount SHALL return the capacity
in complete client rows as a Long, independently of the number of nodes, before layout,
in Form_Load and after Show. The client inset on each side SHALL be 2 pixels for Appearance 1 /
BorderStyle 0, 3 for 1 / 1, zero for 0 / 0 and 1 for 0 / 1. Drawing and pointer hit-testing
SHALL use that inset. Appearance 1 SHALL draw the sunken 3D edge and BorderStyle 1 SHALL
add a single line inside it (or alone with Appearance 0).

#### Scenario: Expand and mutate a tree from code

- **WHEN** code adds, removes, renames, sorts, moves or expands nodes
- **THEN** the next drawing reflects the current hierarchy and visible labels

#### Scenario: Capacity of the eight measured controls

- **WHEN** GetVisibleCount is read with (Appearance, BorderStyle) pairs (1,0), (1,1), (0,0), (0,1)
- **THEN** the 1200-twip tree returns 4, 4, 5, 4, and the 3000-twip tree returns 12 in each case
- **AND** Form_Load and reads after Show agree, with zero or 30 nodes

#### Scenario: Capacity of the oracle control

- **WHEN** GetVisibleCount is read on the oracle's 3000-twip-high control
- **THEN** it returns 12 as a Long, including when there are no nodes

### Requirement: Choose decorations from the control state

The control SHALL draw lines for Styles 4–7 and expansion buttons for Styles 2, 3, 6 and 7.
Root decorations SHALL appear only with LineStyle 1. Checkboxes SHALL draw a box for every
visible node and show its Checked state. Selection SHALL highlight the label, with HideSelection
suppressing the highlight when unfocused and an unfocused visible selection using grey.
Other pixel metrics are not specified.

#### Scenario: Text-only tree

- **WHEN** Style is 0
- **THEN** labels retain indentation without hierarchy lines or expansion buttons

#### Scenario: Checkboxes and an unfocused selection

- **WHEN** Checkboxes is True and HideSelection is False
- **THEN** visible nodes have boxes and the selected label remains highlighted when unfocused

### Requirement: Dispatch measured node events

The control SHALL dispatch NodeClick on each label click, including a right click, selecting the node.
Expansion-button clicks SHALL toggle expansion without NodeClick; checkbox clicks SHALL toggle Checked
and dispatch NodeCheck without changing selection. MouseDown SHALL precede the node event, followed by
MouseUp and Click. A label double click SHALL toggle expansion before DblClick, with no second MouseDown
or Click. Mouse coordinates SHALL be client-area twips, subtracting the measured
Appearance/BorderStyle edge from the outer pointer position.

#### Scenario: Mouse coordinates inside the edge

- **GIVEN** a default-edged tree or a flat borderless tree
- **WHEN** the pointer moves, presses and releases 21 pixels from the outer top-left corner
- **THEN** MouseMove, MouseDown and MouseUp report X and Y as 285 twips with the default edge,
  or 315 twips with no edge

#### Scenario: Collapse an ancestor of the selected node

- **WHEN** a selected node's ancestor collapses from code or its button
- **THEN** selection moves to that ancestor and Collapse is raised without NodeClick

### Requirement: Keep code mutations separate from input selection

Expanded changes and EnsureVisible SHALL raise Expand/Collapse for the nodes they open or close.
Setting SelectedItem, Selected, Checked or Text, adding/removing/clearing nodes and sorting SHALL NOT
raise NodeClick or NodeCheck. On gaining focus with no selection, the control SHALL select its first node.
Adding nodes while already focused SHALL NOT select them.

#### Scenario: Reveal a descendant

- **WHEN** code calls EnsureVisible
- **THEN** each collapsed ancestor opens with Expand and the return value remains False

### Requirement: Dispatch measured keyboard and SingleSel sequences

With no label editor open, Enter SHALL toggle the selected node before KeyDown. Home and Down SHALL select with NodeClick;
Right SHALL expand or select the first child, and Left SHALL collapse or select the parent.
Numpad multiply/subtract SHALL expand/collapse. Space with Checkboxes SHALL toggle Checked with
NodeCheck. These actions other than Enter SHALL follow KeyDown and precede KeyUp.
SingleSel label selection SHALL collapse the old selected node unless it is an ancestor, expand the new
node, then raise NodeClick.

#### Scenario: Select another root with SingleSel

- **WHEN** the user clicks a collapsed root with another expanded root selected and SingleSel True
- **THEN** Collapse of the old root precedes Expand of the new root and NodeClick

### Requirement: Preserve native TreeView files

The loader SHALL use Style 0 when a saved block omits Style, while a new control SHALL start at 7.
The writer SHALL order extents before `_Version`, then use the measured owned-property order, retaining
HIMETRIC Indentation and typelib property names. Appearance SHALL always be written and OLEDragMode
and OLEDropMode SHALL each be written once. Owned values without modelled value writers SHALL remain
carried verbatim and unavailable for designer editing, including Font and ImageList references.
Missing extents on unchanged loaded controls SHALL retain their absence; that edge remains unmeasured.

#### Scenario: Save the measured TreeView form

- **WHEN** the VB6-authored treeview corpus form and companion are loaded and saved
- **THEN** both files are reproduced byte for byte, including the three distinct Style cases

#### Scenario: Add a new TreeView

- **WHEN** a new TreeView is saved
- **THEN** its block writes Style 7 and Appearance 1

### Requirement: Edit a label

StartLabelEdit SHALL open a shared label editor for a visible selected node in either LabelEdit mode.
With no selection it SHALL do nothing. F2 SHALL only dispatch KeyDown 113, and manual-mode label
clicks SHALL NOT start an edit. The editor SHALL exist with the original text at zero size
before BeforeLabelEdit runs, with Cancel as Integer zero; cancellation SHALL remove it.
Otherwise it SHALL open two pixels left and one above the label with all text selected and a single
border. The first key SHALL increase height by four pixels and move it two pixels upward.
Enter, Tab and focus loss SHALL commit changed text, and Escape SHALL discard it. Tab SHALL move focus
to the next control after committing. Unchanged text SHALL NOT
raise AfterLabelEdit. During AfterLabelEdit the node SHALL retain its original Text; afterward it
SHALL take the handler's NewString unless Cancel is set. In automatic mode a click on an already-selected
label SHALL start editing one host double-click time after MouseUp. A double-click SHALL cancel the
pending edit. Commit-before-click ordering and focus ordering when an event handler suspends remain outstanding; positioning a selected invisible node and restarting an active edit remain unmeasured and refused.

#### Scenario: A handler substitutes a label

- **WHEN** an edit of Node 2 is committed with xyz and AfterLabelEdit changes NewString to Changed
- **THEN** Text reads Node 2 inside the handler and Changed after it returns

#### Scenario: Click the selected label

- **WHEN** an already-selected label is clicked with LabelEdit 0
- **THEN** MouseDown, NodeClick and MouseUp precede BeforeLabelEdit
- **AND** the editor opens after the host double-click interval from MouseUp
- **AND** a second press forming a double-click cancels that pending editor
