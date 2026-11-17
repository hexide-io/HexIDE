### Added

- **Keyboard handlers run on buttons, lists and empty forms** (#2325): CommandButton, CheckBox,
  OptionButton, ListBox and ComboBox Styles 0 and 2 offer KeyDown, KeyPress and KeyUp, with form-first
  KeyPreview and writable KeyAscii. Key events surround typing, selection and dropdown changes in the
  measured order; button Space clicks follow KeyUp. Empty forms receive their own keys, including Tab
  without KeyUp. Opening and navigating a ComboBox list raises no spurious GotFocus. Enter/Esc on forms holding controls remain tracked by #4371.
