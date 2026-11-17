# #3520 Linux wire-stress validation

Measured on 2026-11-16 in the task clone, after merging `upstream/main`.
Builds and tests used a PATH without `/mnt/` entries and exported
`UseSharedCompilation=false`. Full suites used `HEXIDE_FOREIGN_LSP_DOWNLOAD=0`.

## Reproduction attempt

Two batches of two processes ran concurrently with full `HexIDE.Tests` suites. Each process used
`HEXIDE_WIRE_STRESS_SECONDS=900`, the default eight pairs, and the filter
`--filter-method '*ConcurrentConnectionsKeepEveryFrameValid'`.

| Process | Load seconds | Pairs | Valid received frames | Bad frames |
|---|---:|---:|---:|---:|
| A | 900 | 8 | 7,456,385 | 0 |
| B | 900 | 8 | 7,432,745 | 0 |
| C (final source) | 900 | 8 | 5,764,444 | 0 |
| D (final source) | 900 | 8 | 5,772,764 | 0 |
| Total | 3,600 | 8 per process | 26,426,338 | 0 |

The reported corruption did **not** reproduce. These are received frames across
both directions; write-side validation does not double the count. The cause remains
unknown and no product fix is claimed.

After the long attempt, pressure-thread cleanup was tightened to wake and join the
thread on early exit, and size arithmetic was made safe for longer runs. The final
four-test set passed with both the default settings (22,586 received frames) and
an explicit two-pair, two-second override (4,382 received frames), zero bad frames
in each. The final-source 900-second runs C and D then checked another 11,537,208
frames with zero bad. An earlier confirming default run checked 14,070 frames with zero bad.
Development runs are separate from the 3,600-second reproduction attempts above.

## Teeth

Temporarily replacing the checker's input span with an empty span made all four
tests fail: both injected-fault cases lost their diagnostics, the fragmentation
case counted zero frames, and the real-connection stress test counted zero frames.
Restoring the checker restored all four passes. This proves the checker is used;
it does not reproduce the original intermittent defect.

## Other checks

- Debug desktop build: passed, zero warnings and errors.
- Tree hygiene: passed.
- `MISSING_LANGUAGE` counts: unchanged; no language behavior changed.
- Diagnostics are test output for developers, not shipped IDE strings.

## Full-suite run invalidated by a rebuild

The first full run completed all 9,191 tests: 9,122 passed, one failed and 68
skipped, in 36 minutes 35 seconds. While it ran, a targeted rebuild with
`BuildProjectReferences=false` removed the copied `HexIDE.LspGuard.dll` from the
test output. `UntitledDocumentNamesTests.TheServerThatAcceptsOnlyFileUrisSaysSoOnStandardError`
then failed because its guard process could not find that assembly (exit 154).
The assembly was restored and a normal build passed with zero warnings and errors.
This was a validation mistake, not a reproduction of wire corruption. The complete
clean rerun uses the final source with no build interleaved with it.

## Clean full-suite result

The final-source full run completed on 2026-11-17: **9,122 passed, zero failed,
69 skipped, 9,191 total**, in 53 minutes 52 seconds. The xUnit report records zero
errors and zero not-run tests. All four new tests passed; its default stress load
checked 15,985 received frames with zero bad frames. The guard-dependent test that
failed in the invalidated run passed in this run.

The extra skip compared with the earlier run was
`DebugAdapter.ForeignAdapterTests.Debugpy_runs_a_complete_session`: debugpy's
extraction cleanup raised an IO error (directory not empty). That skip is not
coverage. The other skips are platform or missing-template cases.

## Skipped tests in the clean full suite

Each row names a method; the count includes every skipped theory case of that
method. None of these skipped cases is claimed as coverage.

| Method | Cases | Reason |
|---|---:|---|
| `Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header` | 1 | VB6 templates absent |
| `Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched` | 1 | VB6 templates absent |
| `Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical` | 1 | VB6 templates absent |
| `Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag` | 1 | VB6 templates absent |
| `Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write` | 1 | VB6 templates absent |
| `Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them` | 1 | VB6 templates absent |
| `Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact` | 1 | VB6 templates absent |
| `Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step` | 1 | VB6 templates absent |
| `Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce` | 1 | VB6 templates absent |
| `Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity` | 1 | VB6 templates absent |
| `Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception` | 1 | Windows only |
| `IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves` | 1 | Windows only |
| `Infrastructure.ProjectWriteSafetyTests.A_copy_that_cannot_be_removed_is_kept_and_named` | 1 | Windows only |
| `Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess` | 1 | Windows only |
| `LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches` | 1 | Windows only |
| `LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath` | 1 | Windows only |
| `LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile` | 1 | Windows only |
| `LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole` | 1 | Windows only |
| `Processes.ChildProcessJobTests.A_closed_job_refuses_a_process` | 1 | Windows only |
| `Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it` | 1 | Windows only |
| `Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it` | 1 | Windows only |
| `Profiles.ProfileContainmentTests.JunctionEscapeIsRefused` | 1 | Windows only |
| `Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead` | 1 | Windows only |
| `Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused` | 1 | Windows only |
| `Projects.ContributedProjectTests.A_contributed_project_already_open_is_refused_when_opened_again_with_different_drive_and_folder_case` | 1 | Windows only |
| `Projects.DuplicateProjectOpenTests.Opening_the_same_project_with_different_drive_and_folder_case_is_refused` | 1 | Windows only |
| `Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main` | 1 | Windows only |
| `Repack.LinuxRepackerTests.A_linux_build_is_refused_on_windows` | 1 | Windows only |
| `Repack.MacRepackerTests.A_mac_build_is_refused_on_windows` | 1 | Windows only |
| `Repack.WindowsRepackerTests.A_brand_with_no_copyright_keeps_stocks_rather_than_claiming_none` | 1 | Windows only |
| `Repack.WindowsRepackerTests.A_branded_process_declares_that_identity_to_Windows` | 1 | Windows only |
| `Repack.WindowsRepackerTests.A_default_profile_folder_travels_with_the_brand` | 1 | Windows only |
| `Repack.WindowsRepackerTests.A_repacked_executable_says_it_is_the_brand_when_Windows_reads_it` | 1 | Windows only |
| `Repack.WindowsRepackerTests.A_run_that_fails_partway_leaves_nothing_behind` | 1 | Windows only |
| `Repack.WindowsRepackerTests.A_signed_stock_executable_comes_out_unsigned_and_the_run_says_so` | 1 | Windows only |
| `Repack.WindowsRepackerTests.Every_file_but_the_executable_is_exactly_as_published` | 1 | Windows only |
| `Repack.WindowsRepackerTests.Included_files_reach_the_installable_payload_and_report` | 1 | Windows only |
| `Repack.WindowsRepackerTests.Included_folder_below_a_link_is_allowed` | 1 | Windows only |
| `Repack.WindowsRepackerTests.Included_links_are_refused_at_the_root_or_inside` | 1 | Windows only |
| `Repack.WindowsRepackerTests.Included_missing_folder_is_refused` | 1 | Windows only |
| `Repack.WindowsRepackerTests.Included_output_ancestor_is_refused_before_staging` | 1 | Windows only |
| `Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output` | 12 | Windows only |
| `Repack.WindowsRepackerTests.The_brands_files_land_where_the_IDE_will_look_for_them` | 1 | Windows only |
| `Repack.WindowsRepackerTests.The_icon_is_the_brands_with_nothing_left_of_stocks` | 1 | Windows only |
| `Repack.WindowsRepackerTests.The_installer_inputs_are_written_beside_the_build_and_not_inside_it` | 1 | Windows only |
| `Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible` | 1 | Windows only |
| `Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it` | 1 | Windows only |
| `Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet` | 1 | Windows only |
| `Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it` | 1 | Windows only |
| `Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn` | 1 | Windows only |
| `Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation` | 1 | Windows only |
| `Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot` | 1 | Windows only |
| `Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests` | 1 | Windows only |
| `UserDataPathTests.OnWindowsItIsUnderAppData` | 1 | Windows only |
| `DebugAdapter.ForeignAdapterTests.Debugpy_runs_a_complete_session` | 1 | debugpy extraction IO error |
| `Repack.MacRepackerTests.A_mac_build_is_repacked_as_one` | 1 | macOS only |
| `Repack.MacRepackerTests.A_signature_macOS_rejects_stops_the_run_and_leaves_nothing` | 1 | macOS only |
| `Repack.MacRepackerTests.macOS_accepts_the_property_list_signature_and_icon` | 1 | macOS only |

A subsequent isolated run of
`DebugAdapter.ForeignAdapterTests.Debugpy_runs_a_complete_session` passed on the
final source: **one passed, zero failed, zero skipped**, in 6.9 seconds. This is the
coverage for that session, rather than its skipped case in the full-suite report.
