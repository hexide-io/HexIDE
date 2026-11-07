## Tab and the arrow keys among option buttons (2026-10-23, #1583)

**The question.** "GotFocus and LostFocus on each control" found that VB6's Tab key skips an option button that isn't
selected. What does it do in a group with no option selected, and does focus by Tab select there, as `SetFocus` does?
What do Shift+Tab and the arrow keys do? And is an option already selected when its GotFocus runs, or only after?

**The probe.** Two compiled forms, native and p-code, run on the oracle guest's console session through the agent, as
"SetFocus" was. Each form held, in TabIndex order:
- Text1;
- Option1, Option2 and Option3 on the form, Option2 saved `Value = -1 'True`;
- Text2;
- Frame1, holding Option4 and Option5, neither selected;
- Text3.

Every option's GotFocus and Click logged its name and every option's Value, as a digit for each one selected: `-2- 4-`
is Option2 and Option4 selected. Every TextBox's GotFocus logged its name. The Timer took one step each 300 ms, by
`SetFocus` or by `SendKeys`, and logged `Screen.ActiveControl` and the Values after each.
- **The first form:** SetFocus Text1, Tab seven times, Shift+Tab seven times, then SetFocus Text1, Tab, and Down, Down,
  Down, Up, Right, Left on what had the focus, then Tab and Shift+Tab.
- **The second form:** SetFocus Text3, Shift+Tab three times, Up twice, SetFocus Option5, Up twice, SetFocus Option3,
  Right, SetFocus Option1, Left, SetFocus Option2, then Space.

Native and p-code logged the same lines, line for line: 74 for the first form and 54 for the second.

**The Tab key:**

| From | Key | Focus went to | Logged |
|---|---|---|---|
| Text1 | Tab | Option2, the selected one | `Option2_GotFocus -2- --` |
| Option2 | Tab | Text2 | `Text2_GotFocus` |
| Text2 | Tab | Option4, the first in a group with none selected | `Option4_GotFocus -2- 4-`, `Option4_Click -2- 4-` |
| Option4 | Tab | Text3 | `Text3_GotFocus` |
| Text3 | Shift+Tab | Option4, now its group's selected one | `Option4_GotFocus -2- 4-` |
| Text3, with none selected in Frame1 | Shift+Tab | Option5, the last in the group | `Option5_GotFocus -2- -5`, `Option5_Click -2- -5` |

- **An option group is one Tab stop.** With an option selected, Tab and Shift+Tab both go to it and to nothing else in
  the group. The Tab key then selects nothing and raises no Click.
- **A group with none selected takes the first option in the key's direction:** the lowest TabIndex for Tab, the highest
  for Shift+Tab. It is selected there, and its Click follows its GotFocus, as under `SetFocus`. From then on the group is
  one stop at that option.
- **A group is the options in one container.** Option1 to Option3 on the form and Option4 and Option5 in the Frame were
  two groups throughout, each with its own selected option.

**Selected before its GotFocus runs.** Inside `Option4_GotFocus`, Option4 already read True and Option5 False. The same
held under `SetFocus`: `Option3.SetFocus` logged `Option3_GotFocus --3 -5`, then `Option3_Click --3 -5`. "GotFocus and
LostFocus on each control" logged Click after GotFocus and read Value only after both. HexIDE's code took that order to
mean the option was selected after its GotFocus. It is selected first, and only the Click waits.

**SetFocus on the selected option** raised its GotFocus and no Click: `Option5_GotFocus 1-- -5`. **Space on the
selected option** raised nothing.

**The arrow keys:**

| From | Key | Focus went to | Logged |
|---|---|---|---|
| Option2, selected | Down | Option3 | `Option3_GotFocus --3 4-`, `Option3_Click --3 4-` |
| Option3, selected | Down | Text2 | `Text2_GotFocus`; Option3 stays selected |
| Option2, selected | Up | Option1 | `Option1_GotFocus 1-- -5`, `Option1_Click 1-- -5` |
| Option1, selected | Up | Text1 | `Text1_GotFocus` |
| Option5, in Frame1 | Up | Option4 | `Option4_GotFocus 1-- 4-`, `Option4_Click 1-- 4-` |
| Option4, in Frame1 | Up | Option5: it wraps inside the Frame | `Option5_GotFocus 1-- -5`, `Option5_Click 1-- -5` |
| Option3, selected | Right | Text2, as Down | `Text2_GotFocus` |
| Option1, selected | Left | Text1, as Up | `Text1_GotFocus` |
| Text2 | Down, Up, Right, Left | nowhere: the TextBox keeps them | nothing |

**An arrow moves to the previous or next control in the option's own container, by TabIndex, wrapping within it.** That
control need not be an option button. On the form the TextBoxes either side were the next and previous controls. In
the Frame, whose only controls are the two options, it went round between them. An option button it lands on is
selected, with GotFocus and then Click.

**Not measured:**
- a group whose selected option is disabled, hidden or has `TabStop = False`;
- `TabStop = False` on an option that isn't selected, in a group with none selected;
- the arrow keys on a CheckBox or a CommandButton, which Windows' dialog manager also moves between;
- an arrow onto a control that is disabled or has `TabStop = False`.

The last two, and the order an arrow follows, are measured in "Where the arrow keys go from a button" (#1625).

### In HexIDE (#1583)

- **An option group is one Tab stop.** It is its selected option, or each of its options while none is selected, so
  Tab and Shift+Tab reach the first in their direction. A group is the options in one container, which is how HexIDE
  already made them exclusive. An option's own `TabStop = False` still keeps the Tab key off it.
- **Focus by the Tab key selects an option, as `SetFocus` does,** with Click after GotFocus. Until now the Tab key was
  kept from selecting, because it stopped at every option and selecting would have changed the user's choice.
- **An option is selected before its GotFocus runs.** Before, a GotFocus raised at once read its own option unselected.
  The Click is raised after the GotFocus, whether the two run at once or wait behind a running handler.
- **The arrow keys** did nothing on an option button. They follow VB6 since #1625: "Where the arrow keys go from a
  button" has the rule.

