## Data-bound TextBox startup value (#859, #856)

Provenance: the oracle VM probe reported in hexide-io/HexIDE#859, using VB6 6.0 SP6's
saved form and an EXE built with `vb6 /make`, at HexIDE fc9f790. This is a **native-code
measurement only**: neither VB6 F5 nor p-code was measured (#842).

The form had Data1 opening BIBLIO.MDB's Authors, Text1 with `DataSource = "Data1"`,
`DataField = "Author"` and design-time `Text = "Text1"`, and a Timer at Interval 1500.
`Form_Load` read Text1.Text as `Text1`. Data1_Reposition ran once after Load and also
read `Text1`. By the first Timer tick Text1.Text was `Jacobs, Russell`, the Authors
record with Au_ID 1. Reposition also fired on the probe's MoveNext and Refresh.
Both Data controls on the original form displayed navigation arrows; the second,
Data2, opened NWIND.MDB's Customers and had no bound control.

This establishes why retaining the design-time value silently is wrong; HexIDE refuses
data-bound forms rather than implementing binding. No behaviour for a UserControl or
Data Environment source was measured in this probe.
