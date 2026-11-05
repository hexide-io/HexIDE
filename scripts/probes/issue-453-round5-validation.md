# Issue 453, round 5 validation

Base: `4f62adb6`, merged with upstream `3f5001d2` as `b681b5b0`.
The upstream history contains #3078 (code lenses and client-run commands).

## Regression teeth

Run the new tests against the merged base, without this round's product changes:

```sh
dotnet test IDE/HexIDE.Tests/ -- --filter-class '*CodeActionTests'
dotnet test IDE/HexIDE.Integration.Tests/ -- --filter-class '*QuickFixTests'
```

Measured on Linux, 2026-11-04: **9 new cases fail**, none skip.

- Client tests: 3 fail, 6 existing cases pass. Unlisted action commands reach the peer;
  each claimant receives both diagnostics; the group label is the registration id.
- Headless tests: 6 fail, 8 existing cases pass. Both rename aliases and a rename into
  an editor without an attached view produce no input prompt; unlisted commands have
  no localized refusal; pending text has not been sent when the command executes.
- The real JSON-RPC peer sees only `executeCommand`, rather than `didChange` followed
  by `executeCommand`. It checks the received text as well as the message order.

The rename cases use a percent-encoded equivalent URI on Linux and the drive-letter
case variant on Windows. The initial Linux run used two distinct case-sensitive paths;
that test setup was corrected without changing the URI comparer.

The existing command fixtures now explicitly advertise the commands they expect to execute.
The existing registry expectation now uses display names.

## Live checks for the supervisor

- Select an RDCore rename fix: HexIDE's rename prompt opens at the supplied name,
  including when the server uses a lower-case drive letter or names another loaded module.
- A scripted edit-plus-command action sends `didChange` before `executeCommand`;
  the command handler observes the edited source. Undo still restores the edit in one step.
- An unlisted, unknown command shows a localized refusal naming the command, with no RPC request.
- Quick Fixes headers show display names. With two servers publishing at the same range,
  each code-action request carries only that server's diagnostic data.
- Code-lens show-references commands still open their supplied locations.

## Completed checks

- Full `HexIDE.Integration.Tests`: **4,954 passed, 2 skipped, 0 failed** (4,956 total).
  The skipped cases are `ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent`
  and `ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements`; these Windows font/GDI
  checks do not establish Linux coverage. Every new round-5 case runs on Linux.
- Desktop Debug build: **0 warnings, 0 errors**.
- Tree hygiene and `git diff --check`: pass.
- The merged LSP coverage rows had stale headline/section counts. Regenerated with
  `scripts/regenerate-lsp-coverage.py` and the pinned cached metamodel; no table rows were hand-edited.

Windows PATH entries were removed before testing. The NuGet HTTP cache was redirected inside the
clone because its normal user-cache location is read-only in the sandbox. Runs with `--hangdump`
stalled before test output here; the completed integration run used an external 15-minute timeout.
No VB6 runtime behavior or `MISSING_LANGUAGE` counts change in this round. The existing deferred
keyboard route is outside the round-5 deliverable.

The final focused `CodeActionTests` run passes **9 of 9**, with no skips. Combined with the full
headless result, all nine new regression cases pass after the fix. The targeted run writes its results
to a separate directory so it does not overwrite the full IDE suite's results.

The corrected full `dotnet test HexIDE.Tests/` run did not return a summary within an external
15-minute limit (exit 124); no failures were reported in that run. This is **not** a full-suite pass.
The full direct xUnit run with explicit serial execution and verbose case progress completed in
1,138.828 seconds (about 19 minutes), exceeding that earlier elapsed-time limit. The elapsed-time limit alone does not
establish the cause of the incomplete `dotnet test` run.


Full direct serial `HexIDE.Tests` result: **5,781 passed, 35 skipped, 0 failed**, 5,816 total,
above the repository's 5,378-test floor. This is a complete assembly run, not a claim that the
15-minute `dotnet test` invocation completed. The source guard took 80.5 seconds and the corpus smoke
case took 144.6 seconds. No stuck test was established by the diagnostic run.

```sh
cd IDE
HEXIDE_FOREIGN_LSP_DOWNLOAD=0 dotnet HexIDE.Tests/bin/Debug/net10.0/HexIDE.Tests.dll \
  -parallel none -reporter verbose -diagnostics -longRunning 120 -showLiveOutput \
  -xml ../artifacts/453-native/results.xml
```

Skipped IDE cases (none provide coverage here):

- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_closed_job_refuses_a_process`: Job objects exist only on Windows.
- `HexIDE.Tests.Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it`: Job objects exist only on Windows.
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it`: Job objects exist only on Windows.
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole`: Type libraries are read only on Windows.
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile`: Type libraries are read only on Windows.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionEscapeIsRefused`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath`: A link to a share is a Windows path.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main`: A made program is started on Windows only: elsewhere it needs a desktop session.
- `HexIDE.Tests.UserDataPathTests.OnWindowsItIsUnderAppData`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.


Final review retained server ids as group keys and added a separate display-name field, so equal labels
do not merge distinct servers. The strengthened existing menu test was run against the prior view and
failed on its raw-id headers (one failed, zero skipped) before the header binding was changed. This is
additional teeth in an existing case, not a tenth new case. The final full suites are rerun after this
refinement; the earlier full direct-run counts above describe the preceding implementation.


After the final id/display-name refinement, the full `dotnet test HexIDE.Integration.Tests/` run
again passes **4,954 cases, 2 skipped, 0 failed** (4,956 total), enforcing the 4,599-test floor.
The strengthened equal-name menu case is included. The focused client run again passes 9 of 9.
The final Desktop Debug build has 0 warnings and 0 errors; tree hygiene and diff whitespace checks pass.


## Final requested full-suite result

After the id/display-name refinement, the exact full `dotnet test HexIDE.Tests/` invocation completes:
**5,781 passed, 35 skipped, 0 failed, 5,816 total**, in 15 minutes 52 seconds. It has no elapsed-time
cutoff and enforces the repository floor. This final result supersedes the earlier elapsed-time-limited
attempts and the preceding direct-run result for validating the final code. The 35 skip names above
remain the same; none of the nine new cases skip.

The final integration result is **4,954 passed, 2 skipped, 0 failed, 4,956 total**. Desktop Debug builds
with **0 warnings and 0 errors**. The CI floors are raised to 5,525 IDE and 4,708 integration tests
(95% of these measured totals), as required by CLAUDE.md. This round changes no runtime-language counts.

The updated floor checks pass **5 of 5**, with no skips. Final tree hygiene passes with its nine
non-blocking attribution warnings, and `git diff --check` is clean. All task changes remain uncommitted;
only the required upstream merge was committed.
