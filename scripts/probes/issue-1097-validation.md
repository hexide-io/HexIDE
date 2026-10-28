# Issue #1097 validation (2026-10-28, WSL/Linux)

All three full suites completed without failures. No FlatScrollBar test skipped.

| Suite | Passed | Skipped | Total | Current CI floor |
|---|---:|---:|---:|---:|
| HexIDE.Runtime.Tests | 7834 | 13 | 7847 | 6885 |
| HexIDE.Tests | 4707 | 28 | 4735 | 4381 |
| HexIDE.Integration.Tests | 4027 | 2 | 4029 | 3379 |

`HexIDE.Tests` ran with `HEXIDE_FOREIGN_LSP_DOWNLOAD=0`. The installed foreign servers were available; none of its foreign-server tests skipped.

The focused `FlatScrollBarFileTests` run had 2 failures and 11 passes before the product fix, then 13 passes and no skips after it. The 2 failures were `Corpus_bars_are_selectable_designer_components` and `Saved_blocks_round_trip_verbatim_including_after_a_form_edit`. The other cases are compatibility checks, already passing before the fix. The adjusted project-load warning test passed in the full IDE suite; no separate fail-before result is claimed for it.

`MissingLanguageDocTests`: 2 passed, no skips; inventory counts unchanged. Debug desktop build: zero warnings and errors. Tree hygiene: passed with 9 existing advisory warnings. Builds used single-worker MSBuild and a temporary metadata override selecting the installed Java executable after ANTLR automatic Java lookup failed on its named mutex; no repository build configuration changed.

## Skipped tests

These are not coverage for this run. Runtime skips require Windows filesystem behaviour or unavailable VB6 templates/oracle; IDE skips require Windows/native facilities or unavailable VB6 fixture inputs. Integration skips require Windows GDI or the measured Windows font.

### HexIDE.Runtime.Tests

- `HexIDE.Runtime.Tests.Vb6OracleRoundTripTests.HexIDE_output_still_builds_in_vb6`
- `HexIDE.Runtime.Tests.FileSystemTests.CurDir_uses_only_the_first_drive_character`
- `HexIDE.Runtime.Tests.FileSystemTests.Windows_error_rows`
- `HexIDE.Runtime.Tests.FileSystemTests.Windows_numeric_rows`
- `HexIDE.Runtime.Tests.FileSystemTests.Windows_attribute_rows`
- `HexIDE.Runtime.Tests.FileSystemTests.Windows_name_rows`
- `HexIDE.Runtime.Tests.ClassHeaderPreservationTests.Instancing_and_data_binding_settings_survive`
- `HexIDE.Runtime.Tests.ClassHeaderPreservationTests.A_real_vb6_class_round_trips_byte_for_byte`
- `HexIDE.Runtime.Tests.ClassHeaderPreservationTests.A_renamed_module_retargets_VB_Name_but_keeps_everything_else`
- `HexIDE.Runtime.Tests.ContainerHierarchySaveTests.RoundTrip_OfVb6sOwnNestedTemplates_ReproducesTheBeginStructure`
- `HexIDE.Runtime.Tests.Vb6TextFileTests.The_one_corpus_file_with_high_bytes_round_trips`
- `HexIDE.Runtime.Tests.MenuHierarchySaveTests.Vb6sOwnMenuTemplates_AreNoLongerHeldReadOnly`
- `HexIDE.Runtime.Tests.MenuHierarchySaveTests.RoundTrip_OfVb6sOwnMenuTemplates_ReproducesTheMenuShape`

### HexIDE.Tests

- `HexIDE.Tests.IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves`
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_closed_job_refuses_a_process`
- `HexIDE.Tests.Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it`
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header`
- `HexIDE.Tests.Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess`
- `HexIDE.Tests.LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches`
- `HexIDE.Tests.Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main`
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot`
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation`
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write`
- `HexIDE.Tests.Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception`
- `HexIDE.Tests.LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath`
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does`
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text`
- `HexIDE.Tests.UserDataPathTests.OnWindowsItIsUnderAppData`
- `HexIDE.Tests.Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn`
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile`
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole`

### HexIDE.Integration.Tests

- `HexIDE.Integration.Tests.Views.ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements`
- `HexIDE.Integration.Tests.Views.ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent`
