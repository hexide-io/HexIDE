# Windows date data on other hosts

For Gregorian-default locales, programs on Linux and macOS use the date defaults in
`IDE/HexIDE.Runtime/Interpreter/Data/windows-date-locales.json` (#3928), through the same mapping
as live Windows NLS data. The table records Windows build 10.0.26100.33650. Locales absent from it,
and locales whose .NET or recorded Windows default calendar is not Gregorian, retain their existing
ICU behaviour (including the runtime's existing Gregorian adaptation).

Regenerate on Windows from the repository root with `pwsh scripts/dump-windows-date-locales.ps1`.
Do not hand-edit the output. A new Windows build may change patterns, names and era text; review
those changes and the tests against the measurements in [#3928](https://github.com/hexide-io/HexIDE/issues/3928).
These are Windows default-data measurements, not new vb6.exe measurements.

On Windows the runtime still reads live NLS, including the user's settings where applicable.
Customised Windows settings do not travel to another host. macOS preferences, time patterns and
number formats are outside this table's scope. Era text uses #3946's shared mapping, including its
documented divergence from VB6's literal `gg`.
