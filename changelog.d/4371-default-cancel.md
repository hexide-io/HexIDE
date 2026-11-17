### Fixed

- **A form's Default and Cancel CommandButtons now take Enter and Esc** (#4371), before control or KeyPreview events. The properties load, save and work in running programs; assigning either role clears it on the other buttons, while hidden or disabled holders leave the keys to the focused control. A TextBox with no receiver raises the measured 13/27 key events. Its headless regression class matches its filename so the automatic Windows check selects all 20 cases.
