# Issue #2921 validation (2026-11-03, WSL/Linux)

Baseline: upstream/main at `00c937ea`. Counts are passed / failed / skipped.

| Project | Before | After | Total, unchanged |
|---|---:|---:|---:|
| HexIDE.Runtime.Tests | 8121 / 0 / 13 | 8121 / 0 / 13 | 8134 |
| HexIDE.Tests | 5578 / 2 / 35 | 5580 / 0 / 35 | 5615 |
| HexIDE.Integration.Tests | 4800 / 0 / 2 | 4800 / 0 / 2 | 4802 |
| HexIDE.VbLspServer.Tests | 622 / 0 / 0 | 622 / 0 / 0 | 622 |

The two baseline IDE failures were both
`BundledServerBuildTests.DependencyFollowsReferencedOutputForBuildAndPublish` cases
(`artifactsLayout: True` and `False`). Their child builds failed with
`CS2012: Read-only file system`. The final run disabled shared compilation and
MSBuild node reuse for child builds via `UseSharedCompilation=false` and
`MSBUILDDISABLENODEREUSE=1`; no repository build configuration changed.

Full suites used `scripts/test.ps1` and its CI floors, with `--no-build --no-restore`
after separate single-worker builds. Windows PATH entries were removed.
`HexIDE.Tests` used `HEXIDE_FOREIGN_LSP_DOWNLOAD=0`; no foreign-server skips were
recorded. Clone-local `XDG_RUNTIME_DIR` and NuGet HTTP cache paths avoided
read-only sandbox locations. Integration retained its ten-minute HangDump timeout.

The three IDE-side suites have zero xUnit report errors. Their discovered case
names (including multiplicity) and skipped-case names match before and after.
LSP counts come from the completed runner summaries.

## Regression evidence

- The unseeded full baseline passed all 38 `FormatSelectionTests` cases.
- With only the deterministic startup setup added to `TestApp`, the original
  product code passed 29 and failed all nine predicate cases at the existing
  `requeried.Should().BeTrue()` assertion; no skips or xUnit errors.
- After the final product fix, 20 fresh-process class runs each passed all 38:
  760 passes, zero failures and skips. The full IDE run also passed all 38.
- Only those nine predicate cases are claimed as fail-before evidence. Their
  selection predicates, property notifications, forced collections and removal
  assertion remain unchanged.

A standalone lifecycle probe also observed: old operation `Pending`, dispatcher
replaced, no requery delivered by pumping the new dispatcher, old operation still
`Pending`. The fix coalesces only against an operation on the current dispatcher,
preserving the thread-local manager and its listeners.

This reproduces the reported symptom. The original Windows run's preceding
thread history was not captured, so its exact trigger remains unconfirmed.

## Other checks and scope

Debug IDE/test and integration builds: zero warnings and errors.
Tree hygiene: passed, with nine existing advisory warnings; identity scan was
not configured (`HEXIDE_IDENTITY_PATTERN` unset).
`MISSING_LANGUAGE.md` counts and feature statuses did not change. No VB6 values
were pinned, no oracle page was changed, and no member was left unimplemented.
No changelog fragment was added under the latest supervisor instruction: the
change addresses headless dispatcher resets, with no changed normal
single-dispatcher IDE behavior.

A supervisor's live smoke check can select two designer controls, confirm Format
alignment/sizing commands are enabled, then reduce to one and confirm they
disable. No live Windows check is claimed here.

## Skipped tests

These are not coverage for this run. Runtime skips need VB6 templates, the VB6
oracle, or Windows filesystem behavior. IDE skips need Windows/native facilities
or unavailable VB6 fixtures. Integration skips need Windows GDI or the measured
Windows font. The lists were identical before and after.

### HexIDE.Runtime.Tests

- `HexIDE.Runtime.Tests.ClassHeaderPreservationTests.A_real_vb6_class_round_trips_byte_for_byte`
- `HexIDE.Runtime.Tests.ClassHeaderPreservationTests.A_renamed_module_retargets_VB_Name_but_keeps_everything_else`
- `HexIDE.Runtime.Tests.ClassHeaderPreservationTests.Instancing_and_data_binding_settings_survive`
- `HexIDE.Runtime.Tests.ContainerHierarchySaveTests.RoundTrip_OfVb6sOwnNestedTemplates_ReproducesTheBeginStructure`
- `HexIDE.Runtime.Tests.FileSystemTests.CurDir_uses_only_the_first_drive_character`
- `HexIDE.Runtime.Tests.FileSystemTests.Windows_attribute_rows`
- `HexIDE.Runtime.Tests.FileSystemTests.Windows_error_rows`
- `HexIDE.Runtime.Tests.FileSystemTests.Windows_name_rows`
- `HexIDE.Runtime.Tests.FileSystemTests.Windows_numeric_rows`
- `HexIDE.Runtime.Tests.MenuHierarchySaveTests.RoundTrip_OfVb6sOwnMenuTemplates_ReproducesTheMenuShape`
- `HexIDE.Runtime.Tests.MenuHierarchySaveTests.Vb6sOwnMenuTemplates_AreNoLongerHeldReadOnly`
- `HexIDE.Runtime.Tests.Vb6OracleRoundTripTests.HexIDE_output_still_builds_in_vb6`
- `HexIDE.Runtime.Tests.Vb6TextFileTests.The_one_corpus_file_with_high_bytes_round_trips`

### HexIDE.Tests

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

### HexIDE.Integration.Tests

- `HexIDE.Integration.Tests.Views.ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements`
- `HexIDE.Integration.Tests.Views.ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent`
