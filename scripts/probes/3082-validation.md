# Editor re-dock validation — #3082

Measured in WSL on 2026-11-04. Changes are left uncommitted for supervisor review.

## Cause and regression teeth

`upstream/main` contains #3078 in batch 175 (`3f5001d2`). The probable cause holds:
`DockFactory.RefreshLayout` replaces the proportional dock's child list to clear cached presenter sizing.
That detaches and reattaches code views; the attach path starts tokens, folds, hints and lenses again.
The regression opens a bottom tool pane and performs that same list replacement in a real Dock host,
asserting actual detach and reattachment before examining frames received by a JSON-RPC server.

With all four modified existing product files restored to HEAD, **all eight new cases fail** at the
no-rediscovery assertion: each observes four requests instead of one. These cover each request kind with
completed answers and with answers still pending. The fix was restored afterward. All eight cases pass
with the fix, without skips, both in isolation and in the complete Integration suite.

The cases also assert retained nonempty renderer answers, didChange containing an edit followed by fresh
analysis, active code-lens refresh, and deferral of refresh while detached until reattachment. Existing
viewport tests cover scrolling. The debugger attachment guard from #3048 is unchanged.

## Final checks

| Check | Passed | Skipped | Failed |
|---|---:|---:|---:|
| HexIDE.Tests | 5758 | 35 | 0 |
| HexIDE.Integration.Tests | 4948 | 2 | 0 |

Both complete suites passed the script's CI test floor and `scripts/check-xunit-report.sh`; their XML
reports contain no errors. The Debug desktop build passed with zero warnings and zero errors.
Tree hygiene passed with nine existing advisory warnings; `git diff --check` passed.

No VB6 runtime behaviour is pinned or changed. `docs/MISSING_LANGUAGE.md` and its counts are unchanged.
The code-lenses row in `docs/MISSING_FEATURES.md`, the LSP client documentation, and the live code-editor
and LSP client specs now describe document-owned answers and edit debounce. Semantic-token and inlay-hint
workspace refresh support remains unadvertised, as before this change.

## Invocation and environment

Windows PATH entries were removed before tests and `HEXIDE_FOREIGN_LSP_DOWNLOAD=0` was set. Cached
foreign servers still ran. `XDG_RUNTIME_DIR` was redirected to a writable temporary directory.
An initial Integration invocation failed before tests because hang-dump support could not create a mutex
in the sandbox's read-only default runtime directory; it provides no coverage. An initial captured IDE
run was interrupted after about sixteen quiet minutes without a summary and also provides no coverage.
The successful IDE retry used live output and a timeout; its duration was 15 minutes 42 seconds.

The final complete-suite commands, from the repository root, were:

```sh
pwsh scripts/test.ps1 IDE/HexIDE.Integration.Tests/ --no-build --no-restore -- --hangdump --hangdump-timeout 10m --hangdump-type Mini --report-xunit --report-xunit-filename integration.xml --results-directory /tmp/3082-results/integration-retry
pwsh scripts/test.ps1 IDE/HexIDE.Tests/ --no-build --no-restore -p:TestingPlatformCaptureOutput=false -- --long-running 30 --xunit-diagnostics on --output Detailed --timeout 20m --report-xunit --report-xunit-filename ide.xml --results-directory /tmp/3082-results/ide-retry
```

The assemblies had been rebuilt with the fix before these runs; no product source changed afterward.
The IDE retry also set `MSBUILDDISABLENODEREUSE=1` and `DOTNET_CLI_USE_MSBUILD_SERVER=0`.

## Live check for the supervisor

With two code windows and a server offering all four features, click a lens to open the first Find
References pane. Confirm that existing lenses, colours, hints and folds remain visible and that the
protocol inspector records no new discovery requests for unchanged documents. Edit afterward and check
for didChange and fresh analysis. Scroll to previously unseen lenses and issue a server code-lens refresh
to confirm those requests still work. Repeat opening a pane while paused in an external debugger to
check that no stale detached callback touches the editor. This run did not drive the live IDE.

## Files changed

- `IDE/HexIDE.Core/Lsp/CodeLensSession.cs`
- `IDE/HexIDE/Controls/CodeLensDisplay.cs`
- `IDE/HexIDE/Forms/ViewModels/CodeEditorAnalysis.cs`
- `IDE/HexIDE/Forms/ViewModels/CodeEditorViewModel.cs`
- `IDE/HexIDE/Forms/Views/CodeEditorView.axaml.cs`
- `IDE/HexIDE.Integration.Tests/Views/EditorRedockTests.cs`
- `docs/MISSING_FEATURES.md`
- `docs/lsp-client.md`
- `openspec/specs/code-editor/spec.md`
- `openspec/specs/lsp-client/spec.md`
- `changelog.d/3082.md`
- `scripts/probes/3082-validation.md`

## Skipped tests

These existing skips provide no coverage. The unit skips require Windows facilities, a Windows desktop,
or the unavailable VB6 Template tree; the Integration skips require Windows font metrics or GDI.

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
