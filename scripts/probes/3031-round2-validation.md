# #3031 round-two validation

Validated under WSL on 2026-11-04 after merging `upstream/main` into `fix/3031-codex`.
The supervisor's round-two scope is in [issue #3031](https://github.com/hexide-io/HexIDE/issues/3031).

## Regression evidence

Before the product change, `LanguageServerProblemLocalizationTests` ran 35 cases:
6 failed and 29 passed. The failures were singular unknown-field headings in English and Bulgarian,
Bulgarian trace/pipe-role quotation marks, and allowed-transport quotation marks in English and Bulgarian.
The two-field cases already passed; they are preservation checks, not additional regression teeth.
The same class passed all 35 cases after the change.

`LanguageServersScrollTests.LongProblemKeepsItsLastCharacterInsideTheTab` failed all 4 cases before
explicit wrapping was added: English and Bulgarian, each at 100% and 150%. In a 360-DIP tab, the final
character's right edge was about 1,734 DIPs in English and 1,838 in Bulgarian. All 4 cases passed in the
full Integration run after the change, including selecting the complete text and retaining the path.

Total: **10 failing-before cases out of 12 added cases**, all runnable on Linux.

## Completed checks

- Full `HexIDE.Tests`: **5,704 passed, 35 skipped, 0 failed**; 5,739 total, floor 5,378 enforced.
- Full `HexIDE.Integration.Tests`: **4,885 passed, 2 skipped, 0 failed**; 4,887 total, floor 4,599 enforced.
- Both xUnit XML reports passed `scripts/check-xunit-report.sh`, with no runner errors.
- `ShippedPackParityTests`: **18 passed**, no skips.
- `LocalizationCoverageTests`: **4 passed**, no skips.
- Debug Desktop build: **0 warnings, 0 errors**.
- `scripts/check-tree-hygiene.sh`: passed, with 9 nonblocking warnings.
- `git diff --check`: passed.

Windows entries were removed from `PATH` before running .NET commands. Foreign-server downloads were
disabled with `HEXIDE_FOREIGN_LSP_DOWNLOAD=0`; cached external servers still ran. The sandbox makes
`/run/user` read-only, so the completed full runs used `XDG_RUNTIME_DIR` under ignored `artifacts/runtime`.
The first Integration attempt failed before tests because the hang-dump mutex needed a writable runtime
directory. Two earlier silent unit runs were interrupted before completion; their partial results are not
counted. The completed unit run used `-p:TestingPlatformCaptureOutput=false -- --long-running 60`
to expose progress. The completed Integration run retained `--hangdump --hangdump-timeout 10m`.

## Skipped tests

These are environmental exclusions, not coverage of this change. None of the added cases skipped.

### HexIDE.Tests (35)

- `HexIDE.Tests.UserDataPathTests.OnWindowsItIsUnderAppData` — Windows-only dependency or behavior.
- `HexIDE.Tests.Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess` — Windows-only dependency or behavior.
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does` — Windows-only dependency or behavior.
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text` — Windows-only dependency or behavior.
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_closed_job_refuses_a_process` — Windows-only dependency or behavior.
- `HexIDE.Tests.Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it` — Windows-only dependency or behavior.
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it` — Windows-only dependency or behavior.
- `HexIDE.Tests.LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath` — Windows-only dependency or behavior.
- `HexIDE.Tests.Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn` — Windows-only dependency or behavior.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation` — Windows-only dependency or behavior.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests` — Windows-only dependency or behavior.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot` — Windows-only dependency or behavior.
- `HexIDE.Tests.LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches` — Windows-only dependency or behavior.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet` — Windows-only dependency or behavior.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible` — Windows-only dependency or behavior.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it` — Windows-only dependency or behavior.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it` — Windows-only dependency or behavior.
- `HexIDE.Tests.Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception` — Windows-only dependency or behavior.
- `HexIDE.Tests.Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main` — Windows-only dependency or behavior.
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead` — Windows-only dependency or behavior.
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionEscapeIsRefused` — Windows-only dependency or behavior.
- `HexIDE.Tests.Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused` — Windows-only dependency or behavior.
- `HexIDE.Tests.IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves` — Windows-only dependency or behavior.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write` — VB6 template installation unavailable.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce` — VB6 template installation unavailable.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag` — VB6 template installation unavailable.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact` — VB6 template installation unavailable.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step` — VB6 template installation unavailable.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity` — VB6 template installation unavailable.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them` — VB6 template installation unavailable.
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole` — Windows-only dependency or behavior.
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile` — Windows-only dependency or behavior.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header` — VB6 template installation unavailable.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical` — VB6 template installation unavailable.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched` — VB6 template installation unavailable.

### HexIDE.Integration.Tests (2)

- `HexIDE.Integration.Tests.Views.ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent` — Windows-only dependency or behavior.
- `HexIDE.Integration.Tests.Views.ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements` — Windows-only dependency or behavior.

## Supervisor live check

At 150%, open Language Servers with a malformed configuration at a long path. Switch en → bg → en.
Confirm the complete path, JSON pointer and final line number wrap within the tab and remain selectable.
Check one and two unknown fields, then an invalid trace level and pipe role: all quoted values within each
message should use that pack's quotation marks. Confirm logs still use the loader's English messages.

No VB6 behavior was inferred or changed. `MISSING_LANGUAGE.md` counts and feature-support statuses are
unchanged. The active manifest has 39 neutral packs; all were updated, together with the differing
`en-GB` spelling. `CLAUDE.md`'s older pack list was left untouched as instructed.
