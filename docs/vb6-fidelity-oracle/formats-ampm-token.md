## Format's AMPM token (2026-10-26, #2040)

Measured with vb6.exe SP6, `/make`, native and p-code identical on every row. `Format$` and `Format` gave the same
text on every row. The guest's regional settings are en-GB, whose AM and PM designators are `AM` and `PM`.

### What `AMPM` is

- **`AMPM`, in any letter case** (`AMPM`, `ampm`, `AmPm`, `aMpM`), prints the user's own AM or PM designator,
  exactly as the regional settings write it. It does not follow the token's case: `ampm` prints `AM`, not `am`.
- **It makes `h` and `hh` 12-hour**, as `AM/PM` does: 00:00:01 is `12`, 13:05:03 is `1`.
- **With no designators set**, `AMPM` prints nothing, and the hour stays 12-hour.
- **`AM/PM`, `am/pm`, `A/P` and `a/p` are fixed text.** They print the same letters whatever the regional settings
  say. Only `AMPM` reads them.
- **The thread locale plays no part.** A program that called `SetThreadLocale` with 1033, 1031, 1036, 1053, 1078
  and 1043 still printed `AM` and `PM` under every one, although 1078 (af-ZA) designators are `vm.` and `nm.`.
  `Format` reads the user's settings.

### en-GB

| Time | `hh:nn:ss AMPM` | `h:nn:ss AMPM` | `h AMPM` | `hh AMPM` | `AMPM` | `ampm`, `Ampm`, `aMpM` | `h ampm` | `h:nn AMPM` | `h AM/PM` | `hh:nn:ss` |
|---|---|---|---|---|---|---|---|---|---|---|
| 00:00:01 | `12:00:01 AM` | `12:00:01 AM` | `12 AM` | `12 AM` | `AM` | `AM` | `12 AM` | `12:00 AM` | `12 AM` | `00:00:01` |
| 09:05:03 | `09:05:03 AM` | `9:05:03 AM` | `9 AM` | `09 AM` | `AM` | `AM` | `9 AM` | `9:05 AM` | `9 AM` | `09:05:03` |
| 11:59:59 | `11:59:59 AM` | `11:59:59 AM` | `11 AM` | `11 AM` | `AM` | `AM` | `11 AM` | `11:59 AM` | `11 AM` | `11:59:59` |
| 12:00:01 | `12:00:01 PM` | `12:00:01 PM` | `12 PM` | `12 PM` | `PM` | `PM` | `12 PM` | `12:00 PM` | `12 PM` | `12:00:01` |
| 13:05:03 | `01:05:03 PM` | `1:05:03 PM` | `1 PM` | `01 PM` | `PM` | `PM` | `1 PM` | `1:05 PM` | `1 PM` | `13:05:03` |
| 23:59:59 | `11:59:59 PM` | `11:59:59 PM` | `11 PM` | `11 PM` | `PM` | `PM` | `11 PM` | `11:59 PM` | `11 PM` | `23:59:59` |

### Other designators

The user's designators were set with `SetLocaleInfo` (`LOCALE_S1159`, `LOCALE_S2359`) and put back afterwards. The
same probe ran under each setting.

| Format | 09:05:03, `xAM`/`yPM` | 13:05:03, `xAM`/`yPM` | 09:05:03, empty | 13:05:03, empty |
|---|---|---|---|---|
| `hh:nn:ss AMPM` | `09:05:03 xAM` | `01:05:03 yPM` | `09:05:03 ` | `01:05:03 ` |
| `h AMPM` | `9 xAM` | `1 yPM` | `9 ` | `1 ` |
| `ampm` | `xAM` | `yPM` | (empty) | (empty) |
| `AmPm` | `xAM` | `yPM` | (empty) | (empty) |
| `h AM/PM` | `9 AM` | `1 PM` | `9 AM` | `1 PM` |
| `h am/pm` | `9 am` | `1 pm` | `9 am` | `1 pm` |
| `h A/P` | `9 A` | `1 P` | `9 A` | `1 P` |
| `h a/p` | `9 a` | `1 p` | `9 a` | `1 p` |
| `Long Time`, `ttttt` | `09:05:03` | `13:05:03` | `09:05:03` | `13:05:03` |
| `CStr` | `09:05:03` | `13:05:03` | `09:05:03` | `13:05:03` |

`Long Time`, `ttttt` and `CStr` are unchanged because en-GB's long time format, `HH:mm:ss`, has no designator in it.

### What HexIDE did

`FormatDate` knew `AM/PM`, `am/pm`, `A/P` and `a/p` but not `AMPM`. It read the four letters one at a time, so
`hh:nn:ss AMPM` at 12:00:01 printed `12:00:01 A12P12`.
