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

## Round 3: restore SLL startup parsing

2026-11-07, WSL. Completed the already-staged merge of `upstream/main` at
`0ee530a5` as `63032646`, then reproduced the new #3259 startup guard's failure.
No VB6 measurement was made in this round.

### Failing prediction and correction

With SLL and BailErrorStrategy, the receiver's `implicitCallStmt_InStmt`
(decision 455 in the merged round-2 Runtime parser) chose `iCS_S_MembersCall`.
The trace consumed `Debug.Print` as the receiver. The following `Match(DOT)`
in `iCS_B_MemberProcedureCall`, state 2695, then encountered the space at
line 2, column 11 and raised InputMismatchException. The optional receiver
(decision 447) had entered; the wrong choice was the receiver's member-call
alternative, before the output list was entered.

Guarded bare-name and complete-call alternatives now precede the original
member-call alternatives in both grammars. The first choices apply to complete
names/calls, preserving a following dot or dictionary selector. A simple
statement receiver immediately before `.Print` also takes the bare-name path;
that predicate receives `$ctx` explicitly so ANTLR treats it as local to the
rule's own prediction. The original alternatives remain as fallbacks.

Tracing beyond the receiver showed the same ambiguity for `Hour(d)` and its
bare `d` argument: SLL selected `iCS_S_MembersCall` for each instead of the
complete call or bare name. Removing the old bare-name predicate's context
walk alone did not fix the failure. A broad alternative reorder passed the
startup guard but failed sixteen tests in an initial full Runtime attempt,
including a chained no-argument call and dictionary member access. That
candidate was replaced, not retained. The final focused checks include those
classes and additional member/dictionary grouping controls.

The Runtime startup assertion now compares the strings `SLL` and `LL`, so a
failure names the modes. A matching server test checks the reported prediction
stage for the same forty-line workload. Both startup guards failed before the
fix: **2 regression failures in this round**, in addition to the 42 original
output-list regression failures recorded above. Preservation controls are not
counted as new teeth.

### Corpus diagnostics

Ran `scripts/probes/for-each-corpus` on main `0ee530a5`, the merged round-2
branch before editing, and the final grammars. Both `diff -u` comparisons were
empty (exit 0): **172 files, 6 Runtime and 12 LSP syntax diagnostics**, unchanged
in all three snapshots. Main was extracted inside this clone's ignored
`artifacts/` directory; no other clone was touched.

### Focused verification

Runtime's OutputListTests, ParserPredictionTests, BlockParsingCostTests,
GrammarParityTests, ImageComboModelTests, AsNewTests, ImageListTests and
CollectionTests passed **143/143**, without skips. All oracle rows remain
covered; Debug.Print adjacency still asserts compile acceptance only, as
measured in #3249. Added grouping rows cover calls followed by members and a
dictionary followed by a member; they preserve existing parsing rather than
pinning new VB6 values.

The existing build.yml changes raise only the Runtime and LSP test-count
floors to reflect round 2's measured suite growth (7813 and 672 respectively).
This round makes no additional workflow change. MISSING_LANGUAGE statuses and
counts do not move. Other operators after Spc/Tab, empty calls, IDE relisting,
/run and form/PictureBox Print remain as documented above.

### Round 3 prediction cost

Ran the same BlockParsingCostTests driver against main `0ee530a5`'s Runtime
assembly and the final Runtime assembly, in separate directories/processes.
Both runs passed **4/4**, with no errors or skips. The main driver was copied beside main's Runtime DLL to isolate assembly
resolution. These are prediction counters, not a wall-clock
benchmark:

| Depth | Main SLL lookahead | Final SLL lookahead | Main/final max SLL | Main/final LL | Main/final DFA states |
|---|---:|---:|---:|---:|---:|
| 100 | 3071 | 3070 | 6 | 0 | 156 |
| 400 | 12071 | 12070 | 6 | 0 | 156 |

Both 100- and 400-term Print-expression guards also passed against each
assembly. The final 172-file corpus comparison was repeated after all grammar
changes and remained empty against both main and the merged round-2 branch.

### Round 3 full suites

Runtime passed **8216**, skipped **13**, failed **0** (8229 total; floor 7813).
LSP passed **713**, skipped **0**, failed **0** (floor 672). The Runtime skips
provide no coverage: seven need VB6 templates, five need Windows filesystem
behaviour, and one is the opt-in VB6 round-trip oracle. No #3248 test skipped.

- `Vb6OracleRoundTripTests.HexIDE_output_still_builds_in_vb6`
- `FileSystemTests.Windows_attribute_rows`
- `FileSystemTests.Windows_error_rows`
- `FileSystemTests.CurDir_uses_only_the_first_drive_character`
- `FileSystemTests.Windows_name_rows`
- `FileSystemTests.Windows_numeric_rows`
- `ContainerHierarchySaveTests.RoundTrip_OfVb6sOwnNestedTemplates_ReproducesTheBeginStructure`
- `ClassHeaderPreservationTests.Instancing_and_data_binding_settings_survive`
- `ClassHeaderPreservationTests.A_renamed_module_retargets_VB_Name_but_keeps_everything_else`
- `ClassHeaderPreservationTests.A_real_vb6_class_round_trips_byte_for_byte`
- `MenuHierarchySaveTests.RoundTrip_OfVb6sOwnMenuTemplates_ReproducesTheMenuShape`
- `MenuHierarchySaveTests.Vb6sOwnMenuTemplates_AreNoLongerHeldReadOnly`
- `Vb6TextFileTests.The_one_corpus_file_with_high_bytes_round_trips`

MissingLanguageDocTests passed **18/18**, without skips; statuses and counts
remain unchanged. The nineteen existing-expression grouping rows also passed
against main's Runtime assembly (**19/19**, no errors or skips), including the
four added member/dictionary controls. Those are preservation checks, not
regression teeth.

Repeated the original output-list regression slice against main's Runtime and
LSP assemblies with the current test drivers (excluding the new SLL guard).
Each ran 47 cases: **21 failed, 26 passed, 0 errors, 0 skipped**. These repeat
round 2's 42 original regression failures; the four extra passing cases per
project are the new grouping controls. Together with the two observed SLL
failures on the merged round-2 branch, that is **44 failures before their
respective fixes**, all rechecked in this round. The final suites above use
the final assemblies, whose tests pass.

The full IDE MTP run passed **6583**, skipped **36**, failed **0** (6619 total;
floor 6166), in 22m 32s. Its copied Runtime DLL was checked byte-for-byte against
the final Runtime test DLL. These Windows/template-dependent skips provide no
coverage in this run:

- `Profiles.ProfileContainmentTests.JunctionEscapeIsRefused`
- `Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead`
- `Projects.DuplicateProjectOpenTests.Opening_the_same_project_with_different_drive_and_folder_case_is_refused`
- `UserDataPathTests.OnWindowsItIsUnderAppData`
- `LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches`
- `Processes.ChildProcessJobTests.A_closed_job_refuses_a_process`
- `Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it`
- `Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it`
- `Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation`
- `Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests`
- `Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot`
- `Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does`
- `Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text`
- `Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet`
- `Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it`
- `Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it`
- `Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible`
- `Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess`
- `Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused`
- `Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched`
- `Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical`
- `Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header`
- `Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception`
- `Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn`
- `LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile`
- `LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole`
- `Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main`
- `IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves`
- `Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag`
- `Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact`
- `Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity`
- `Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce`
- `Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them`
- `Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step`
- `Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write`
- `LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath`

Final tree hygiene passed with nine existing nonblocking warnings; its identity
scan was skipped because HEXIDE_IDENTITY_PATTERN is unset. `git diff --check`
passed, and this round's changes remain uncommitted for supervisor review.
