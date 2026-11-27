### Fixed

- **A form whose TabIndex is written in hex, such as `TabIndex = &H3`, keeps its tab order** (part of #4988). The
  reader refused the value, so the control's siblings were renumbered and a save wrote a different TabIndex into one of
  them. It now reads the value as VB6 does, and a save writes it in decimal.
- **An unquoted Tag in a .frm, such as `Tag = abc`, is refused, as VB6 refuses it** (part of #4988). It was read as
  text and saved back quoted. Now it's logged as an invalid value, the Tag keeps its default, and a save writes the
  line back as it was, as an unquoted Caption already is.
