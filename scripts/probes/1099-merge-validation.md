# Issue 1099: second merge validation, 2026-10-28

The latest supervisor brief requests only a merge of upstream/main, with no new behaviour.
Merge commit: 6c9b3956; incoming main: fca767a7; previous branch head: 95dbfe37.

## Conflicts

- .github/workflows/build.yml: retain the greater floor from each side: runtime 6885,
  IDE 4427, integration 3379. Main supplied runtime/integration; the branch supplied IDE.
- IDE/HexIDE.Runtime/Components/InBox/RuntimeLibrary.cs: retain main's MSMask entry and
  the branch's MSFlexGridLib entry in ControlLibraries.All, alongside every other library.
- corpus/inbox-controls/README.md: retain both saved/msflexgrid and saved/maskedbox rows,
  plus main's MaskedEdBox provenance and limitation notes.
- docs/MISSING_FEATURES.md: use main's current common-controls row, insert the branch's
  complete MSFlexGrid status, and exclude both implemented controls from the refusal list.

Automatically merged shared dispatch, serialization, event and execution-root code was reviewed
against main: its changes remain alongside MSFlexGrid's existing bindings. No uncertain resolution
or new VB6 measurement was needed. Existing MSFlexGrid refusals remain unchanged.

## Validation

The initial dotnet test attempts emitted no completed totals and were interrupted; they are not
passes. Integration's first hang-dump attempt failed before testing because its mutex directory
under /run/user was read-only. Moving XDG_RUNTIME_DIR inside the clone avoided that startup error.
Runtime/IDE do not install the hang-dump extension and rejected those diagnostic switches.

The completed full-suite results below use the already-built native xUnit v3 executable with
-noColor -verbose -longRunning 60 -xml. HEXIDE_FOREIGN_LSP_DOWNLOAD=0 was set for HexIDE.Tests.
No filters were applied. XML reports were checked with scripts/check-xunit-report.sh; totals
are compared manually with the retained CI floors. Skipped cases are not coverage.

| Suite | Total | Passed | Skipped | Failed/errors | Floor |
|---|---:|---:|---:|---:|---:|
| Runtime | 7847 | 7834 | 13 | 0/0 | 6885 |
| IDE | 5050 | 5022 | 28 | 0/0 | 4427 |
| Integration | 4109 | 4107 | 2 | 0/0 | 3379 |

- MissingLanguageDocTests: 2 passed, 0 skipped, using dotnet test and its class filter.
- Debug desktop build: 0 warnings, 0 errors.
- Tree hygiene: passed, 9 non-blocking warnings; identity scan unset.
- No new tests or behaviour: new before/after teeth count is zero (not applicable to this merge).
- MISSING_LANGUAGE's inventory changes come from main: 1184 to 1185 names;
  Dies 96 to 89 and Faithful 469 to 477. No guard-requested count correction was needed.
- Live supervisor smoke check: Components retains MSFlexGrid and main's libraries, and an existing
  MSFlexGrid form still opens, runs, draws, accepts measured keyboard input and saves its carried block.

## Skipped tests

### Ide

- HexIDE.Tests.Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- HexIDE.Tests.Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- HexIDE.Tests.Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- HexIDE.Tests.UserDataPathTests.OnWindowsItIsUnderAppData: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- HexIDE.Tests.Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- HexIDE.Tests.Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main: A made program is started on Windows only: elsewhere it needs a desktop session.
- HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- HexIDE.Tests.Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- HexIDE.Tests.Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- HexIDE.Tests.Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- HexIDE.Tests.LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- HexIDE.Tests.Processes.ChildProcessJobTests.A_closed_job_refuses_a_process: Job objects exist only on Windows.
- HexIDE.Tests.Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it: Job objects exist only on Windows.
- HexIDE.Tests.Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it: Job objects exist only on Windows.
- HexIDE.Tests.IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole: Type libraries are read only on Windows.
- HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile: Type libraries are read only on Windows.
- HexIDE.Tests.LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath: A link to a share is a Windows path.
- HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- HexIDE.Tests.Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- HexIDE.Tests.Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- HexIDE.Tests.Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn: Windows-only: this pins behaviour the product deliberately varies by host filesystem.

### Integration

- HexIDE.Integration.Tests.Views.ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent: Requires Windows GDI
- HexIDE.Integration.Tests.Views.ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements: Requires the oracle's Windows Segoe UI font build

### Runtime

- HexIDE.Runtime.Tests.ContainerHierarchySaveTests.RoundTrip_OfVb6sOwnNestedTemplates_ReproducesTheBeginStructure: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- HexIDE.Runtime.Tests.ClassHeaderPreservationTests.A_renamed_module_retargets_VB_Name_but_keeps_everything_else: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- HexIDE.Runtime.Tests.ClassHeaderPreservationTests.A_real_vb6_class_round_trips_byte_for_byte: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- HexIDE.Runtime.Tests.ClassHeaderPreservationTests.Instancing_and_data_binding_settings_survive: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- HexIDE.Runtime.Tests.MenuHierarchySaveTests.RoundTrip_OfVb6sOwnMenuTemplates_ReproducesTheMenuShape: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- HexIDE.Runtime.Tests.MenuHierarchySaveTests.Vb6sOwnMenuTemplates_AreNoLongerHeldReadOnly: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- HexIDE.Runtime.Tests.Vb6OracleRoundTripTests.HexIDE_output_still_builds_in_vb6: Set HEXIDE_ORACLE=1 to compile round-tripped forms with VB6.
- HexIDE.Runtime.Tests.FileSystemTests.CurDir_uses_only_the_first_drive_character: Windows filesystem attributes required.
- HexIDE.Runtime.Tests.FileSystemTests.Windows_name_rows: Windows filesystem attributes required.
- HexIDE.Runtime.Tests.FileSystemTests.Windows_error_rows: Windows filesystem attributes required.
- HexIDE.Runtime.Tests.FileSystemTests.Windows_attribute_rows: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- HexIDE.Runtime.Tests.FileSystemTests.Windows_numeric_rows: Windows filesystem attributes required.
- HexIDE.Runtime.Tests.Vb6TextFileTests.The_one_corpus_file_with_high_bytes_round_trips: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
