# Stdio process log identity test verification (#2347)

Verified on Linux/WSL, 2026-10-29.

The original fixture's shell writes stderr and exits immediately. The transport starts the process,
assigns its Windows job, then starts the stderr reader. An exit before job assignment can take the
launch-failure path before stderr reading starts. The test did not assert connection success, so
that path could instead appear as a missing Debug log after two 30-second polling windows.
This is a candidate explanation for the Windows CI failure, not a reproduction of that incident.
The replacement fixture cannot exit until the test sends an exit frame after connection setup.
A ping response and an alive assertion check its lifetime before release.

## Checks

- Teeth: temporarily restored only the original shell command selection while retaining the strengthened
  assertions. Debug, Information and Warning all failed with a null ping reply; Error passed unchanged.
  These three failures prove the new fixture-lifetime assertions, not the original Windows CI failure.
- Corrected class: 20 consecutive full-class runs, 80 passed, zero failed, zero skipped, using
  `dotnet test HexIDE.Tests/ --no-build --no-restore -- --filter-class '*StdioProcessLoggingTests'`
  from `IDE/`, with `HEXIDE_FOREIGN_LSP_DOWNLOAD=0`.
- Full suite: `cd IDE && HEXIDE_FOREIGN_LSP_DOWNLOAD=0 dotnet test HexIDE.Tests/`:
  4,902 passed, 32 skipped, zero failed (4,934 total).
- Desktop Debug build: `cd IDE && dotnet build HexIDE.Desktop/HexIDE.Desktop.csproj -c Debug`: zero warnings and errors.
- `bash scripts/check-tree-hygiene.sh`: passed with 10 nonblocking warnings.
- No VB6 semantics changed; MISSING_LANGUAGE counts are unchanged.

## Full-suite skips

These skipped tests supply no coverage for this change. In particular, the skipped Windows job-object
checks do not validate the candidate explanation above; Windows confirmation remains outstanding.

- `Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception`
- `Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn`
- `Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it`
- `Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet`
- `Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it`
- `Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible`
- `Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical`
- `Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header`
- `Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched`
- `Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text`
- `Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does`
- `LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile`
- `LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole`
- `Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag`
- `Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact`
- `Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write`
- `Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce`
- `Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity`
- `Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them`
- `Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step`
- `Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess`
- `LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches`
- `IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves`
- `UserDataPathTests.OnWindowsItIsUnderAppData`
- `Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main`
- `Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests`
- `Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot`
- `Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation`
- `LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath`
- `Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it`
- `Processes.ChildProcessJobTests.A_closed_job_refuses_a_process`
- `Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it`
