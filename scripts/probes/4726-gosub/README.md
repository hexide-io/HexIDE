# #4726 validation

WSL Ubuntu, .NET 11, 2026-11-18. Measurement authority: [the latest issue comment](https://github.com/hexide-io/HexIDE/issues/4726), rows G1–G35 and G41–G42 (oracle PR #4727). Error descriptions 5/94 also appear in the existing [Error catalogue](../../../docs/vb6-fidelity-oracle/strcomp-strconv-environ-switch-and-error.md). No oracle pages were changed.

## Files changed

- `IDE/HexIDE.Integration.Tests/Views/RunFaultTests.cs`
- `IDE/HexIDE.Runtime.Integration.Tests/Views/FormInstanceTests.cs`
- `IDE/HexIDE.Runtime.Integration.Tests/Views/StandaloneErrorTests.cs`
- `IDE/HexIDE.Runtime.Tests/CorpusBehaviourTests.cs`
- `IDE/HexIDE.Runtime.Tests/GoSubTests.cs`
- `IDE/HexIDE.Runtime.Tests/ResumeNextIntoABlockTests.cs`
- `IDE/HexIDE.Runtime.Tests/RunErrorTextTests.cs`
- `IDE/HexIDE.Runtime.Tests/UnimplementedIsNotTrappableTests.cs`
- `IDE/HexIDE.Runtime/Interpreter/StatementExecutor.cs`
- `IDE/HexIDE.Tests/ViewModels/MainViewViewModelTests.cs`
- `changelog.d/4726-gosub.md`
- `docs/MISSING_LANGUAGE.md`
- `docs/debugger-vb6-divergences.md`
- `docs/interpreter-gaps.md`
- `openspec/specs/interpreter-core/spec.md`
- `scripts/probes/4726-gosub/README.md`

## Results

Windows PATH entries were removed; `UseSharedCompilation=false` and a fresh writable `XDG_RUNTIME_DIR` were exported. MSBuild worker reuse was disabled and the NuGet HTTP cache redirected inside this clone to avoid other sandboxes' workers and the read-only home cache.

| Run | Passed | Failed | Skipped | Total |
|---|---:|---:|---:|---:|
| Runtime (full) | 10557 | 0 | 20 | 10577 |
| Runtime integration (full) | 3839 | 0 | 2 | 3841 |
| IDE (full) | 10319 | 0 | 73 | 10392 |
| RunFaultTests | 33 | 0 | 0 | 33 |
| MainViewViewModelTests | 169 | 0 | 0 | 169 |
| RunErrorTextTests | 7 | 0 | 0 | 7 |
| MissingLanguageDocTests | 20 | 0 | 0 | 20 |

The runtime integration and RunFault xUnit reports were checked for missing tests/setup errors. Tree hygiene passed (nine existing warnings). Strict OpenSpec validation reports 54 existing requirement-length warnings on both main and this change: the issue arrays are identical after accounting for the inserted requirement's index.

## Teeth

All 42 new cases failed against main's executor at `7929177e6`: 37 measured rows and five explicit-refusal cases. The baseline added only the internal depth constant needed to compile the tests; execution stayed unchanged. A temporary source copy inside this clone kept these checks separate from the full suites.

The supervisor's `mutation-check.ps1` ran a green baseline, mutated, rebuilt, tested, restored byte-for-byte, rebuilt and tested green for each mutation:

| Mutation | Failing rows | Count |
|---|---|---:|
| Narrow to Double then truncate, retaining Empty/Null conversion | G7, G8 | 2 |
| Return Nothing when a nested pass reaches procedure end | G30, G31 | 2 |
| Remove the depth count cap, retaining the stack probe | G41, G42 | 2 |
| Remove Return's depth-zero check | G19, G25 | 2 |

The precise selector check ran 37 rows. The other three checks' method filter also selected existing measured-row tests: each baseline/restored run passed 226 cases. An earlier coarse selector mutation additionally broke Empty (G11); the final mutation above isolates fractional truncation.

The updated corpus guard also failed against the old executor, solely on `gap-fill/gosub-return-colon-joined-one-line`, and passed in the full runtime suite: 43 tests in total have before-change failure evidence. The AppActivate stand-in and formatting edits retain existing guard behaviour; no teeth are claimed for them.

The requested regression classes ran in the full runtime suite: ErrorHandlingGoToTests, LineLabelTests, NumericLineLabelTests, ErlTests, ResumeNextIntoABlockTests, NextCounterListTests and SetNextStatementTests. No measured GoSub row already passed on main. The corpus's existing GoSub case now matches its recorded result, and its known-divergence entry was removed.

## Remaining limits

- Integer-overflow selectors refuse as unmeasured, naming #4726.
- Resume in a nested pass with no fault of its own refuses as unmeasured, naming #4726.
- Labels inside blocks, nested-granular Resume, stepping and Set Next Statement retain their existing behaviour. GoSub stepping is unmeasured; no live stepping fidelity is claimed.
- HexIDE's maximum GoSub depth is 256, subject also to the stack probe, versus p-code's measured 252,154. G40's handler call can succeed at HexIDE's earlier cap. (Review: the cap became 2,000, measured against the stack the interpreter has on each platform; see the PR.)
- New refusal descriptions are interpreter messages in English. Existing localized IDE wrappers continue to present them.
- CLAUDE.md’s pre-pass/wall wording is reserved for supervisor review, as the latest issue comment requests.

MISSING_LANGUAGE's named inventory remains 1,193: Dies 47 → 42, Partial 518 → 523. Statements move four names (Dies 7 → 3, Partial 55 → 59); keywords move one (Dies 1 → 0, Partial 23 → 24). The two unmeasured member rows are outside that named inventory.

## Skipped tests

These skips provide no coverage. All 42 new GoSub/refusal cases ran on Linux. Runtime's UiThread child-only case skips direct discovery; its Linux parent runs in the full suite.

### Runtime (full)

- `HexIDE.Runtime.Tests.ContainerHierarchySaveTests.RoundTrip_OfVb6sOwnNestedTemplates_ReproducesTheBeginStructure`
- `HexIDE.Runtime.Tests.Vb6OracleRoundTripTests.HexIDE_output_still_builds_in_vb6`
- `HexIDE.Runtime.Tests.ShellTests.RecordsMeasuredStylesAndDefault`
- `HexIDE.Runtime.Tests.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text`
- `HexIDE.Runtime.Tests.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does`
- `HexIDE.Runtime.Tests.ClassHeaderPreservationTests.A_real_vb6_class_round_trips_byte_for_byte`
- `HexIDE.Runtime.Tests.ClassHeaderPreservationTests.A_renamed_module_retargets_VB_Name_but_keeps_everything_else`
- `HexIDE.Runtime.Tests.ClassHeaderPreservationTests.Instancing_and_data_binding_settings_survive`
- `HexIDE.Runtime.Tests.FileSystemTests.Windows_name_rows`
- `HexIDE.Runtime.Tests.FileSystemTests.Windows_numeric_rows`
- `HexIDE.Runtime.Tests.FileSystemTests.Windows_attribute_rows`
- `HexIDE.Runtime.Tests.FileSystemTests.Windows_error_rows`
- `HexIDE.Runtime.Tests.FileSystemTests.CurDir_uses_only_the_first_drive_character`
- `HexIDE.Runtime.Tests.Vb6TextFileTests.The_one_corpus_file_with_high_bytes_round_trips`
- `HexIDE.Runtime.Tests.MenuHierarchySaveTests.RoundTrip_OfVb6sOwnMenuTemplates_ReproducesTheMenuShape`
- `HexIDE.Runtime.Tests.MenuHierarchySaveTests.Vb6sOwnMenuTemplates_AreNoLongerHeldReadOnly`
- `HexIDE.Runtime.Tests.UiThreadTests.Child_ReportsTheUiThreadsStack`
- `HexIDE.Runtime.Tests.WindowsDateLocaleTests.Era_date_output_matches_GetDateFormatEx_and_reads_without_a_wrong_date`
- `HexIDE.Runtime.Tests.WindowsDateLocaleTests.Date_output_matches_GetDateFormatEx`
- `HexIDE.Runtime.Tests.LocaleCompareTests.StrComp_matches_every_measured_cell_on_Windows`

### Runtime integration (full)

- `HexIDE.Runtime.Integration.Tests.Views.ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent`
- `HexIDE.Runtime.Integration.Tests.Views.ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements`

### IDE (full)

- `HexIDE.Tests.Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused`
- `HexIDE.Tests.Repack.MacRepackerTests.A_mac_build_is_repacked_as_one`
- `HexIDE.Tests.Repack.MacRepackerTests.A_sealed_bundle_is_resealed_after_branding`
- `HexIDE.Tests.Repack.MacRepackerTests.macOS_accepts_the_property_list_signature_and_icon`
- `HexIDE.Tests.Repack.MacRepackerTests.A_mac_build_is_refused_on_windows`
- `HexIDE.Tests.IDE.CodeTrustTests.MacOS_reads_the_quarantine_attribute`
- `HexIDE.Tests.IDE.CodeTrustTests.Windows_reads_the_Zone_Identifier_stream_beside_a_file`
- `HexIDE.Tests.Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main`
- `HexIDE.Tests.Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess`
- `HexIDE.Tests.UserDataPathTests.OnWindowsItIsUnderAppData`
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile`
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole`
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests`
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot`
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation`
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead`
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionEscapeIsRefused`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it`
- `HexIDE.Tests.Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception`
- `HexIDE.Tests.LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched`
- `HexIDE.Tests.Infrastructure.ProjectWriteSafetyTests.A_copy_that_cannot_be_removed_is_kept_and_named`
- `HexIDE.Tests.LspClient.TypeLibraryServicePathTests.GuidOnlyReferenceLoadsWindowsOleAutomationLibrary`
- `HexIDE.Tests.IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves`
- `HexIDE.Tests.Repack.LinuxRepackerTests.A_linux_build_is_refused_on_windows`
- `HexIDE.Tests.Infrastructure.PlatformWindowProbeTests.Avalonia_still_creates_the_window_the_session_end_hook_answers_on`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write`
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_closed_job_refuses_a_process`
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it`
- `HexIDE.Tests.Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it`
- `HexIDE.Tests.Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn`
- `HexIDE.Tests.Projects.ContributedProjectTests.A_contributed_project_already_open_is_refused_when_opened_again_with_different_drive_and_folder_case`
- `HexIDE.Tests.LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath`
- `HexIDE.Tests.LspClient.WireStressTests.ConcurrentConnectionsKeepEveryFrameValid`
- `HexIDE.Tests.Projects.DuplicateProjectOpenTests.Opening_the_same_project_with_different_drive_and_folder_case_is_refused`
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: "revocations.json")`
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: "nested/HEXIDE-FIRSTPARTY.PUB")`
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: "acme-ide.exe")`
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: "revocations-cache.json")`
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: "LICENSE")`
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: "revocations.sig")`
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: "brand.json")`
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: "revocations-cache.signed")`
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: "hexide-root.pub")`
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: "branding/logo.png")`
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: "HexIDE.exe")`
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: "revocations-cache.sig")`
- `HexIDE.Tests.Repack.WindowsRepackerTests.A_repacked_executable_says_it_is_the_brand_when_Windows_reads_it`
- `HexIDE.Tests.Repack.WindowsRepackerTests.Every_file_but_the_executable_is_exactly_as_published`
- `HexIDE.Tests.Repack.WindowsRepackerTests.The_icon_is_the_brands_with_nothing_left_of_stocks`
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_files_reach_the_installable_payload_and_report`
- `HexIDE.Tests.Repack.WindowsRepackerTests.A_default_profile_folder_travels_with_the_brand`
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_output_ancestor_is_refused_before_staging`
- `HexIDE.Tests.Repack.WindowsRepackerTests.A_brand_with_no_copyright_keeps_stocks_rather_than_claiming_none`
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_missing_folder_is_refused`
- `HexIDE.Tests.Repack.WindowsRepackerTests.The_installer_inputs_are_written_beside_the_build_and_not_inside_it`
- `HexIDE.Tests.Repack.WindowsRepackerTests.A_run_that_fails_partway_leaves_nothing_behind`
- `HexIDE.Tests.Repack.WindowsRepackerTests.A_branded_process_declares_that_identity_to_Windows`
- `HexIDE.Tests.Repack.WindowsRepackerTests.The_brands_files_land_where_the_IDE_will_look_for_them`
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_links_are_refused_at_the_root_or_inside`
- `HexIDE.Tests.Repack.WindowsRepackerTests.Included_folder_below_a_link_is_allowed`
- `HexIDE.Tests.Repack.WindowsRepackerTests.A_signed_stock_executable_comes_out_unsigned_and_the_run_says_so`

Possible drift: none.
