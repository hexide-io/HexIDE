# Find References validation — #2907

Measured in WSL on 2026-11-04. Implementation changes are left uncommitted for supervisor review.

## Final checks

| Check | Passed | Skipped | Failed |
|---|---:|---:|---:|
| HexIDE.Tests | 5618 | 35 | 0 |
| HexIDE.Integration.Tests | 4849 | 2 | 0 |

Both complete suites passed the script’s CI test floor and `scripts/check-xunit-report.sh`; neither XML report contained test errors. All ten new regression cases passed in their full suite, with no skips.

The Debug desktop build passed with zero warnings and zero errors. `scripts/check-tree-hygiene.sh` passed with nine existing, non-blocking advisory warnings. `git diff --check` passed.

## Regression failure check

Seven unit cases and three headless input cases failed with the new behavior disabled. New API signatures and view types were retained as compile-time stubs: searches did not request or populate results, supplied locations did not populate or open the pane, and the editor command did nothing. The registry used round one’s original `GatherAsync` implementation, with a forwarding result wrapper, so a failed server faulted the request. The implementation was restored before the successful final runs.

The cases cover grouping/order/deduplication and highlighted text, empty replacement results, mixed results/errors, navigation, replacement of an outstanding search by supplied locations, partial server failure, Immediate-group docking, Shift+F12, Enter and double-click. The five model cases, two registry/dock cases and three input cases also passed in focused final runs.

## Invocation and environment

Windows PATH entries were removed before tests. `HEXIDE_FOREIGN_LSP_DOWNLOAD=0` was set; cached foreign servers still ran, and no server downloads were requested. NuGet’s HTTP cache and `XDG_RUNTIME_DIR` were directed into this clone’s ignored `artifacts/` directory because the sandbox makes their normal locations read-only.

The unit suite’s live diagnostic invocation was:

```sh
pwsh scripts/test.ps1 IDE/HexIDE.Tests/ --no-build -p:TestingPlatformCaptureOutput=false -- --long-running 30 --xunit-diagnostics on --output Detailed --timeout 20m --report-xunit --report-xunit-filename issue-2907-unit.xml
```

The unit assembly had just been rebuilt after updating the shared-format localization guard. The integration invocation was:

```sh
pwsh scripts/test.ps1 IDE/HexIDE.Integration.Tests/ -- --hangdump --hangdump-timeout 10m --report-xunit --report-xunit-filename issue-2907-integration.xml
```

An earlier headless invocation failed before executing tests because hang-dump initialization could not create a named mutex in the default runtime directory. That invocation contributes no coverage; the successful runs used the writable runtime directory.

## Skipped tests

These existing skips contribute no coverage. The unit skips require Windows host facilities, a Windows desktop, or the unavailable VB6 Template tree. The integration skips require the oracle’s Windows font build or Windows GDI. No new test skipped.

### HexIDE.Tests

- `Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it`
- `Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it`
- `Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet`
- `Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible`
- `Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical`
- `Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched`
- `Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header`
- `Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot`
- `Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation`
- `Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests`
- `Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main`
- `Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn`
- `Profiles.ProfileContainmentTests.JunctionEscapeIsRefused`
- `Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead`
- `LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath`
- `Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception`
- `Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused`
- `Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess`
- `LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile`
- `LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole`
- `Processes.ChildProcessJobTests.A_closed_job_refuses_a_process`
- `Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it`
- `Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it`
- `Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does`
- `Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text`
- `LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches`
- `Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity`
- `Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them`
- `Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce`
- `Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step`
- `Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag`
- `Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact`
- `Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write`
- `UserDataPathTests.OnWindowsItIsUnderAppData`
- `IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves`

### HexIDE.Integration.Tests

- `Views.ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements`
- `Views.ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent`

## Supervisor live check

Use the brief’s two-module Greet project against the specified RDCore publish. Confirm the bottom pane shares Immediate’s group and becomes active; four returned locations appear under the two module headings with the correct line text and highlighted ranges; both menus show Shift+F12; Enter and double-click navigate to a Module2 call. Confirm one references request with includeDeclaration true, procedure-local variable scoping, and record the keyword response. A missing declaration is a backend observation to report.

No VB6 runtime behavior was changed or newly measured. MISSING_LANGUAGE counts are unchanged. Actual code-lens command dispatch remains in #2906; ShowLocations is the request-free entry point provided by this change.
