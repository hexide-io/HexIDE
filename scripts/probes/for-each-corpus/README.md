# For Each grammar corpus comparison (#3213)

From the repository root, run before and after editing the grammars:

```sh
dotnet run --project scripts/probes/for-each-corpus -- artifacts/3213-before.txt
# Edit the grammars.
dotnet run --project scripts/probes/for-each-corpus -- artifacts/3213-after.txt
diff -u artifacts/3213-before.txt artifacts/3213-after.txt
```

Each snapshot lists every `.bas`, `.cls`, `.frm` and `.ctl` under `corpus/` and
`TestFiles/`, followed by each parser's raw syntax diagnostics. It deliberately
keeps existing diagnostics rather than assuming every fixture is valid VB6.

Measured in the #3213 WSL run at base `04c3c527`: 172 files, with identical
before/after diagnostics for both parsers after the reference-only collection
rule and bare-Nothing predicate. This is parser compatibility evidence, not a
new VB6 measurement.

Regression evidence in this run: all ten syntax-refusal rows failed against each
original grammar (20 failures). Four direct scalar-declaration rows, one
early-bound member row and the two corrected late-bound array-return rows failed
before the runtime change (7 failures). The two Nothing-receiver member rows then
failed against the first implementation, before its metadata lookup correction.
That is 29 observed failures before their respective fixes: 27 on the original
product code and 2 on the intermediate implementation. Accepted-form and
unchanged-behaviour controls are safeguards, not additional regression teeth.
