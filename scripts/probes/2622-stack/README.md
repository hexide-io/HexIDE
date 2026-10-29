# Pointer identity stack regression (#2622)

Run from the repository root:

```sh
dotnet run --project scripts/probes/2622-stack/StackProbe.csproj
```

The probe runs a recursive Function on a new thread with a 1 MB stack. It prints the
size of `Vb6Value`, the last argument reached and the trapped error. Depth is a host
measurement, not a VB6 expectation. No compiler or native address is involved.

On Linux x64 with .NET 10.0.13 (Debug, 2026-10-28), this standalone probe printed:

| Version | Value bytes | Depth reached | Error |
|---|---:|---:|---:|
| Before | 40 | 24 | 28 |
| After | 32 | 25 | 28 |

The same body, temporarily run through `StackSpaceTests` with .NET
10.0.13 (Debug, 2026-10-28), reached depth 25 with the branch's 40-byte value and
26 with the 32-byte payload representation. Main (`64a5989c`) reached 26 with its
32-byte value too. Forcing the existing `Down(30)` test onto a 1 MB thread failed
on main as well as the branch on Linux; its normal-stack test passed. The supervisor
must rerun that unchanged test on Windows, where the regression was reported.

The permanent regression checks the value's size against main's three references
and two flags, including alignment. The existing finite-recursion floor stays 30.

## Validation (2026-10-28, Linux x64)

| Suite | Passed | Skipped | Failed / errors |
|---|---:|---:|---:|
| Runtime | 7981 | 13 | 0 / 0 |
| IDE | 4807 | 32 | 0 / 0 |
| Integration | 4074 | 2 | 0 / 0 |
| LSP | 598 | 0 | 0 / 0 |

`MissingLanguageDocTests`: 2 passed. The new size regression failed against the
pre-fix 40-byte value and passes after the fix (one test with teeth). No new test
skipped. The existing finite-recursion test passes on Linux; Windows is still for
the supervisor to verify. The integration harness needed `XDG_RUNTIME_DIR` pointed
inside the clone because its hang-dump mutex could not write the sandbox runtime
directory.

### Skipped tests

These provide no coverage in this run. Skips require Windows, Windows fonts/GDI,
VB6 templates, or the opt-in VB6 round-trip oracle.

**Runtime**

- `ClassHeaderPreservationTests.A_real_vb6_class_round_trips_byte_for_byte`
- `ClassHeaderPreservationTests.A_renamed_module_retargets_VB_Name_but_keeps_everything_else`
- `ClassHeaderPreservationTests.Instancing_and_data_binding_settings_survive`
- `ContainerHierarchySaveTests.RoundTrip_OfVb6sOwnNestedTemplates_ReproducesTheBeginStructure`
- `FileSystemTests.CurDir_uses_only_the_first_drive_character`
- `FileSystemTests.Windows_attribute_rows`
- `FileSystemTests.Windows_error_rows`
- `FileSystemTests.Windows_name_rows`
- `FileSystemTests.Windows_numeric_rows`
- `MenuHierarchySaveTests.RoundTrip_OfVb6sOwnMenuTemplates_ReproducesTheMenuShape`
- `MenuHierarchySaveTests.Vb6sOwnMenuTemplates_AreNoLongerHeldReadOnly`
- `Vb6OracleRoundTripTests.HexIDE_output_still_builds_in_vb6`
- `Vb6TextFileTests.The_one_corpus_file_with_high_bytes_round_trips`

**IDE**

- `Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception`
- `IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves`
- `Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess`
- `LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches`
- `LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath`
- `LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile`
- `LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole`
- `Processes.ChildProcessJobTests.A_closed_job_refuses_a_process`
- `Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it`
- `Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it`
- `Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header`
- `Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched`
- `Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical`
- `Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag`
- `Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write`
- `Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them`
- `Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact`
- `Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step`
- `Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce`
- `Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity`
- `Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main`
- `Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text`
- `Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does`
- `Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible`
- `Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it`
- `Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet`
- `Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it`
- `Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn`
- `Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation`
- `Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot`
- `Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests`
- `UserDataPathTests.OnWindowsItIsUnderAppData`

**Integration**

- `Views.ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements`
- `Views.ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent`
