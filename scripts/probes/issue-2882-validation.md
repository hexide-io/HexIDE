# #2882 validation — 2026-11-06, WSL

The tool reads preserved blocks through the same `UnmodelledControl.Read` reader the designer uses.
No VB6 semantics changed or new oracle measurements were made. `MISSING_LANGUAGE.md` counts are unchanged.

## Regression teeth

The focused slice is `FormControlsSummaryTests` plus `CarriedControlsDescriptionTests`.
Against the original `HexIdeTools.cs` and its compiled Desktop assembly, the final tests gave
15 total, 9 failed, 6 passed, zero errors/skips. Eight failures are carried-control listing and
case-insensitive named lookup, on the form or inside a container, with the designer open or closed.
They also check that a missing-name reply includes the carried names. The ninth pins the description.
The six existing summary cases still pass. The original source and assembly were restored only for
that check, with the fixed bytes restored in a `finally` block.

## Build and runner setup

The full Debug Desktop build passed with zero warnings/errors, including project references.
The final full-reference Integration build also passed with zero warnings/errors.
Windows PATH entries were removed before builds/tests. `XDG_RUNTIME_DIR` was a private mode-700
folder under ignored `artifacts/`, because the sandbox's default `/run/user` is read-only and the
hang-dump helper cannot create its mutex there. Builds used `-m:1 -nr:false -p:UseSharedCompilation=false`
and a bounded `DOTNET_PROCESSOR_COUNT`.

`dotnet test` attempts were cancelled while awaiting worker output; final tests run through the built xUnit executables:

```sh
IDE/HexIDE.Tests/bin/Debug/net10.0/HexIDE.Tests -namespace HexIDE.Tests.Infrastructure -xml artifacts/2882-final-infra.xml
IDE/HexIDE.Tests/bin/Debug/net10.0/HexIDE.Tests -trait Category=ToolDescription -xml artifacts/2882-final-descriptions.xml
IDE/HexIDE.Integration.Tests/bin/Debug/net10.0/HexIDE.Integration.Tests -longRunning 600 -xml artifacts/2882-complete-integration.xml
```

An early `BuildProjectReferences=false` shortcut omitted `HexIDE.LspGuard.dll` from the test output,
causing a language-server lifetime test to fail before its server could stay running. A normal
full-reference build supplied the helper. Both `LanguageServerLeakGuardTests` then passed; partial-output
runs are not the final validation.

## Results

- Full Integration: 5,244 total, 5,242 passed, two skipped, zero failures/errors (685.197 seconds).
- Fixed focused slice: 15 passed, zero skips/failures/errors.
- Infrastructure: 615 total, 614 passed, one skipped, zero failures/errors.
- Tool descriptions: 103 passed, zero skips/failures/errors.
- Infrastructure's skip: `WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess`
  (Windows-only); this is not coverage here.

Integration's two skips are `ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements`
and `ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent`; they require
Windows font/GDI measurements and prove nothing in this sandbox. XML report checks passed for
Integration, Infrastructure and tool descriptions. Tree hygiene passed with nine existing warnings.

## Live check for the supervisor

On `corpus/inbox-controls/pending/imagecombo`'s Form1, list the controls and confirm eight carried
ImageCombo rows alongside Form1 and ImageList1. Request `controlName: "ImageCombo1"`, then lowercase it:
each reply should contain its row, `carried: true`, pixel position/size, and the note that HexIDE draws
but does not model it, so it cannot be moved, sized or set. No live MCP check was performed in this run.
