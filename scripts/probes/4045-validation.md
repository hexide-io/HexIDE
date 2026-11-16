# Issue #4045 validation

Checked in WSL on 2026-11-16. This continues the interrupted WIP at `4bdc35408`.
No real RDCore session was driven here; live check pending (hexide-mcp).

## Changes retained and completed

External Run To Cursor uses a source's user breakpoints and conditions plus an unconditional
cursor breakpoint, without adding a gutter marker or changing the saved set. Unverified or moved
targets refuse without continuing. Stop, End and request failure restore the user set, and an
ended session drops the temporary line without a request. Menu and automation use the same runner
route.

**Changed in review (hexide-mcp).** The lane's update reader awaited the restore's round trip before
reading on. Updates is a bounded queue of 256, so a program printing during a slow restore faulted
the session; the `cursor-chatty` fixture row (300 events, restore answered after 1 s) failed on the
lane's reader and passes now, checked with a mutation run. Termination no longer sends
`setBreakpoints` to an adapter that need not answer, so `DapSession` keeps refusing it once ended.
A refused restore is reported with its line and keeps the session instead of disconnecting, and the
restore has a 5 s deadline. The test-floor change was dropped as out of scope, and the gap entry
keeps its name, `closed/2968.md`.
Automation reports the actual top frame, running or ended, and an early stop.

This continuation fixed the WIP's noncompiling cleanup dispatch (explicit `InvokeAsync<Task>`),
made the resolved-document refusal paths explicit to the naming guard, and repaired the headless
menu fixture. That fixture now uses the same root as the view, activates its editor after startup,
unloads its project, and asserts that its language-server sessions were released. Reading
`composition.Root` twice had constructed a second root and reset docking before the command.
Closing the dirty editor alone retained a project-owned document session by design.
Stale refusal paragraphs in the DAP and launch documentation were corrected. A linked-source
comparison regression was also reproduced and fixed in the actual-stop reply.

Changed files relative to the supplied upstream main:

- `IDE/HexIDE.Dap.TestAdapter/Program.cs`
- `IDE/HexIDE.Dap/DapSession.cs`
- `IDE/HexIDE.Desktop/Server/HexIdeTools.cs`
- `IDE/HexIDE.Integration.Tests/Views/ExternalRunToCursorTests.cs`
- `IDE/HexIDE.Tests/Infrastructure/ExternalRunControlTests.cs`
- `IDE/HexIDE/Debugging/ExternalDebugService.Breakpoints.cs`
- `IDE/HexIDE/Debugging/ExternalDebugService.cs`
- `IDE/HexIDE/IDE/ProjectRunnerService.cs`
- `changelog.d/4045.md`
- `docs/MISSING_FEATURES.md`
- `docs/dap-client.md`
- `docs/debug-launch.md`
- `docs/mcp-gaps/open/2968.md` moved to `docs/mcp-gaps/closed/2968.md`
- `openspec/specs/dap-client/spec.md`
- This validation note

## Tests and teeth

Windows entries were removed from PATH before every dotnet test command, as the brief requires.
Commands below run from `IDE/`.

| Check | Result |
|---|---|
| `dotnet test HexIDE.Tests/ -- --filter-class '*ExternalRunControlTests'` | 30 passed, no skips |
| `dotnet test HexIDE.Integration.Tests/` (with xUnit XML output) | 3,639 passed, 4 skipped, total 3,643; XML records no errors |
| `dotnet test HexIDE.Tests/ -- --filter-namespace 'HexIDE.Tests.Infrastructure'` | 886 passed, 2 skipped, total 888 |
| `dotnet test HexIDE.Tests/ -- --filter-trait 'Category=ToolDescription'` | 163 passed, no skips |
| `dotnet test HexIDE.Tests/` final replay | 8,388 passed, 67 skipped, total 8,455; XML records no errors |
| `dotnet build HexIDE.Desktop/HexIDE.Desktop.csproj -c Debug` | 0 warnings, 0 errors |
| `bash scripts/check-tree-hygiene.sh` (repository root) | Passed with 9 pre-existing nonblocking warnings |

**Nine new cases have teeth.** Restoring main's tool refusal and runner route made all seven
new adapter cases fail (0 passed, 7 failed): target arrival, earlier user breakpoint, target already
a conditional user breakpoint, termination, unverified target, moved target, and continue refusal.
Restoring main's runner route also made the corrected menu case fail: it stayed at line 6 instead
of reaching line 19. After restoring the fix the same menu case passed, including session cleanup.
The menu's `IsEnabled` assertion already passed on main's route; no separate teeth are claimed for
that assertion. It is checked as requested.

The ninth case uses a symbolic link to the source directory while the adapter reports its real
working-directory path. Before the source comparison fix it failed: the reply said both "line 19"
and "Stopped before reaching" despite reaching the target (7 other matrix cases passed). After
resolving linked parents for comparison, all 30 run-control tests passed. The source file need not
exist locally to resolve its parent links. Identical paths retain the direct comparison.

The first complete unit run finished without being stopped: 8,386 passed, 1 failed, 67 skipped,
total 8,454 in 34m 27s. Its sole failure was the naming source guard, subsequently corrected;
all 23 naming tests and the complete infrastructure namespace passed afterwards. A second complete
run passed: 8,387 passed, 67 skipped, total 8,454 in 43m 17s, with no XML errors. The source-alias
regression was then added and fixed; infrastructure passed 886/888 (2 skipped) and all 163
description tests passed again. The final full run, including that ninth new case, passed:
8,388 passed, 67 skipped, total 8,455 in 32m 14s, with no XML errors. The final successful
integration run took 15m 37s.
The two test floors were raised to 95% of the measured totals: 8,032 and 3,460.

Earlier integration runs exposed the menu fixture's second-root error and retained project
session. The final full integration run passed both the menu case and the suite's leak guard.
An optional HangDump attempt failed before discovery because its mutex directory was read-only
in this sandbox; it supplies no coverage. The successful full integration run used the requested
command without HangDump and included xUnit XML verification.

`bash scripts/test-linux.selftest.sh` also passed after updating the floors.

Generated `bin/` and `obj/` outputs were removed after verification per the repository workflow.

## Skipped tests

These supply no coverage here. None of the new cases skipped.

### HexIDE.Tests (67)

- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.UserDataPathTests.OnWindowsItIsUnderAppData`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Repack.LinuxRepackerTests.A_linux_build_is_refused_on_windows`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionEscapeIsRefused`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it`: Job objects exist only on Windows.
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_closed_job_refuses_a_process`: Job objects exist only on Windows.
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it`: Job objects exist only on Windows.
- `HexIDE.Tests.Repack.MacRepackerTests.macOS_accepts_the_property_list_signature_and_icon`: macOS-only: asks macOS's own tools about the result.
- `HexIDE.Tests.Repack.MacRepackerTests.A_signature_macOS_rejects_stops_the_run_and_leaves_nothing`: macOS-only: asks macOS's own tools about the result.
- `HexIDE.Tests.Repack.MacRepackerTests.A_mac_build_is_refused_on_windows`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Repack.MacRepackerTests.A_mac_build_is_repacked_as_one`: macOS-only: asks macOS's own tools about the result.
- `HexIDE.Tests.LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath`: A link to a share is a Windows path.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: \"branding/logo.png\")`: Needs a Windows x64 test host, whose launcher stands in for a stock executable.
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: \"revocations-cache.sig\")`: Needs a Windows x64 test host, whose launcher stands in for a stock executable.
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: \"LICENSE\")`: Needs a Windows x64 test host, whose launcher stands in for a stock executable.
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: \"nested/HEXIDE-FIRSTPARTY.PUB\")`: Needs a Windows x64 test host, whose launcher stands in for a stock executable.
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: \"revocations.json\")`: Needs a Windows x64 test host, whose launcher stands in for a stock executable.
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: \"brand.json\")`: Needs a Windows x64 test host, whose launcher stands in for a stock executable.
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: \"acme-ide.exe\")`: Needs a Windows x64 test host, whose launcher stands in for a stock executable.
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: \"HexIDE.exe\")`: Needs a Windows x64 test host, whose launcher stands in for a stock executable.
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: \"revocations-cache.json\")`: Needs a Windows x64 test host, whose launcher stands in for a stock executable.
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: \"hexide-root.pub\")`: Needs a Windows x64 test host, whose launcher stands in for a stock executable.
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: \"revocations.sig\")`: Needs a Windows x64 test host, whose launcher stands in for a stock executable.
- `HexIDE.Tests.Repack.WindowsRepackerTests.The_installer_inputs_are_written_beside_the_build_and_not_inside_it`: Needs a Windows x64 test host, whose launcher stands in for a stock executable.
- `HexIDE.Tests.Repack.WindowsRepackerTests.A_default_profile_folder_travels_with_the_brand`: Needs a Windows x64 test host, whose launcher stands in for a stock executable.
- `HexIDE.Tests.Repack.WindowsRepackerTests.A_repacked_executable_says_it_is_the_brand_when_Windows_reads_it`: Needs a Windows x64 test host, whose launcher stands in for a stock executable.
- `HexIDE.Tests.Repack.WindowsRepackerTests.The_brands_files_land_where_the_IDE_will_look_for_them`: Needs a Windows x64 test host, whose launcher stands in for a stock executable.
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_links_are_refused_at_the_root_or_inside`: Needs a Windows x64 test host, whose launcher stands in for a stock executable.
- `HexIDE.Tests.Repack.WindowsRepackerTests.A_branded_process_declares_that_identity_to_Windows`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Repack.WindowsRepackerTests.Every_file_but_the_executable_is_exactly_as_published`: Needs a Windows x64 test host, whose launcher stands in for a stock executable.
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_files_reach_the_installable_payload_and_report`: Needs a Windows x64 test host, whose launcher stands in for a stock executable.
- `HexIDE.Tests.Repack.WindowsRepackerTests.A_signed_stock_executable_comes_out_unsigned_and_the_run_says_so`: Needs a Windows x64 test host, whose launcher stands in for a stock executable.
- `HexIDE.Tests.Repack.WindowsRepackerTests.A_run_that_fails_partway_leaves_nothing_behind`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Repack.WindowsRepackerTests.The_icon_is_the_brands_with_nothing_left_of_stocks`: Needs a Windows x64 test host, whose launcher stands in for a stock executable.
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_folder_below_a_link_is_allowed`: Needs a Windows x64 test host, whose launcher stands in for a stock executable.
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_missing_folder_is_refused`: Needs a Windows x64 test host, whose launcher stands in for a stock executable.
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_output_ancestor_is_refused_before_staging`: Needs a Windows x64 test host, whose launcher stands in for a stock executable.
- `HexIDE.Tests.Repack.WindowsRepackerTests.A_brand_with_no_copyright_keeps_stocks_rather_than_claiming_none`: Needs a Windows x64 test host, whose launcher stands in for a stock executable.
- `HexIDE.Tests.Projects.ContributedProjectTests.A_contributed_project_already_open_is_refused_when_opened_again_with_different_drive_and_folder_case`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Projects.DuplicateProjectOpenTests.Opening_the_same_project_with_different_drive_and_folder_case_is_refused`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Infrastructure.ProjectWriteSafetyTests.A_copy_that_cannot_be_removed_is_kept_and_named`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main`: A made program is started on Windows only: elsewhere it needs a desktop session.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile`: Type libraries are read only on Windows.
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole`: Type libraries are read only on Windows.
- `HexIDE.Tests.LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.

### HexIDE.Integration.Tests (4)

- `HexIDE.Integration.Tests.Views.ContextMenuMeasureTests.CodeMenuFitsItsShortcutsAndSubmenuArrowsOnFirstOpening`: Needs Avalonia 12.1.5; pinned back to 12.1.4 for #3869
- `HexIDE.Integration.Tests.Automation.UiAutomationDriverTests.Interact_Collapse_LeafNode_RefusesClearly`: Needs Avalonia 12.1.5; pinned back to 12.1.4 for #3869
- `HexIDE.Integration.Tests.Automation.UiAutomationDriverTests.Inspect_TreeNode_ReportsExpandCollapseState`: Needs Avalonia 12.1.5; pinned back to 12.1.4 for #3869
- `HexIDE.Integration.Tests.Automation.UiAutomationDriverTests.Interact_Expand_LeafNode_RefusesClearly`: Needs Avalonia 12.1.5; pinned back to 12.1.4 for #3869

## Left for supervision

- Live-check the Debug menu's IsEnabled and action, and `run_to_cursor`, against RDCore in a
  stopped session with a saved source. Check target arrival, a user breakpoint or exception first,
  conditions unchanged in the adapter and gutter, refusal without continue, and termination cleanup.
- Tick #3364's issue line after review. No GitHub commands or external writes were made here.
- No VB6 measurement was needed or added. No language member was left unimplemented;
  `MISSING_LANGUAGE` counts did not move. The interpreter route is unchanged.

## Possible drift

#4045's Expected point 3 and supervisor's cleanup-on-next-stop instruction discard the external
cursor target at **any** stop. The interpreter retains its target after a user breakpoint and clears
it only when hit (`IDE/HexIDE.Runtime/Debugging/DebugController.cs`, `runToHit`), also stated in the
`run_to_cursor` description. This gives peer engines different behavior for the same command,
which may head away from CLAUDE.md's peer-engine goal. The requested external rule was implemented;
review whether that difference should be intentional. This is a comparison with current code,
not a claim about unmeasured VB6 behavior.
