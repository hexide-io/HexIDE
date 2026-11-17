### Fixed

- **A class's default member compared with a String runs, as vb6.exe compares it** (#174, #211). `w > "5"` stopped
  the program. Now a typed variable compares the member's value as its declared type, numerically for a number, and
  through a Variant or an `As Object` variable, or from a member returning a Variant, it compares as a Variant does:
  `42 > "5"` is False, as text.
