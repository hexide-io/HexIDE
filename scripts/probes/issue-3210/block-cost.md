# #3210 runtime block prediction, round 2

Measured on Ubuntu 26.04, .NET 10.0.14, x64 Debug, 2026-11-06. This is host parser
profiling, not a new VB6 measurement. The VB6 acceptance references remain in
[the issue](https://github.com/hexide-io/HexIDE/issues/3210) and the linked oracle page.

`BlockParsingCostTests` enables `parser.Profile` and invokes the interpreter's
`ParseWithFallback`, clearing the shared DFA first for each depth. Its nonparallel
collection excludes concurrent parsers. Both depths complete in SLL: **zero LL
fallbacks, zero LL lookahead**, so the cost is not SLL conflict recovery. Decision
numbers change after grammar regeneration; the rule and decision role identify them.
Raw per-decision counters are in [before](block-cost-before.txt) and [after](block-cost-after.txt).
ANTLR 4.13.1's C# profiler uses `DateTime.Now.ToFileTime`, so `timeInPrediction` is
in 100 ns units ([source](https://github.com/antlr/antlr4/blob/4.13.1/runtime/CSharp/src/Atn/ProfilingATNSimulator.cs)).
Timings describe this run only; tests assert structural costs, never elapsed time.

| Grammar | Depth | Decision | Prediction seconds | SLL lookahead total / maximum | DFA states added |
|---|---:|---|---:|---:|---:|
| Before | 100 | 74, block continuation | 0.789 | 1,008 / 806 | 809 |
| Before | 400 | 74, block continuation | 4.227 | 4,008 / 3,206 | 3,209 |
| Before | 100 | 200, ifBlockBody alternative | 1.093 | 40,300 / 799 | 1,295 |
| Before | 400 | 200, ifBlockBody alternative | 5.547 | 641,200 / 3,199 | 5,195 |
| After | 100 | 79, block continuation | 0.001 | 204 / 2 | 5 |
| After | 400 | 79, block continuation | 0.006 | 804 / 2 | 5 |
| After | 100 | 205, ifBlockBody suffix | 0.0001 | 200 / 2 | 3 |
| After | 400 | 205, ifBlockBody suffix | 0.0005 | 800 / 2 | 3 |

All decisions combined: 2,245 / 8,545 DFA states before at 100 / 400 levels;
156 / 156 after. Total SLL lookahead changes from 43,971 / 655,671 to 3,071 / 12,071.
The tests fail at both depths before the change (2 cases), then pass with bounded
maximum lookahead, linear total lookahead, and a depth-independent DFA ceiling.

Two ambiguities caused the cost. `ifBlockBody` chose between two alternatives
starting with `block`, so prediction traversed every body's nested contents. Factoring
that prefix removes the quadratic lookahead. Separately, `moduleBodyElement` included
`moduleBlock`, allowing adjacent module blocks: a separator could continue a block or
start another module block. Grouping adjacent module statements into a single block
removes that competing continuation. Named module members still separate blocks.
The corpus dumper follows module-body children in source order after that tree change.

The server also carries the adjacent-module-block shape. Its `ifBlockBody` already has
one alternative, `block blockSep`. The server grammar is unchanged in this round.

A short token predicate stops block continuation before a line head followed by `End If`,
`ElseIf` or `Else`. It preserves the existing #223/#2529 preference for closing-line
labels over bare calls after the body is factored. ClosingLineTests, SeparatorIssue223Tests
and the corpus exercise that preference; their expectations are unchanged.

## Cold parse and memory

Build [BlockCostProbe.csproj](../issue-3210-block-cost/BlockCostProbe.csproj), then start its DLL in a fresh process for each depth.
It calls `SyntaxChecker.Run`; it does not execute the body. The probe has a sibling
directory so the existing stack probe on main cannot implicitly compile its entry point. These timings are independent
of the profiler. Bytes are process private memory, before parse, after return, and after
two full GCs with finalizers drained. They include the sized thread's reserved stack and
runtime allocations, not just the DFA. Overall memory includes source, tokens and tree;
bounded DFA state does not mean constant total parsing memory.

| If depth | Cold seconds | Private bytes before → after → full GC |
|---:|---:|---|
| 100 | 0.567 | 75,898,880 → 159,588,352 → 159,555,584 |
| 400 | 0.592 | 75,960,320 → 160,108,544 → 159,813,632 |
| 1,000 | 1.067 | 76,029,952 → 160,817,152 → 160,260,096 |
| 2,000 | 1.403 | 76,173,312 → 162,902,016 → 161,914,880 |
| 7,227 | 1.700 | 77,000,704 → 178,970,624 → 163,725,312 |

No additional cost cap is needed for this shape: the existing stack-derived bound
(7,989 nested If blocks), rule count and physical probe remain. Windows timing and
memory for the revised grammar need supervisor verification. Execution and conditional
compilation limits are unchanged; execution stack work belongs to #3228.

```sh
# Drop Windows PATH entries first under WSL, as required by the issue.
cd IDE
dotnet test HexIDE.Runtime.Tests/ -- --filter-class '*BlockParsingCostTests' --report-xunit
cd ..
dotnet build scripts/probes/issue-3210-block-cost/BlockCostProbe.csproj
for depth in 100 400 1000 2000 7227; do
  dotnet scripts/probes/issue-3210-block-cost/bin/Debug/net10.0/BlockCostProbe.dll "$depth"
done
```

The sandbox refused Antlr4BuildTasks' lock initialization when regenerating the grammar.
For this run the cached ANTLR 4.13.1 jar and Java runtime generated the usual listener and
visitor C# files into the project's `obj/Debug/net10.0`; touching its three generated-file
manifests then let normal compilation proceed. No generated file or build override is
part of the change. A normal Windows build should regenerate through Antlr4BuildTasks.

## Validation

- Full Runtime suite: **8,129 passed, 13 skipped, 0 failed** (8,142 total), 8m52s.
  GrammarParity, corpus conformance/behaviour, both reference-depth parse paths,
  physical stack refusal and the two new cost cases pass.
- Full IDE suite: **6,456 passed, 35 skipped, 0 failed** (6,491 total), 23m51s.
- New regression teeth: **2 cases fail before**, both pass after; no elapsed-time assertions.
- ClosingLineTests, SeparatorIssue223Tests and cost tests together: **17 passed**, no skips.
- MissingLanguageDocTests: **18 passed**, no skips; coverage counts unchanged.
- Tree hygiene and `git diff --check`: pass.

The Runtime skips below establish no coverage here: Windows filesystem cases,
missing VB6 template fixtures and the opt-in VB6 compiler round trip.

- `FileSystemTests.CurDir_uses_only_the_first_drive_character`
- `FileSystemTests.Windows_name_rows`
- `FileSystemTests.Windows_error_rows`
- `FileSystemTests.Windows_attribute_rows`
- `FileSystemTests.Windows_numeric_rows`
- `MenuHierarchySaveTests.RoundTrip_OfVb6sOwnMenuTemplates_ReproducesTheMenuShape`
- `MenuHierarchySaveTests.Vb6sOwnMenuTemplates_AreNoLongerHeldReadOnly`
- `ClassHeaderPreservationTests.A_renamed_module_retargets_VB_Name_but_keeps_everything_else`
- `ClassHeaderPreservationTests.A_real_vb6_class_round_trips_byte_for_byte`
- `ClassHeaderPreservationTests.Instancing_and_data_binding_settings_survive`
- `Vb6OracleRoundTripTests.HexIDE_output_still_builds_in_vb6`
- `Vb6TextFileTests.The_one_corpus_file_with_high_bytes_round_trips`
- `ContainerHierarchySaveTests.RoundTrip_OfVb6sOwnNestedTemplates_ReproducesTheBeginStructure`

The IDE skips also establish no coverage here (Windows-only host/process/path behavior
and absent companion-binary/template fixtures):

- `Processes.ChildProcessJobTests.A_closed_job_refuses_a_process`
- `Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it`
- `Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it`
- `Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write`
- `Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce`
- `Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag`
- `Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact`
- `Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step`
- `Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity`
- `Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them`
- `UserDataPathTests.OnWindowsItIsUnderAppData`
- `Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess`
- `LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath`
- `Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation`
- `Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests`
- `Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot`
- `Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does`
- `Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text`
- `Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead`
- `Profiles.ProfileContainmentTests.JunctionEscapeIsRefused`
- `IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves`
- `Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet`
- `Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible`
- `Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it`
- `Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it`
- `LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole`
- `LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile`
- `LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches`
- `Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception`
- `Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused`
- `Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main`
- `Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header`
- `Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical`
- `Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched`
- `Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn`
