# #4273: shutdown versus the disconnection record

Checked on WSL/Linux on 2026-11-16–17, starting from `cd705ecea`.

## Code path

At that revision, `TheRecordSeesTheProcessTests.cs:415–416` (`NoticingFakeTransport.Hangup`)
first releases the held shutdown reply and then
disposes the server RPC. `VBLspClient.StopAsync` can receive that reply and finish its exit send before
the client's transport-disconnection callback runs (`VBLspClient.cs:2519–2524`).
`DisposeRpc` then unsubscribes
`OnRpcDisconnected` before disposing the client RPC (`VBLspClient.cs:674–675`).
Only `OnRpcDisconnected` (`VBLspClient.cs:603–607`) wrote the
`disconnected:` lifecycle entry. Thus teardown could suppress that entry altogether; a 30-second
wait taking that path would wait for something no longer subscribed to be recorded.

The fix records a stopping disconnection before unsubscribing. A latch captured by each connection's
callback and its stopping recorder allows only one entry, whichever path wins. The callback snapshots
whether the client is stopping so the detail and fault flag agree.

## Measurements

- Before the product fix, the new
  `A_stop_that_finishes_before_the_hangup_callback_records_one_non_fault_disconnection` test failed:
  its disconnection collection was empty. It performs the real stream/RPC handshake, holds shutdown,
  releases the reply, awaits stop, then hangs up the fake server. This forces teardown before any later
  hangup callback, without changing the timeout or directly calling the client's callback.
- With the original timeout exception restored, the new
  `A_failed_wait_lists_only_this_connections_entries_in_order` test failed after 30 seconds:
  `no missing entry reached the record for 'vb6'` lacked the expected entry listing.
- `4273-stop-stress.py` ran the original class **50 times** against pre-fix product binaries, excluding
  the added deterministic test. **0/50 runs failed**; each ran **16 tests**, so **800 passed**, none
  skipped. The test processes and two Python CPU workers shared affinity to CPUs 0 and 1.
  There was no failed wait, hence no failure entry listing to report. This did not reproduce the
  original CI interleaving; the deterministic test and the unsubscribe path establish the missing entry.

After the fix, the complete class passed **18/18**, with no skips, including both added regressions.

## Repeating the load check

Build `IDE/HexIDE.Tests` first, dropping Windows PATH entries and exporting
`UseSharedCompilation=false` as the repository instructions require. Then, from the repository root:

```sh
python3 scripts/probes/4273-stop-stress.py
```

The probe keeps per-run output under disposable `artifacts/4273/`, stops only the CPU workers it starts,
and prints the failed-run count. It excludes the two added regression tests so a repeat against the
final tree exercises the same 16 original cases.

## Final checks

- Full `HEXIDE_FOREIGN_LSP_DOWNLOAD=0 dotnet test HexIDE.Tests/`: **9,121 passed, 68 skipped,
  9,189 total**, zero failures, **50m 43s**. The run completed without interruption.
- Debug Desktop build: zero warnings and zero errors.
- Tree hygiene: passed, nine existing non-blocking warnings. Whitespace check passed.
- Both new regressions failed before their corresponding changes and passed afterward: **2/2 teeth**.
- No `IDE/HexIDE/` or bundled-server source changed, so the conditional integration and server-suite
  checks do not apply. `MISSING_LANGUAGE.md` and its counts are unchanged.
- The full suite outlasted the 45-minute allowance. A non-terminating diagnostic snapshot of this
  clone's test host at 00:39 UTC showed `DockFactoryTests.SplitDocks_RoundTripPreservesTopologyTabsAndProportions`
  constructing `TranslationEditorViewModel`, with an active NSubstitute argument-matching frame.
  This identifies work sampled during the long run, not a measured explanation for its duration.

### Skipped cases

These 68 cases skipped in the full run and provide no coverage here: 55 require Windows, three
require macOS, and ten require an unavailable VB6 template tree. The new regressions did not skip.
Names below omit the common `HexIDE.Tests.` prefix.

```text
Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it
Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it
Processes.ChildProcessJobTests.A_closed_job_refuses_a_process
Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main
Projects.ContributedProjectTests.A_contributed_project_already_open_is_refused_when_opened_again_with_different_drive_and_folder_case
Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess
LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole
LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile
Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn
UserDataPathTests.OnWindowsItIsUnderAppData
Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag
Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity
Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them
Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step
Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write
Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact
Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce
Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead
Profiles.ProfileContainmentTests.JunctionEscapeIsRefused
Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header
Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched
Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical
Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: "branding/logo.png")
Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: "nested/HEXIDE-FIRSTPARTY.PUB")
Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: "revocations.sig")
Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: "acme-ide.exe")
Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: "hexide-root.pub")
Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: "revocations-cache.sig")
Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: "revocations-cache.json")
Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: "brand.json")
Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: "revocations.json")
Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: "revocations-cache.signed")
Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: "HexIDE.exe")
Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: "LICENSE")
Repack.WindowsRepackerTests.Included_missing_folder_is_refused
Repack.WindowsRepackerTests.Included_output_ancestor_is_refused_before_staging
Repack.WindowsRepackerTests.The_installer_inputs_are_written_beside_the_build_and_not_inside_it
Repack.WindowsRepackerTests.The_brands_files_land_where_the_IDE_will_look_for_them
Repack.WindowsRepackerTests.Included_files_reach_the_installable_payload_and_report
Repack.WindowsRepackerTests.The_icon_is_the_brands_with_nothing_left_of_stocks
Repack.WindowsRepackerTests.A_default_profile_folder_travels_with_the_brand
Repack.WindowsRepackerTests.A_signed_stock_executable_comes_out_unsigned_and_the_run_says_so
Repack.WindowsRepackerTests.A_run_that_fails_partway_leaves_nothing_behind
Repack.WindowsRepackerTests.A_branded_process_declares_that_identity_to_Windows
Repack.WindowsRepackerTests.A_brand_with_no_copyright_keeps_stocks_rather_than_claiming_none
Repack.WindowsRepackerTests.Every_file_but_the_executable_is_exactly_as_published
Repack.WindowsRepackerTests.Included_folder_below_a_link_is_allowed
Repack.WindowsRepackerTests.A_repacked_executable_says_it_is_the_brand_when_Windows_reads_it
Repack.WindowsRepackerTests.Included_links_are_refused_at_the_root_or_inside
Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused
Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception
Projects.DuplicateProjectOpenTests.Opening_the_same_project_with_different_drive_and_folder_case_is_refused
LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath
Repack.MacRepackerTests.A_mac_build_is_repacked_as_one
Repack.MacRepackerTests.A_signature_macOS_rejects_stops_the_run_and_leaves_nothing
Repack.MacRepackerTests.A_mac_build_is_refused_on_windows
Repack.MacRepackerTests.macOS_accepts_the_property_list_signature_and_icon
Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it
Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it
Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet
Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible
IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves
Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests
Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation
Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot
Infrastructure.ProjectWriteSafetyTests.A_copy_that_cannot_be_removed_is_kept_and_named
Repack.LinuxRepackerTests.A_linux_build_is_refused_on_windows
LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches
```
