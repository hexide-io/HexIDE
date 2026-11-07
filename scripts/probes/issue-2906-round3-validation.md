# Issue #2906 — code-window drawing and clicks

Round 3, Linux (WSL), 2026-11-04. Continues the transport and scheduling slices recorded in
[the previous validation note](issue-2906-validation.md).

The code window now owns a lens session while attached. It reserves visual space with a zero-character,
zero-width element and overlays clickable labels above the member text. The labels use the editor font,
smaller and dimmed. Viewport changes drive the existing resolution scheduler. Detaching disposes the
session and cancels its outstanding work. Server connection changes recreate the session.

Show-references commands publish their supplied locations to the singleton Find References pane. They
send no references request. Other commands use the existing declared-command router. Unknown commands
are refused and logged once per command.

## Regression teeth

- The six `CodeLensTests.LensInCodeWindow` cases fail with the pre-round-3 `HexIDE.dll`: no label exists,
  and the viewport case sees zero resolution requests.
  The cases cover placement, document/selection/line-number invariance, both show-references command
  names, forwarding a server command, and resolving only a short viewport among fifty members, then
  resolving more when scrolled to the end. They run at 150% scaling and click with pointer input.
- `UnknownLensCommandIsNotSentAndIsLoggedOnce` fails with the pre-round-3 `HexIDE.Lsp.dll`: two logs
  instead of one. Neither version sends an undeclared command; only the log count is newly pinned.
- Total: seven failing cases before their changes. Baseline assemblies were saved before rebuilding,
  substituted into the test output for the focused runs, then restored. No source changes were hidden.

## Remaining

Edit-debounce refetching and the localized default-on Options toggle remain for the next round, as the
latest supervisor comment permits. No VB6 runtime behaviour changed; no fidelity measurement was needed,
and MISSING_LANGUAGE's counts did not move.

The supervisor's live check should inspect the Greet/Unused labels and report RDCore's counts, verify
selection and numbered lines, click Greet into the existing pane without a references request, scroll a
long module to observe lazy resolution/cancellation, and check a bundled-server project has no lenses.
The Options and edit-debounce checks remain pending with their implementation.

## Sandbox setup

The first full Integration attempt stopped before any tests: hang-dump support could not create its
named mutex in the read-only runtime directory (`EROFS`). Set `XDG_RUNTIME_DIR` to a private, mode-700
ignored directory under this clone's `artifacts/` for the retry. Hang-dump protection stays enabled.
A subsequent build also hit a read-only reference-file error and an MSBuild invalid-node-id error.
The retry uses `MSBUILDDISABLENODEREUSE=1`, `DOTNET_CLI_USE_MSBUILD_SERVER=0`, `-m:1`, `-nr:false`
and `-p:UseSharedCompilation=false`; it does not stop shared build processes belonging to other clones.
The initial unit attempt was interrupted after repeated stdio-helper PID-evidence timeouts; the full
suite was rerun with the corrected environment and freshly built project references. A quiet retry was
restarted prematurely before enabling the long-running reporter. An earlier full report in this clone
took 1,013.818 seconds for 5,665 tests; the completed run here took about eighteen minutes.

## Completed checks

- Full IDE unit suite: 5,686 passed, 35 skipped, 0 failed (5,721 total), in 17m 59s; xUnit report
  guard records no errors. The skips are 25 Windows-specific cases and 10 unavailable VB6-template cases.
- Full Integration suite: 4,887 passed, 2 skipped, 0 failed (4,889 total); xUnit report guard records no errors.
  Both skips are `ToolbarCaptionTests`: the Windows Segoe UI measurement and the Windows GDI extent.
  They are not coverage for this run. All six new lens cases ran and passed on Linux.
- Debug Desktop build: succeeded with 0 warnings and 0 errors.

## Files

- `IDE/HexIDE/Controls/CodeLensDisplay.cs`: visual spacing, labels, viewport scheduling and lifetime.
- `IDE/HexIDE/Forms/Views/CodeEditorView.axaml` and `.axaml.cs`: styled label canvas and attachment wiring.
- `IDE/HexIDE/Forms/ViewModels/CodeEditorViewModel.cs`: lens session factory and command execution.
- `IDE/HexIDE/Events/ShowReferenceLocationsEvent.cs` and `IDE/HexIDE/MainViewViewModel.cs`: reuse the results pane.
- `IDE/HexIDE.Lsp/LspClientRegistry.cs`: log unknown commands once.
- `IDE/HexIDE.Integration.Tests/Views/CodeLensTests.cs` and
  `IDE/HexIDE.Tests/LspClient/LspClientRegistryTests.cs`: seven regression cases.
- `openspec/specs/lsp-client/spec.md`, `docs/lsp-client.md`, `docs/MISSING_FEATURES.md` and
  `changelog.d/2906.md`: current behaviour and remaining scope.
- This validation note.

## Skipped tests (not coverage)

### IDE unit

- `HexIDE.Tests.LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header`
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation`
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests`
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot`
- `HexIDE.Tests.Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn`
- `HexIDE.Tests.IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves`
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionEscapeIsRefused`
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead`
- `HexIDE.Tests.Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write`
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_closed_job_refuses_a_process`
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it`
- `HexIDE.Tests.Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it`
- `HexIDE.Tests.Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main`
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does`
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text`
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile`
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole`
- `HexIDE.Tests.Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess`
- `HexIDE.Tests.Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception`
- `HexIDE.Tests.LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath`
- `HexIDE.Tests.UserDataPathTests.OnWindowsItIsUnderAppData`

### Integration

- `HexIDE.Integration.Tests.Views.ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements`
- `HexIDE.Integration.Tests.Views.ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent`

