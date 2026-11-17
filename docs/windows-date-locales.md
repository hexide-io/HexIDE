# Windows date data on other hosts

For Gregorian-default locales, programs on Linux and macOS use the date defaults and AM/PM designators in
`IDE/HexIDE.Runtime/Interpreter/Data/windows-date-locales.json` (#3928), through the same mapping
as live Windows NLS data. The table records Windows build 10.0.26100.33650. Locales absent from it,
and locales whose .NET or recorded Windows default calendar is not Gregorian, retain their existing
ICU behaviour (including the runtime's existing Gregorian adaptation).

Regenerate on Windows from the repository root with `pwsh scripts/dump-windows-date-locales.ps1`.
Do not hand-edit the output. A new Windows build may change patterns, names, era text and designators; review
those changes and the tests against the measurements in [#3928](https://github.com/hexide-io/HexIDE/issues/3928).
These are Windows default-data measurements, not new vb6.exe measurements.

On Windows the runtime still reads live NLS, including the user's settings where applicable.
Customised Windows settings do not travel to another host. macOS preferences, short time patterns and
number formats are outside this table's scope. Era text uses #3946's shared mapping, including its
documented divergence from VB6's literal `gg`.

Designators use Windows' `LOCALE_S1159` and `LOCALE_S2359`, including empty strings (#4378),
with the same calendar and user-override rules as dates. The en-GB `AMPM` result is measured in
[the date-fraction oracle](vb6-fidelity-oracle/a-dates-fraction-of-a-second.md) and
[#4378](https://github.com/hexide-io/HexIDE/issues/4378): `12:00:01 PM`.
Other locales follow the documented system-data source, not new VB6 measurements; VB6's output
with empty designators, and CStr/no-mask time output in twelve-hour locales, remain unmeasured.

Windows' `LOCALE_STIMEFORMAT` supplies the long time pattern alongside its designators
([#4463](https://github.com/hexide-io/HexIDE/issues/4463)). A comparison of the 605 table
patterns with .NET on Linux found 162 raw differences, or 98 after the runtime's ordinary-space
normalization. The latter include 32 changes from 12-hour to 24-hour, six in the other direction,
43 padding changes and 17 other separator, literal or designator-position changes. These compare
system data, including locales outside the Gregorian mapping; they are not VB6 measurements.
VB6 itself reading `STIMEFORMAT` remains unmeasured.
