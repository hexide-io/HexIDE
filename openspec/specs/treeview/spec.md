# TreeView

## Purpose

Describe the runtime drawing, input and native file persistence of MSComctlLib.TreeView (#1086). ImageList binding awaits
#1083; label editing, remaining runtime members and the sample-node designer preview remain later slices.

## Requirements

### Requirement: Draw the runtime hierarchy

The control SHALL draw visible nodes in sibling order, descending into expanded nodes, with
Indentation controlling each hierarchy level. Text SHALL reflect node Text and Bold. Model changes
SHALL invalidate the drawing. Rows SHALL start at the top and be 16 pixels high at 96 dpi,
with pointer hit-testing using the same row height. GetVisibleCount SHALL return the capacity
in complete rows as a Long, independently of the number of nodes.

#### Scenario: Expand and mutate a tree from code

- **WHEN** code adds, removes, renames, sorts, moves or expands nodes
- **THEN** the next drawing reflects the current hierarchy and visible labels

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
or Click. Mouse coordinates SHALL be twips.

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

Enter SHALL toggle the selected node before KeyDown. Home and Down SHALL select with NodeClick;
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
