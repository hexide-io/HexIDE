# [#3155](https://github.com/hexide-io/HexIDE/issues/3155) scrolling command menus

## Reproduction

`ScrollingCommandMenuTests` opens the real code editor's menu in a 240 DIP host. Headless
popups constrain to the screen rather than their host, so the test caps the presenter at 180 DIP
and asserts that its extent exceeds its viewport. It separately checks opening focus and compares
menu commands' enabled states with their predicates queried directly against the editor. It then
focuses a scroll button and runs Toggle Bookmark, checking the bookmark store.

Two further cases, with hidden and disabled entries before the first enabled item, exercise the same scrolling template with `MenuFlyout` and `ContextMenu`.
They check initial focus, command availability after explicit scroll-button focus, and execution
against a binding on the original owner. Each also opens a scrolling submenu and repeats the
scroll-button focus and execution checks. None is platform-gated.

The generic scrolling `MenuFlyout` and `ContextMenu` cases fail their opening-focus assertion
on both unchanged `00aa7025` and `91a244e5f` (Batch 180). Thus the forced-scroll reproduction
has no good/bad bracket in the proposed `91a244e5f..0b53484fc` range: it already fails at the
range start. No first bad commit in that range can honestly be named from this test. This does
not contradict a live good result with a different available menu height; whether the older live
menu scrolled was explicitly unmeasured in the issue. No height threshold is inferred.

The first editor fixture called the non-focusable `TextEditor.Focus()` rather than
`TextArea.Focus()`. Its initial failures are not counted as command-routing evidence. The final
fixture asserts that the editing surface really has focus, that Cut can execute through the
command manager before opening, and that Call Hierarchy's view-model predicate is true.

On unchanged main `00aa7025`, the final fixture fails **4/4 cases**, with **0 skips**.
The editor command-state case explicitly reports `RepeatButton` as the routed target after
opening, with `CutCommand` disabled; the fixture's pre-opening command assertion passed.
The other three cases fail their opening-focus assertions.

## Cause

`CommandManager.GotFocusEventHandler` excludes `MenuItem` alone. A scrolling menu's
`RepeatButton` is therefore recorded as the routed target, replacing the editor. The button has
no route to the editor's command bindings. Avalonia's `PopupFlyoutBase` chooses the first
control reached by tab navigation; SimpleTheme's menu scroll buttons are tab stops and precede
the items in its template.

The command manager, menu direction style and Avalonia version pins have no diff across
`91a244e5f..0b53484fc`. The menu acquires Quick Fixes in `06fea62d` (Batch 182), then Run code
lens in `b6e655d5` (Batch 183). These are potential height triggers, not changes to command routing.
`git blame -L 139,148 91a244e5f -- IDE/HexIDE/AvaloniaLabs.CommandManager/CommandManager.cs`
traces the `MenuItem`-only exclusion to the repository root commit `94be7628`; this is source
provenance, not a test run on that root commit.

Vendor check: targeted searches of AvaloniaUI/Avalonia issues for MenuFlyout/focus/scroll and
"scroll button"/focus found no matching fix. The above behavior was read in Avalonia 12.0.5's
`src/Avalonia.Controls/Flyouts/PopupFlyoutBase.cs` and
`src/Avalonia.Themes.Simple/Controls/ScrollViewer.xaml`.

## Other menus

Project Explorer's applicable commands bind directly to its view model; Print deliberately uses
`DisabledCommand`. The shared scrolling-menu focus correction applies to it too. Immediate's
`ImmediateToolView.axaml` defines no context menu or context flyout. Neither needs new bindings.
The code editor's Toggle Breakpoint entry likewise deliberately uses `DisabledCommand`; enabling
that stub is outside this routing fix.

## Validation

- Final focused run: 4 passed, 0 skipped. All four fail on unchanged main with the same final fixture.
- Full HexIDE.Tests: 6,048 passed, 35 skipped, 0 failed; the 5,585-test floor passed.
  `check-xunit-report.sh` confirms 6,083 cases and no xUnit errors. Downloads were disabled.
- Full Integration: 5,038 passed, 2 skipped, 0 failed; the 4,708-test floor passed.
  `check-xunit-report.sh` confirms 5,040 cases and no xUnit errors.
- Debug Desktop build: 0 warnings, 0 errors.
- Tree hygiene: passed, with 9 non-blocking warnings in existing files.
- OpenSpec strict validation reports 15 long-requirement-body warnings in untouched requirements;
  none refers to the new requirement. Its requirement is inside `## Requirements`.
- `MISSING_LANGUAGE.md` and its counts do not change: this fix changes menu focus and routing.

Integration skipped (neither is coverage in this Linux run):

- `ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent`: requires Windows GDI.
- `ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements`: requires the oracle's Windows Segoe UI font build.

Unit-suite skips (none is counted as coverage here):

<details>
<summary>All 35 skipped cases and their reported reasons</summary>

- `HexIDE.Tests.Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_closed_job_refuses_a_process`: Job objects exist only on Windows.
- `HexIDE.Tests.Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it`: Job objects exist only on Windows.
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it`: Job objects exist only on Windows.
- `HexIDE.Tests.LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath`: A link to a share is a Windows path.
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionEscapeIsRefused`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.UserDataPathTests.OnWindowsItIsUnderAppData`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole`: Type libraries are read only on Windows.
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile`: Type libraries are read only on Windows.
- `HexIDE.Tests.Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main`: A made program is started on Windows only: elsewhere it needs a desktop session.
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.

</details>

## Live verification for the supervisor

At 150% on Windows, maximize the IDE to the issue's short document layout. Open a module at a
call site by right-click, Shift+F10 and automation expand. Verify that the scrolling menu opens
on the first enabled entry, that applicable commands stay enabled while navigating and scrolling,
and that Alt+A runs Call Hierarchy at the caret. Keep Run code lens and Quick Fixes visible;
verify their existing capability/position predicates still apply. Repeat with Project Explorer's
context menu in a constrained layout, including a selection with hidden archive-only entries.
