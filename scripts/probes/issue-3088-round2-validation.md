# #3088 round 2 validation — 2026-11-04

## Cause and implementation

Merged `upstream/main` as `0850f818` before changing the round 1 service.
The round 1 code captured normal bounds in move/resize handlers. Delivering a move and a resize while
Normal, followed by Maximized, saved `Maximized -11,-11 1280×649` instead of the preceding normal bounds.
The regression explicitly observes a resize while Normal before delivering the state change.

The other regression observed the client size at the maximize request: it was still 800×600 even though
restoration had set Width/Height to 860×640. This confirms the missing layout/resize acknowledgement in
the headless model; the Windows live measurements are in
[the issue](https://github.com/hexide-io/HexIDE/issues/3088) and
[the supervising live check](https://github.com/hexide-io/HexIDE/pull/3098#issuecomment-6638897990).

Read against Avalonia 12.0.5's own source:

- [Window.ArrangeSetBounds](https://github.com/AvaloniaUI/Avalonia/blob/12.0.5/src/Avalonia.Controls/Window.cs)
  sends the layout size to the platform.
- [Win32 WindowImpl.Resize](https://github.com/AvaloniaUI/Avalonia/blob/12.0.5/src/Windows/Avalonia.Win32/WindowImpl.cs)
  updates `WINDOWPLACEMENT.NormalPosition` through `SetWindowPlacement`.
- [HeadlessWindowImpl.Resize](https://github.com/AvaloniaUI/Avalonia/blob/12.0.5/src/Headless/Avalonia.Headless/HeadlessWindowImpl.cs)
  queues its resize acknowledgement at Send priority to model X11. `UpdateLayout()` alone therefore
  still left the headless client size at 800×600 when maximizing.

The fix samples normal bounds at the debounced save rather than in individual geometry events.
Restoration lays out the normal size and posts maximization at Background priority, allowing queued resize
acknowledgements first. Starting tracking preserves the bounds seeded by restoration. This is the
cross-platform path; no native handle or Windows-only placement reader was added.

## Teeth

The two added tests failed against the original product code before implementation (5 passed, 2 failed).
After refining the event-order test to await headless's resize acknowledgement, the final fixture was
also run against the merged base service, compiled under a temporary type name in Integration:
**5 passed, 2 failed, 0 skipped**. The failures were the saved maximized rectangle and the 800×600 client
size at maximize, respectively. The temporary service copy was removed before the final suites.
With the fix, **all 7 persistence tests passed, 0 skipped**, at 150% scaling in both new cases.

## Live check still required

On Windows at 150%, start with normal client bounds 860×640 at (0,0). Maximize through the caption button
(`SC_MAXIMIZE`), wait past 250 ms, and inspect settings: Maximized must carry the normal bounds. Force-stop
and relaunch: it must open maximized, with a native restore rectangle corresponding to those saved normal
client bounds. Un-maximize and confirm client size 860×640 and the saved position. Also retain round 1's
move/resize/force-stop and disconnected-monitor checks. Headless does not implement a native restore
rectangle, so its assertions are not a claim that this Windows live check passed.

No VB6 runtime behavior changed or new VB6 measurements were used. This run does not change
`MISSING_LANGUAGE.md` or its counts.

## Checks

- Full IDE: **5,758 passed, 35 skipped, 0 failed**, 5,793 total; floor 5,378 enforced.
  `scripts/check-xunit-report.sh` confirmed all 5,793 tests and no runner errors.

- Full Integration: **4,947 passed, 2 skipped, 0 failed**, 4,949 total; floor 4,599 enforced.
  `scripts/check-xunit-report.sh` confirmed all 4,949 tests and no runner errors. All seven persistence
  tests passed in this full run.
- Debug desktop build: **0 warnings, 0 errors**.
- Tree hygiene: passed with nine existing nonblocking warnings; the identity scan was unavailable
  because `HEXIDE_IDENTITY_PATTERN` was unset.
- `git diff --check`: passed.

Windows PATH entries were removed before .NET runs. `HEXIDE_FOREIGN_LSP_DOWNLOAD=0` disabled downloads;
cached foreign servers could still run. The first Integration host crashed before tests because the
hang-dump extension could not create its mutex in read-only `/run/user/1000`. The completed Integration
run used a private mode-700 `XDG_RUNTIME_DIR` inside ignored artifacts, retaining the hang-dump timeout.
Quiet full-suite attempts were interrupted and are not counted. Both completed suites exposed
progress with `-p:TestingPlatformCaptureOutput=false -- --long-running 60` and wrote an xUnit XML report.

## Skips — not coverage

The two Integration skips were Windows-only font/GDI measurements:

- `HexIDE.Integration.Tests.Views.ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements`
- `HexIDE.Integration.Tests.Views.ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent`

The 35 IDE skips were environmental Windows-only or absent VB6-template cases:

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
