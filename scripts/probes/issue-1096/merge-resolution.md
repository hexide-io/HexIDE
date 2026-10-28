# Animation merge into main, 2026-10-28

Scope: [the latest #1096 supervisor decision](https://github.com/hexide-io/HexIDE/issues/1096).
Part C remains deferred. The merge target is `88756040`.

All 19 conflicts and their resolutions:

| File | Resolution |
| --- | --- |
| `.github/workflows/build.yml` | Main's higher floors: runtime 6446, IDE 4288, integration 2850. |
| `IDE/HexIDE.Integration.Tests/Views/UpDownRuntimeTests.cs` | Main's superset, including zero increment, acceleration and Caption/Tag buddy tests. |
| `IDE/HexIDE.Runtime/AvaloniaInterop/AvaloniaInteroperability.cs` | Keep Animation tab registration and main's new registrations. |
| `IDE/HexIDE.Runtime/AvaloniaInterop/AvaloniaMethodsInteroperability.cs` | Keep Animation method dispatch and main's Timer/TabStrip handling. |
| `IDE/HexIDE.Runtime/AvaloniaInterop/TabOrder.cs` | Combine Animation with main's UserControl and SSTab support. |
| `IDE/HexIDE.Runtime/BuiltinControls/AttachedEvents.cs` | Keep both independent options: TreeView's deferred double-click and Animation's suppressed second MouseUp. |
| `IDE/HexIDE.Runtime/BuiltinControls/VBUpDown.cs` | Main's measured follow-up behavior replaces the older edge-case refusals. |
| `IDE/HexIDE.Runtime/Components/InBox/InBoxComponentClass.cs` | Main's superset of toolbox components; Animation remains runtime-only. |
| `IDE/HexIDE.Runtime/Components/InBox/RuntimeLibrary.cs` | Main's superset of libraries; one MSComCtl2 entry already includes UpDown, Animation and FlatScrollBar. |
| `IDE/HexIDE.Runtime/Interpreter/ObjectAsValue.cs` | Keep Animation's missing-default-member refusal and main's other default members. |
| `IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs` | Keep main's TabStrip SelectedItem assignment handling. |
| `IDE/HexIDE.Runtime/Interpreter/VB6BuiltIns.Conversion.cs` | Keep main's target-aware numeric conversion signature and call. |
| `IDE/HexIDE.Runtime/VBLoader.cs` | Keep Animation creation and stacking with main's loaders and windowless ordering; drop CommonDialogBlock and its exemption; retain generic underscore-line skipping. |
| `IDE/HexIDE.Tests/ViewModels/ComponentsViewModelTests.cs` | Main's superset of library rows and assertions. |
| `changelog.d/1093.md` | Main's superset, including measured UpDown follow-up. |
| `corpus/inbox-controls/README.md` | Main's reorganized catalogue, including pending Animation fixtures; no files moved for Part C. |
| `docs/MISSING_FEATURES.md` | Main's expanded catalogue plus Animation's runtime/input/drawing status. |
| `docs/MISSING_LANGUAGE.md` | Keep Animation rows and main's newer UpDown and other runtime rows. |
| `docs/vb6-fidelity-oracle/in-box-controls-the-updown.md` | Main's measurement record verbatim; no measurements authored here. |

The small follow-up adds Animation to `FormDeserializer.RunsWhenCarried`, with
case-insensitive matching and the same nested/control-array restrictions as UpDown.
Warning tests cover both allowed spellings and both refused shapes.

Merge commit: `d0ddfc51`. The carried-Animation follow-up remains uncommitted for
supervisor review. Two new warning cases (canonical and lowercase type names) failed
before the allowlist change; the two nested/array boundary cases already passed.
The focused baseline was 8 passed, 2 failed. The MissingLanguageDocTests guard passed
2/2 without changing inventory counts. Desktop Debug built with zero warnings and
zero errors. No new VB6 measurements were made and no oracle page was edited beyond
taking main's merge version.

Live review should open a project with a top-level Animation, confirm there is no
unsupported-control warning and that its designer placeholder is view-only, then
run its existing playback/input/overlap checks. Also check UpDown and Animation
coexist under the single MSComCtl2 reference and that CommonDialog still opens its
dialogs through main's component path. Part C (editable file integration and moving
the Animation corpus fixtures) remains deferred by the supervisor's latest comment.

Initial full Integration run: 3,927 passed, 2 skipped, 3,929 total (floor 2,850).
The skips were `ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements`
and `ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent`;
neither is counted as Linux coverage. A second run enables the xunit XML report
for the initialization-error guard from main (#2486).

Runtime full suite: 7,419 passed, 13 skipped, 7,432 total (floor 6,446); no failures.
Skipped runtime tests (no coverage claimed):

- `HexIDE.Runtime.Tests.Vb6OracleRoundTripTests.HexIDE_output_still_builds_in_vb6`
- `HexIDE.Runtime.Tests.FileSystemTests.CurDir_uses_only_the_first_drive_character`
- `HexIDE.Runtime.Tests.FileSystemTests.Windows_error_rows`
- `HexIDE.Runtime.Tests.FileSystemTests.Windows_numeric_rows`
- `HexIDE.Runtime.Tests.FileSystemTests.Windows_attribute_rows`
- `HexIDE.Runtime.Tests.FileSystemTests.Windows_name_rows`
- `HexIDE.Runtime.Tests.MenuHierarchySaveTests.Vb6sOwnMenuTemplates_AreNoLongerHeldReadOnly`
- `HexIDE.Runtime.Tests.MenuHierarchySaveTests.RoundTrip_OfVb6sOwnMenuTemplates_ReproducesTheMenuShape`
- `HexIDE.Runtime.Tests.Vb6TextFileTests.The_one_corpus_file_with_high_bytes_round_trips`
- `HexIDE.Runtime.Tests.ClassHeaderPreservationTests.Instancing_and_data_binding_settings_survive`
- `HexIDE.Runtime.Tests.ClassHeaderPreservationTests.A_real_vb6_class_round_trips_byte_for_byte`
- `HexIDE.Runtime.Tests.ClassHeaderPreservationTests.A_renamed_module_retargets_VB_Name_but_keeps_everything_else`
- `HexIDE.Runtime.Tests.ContainerHierarchySaveTests.RoundTrip_OfVb6sOwnNestedTemplates_ReproducesTheBeginStructure`

Report-enabled full Integration run: again 3,927 passed, 2 skipped, 3,929 total.
`check-xunit-report.sh` passed: 3,929 tests run, zero initialization errors.
All 54 cases in the Animation runtime/input/drawing classes passed; none skipped.

Full IDE suite (`HEXIDE_FOREIGN_LSP_DOWNLOAD=0`): 4,687 passed, 28 skipped,
4,715 total (floor 4,288), zero failures. Its XML initialization-error guard passed.
The focused warning test passed all 10 cases after the change; the two supported
Animation cases are the two that failed before it. The other two added cases pin
unchanged nested/control-array refusal boundaries and already passed before.

Skipped IDE tests (Windows-only or unavailable VB6 template fixtures; no coverage claimed):

- `HexIDE.Tests.Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess`
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
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does`
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text`
- `HexIDE.Tests.LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches`
- `HexIDE.Tests.IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header`
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_closed_job_refuses_a_process`
- `HexIDE.Tests.Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it`
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it`
- `HexIDE.Tests.LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath`
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile`
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole`
- `HexIDE.Tests.UserDataPathTests.OnWindowsItIsUnderAppData`
- `HexIDE.Tests.Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn`

All three suite totals exceed main's CI floors. No inventory count changes were
needed. No new localization strings or VB6 measurements were introduced.
