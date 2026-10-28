# LSet and RSet into strings

Provenance: issue #2517's measurements, recorded 2026-10-28 with vb6.exe SP6 on an en-GB
machine, with identical native and p-code output. These are supplied measurements, not a new probe.

Each row below was measured for both statements. Brackets delimit the result; all successful
results are Strings, including the Variant targets. Every successful row has Err 0.

| Target declaration and initial value | Source | LSet result | RSet result | Length | Err |
|---|---|---|---|---|---|
| String, `abcde` | `"xy"` | `[xy   ]` | `[   xy]` | 5 | 0 |
| String, `abc` | `"123456"` | `[123]` | `[123]` | 3 | 0 |
| String, `abc` | `""` | `[   ]` | `[   ]` | 3 | 0 |
| String, empty | `"xy"` | `[]` | `[]` | 0 | 0 |
| String, `abcde` | `12` | `[12   ]` | `[   12]` | 5 | 0 |
| String, `abcde` | `1.5` | `[1.5  ]` | `[  1.5]` | 5 | 0 |
| String, `abcde` | `True` | `[True ]` | `[ True]` | 5 | 0 |
| String, `abcde` | `Null` | `[abcde]` | `[abcde]` | 5 | 94 |
| String, `abcde` | `Empty` | `[     ]` | `[     ]` | 5 | 0 |
| String, `abcde` | `"xyz" & "w"` | `[xyzw ]` | `[ xyzw]` | 5 | 0 |
| String * 5 | `"ab"` | `[ab   ]` | `[   ab]` | 5 | 0 |
| String * 5 | `"1234567"` | `[12345]` | `[12345]` | 5 | 0 |
| String * 5 | `""` | `[     ]` | `[     ]` | 5 | 0 |
| Variant, `"abcde"` | `"x"` | `[x    ]` | `[    x]` | 5 | 0 |
| Variant, `12345` | `"x"` | `[x    ]` | `[    x]` | 5 | 0 |

The width comes from the target's current String form. Both statements truncate from the left,
including RSet. Null refuses the store, leaving the target unchanged. UDT memory copying is outside
these measurements. Other Variant contents and member/indexed targets were not measured here.
