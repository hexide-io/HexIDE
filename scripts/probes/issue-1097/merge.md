# FlatScrollBar merge, 2026-10-28

Merged upstream/main (74e18e06) into fix/1097-codex (8e17d236), as scoped by
[the supervisor's issue comment](https://github.com/hexide-io/HexIDE/issues/1097).
No new VB6 measurements or control behaviour.

## Conflicts

Paths below are relative to the repository root. Each row records one conflicted file.

| File | Resolution |
|---|---|
| `.github/workflows/build.yml` | Main's higher test floors. |
| `IDE/HexIDE.Integration.Tests/Views/InBoxToolboxTests.cs` | Retain FlatScrollBar toolbox test alongside main's tests. |
| `IDE/HexIDE.Integration.Tests/Views/UpDownRuntimeTests.cs` | Main's measured UpDown follow-up tests. |
| `IDE/HexIDE.Runtime/AvaloniaInterop/AvaloniaMethodsInteroperability.cs` | Both FlatScrollBar refusal handling and main's Timer, TabStrip and TreeView handling. |
| `IDE/HexIDE.Runtime/AvaloniaInterop/TabOrder.cs` | Main's UserControl and SSTab support; FlatScrollBar still excluded. |
| `IDE/HexIDE.Runtime/BuiltinControls/VBUpDown.cs` | Main's measured zero Increment, Acceleration and buddy fixes. |
| `IDE/HexIDE.Runtime/Components/InBox/InBoxComponentClass.cs` | Union of implemented controls. |
| `IDE/HexIDE.Runtime/Components/InBox/RuntimeLibrary.cs` | One MSComCtl2 library with UpDown and FlatScrollBar, retaining FlatScrollBar constants; all main libraries retained. |
| `IDE/HexIDE.Runtime/HexIDE.Runtime.csproj` | Both MSComCtl2 and TabDlg constant resources. |
| `IDE/HexIDE.Runtime/Interpreter/ExpressionExecutor.cs` | Both UpDown and FlatScrollBar identify as MSComCtl2. |
| `IDE/HexIDE.Runtime/Interpreter/ObjectAsValue.cs` | FlatScrollBar default Value alongside main's other default-member handling. |
| `IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs` | Both FlatScrollBar early-bound/default assignment handling and main's UserControl/TabStrip handling. |
| `IDE/HexIDE.Runtime/Interpreter/VB6BuiltIns.Conversion.cs` | Main's target-aware numeric conversion. |
| `IDE/HexIDE.Runtime/Serialization/FormSerializer.cs` | Both FlatScrollBar default enum writes and main's SSTab/carried-property writers. |
| `IDE/HexIDE.Runtime/VBLoader.cs` | FlatScrollBar block loading and runtime warning exemption alongside main's windowless ordering and component-based CommonDialog loading. |
| `IDE/HexIDE.Tests/ViewModels/ComponentsViewModelTests.cs` | Main's expanded library expectations. |
| `changelog.d/1093.md` | Main's measured UpDown follow-up entry. |
| `corpus/inbox-controls/README.md` | Main's reorganized corpus paths and additions, plus FlatScrollBar's saved location and description. |
| `docs/MISSING_FEATURES.md` | Main's expanded feature descriptions plus FlatScrollBar A–C; retain current UpDown follow-up. |
| `docs/MISSING_LANGUAGE.md` | FlatScrollBar rows plus main's current UpDown, CommonDialog and Timer rows. |
| `docs/vb6-fidelity-oracle/in-box-controls-the-updown.md` | Main's copy, including its measured follow-up. |

The separately requested FormDeserializer.RunsWhenCarried exemption is covered by a
Linux-runnable saved-corpus warning test. Live review should open the saved FlatScrollBar
form without an unsupported-control warning, confirm its designer boxes are view-only,
and verify Components offers both UpDown and FlatScrollBar from the same MSComCtl2 reference.

## Verification

| Check | Passed | Skipped | Total | CI floor |
|---|---:|---:|---:|---:|
| HexIDE.Runtime.Tests | 7365 | 13 | 7378 | 6446 |
| HexIDE.Tests (`HEXIDE_FOREIGN_LSP_DOWNLOAD=0`) | 4671 | 28 | 4699 | 4288 |
| HexIDE.Integration.Tests | 3911 | 2 | 3913 | 2850 |

All three full runs passed; none ran below its floor. The IDE and Integration xUnit
reports passed `scripts/check-xunit-report.sh` with no hidden errors.
The saved-FlatScrollBar warning regression failed before the exemption (UnknownControl)
and passed afterwards: one test with teeth, runnable on Linux, no skips.
MissingLanguageDocTests passed 2/2; inventory counts did not move.
The final Debug Desktop build passed with zero warnings and errors. Tree hygiene passed
(with its existing path-example warnings and identity scan unset).

The first build attempt failed in ANTLR Java discovery. Using the already installed JRE
on PATH resolved it without changing the build configuration. A concurrent build emitted
a transient PDB-copy warning; the final isolated Desktop build was clean.

Runtime's 13 skips cover Windows filesystem behaviour, unavailable VB6 templates and the
opt-in real-VB6 round-trip compile lane; they are not claimed as coverage.
The following skips are from this run's IDE and Integration xUnit reports:

### HexIDE.Tests

- `HexIDE.Tests.Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical`: VB6 template tree unavailable.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched`: VB6 template tree unavailable.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header`: VB6 template tree unavailable.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_closed_job_refuses_a_process`: Job objects exist only on Windows.
- `HexIDE.Tests.Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it`: Job objects exist only on Windows.
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it`: Job objects exist only on Windows.
- `HexIDE.Tests.Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main`: A made program is started on Windows only: elsewhere it needs a desktop session.
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile`: Type libraries are read only on Windows.
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole`: Type libraries are read only on Windows.
- `HexIDE.Tests.UserDataPathTests.OnWindowsItIsUnderAppData`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath`: A link to a share is a Windows path.
- `HexIDE.Tests.Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity`: VB6 template tree unavailable.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them`: VB6 template tree unavailable.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce`: VB6 template tree unavailable.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step`: VB6 template tree unavailable.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag`: VB6 template tree unavailable.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact`: VB6 template tree unavailable.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write`: VB6 template tree unavailable.

### HexIDE.Integration.Tests

- `HexIDE.Integration.Tests.Views.ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements`: Requires the oracle's Windows Segoe UI font build
- `HexIDE.Integration.Tests.Views.ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent`: Requires Windows GDI

## Left unchanged

No new measurements or VB6 values were introduced. The existing FlatScrollBar refusals
remain: unmeasured conversions and saved members, native hWnd reads, MouseIcon,
Refresh/AboutBox and hidden _Value writes/nonzero reads. The detailed boundary is in
`docs/MISSING_LANGUAGE.md`. Live IDE verification belongs to the supervisor.
