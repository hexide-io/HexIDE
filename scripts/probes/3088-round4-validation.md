# #3088 round 4 validation

The two new drawn-frame cases in `WindowStatePersistenceTests` failed before the border correction:
both restored a height of 634 instead of 640. After the correction, all 12 persistence cases passed.
The supplied outer/drawn rectangles exercise the fitting path on Linux; they do not exercise native DWM.

| Check | Passed | Skipped | Failed |
|---|---:|---:|---:|
| HexIDE.Tests | 5799 | 35 | 0 |
| HexIDE.Integration.Tests | 4987 | 2 | 0 |

Both suites passed their CI floors. Integration's xUnit report passed `check-xunit-report.sh`
with 4989 tests and no errors. Debug desktop build: zero warnings and errors. Tree hygiene passed
with nine existing advisory warnings. `git diff --check` passed.

Windows PATH entries were removed and `HEXIDE_FOREIGN_LSP_DOWNLOAD=0` was set. Cached foreign
servers still ran. Fresh output under this clone's artifacts directory avoided read-only existing
build caches. NuGet's HTTP cache was redirected there too. Existing generated parser files were
reused without grammar changes because ANTLR's mutex initialization failed in the sandbox.
Integration's first full invocation failed before tests while creating its hang-dump mutex;
redirecting `XDG_RUNTIME_DIR` into the clone allowed the complete retry to pass.
HexIDE.Tests completed in 15m 55s; a redundant diagnostic retry was stopped after that success.

## Skipped tests

Integration skipped `ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent`
(Windows GDI) and `ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements`
(the oracle's Windows Segoe UI font). HexIDE.Tests skipped the following 35 cases: 25 require Windows
and 10 require the absent VB6 template tree. None supplies coverage for this change.

- `HexIDE.Tests.IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves`
- `HexIDE.Tests.Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess`
- `HexIDE.Tests.LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath`
- `HexIDE.Tests.Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn`
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile`
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole`
- `HexIDE.Tests.UserDataPathTests.OnWindowsItIsUnderAppData`
- `HexIDE.Tests.Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception`
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does`
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text`
- `HexIDE.Tests.Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched`
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_closed_job_refuses_a_process`
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it`
- `HexIDE.Tests.Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible`
- `HexIDE.Tests.LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches`
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionEscapeIsRefused`
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead`
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot`
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests`
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag`
- `HexIDE.Tests.Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused`

## Live check for the supervisor

On Windows at 150%, relaunch with saved normal bounds 860×640 at (0,0), including a maximized
launch followed by restore. Confirm the size and saved settings stay unchanged. Restore an oversized
window and check DWM's drawn bottom meets the working-area bottom, with only invisible borders
allowed beyond it; check the drawn right edge too. Native DWM calls remain unverified in WSL.
No VB6 runtime semantics changed; MISSING_LANGUAGE counts and MISSING_FEATURES statuses are unchanged.
