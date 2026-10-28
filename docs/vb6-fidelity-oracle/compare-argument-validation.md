# A compare argument, and when it is checked

Provenance: hexide-int's measurement comment on hexide-io/HexIDE#881,
reported from vb6.exe SP6, native and p-code agreeing, and referenced there as
PR #2279, commit 4324912b. This records the supplied measurements, not a new probe.

A compare of 0 is binary and 1 text. InStr and StrComp reject 2 with error 5;
InStrRev, Split and Replace use text for 2. All five use text for 3, 4 and 1033;
3000 and -1 raise 5 where validation occurs. Whether accepted locale identifiers
select their own locale or simply text comparison remains unmeasured.

InStrRev, Split and Replace validate on every path. The measured empty paths
include `InStrRev("", "x", -1, c)`, `Split("", ",", -1, c)`,
`Split("abc", ",", 0, c)`, `Replace("", "a", "b", 1, -1, c)` and
Replace with count 0, using c = 2, -1 and 3000.

InStr skips validation for Null string1 (returns Null), empty string1 (0),
and empty string2 (start, including start 5 against "abc"). It validates on
other paths, including start past the end and a find longer than string1.
StrComp returns Null for a Null string for every measured compare. Otherwise
it rejects 2 even for two empty strings; -1 and 3000 are checked only when
both strings are non-empty. `StrComp("", "a", -1)` returns -1.

Compare conversion rounds to even: 0.4 and -0.4 select binary, 0.6 and the
String "1" select text. 1.5 and 2.5 read as 2 (error 5 in InStr and StrComp,
text in Split). A Null compare raises 94.
