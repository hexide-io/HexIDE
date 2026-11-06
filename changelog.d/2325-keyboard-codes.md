### Fixed

- **More TextBox keys reach KeyDown and KeyUp** (#2325). Letters, digits, Space,
  navigation/editing keys, F1–F9/F11/F12 and numeric keypad keys now carry the
  measured VB6 codes through the existing event queue and KeyPreview path.
