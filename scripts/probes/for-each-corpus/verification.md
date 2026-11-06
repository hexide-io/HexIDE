# Validation for #3213

The final Runtime MTP run passed 8,153 tests, skipped 13, and failed none (8,166 total).
It used the full project, a minimum-test floor, long-running notices and xUnit XML reporting.
`check-xunit-report.sh` confirmed no framework errors. LSP passed 657 with no skips;
grammar parity passed 2; MissingLanguage guards passed 18. The 172-file raw-parser
comparison and the 29 observed regression failures are described in [README.md](README.md).
MISSING_LANGUAGE statuses and counts did not change.

## Runtime skips

These checks do not provide coverage in this run. Seven require the absent VB6 template
installation, five require Windows filesystem behaviour, and one requires the opt-in VB6 oracle.
No #3213 test skipped.

- `Vb6TextFileTests.The_one_corpus_file_with_high_bytes_round_trips`
- `ClassHeaderPreservationTests.Instancing_and_data_binding_settings_survive`
- `ClassHeaderPreservationTests.A_real_vb6_class_round_trips_byte_for_byte`
- `ClassHeaderPreservationTests.A_renamed_module_retargets_VB_Name_but_keeps_everything_else`
- `Vb6OracleRoundTripTests.HexIDE_output_still_builds_in_vb6`
- `FileSystemTests.CurDir_uses_only_the_first_drive_character`
- `FileSystemTests.Windows_error_rows`
- `FileSystemTests.Windows_numeric_rows`
- `FileSystemTests.Windows_attribute_rows`
- `FileSystemTests.Windows_name_rows`
- `MenuHierarchySaveTests.Vb6sOwnMenuTemplates_AreNoLongerHeldReadOnly`
- `MenuHierarchySaveTests.RoundTrip_OfVb6sOwnMenuTemplates_ReproducesTheMenuShape`
- `ContainerHierarchySaveTests.RoundTrip_OfVb6sOwnNestedTemplates_ReproducesTheBeginStructure`

## IDE suite

The full native xUnit run passed 6,456, skipped 35, failed none and reported no
framework errors (6,491 total). It used `-longRunning 30 -xml` to expose progress
through the long corpus/package lanes after an initial quiet `dotnet test` attempt
was stopped. The final Runtime source was independently verified by the full MTP
run above. All six foreign-server shutdown checks ran and passed.

### IDE skips

These existing Windows- or VB6-template-dependent tests provide no coverage here:

- `Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity`
- `Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them`
- `Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce`
- `Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step`
- `Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag`
- `Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact`
- `Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write`
- `Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot`
- `Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation`
- `Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests`
- `Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it`
- `Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it`
- `Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet`
- `Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible`
- `LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile`
- `LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole`
- `Profiles.ProfileContainmentTests.JunctionEscapeIsRefused`
- `Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead`
- `Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical`
- `Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched`
- `Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header`
- `Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused`
- `Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does`
- `Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text`
- `Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn`
- `Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main`
- `Processes.ChildProcessJobTests.A_closed_job_refuses_a_process`
- `Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it`
- `Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it`
- `LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath`
- `LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches`
- `IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves`
- `Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess`
- `Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception`
- `UserDataPathTests.OnWindowsItIsUnderAppData`
