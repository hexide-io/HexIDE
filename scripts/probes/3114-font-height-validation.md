# #3114 Windows-check follow-up

The brief records two Windows failures out of nine Integration cases, but does not include
case names or assertion messages. This Linux run cannot identify those two failures conclusively.
No product behavior is changed by this follow-up.

## Local reproduction and correction

Added four height-matrix cases with an 18-DIP inherited font, at 150% scaling: both results
panes in 700-DIP and 380-DIP windows. Against the original fixture, 12 cases passed and one failed:
`ResultsOpeningProvidesSixRowsAndPreservesSufficientHeight(0.111, 1.5, 40, true, 700, 18)`.
The unchanged Locals-proportion assertion expected 0.144 within 0.00095238 and found
0.14160184548888632. All nine original cases passed locally.

`MainView` measures tool-pane floors on every Dock layout update. The hand-built fixture did
not: it first installed those floors when results opened. That changed proportions as part of
the action being asserted. The fixture now measures floors while settling layout, as the real
host does. All 13 cases then passed, without relaxing the original proportion assertions or
changing product code. **One local failing-before case** proves this fixture correction;
the other three added cases are passing coverage, not claimed regression teeth.

The live spec and unreleased changelog clarify that the six-row target uses the current font.
`MISSING_FEATURES.md` status and `MISSING_LANGUAGE.md` rows/counts remain unchanged.

## Validation

- Height matrix: 13 passed, no skips.
- Full Integration suite: 5025 passed, 2 skipped, 5027 total; floor 4772 passed.
  `check-xunit-report.sh` confirms 5027 tests and no errors. The skips are
  `ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent` (Windows GDI)
  and `ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements` (Windows oracle font);
  neither is claimed as coverage.
- Desktop Debug build: zero warnings and zero errors.
- Tree hygiene: passed, with nine non-blocking advisories. `git diff --check`: passed.
- Full unit suite: 5975 passed, 35 skipped, 6010 total; floor 5710 passed.
  `check-xunit-report.sh` confirms 6010 tests and no errors. Duration: 21 minutes.
  Skipped cases are listed below and are not claimed as coverage.

The environment removes Windows PATH entries, sets `HEXIDE_FOREIGN_LSP_DOWNLOAD=0`, adds the
cached JRE to PATH, and redirects NuGet HTTP cache and `XDG_RUNTIME_DIR` into this clone's ignored
`artifacts/` directory. Shared compilation and MSBuild node reuse are disabled. Initial build
attempts failed on read-only cache/mutex locations; interrupted builds and one PowerShell
argument-parsing failure are not counted as test results.

## Remaining verification

The supervisor must rerun the automatic Windows check to confirm the two reported failures.
No visual behavior changed; the previous live checks of pane title strips, short-window floors,
View commands and saved-layout recovery remain the relevant UI checks.

## Skipped unit cases

- `HexIDE.Tests.LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath` — Windows only.
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead` — Windows only.
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionEscapeIsRefused` — Windows only.
- `HexIDE.Tests.UserDataPathTests.OnWindowsItIsUnderAppData` — Windows only.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet` — Windows only.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible` — Windows only.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it` — Windows only.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it` — Windows only.
- `HexIDE.Tests.Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception` — Windows only.
- `HexIDE.Tests.IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves` — Windows only.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write` — external VB6 Template tree absent.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce` — external VB6 Template tree absent.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag` — external VB6 Template tree absent.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact` — external VB6 Template tree absent.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step` — external VB6 Template tree absent.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity` — external VB6 Template tree absent.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them` — external VB6 Template tree absent.
- `HexIDE.Tests.Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused` — Windows only.
- `HexIDE.Tests.Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main` — Windows only.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header` — external VB6 Template tree absent.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical` — external VB6 Template tree absent.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched` — external VB6 Template tree absent.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation` — Windows only.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests` — Windows only.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot` — Windows only.
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does` — Windows only.
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text` — Windows only.
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole` — Windows only.
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile` — Windows only.
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_closed_job_refuses_a_process` — Windows only.
- `HexIDE.Tests.Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it` — Windows only.
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it` — Windows only.
- `HexIDE.Tests.Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess` — Windows only.
- `HexIDE.Tests.Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn` — Windows only.
- `HexIDE.Tests.LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches` — Windows only.
