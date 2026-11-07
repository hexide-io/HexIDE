# #977 merge refresh (2026-11-07)

The supervisor owns the pending merge commit. No existing commits were rewritten.

## Conflict resolutions

- BasicInterpreter.cs: retained main's ParseStack.Run around BuildModule and its
  ObjectArrayReturn helper; retained the branch's allowIndexedLet input and
  TryGetModuleProcedure helper as separate methods. Own-module properties resolve
  before other standard modules, with implicit Me through RunNamed for reads/stores.
- interpreter-core/spec.md: kept both sets of independent requirements, inside
  Requirements.
- MISSING_FEATURES.md: retained main's newer numeric/control notes and added the
  standard-module property coverage to the object-model row.
- MISSING_LANGUAGE.md: retained main's newer coverage, including #999 object-array
  returns; replaced the stale #977 failure with Partial and retained Get/Let/Set
  restrictions. Counts are checked by MissingLanguageDocTests.

CHANGELOG.md and CLAUDE.md match upstream/main. The unreleased changelog.d/977.md
remains. Main's oracle subject pages win; the oracle index links the issue's
later measurements instead of duplicating another author's oracle rows.

## Verification

Windows PATH entries were removed before all dotnet commands.

- Runtime: 8193 passed, 13 skipped, 0 failed (8206 total), floor 7757.
- LSP server: 663 passed, 0 skipped, 0 failed.
- Teeth: the four OwnPropertyByNameTests passed with the fix. Temporarily changing
  TryResolveProcedure to pass own: false and restricting TryResolveProperty's
  current-module lookup to Standard modules failed 3, passed 1, skipped 0.
  Restored the source before running the full runtime suite.
- Tree hygiene: passed, 9 nonblocking attribution warnings.

The 13 runtime skips were corpus/oracle-dependent round trips in
ContainerHierarchySaveTests (1), Vb6OracleRoundTripTests (1),
MenuHierarchySaveTests (2), ClassHeaderPreservationTests (3), Vb6TextFileTests (1),
and Windows FileSystemTests rows (5). These skips are not coverage for #977.

The IDE build needed a fresh --artifacts-path within this clone because existing
Dap/Lsp obj files were read-only in the sandbox. NuGet's HTTP cache was redirected
inside artifacts. ANTLR's setup lock was also unavailable, so the existing generated
parsers and Antlr list files were copied into the fresh output directory, with list
paths rewritten. Neither grammar changed. No project settings were changed for this.

## Left unimplemented

The existing explicit refusals remain: external stores through a Private Let
paired with a Public Get, indexed Set, and Let with no accessor when Get returns
a live object. #977 records these as unmeasured; MISSING_LANGUAGE names them.

MissingLanguageDocTests: 18 passed, 0 failed/skipped after restoring its required
empty Silently wrong status table and using HEXIDE_MISSING_LANGUAGE_FIX=1.
Only the guard-named figures changed: Statements silently wrong 1 -> 0,
Statements partial 50 -> 51, total silently wrong 1 -> 0, total partial 480 -> 481.
The inventory stays 1185 names. The Statements heading was recounted too.

The IDE assembly and nested LSP/desktop dependencies rebuilt successfully.
The full IDE run uses --no-build after that rebuild, with floor 6166.

FormInstanceTests: 53 passed, 0 skipped/failed, including the form-own-property
regression. The first test-host launch failed before tests because hang-dump
monitoring could not create its mutex under the sandbox's read-only runtime
directory. Retried with XDG_RUNTIME_DIR inside this clone and retained
--hangdump --hangdump-timeout 10m; all 53 ran successfully.

An initial buffered IDE run was cancelled in favour of a visible-progress run.
That retry hit the locally chosen 600-second cap at 2459 passed / 0 failed
(20 Windows-only skips had been reported by then). This was an insufficient
wall-clock allowance, not a test failure. A fresh full run uses a 1800-second cap.

Full IDE final result: **6583 passed, 36 skipped, 0 failed, 6619 total**, in
21m 06s, meeting floor 6166. This includes the doc guards. No source changes
were needed after the successful Runtime/LSP/FormInstance results.

### IDE skips

These are not coverage for #977:

- HexIDE.Tests.Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused
- HexIDE.Tests.Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn
- HexIDE.Tests.IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves
- HexIDE.Tests.Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it
- HexIDE.Tests.Processes.ChildProcessJobTests.A_closed_job_refuses_a_process
- HexIDE.Tests.Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it
- HexIDE.Tests.Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it
- HexIDE.Tests.Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible
- HexIDE.Tests.Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet
- HexIDE.Tests.Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it
- HexIDE.Tests.Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess
- HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile
- HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole
- HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header
- HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical
- HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched
- HexIDE.Tests.UserDataPathTests.OnWindowsItIsUnderAppData
- HexIDE.Tests.Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main
- HexIDE.Tests.LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches
- HexIDE.Tests.Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests
- HexIDE.Tests.Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation
- HexIDE.Tests.Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot
- HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact
- HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write
- HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce
- HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them
- HexIDE.Tests.Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag
- HexIDE.Tests.Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity
- HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step
- HexIDE.Tests.Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text
- HexIDE.Tests.Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does
- HexIDE.Tests.Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception
- HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead
- HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionEscapeIsRefused
- HexIDE.Tests.Projects.DuplicateProjectOpenTests.Opening_the_same_project_with_different_drive_and_folder_case_is_refused
- HexIDE.Tests.LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath
