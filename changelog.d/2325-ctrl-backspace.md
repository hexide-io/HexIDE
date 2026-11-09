### Fixed

- **TextBox Ctrl+Backspace reports KeyAscii 127** (#2325), so KeyPress handlers on the
  control or its form can suppress or replace it using the measured VB6 code.
