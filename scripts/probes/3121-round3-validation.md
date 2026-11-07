# #3121 round 3 validation

The latest issue comment replaces shared keys with no marker when a label has no free letter.
Call Hierarchy keeps its assigned keys. VB6's own mnemonics were not measured; this is HexIDE's
menu-order policy, not a claim of VB6 mnemonic fidelity.

## Regression proof

With the updated guard and the original packs, `NavigationAccessKeyTests` reported **79 failed,
263 passed, 0 skipped (342 total)**: 64 menu-policy cases and 15 Call Hierarchy cases failed.
After the pack changes, all **342 passed**, with no skips. `ShippedPackParityTests` passed all
18 cases and `LocalizationCoverageTests` passed all 5 cases, with no skips.

The menu guard reads the actual AXAML sibling order, including disabled items, submenu headers and
submenu contents. It checks all Latin-script neutrals and the four shipped regional overrides after
inheritance. No duplicate key is permitted; a missing key requires every letter to be taken.
The changelog fragment records the 189 unmarked entries and their counts in each pack.

Three tool-title strings now have separate menu keys, translated into every shipped neutral pack.
This gives menu entries markers without adding underscores to tool tabs. The no-fix placeholder
also has a separate menu key so its message remains plain. All 15 Quick Fix integration cases pass
in isolation, including the unchanged assertion on the plain no-edit message. Marker-count parity checks
for these menus defer to the stronger menu guard; checks elsewhere retain English parity.

## Completed checks

- Full IDE: **6234 passed, 35 skipped, 0 failed (6269 total)**, in 16m 06s.
  The xUnit report guard confirms all 6269 tests and no runner errors.
- Full Integration: **5206 passed, 2 skipped, 0 failed (5208 total)**, in 6m 10s.
  The xUnit report guard confirms all 5208 tests and no runner errors.
- Debug Desktop build: **0 warnings, 0 errors**.
- Design-record checks: **6 passed, 0 skipped** after clarifying the Call Hierarchy exception.
- Tree hygiene: passed with nine existing non-blocking warnings; `git diff --check` passed.
- CI floors rise to 5955 for IDE and 4947 for Integration, 95% of their measured totals.

Windows PATH entries were removed before tests. `HEXIDE_FOREIGN_LSP_DOWNLOAD=0` was set; no foreign
server test skipped. NuGet's writable package and HTTP caches were placed inside ignored `artifacts/`,
with the existing home package cache used as a read-only fallback. No package references changed.
A rebuild using `BuildProjectReferences=false` left `HexIDE.LspGuard.dll` absent from the test output;
that attempt was stopped. A normal build restored it, all three bundled-server trace round-trip tests
passed in isolation, and the completed full IDE run above used normal project-reference builds.
An earlier attempt containing two obsolete marker-parity assertions was also stopped and is not a
completed full run. Those assertions now defer to the menu guard.
A rebuild hit CS2012 from the shared compiler's read-only sandbox. The completed final builds and
suites set `UseSharedCompilation=false`, `MSBUILDDISABLENODEREUSE=1` and
`DOTNET_CLI_USE_MSBUILD_SERVER=0` in the process environment; no shared build server was stopped.
The first Integration host stopped before tests because HangDump's named mutex tried to create its
runtime directory under read-only `/run/user/1000`. The retry sets `XDG_RUNTIME_DIR` to a private
mode-700 directory inside this clone's ignored `artifacts/`, retaining hang-dump protection.
An initial completed Integration run found three Quick Fix failures: two menu assertions needed the
new markers, while the no-edit message correctly required plain text. The separate no-fix menu key
preserves that plain message; its existing message assertion is unchanged.


## Skips — not coverage

The 35 IDE skips are 25 Windows-specific cases and 10 absent VB6-template cases:

- `HexIDE.Tests.Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception`
- `HexIDE.Tests.IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves`
- `HexIDE.Tests.Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess`
- `HexIDE.Tests.LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches`
- `HexIDE.Tests.LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath`
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile`
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole`
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_closed_job_refuses_a_process`
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it`
- `HexIDE.Tests.Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it`
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionEscapeIsRefused`
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead`
- `HexIDE.Tests.Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity`
- `HexIDE.Tests.Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main`
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text`
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it`
- `HexIDE.Tests.Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn`
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation`
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot`
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests`
- `HexIDE.Tests.UserDataPathTests.OnWindowsItIsUnderAppData`

Integration skips are Windows-only Segoe UI/GDI caption measurements, not coverage:

- `HexIDE.Integration.Tests.Views.ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements`
- `HexIDE.Integration.Tests.Views.ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent`

## Supervisor live check

Inspect View and the code window's context menu in English and the affected Latin-script packs,
including regional overrides. Confirm unique underlined letters, no underline on entries whose
letters are all taken (English Format Document and Format Selection are examples), and unchanged
Call Hierarchy keys. Count disabled items when checking collisions; enable Call Hierarchy with a
capable backend to check activation. Inspect the Toolbars, Bookmarks and Quick Fixes submenus too.
Confirm tool tab titles have no mnemonic underscores.

No VB6 runtime behaviour changed. `docs/MISSING_LANGUAGE.md` and its counts are unchanged.

## Files changed

- `.github/workflows/build.yml`
- `IDE/HexIDE.Integration.Tests/Views/QuickFixTests.cs`
- `IDE/HexIDE.Tests/LocalizationServiceTests.cs`
- `IDE/HexIDE.Tests/NavigationAccessKeyTests.cs`
- `IDE/HexIDE.Tests/ShippedPackParityTests.cs`
- `IDE/HexIDE/Forms/ViewModels/CodeEditorViewModel.cs`
- `IDE/HexIDE/Forms/Views/CodeEditorView.axaml`
- `IDE/HexIDE/Forms/Views/CodeEditorView.axaml.cs`
- `IDE/HexIDE/Localization/Packs/ar.json`
- `IDE/HexIDE/Localization/Packs/bg.json`
- `IDE/HexIDE/Localization/Packs/ca.json`
- `IDE/HexIDE/Localization/Packs/cs.json`
- `IDE/HexIDE/Localization/Packs/da.json`
- `IDE/HexIDE/Localization/Packs/de.json`
- `IDE/HexIDE/Localization/Packs/el.json`
- `IDE/HexIDE/Localization/Packs/en-GB.json`
- `IDE/HexIDE/Localization/Packs/en.json`
- `IDE/HexIDE/Localization/Packs/es.json`
- `IDE/HexIDE/Localization/Packs/et.json`
- `IDE/HexIDE/Localization/Packs/eu.json`
- `IDE/HexIDE/Localization/Packs/fi.json`
- `IDE/HexIDE/Localization/Packs/fr.json`
- `IDE/HexIDE/Localization/Packs/gl.json`
- `IDE/HexIDE/Localization/Packs/he.json`
- `IDE/HexIDE/Localization/Packs/hr.json`
- `IDE/HexIDE/Localization/Packs/hu.json`
- `IDE/HexIDE/Localization/Packs/id.json`
- `IDE/HexIDE/Localization/Packs/it.json`
- `IDE/HexIDE/Localization/Packs/ja.json`
- `IDE/HexIDE/Localization/Packs/ko.json`
- `IDE/HexIDE/Localization/Packs/lt.json`
- `IDE/HexIDE/Localization/Packs/lv.json`
- `IDE/HexIDE/Localization/Packs/nb.json`
- `IDE/HexIDE/Localization/Packs/nl.json`
- `IDE/HexIDE/Localization/Packs/pl.json`
- `IDE/HexIDE/Localization/Packs/pt-BR.json`
- `IDE/HexIDE/Localization/Packs/pt.json`
- `IDE/HexIDE/Localization/Packs/ro.json`
- `IDE/HexIDE/Localization/Packs/ru.json`
- `IDE/HexIDE/Localization/Packs/sk.json`
- `IDE/HexIDE/Localization/Packs/sl.json`
- `IDE/HexIDE/Localization/Packs/sr-Latn.json`
- `IDE/HexIDE/Localization/Packs/sv.json`
- `IDE/HexIDE/Localization/Packs/th.json`
- `IDE/HexIDE/Localization/Packs/tr.json`
- `IDE/HexIDE/Localization/Packs/uk.json`
- `IDE/HexIDE/Localization/Packs/vi.json`
- `IDE/HexIDE/Localization/Packs/zh-Hans.json`
- `IDE/HexIDE/Localization/Packs/zh-Hant.json`
- `IDE/HexIDE/MainView.axaml`
- `changelog.d/3121.md`
- `docs/MISSING_FEATURES.md`
- `openspec/specs/language-packs/spec.md`
- `scripts/probes/3121-round3-validation.md`
