# Output-list verification (#3248)

Round 2, 2026-11-06, WSL. Reviewed the interrupted implementation at `c1fd9f05`
and merged `upstream/main` at `9c0be61f` before verification. The measurements
belong to [PR #3249](https://github.com/hexide-io/HexIDE/pull/3249), now in
[the sequential-files oracle](../../../docs/vb6-fidelity-oracle/sequential-files.md).
No VB6 measurement was made in this run.

## Regression evidence

Temporarily replaced both grammars, StatementExecutor and VbDiagnosticsProvider
with their `upstream/main` versions, keeping the new tests. `OutputListTests`
then had **21 failures and 22 passes in each project**: 42 observed regression
failures altogether. Restored the implementation before the final runs.

Runtime's failures were eleven file-byte rows, nine syntax-refusal rows and
Debug.Print adjacency acceptance. LSP's were seven adjacency-acceptance rows,
nine syntax-refusal rows and five signed-item tree-shape rows. LSP diagnostics
alone already accepted the signs before this fix; the tree-shape assertions
prove that they now start another output item rather than subtracting or adding.

The four oracle controls pass before and after. Existing-expression grouping,
inline Else branch separation, and two existing corpus refusals are preservation
checks, not additional regression teeth. The inline Else check counts branch
bodies, rather than assuming Debug.Print already used outputList on main.
Debug.Print adjacency pins compile acceptance only: the oracle did not measure
its output bytes. The fifteen file-output rows compare actual bytes, including
CR LF, to the oracle, without deriving expectations from the implementation.

## Corpus comparison

Ran `dotnet run --project scripts/probes/for-each-corpus -- <snapshot>` before
and after, over all **172 files** in corpus/ and TestFiles/. `diff -u` was empty
(exit 0). Both snapshots contained the same six Runtime and twelve LSP raw
syntax diagnostics. The separate JSON conformance lane retires one known false
rejection: continuation-drags-a-label-onto-a-statement now admits its adjacent
Debug.Print items, matching the corpus's measured acceptance.

## Parsing cost

BlockParsingCostTests passed all four rows before and after. The two #3210
nesting shapes have these deterministic prediction counts:

| Depth | Before SLL lookahead | After SLL lookahead | Before DFA states | After DFA states |
|---|---:|---:|---:|---:|
| 100 | 3071 | 3072 | 156 | 158 |
| 400 | 12071 | 12072 | 156 | 158 |

Maximum SLL lookahead remains six and LL lookahead remains zero at both depths.
The added work is constant, not multiplied by depth. Both 100- and 400-term
Print-expression cost guards also pass. These counters describe prediction work;
wall-clock timings under concurrent suite load are not a performance benchmark.

## Reproduction environment

Removed `/mnt/` entries from PATH before every test invocation. Set
XDG_RUNTIME_DIR to a temporary directory inside this clone: the sandbox's default
`/run/user/1000` is read-only, preventing .NET from creating ANTLR's named mutex.
No package or product configuration was changed for that environment adjustment.

## Unmeasured behaviour

Other binary operators after Spc/Tab and empty Spc()/Tab() retain their prior
expression fallback. VB6 IDE relisting and /run were not measured. A form's or
PictureBox's Print method remains outside this change (#989). No new refusal or
result is invented for those cases.

## CI floors and documentation

Retained the build.yml changes: Runtime's minimum is 7813 against 8225 measured
tests, and LSP's is 672 against 708, each approximately 95%. Raising those floors
keeps a short run from passing as the suites grow; no CI lane or test selection
changes. The full runs passed those minimums.

All eighteen MissingLanguageDocTests passed. MISSING_LANGUAGE's support levels
and counts did not move; Print #, Write #, Debug.Print, Spc and Tab Detail notes
were updated. lsp-server-features.md lists no output-list limit requiring an
edit. The unreleased changelog.d/3248.md is retained. The oracle pages came from
main and were not edited.

## Completed suite reports

Runtime: 8212 passed, 13 skipped, zero failures; LSP: 708 passed, no skips or
failures. Both xUnit reports record zero framework errors. Runtime includes all
43 OutputListTests, two GrammarParityTests, four BlockParsingCostTests and five
CorpusConformanceTests. LSP includes all 43 OutputListTests. No #3248 test skipped.

### Runtime skips

These provide no coverage in this run: seven need the absent VB6 templates, five
need Windows filesystem behaviour, and one needs the opt-in VB6 compiler oracle.

- `ClassHeaderPreservationTests.A_real_vb6_class_round_trips_byte_for_byte`
- `ClassHeaderPreservationTests.A_renamed_module_retargets_VB_Name_but_keeps_everything_else`
- `ClassHeaderPreservationTests.Instancing_and_data_binding_settings_survive`
- `ContainerHierarchySaveTests.RoundTrip_OfVb6sOwnNestedTemplates_ReproducesTheBeginStructure`
- `FileSystemTests.CurDir_uses_only_the_first_drive_character`
- `FileSystemTests.Windows_attribute_rows`
- `FileSystemTests.Windows_error_rows`
- `FileSystemTests.Windows_name_rows`
- `FileSystemTests.Windows_numeric_rows`
- `MenuHierarchySaveTests.RoundTrip_OfVb6sOwnMenuTemplates_ReproducesTheMenuShape`
- `MenuHierarchySaveTests.Vb6sOwnMenuTemplates_AreNoLongerHeldReadOnly`
- `Vb6OracleRoundTripTests.HexIDE_output_still_builds_in_vb6`
- `Vb6TextFileTests.The_one_corpus_file_with_high_bytes_round_trips`

### IDE suite

Full native xUnit run: **6486 passed, 36 skipped, 0 failed**,
0 framework errors, 19.4 minutes; 6522 total, exceeding the
6166 CI floor. The initial quiet MTP invocation was interrupted without a result;
it is not claimed as a passing run. The completed native run used:

```sh
dotnet IDE/HexIDE.Tests/bin/Debug/net10.0/HexIDE.Tests.dll -longRunning 30 -xml <report.xml>
```

The native report was checked with scripts/check-xunit-report.sh. The
long-running diagnostics showed the Roslyn Avalonia-test guard, intentional slow
server-lifetime waits, LSP document-name checks and the corpus smoke lane.
No product workaround or timeout change was made for them.

The following IDE skips provide no coverage in this Linux run:

- `Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception`
- `IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves`
- `Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess`
- `LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches`
- `LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath`
- `LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile`
- `LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole`
- `Processes.ChildProcessJobTests.A_closed_job_refuses_a_process`
- `Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it`
- `Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it`
- `Profiles.ProfileContainmentTests.JunctionEscapeIsRefused`
- `Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead`
- `Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused`
- `Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header`
- `Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched`
- `Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical`
- `Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag`
- `Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write`
- `Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them`
- `Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact`
- `Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step`
- `Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce`
- `Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity`
- `Projects.DuplicateProjectOpenTests.Opening_the_same_project_with_different_drive_and_folder_case_is_refused`
- `Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main`
- `Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text`
- `Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does`
- `Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible`
- `Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it`
- `Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet`
- `Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it`
- `Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn`
- `Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation`
- `Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot`
- `Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests`
- `UserDataPathTests.OnWindowsItIsUnderAppData`
