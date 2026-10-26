# Format's AMPM token (#2040)

Provenance: the final measurement comment supplied with hexide-io/HexIDE#2040
(2026-10-26 task brief), reporting vb6.exe SP6, identical native and p-code results.
`Format$` and `Format` agreed. The comment references PR #2125; its subject file was
not present in this checkout. This records the supplied results, rather than a new probe.

`AMPM` accepts any letter case (tested `AMPM`, `ampm`, `AmPm`, `aMpM`), preserves
the user's designator spelling, and selects a twelve-hour clock even with empty designators.

| Time | Mask | AM / PM settings | Result |
|---|---|---|---|
| 00:00:01 | hh:nn:ss AMPM | AM / PM | 12:00:01 AM |
| 09:05:03 | hh:nn:ss AMPM | AM / PM | 09:05:03 AM |
| 09:05:03 | h AMPM | AM / PM | 9 AM |
| 12:00:01 | hh:nn:ss AMPM | AM / PM | 12:00:01 PM |
| 13:05:03 | h:nn:ss AMPM | AM / PM | 1:05:03 PM |
| 13:05:03 | hh AMPM | AM / PM | 01 PM |
| 13:05:03 | ampm | AM / PM | PM |
| 23:59:59 | h:nn AMPM | AM / PM | 11:59 PM |
| 09:05:03 | hh:nn:ss AMPM | xAM / yPM | 09:05:03 xAM |
| 09:05:03 | h AMPM | xAM / yPM | 9 xAM |
| 13:05:03 | ampm | xAM / yPM | yPM |
| 09:05:03 | hh:nn:ss AMPM | empty / empty | `09:05:03 ` |
| 09:05:03 | h AMPM | empty / empty | `9 ` |
| 13:05:03 | ampm | empty / empty | empty |

At 13:05:03, `h AM/PM` returned `1 PM` with all three settings above.
The slash tokens `AM/PM`, `am/pm`, `A/P`, `a/p` use fixed text, independent of settings.
Changing the thread locale to af-ZA (1078, vm./nm.) still gave `AM` with the user's
AM/PM settings. Only `AMPM` reads the user's designators.
