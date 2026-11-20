# Cash-flow precision (#2525)

The measurement source is [issue #2525](https://github.com/hexide-io/HexIDE/issues/2525),
also recorded in the measuring author's [oracle page](vb6-fidelity-oracle/npv-irr-and-mirr.md).
Native and p-code agree. This note records calculations against those measurements, not new
VB6 measurements. No compiler binary was examined.

Run `python3 -B scripts/probes/financial-2525/probe.py` from the repository root.
It uses only Python's standard library and the existing #2523 rational/rounding helpers.
Inputs are binary64. Retained intermediates use 64- or 113-bit significands, nearest/even;
fractional powers use Decimal at 100 decimal digits before rounding. Expected bits are used
only for comparison, never to calculate a result.

## NPV

Three summation forms are tested: independently calculated powers, cumulative multiplication,
and reverse Horner evaluation. Each has six grouped spill sites (base, power, term, sum,
residual, iterate); the last two affect only IRR. At both precisions, cumulative multiplication
with only the power spilling to Double matches all three successful rows. Masks 2, 18, 34
and 50 describe that same NPV model. The base `1 + rate` stays extended; each cumulative
power rounds to Double; terms and summation stay extended until the returned Double.
Independent powers' nearest candidate produces `409C45757D31FA35` for the first row,
seven ULPs from `409C45757D31FA2E`. Horner's nearest candidate produces
`409C45757D31FA30` and `407E197C0E9E19C6`, versus measured
`409C45757D31FA2E` and `407E197C0E9E19C5`.

The implementation uses the shared `FinancialNumber` for retained intermediates and Double
spills for the cumulative powers. It matches the three exact Double results and both error-5
rows (rate -1 and an unallocated Double array). Matching these rows does not prove a unique
model for every input. Named calls use the measured parameter names (#4145).
The argument conversions, array shape errors and p-code arithmetic errors measured in
[#4883](https://github.com/hexide-io/HexIDE/issues/4883) now run with exact NPV bits and error numbers.
Non-finite inputs remain refused.

## IRR

The probe tries the Rate family requested in the supervisor's comment: a secant pair starting
at Guess and Guess/2, stopping when the absolute residual is below 1e-7, up to 40 iterations.
Each of the three NPV residual forms runs with all 64 grouped spill masks at both precisions
(384 candidates). None matches any of the three successful rows. The closest aggregate
candidate is Horner, mask 9 (base and sum spills), at either precision:

| Call | Measured | Candidate |
|---|---|---|
| IRR(a) | `3FC875EFF170437B` | `3FC875EFF17028E0` |
| IRR(a, 0.2) | `3FC875EFF1704153` | `3FC875EFF170444A` |
| IRR(b) | `3FB62D72209FBD49` | `3FB62D7220A36D0D` |

For independently powered and cumulatively multiplied residuals, the closest aggregate mask
is 45 (base, term, sum, iterate spills), producing `3FC875EFF17028DC`,
`3FC875EFF170444E`, `3FB62D7220A36D0C` respectively at either precision.
IRR now ships the 64-bit Horner mask-9 model by
[Jen’s decision, 2026-11-17](https://github.com/hexide-io/HexIDE/issues/2525#issuecomment-6954533411).
The all-positive error-5 row matches. #4883’s sign failures, invalid guesses and measured
nonconvergence now raise 5; the model checks the undiscounted residual too, so discounting
at an enormous rate cannot masquerade as convergence. A zero guess remains unmeasured and
refused. The leading-zero row uses the same authorized closest model; the new measured and
computed patterns appear below.

## MIRR

The probe calculates positive and negative NPV separately, compounds the positive NPV to
its terminal value, divides by the negative NPV times `1 + financeRate`, then takes the
`1/(count-1)` power and subtracts one. It varies the three NPV forms and 256 grouped spill
masks, at both precisions (1,536 candidates). Sites are base, cumulative/independent power,
term, sum, terminal power, denominator, ratio and root. None matches both successful rows.
The closest candidate at both precisions is Horner, mask 84 (term, terminal power and ratio
spills):

| Call | Measured | Candidate |
|---|---|---|
| MIRR(b, 0.1, 0.12) | `3FB94260ED237997` | `3FB94260ED23798E` |
| MIRR(a, 0.1, 0.12) | `3FC477233C8903B7` | `3FC477233C8903B7` |

MIRR now ships the 64-bit Horner mask-84 model by the same decision. The first result
misses by nine ULPs. Its measured error-11 and error-5 rows match. The root uses the exact
reciprocal exponent, expanded to 180 binary places with 160-bit retained square roots before
rounding to 64 bits; using the Double nearest 1/5 changes the six-flow row.
#4883’s ReinvestRate -1, sign domains, single-flow and overflow cases now execute with their
measured results/errors, including the measured negative-rate calls. Negative radicands
remain unmeasured and refused. The loss and 10,001-flow rows use the same authorized closest
model; their patterns appear below. Non-finite inputs and powers beyond 20,000 periods remain
refused. All remaining domains cite #4883.
Better models may replace these; this search is not proof that no matching model exists.

## Additional closest-model rows (#4883)

The standing #2525 exception permits IRR/MIRR to differ in their last printed digits.
The new Double-array rows in [#4883](https://github.com/hexide-io/HexIDE/issues/4883) keep the
unchanged models, with these measured versus computed bits. These are model regression
expectations, not claims that the computed bits are VB6’s measured answers.

| Call (Double-array elements in braces) | Measured in #4883 | Computed |
|---|---|---|
| IRR({0, -100, 110}) | `3FB999999999999C` | `3FB9999999999994` |
| MIRR({-1000, 1, 1}, 0.01, 0.01) | `BFEE90BA55C3468B` | `BFEE90BA55C3468C` |
| MIRR(-1000 then 10,000 ones, 0.01, 0.01) | `3F8400F70D382091` | `3F8400F70D38208B` |

## Verification

`CashFlowIntrinsicTests.Authorized_cash_flow_row` pins the five authorized model results
above and the three measured error numbers. All eight rows fail against main’s execution
refusals. The tests retain the measured bits alongside the model expectations; these are
model regression tests, not claims of exact VB6 fidelity. The NPV rows remain exact.
Unmeasured-case tests pin refusals only, establishing no VB6 value or error number.

The results at 15 significant digits (measured → computed) are:

| Call | Measured | Computed |
|---|---|---|
| IRR(a) | 0.191099160089404 | 0.191099160089215 |
| IRR(a, 0.2) | 0.191099160089389 | 0.191099160089410 |
| IRR(b) | 0.0866309480331796 | 0.0866309480365326 |
| MIRR(b, 0.1, 0.12) | 0.0986691073371503 | 0.0986691073371501 |

The [oracle page](vb6-fidelity-oracle/npv-irr-and-mirr.md) owns the measurements;
this note and the implementation introduce none. NPV is unchanged by this delivery.

The 2026-11-17 implementation check ran all 41 cash-flow tests without skips. All eight
replacement result/error rows failed against main before implementation. The mutation-check
tool verified green → red → green with byte-for-byte restoration: removing IRR’s sum spill
failed three result rows; removing MIRR’s ratio spill failed two result rows. The 21 additional
unmeasured-case tests protect refusals only; they are not counted as regression teeth or
as evidence of VB6 semantics.

Full runtime: 9,359 passed, 21 skipped, zero failures (9,380 total). Runtime integration:
3,227 passed, two skipped, zero failures (3,229 total), after retrying an MSBuild worker failure
with one build worker. Both xUnit reports contain zero hidden errors. MissingLanguageDocTests
passes all 20 cases; its recount moves intrinsic Dies/Partial from 6/73 to 4/75 and overall
62/496 to 60/498, retaining 1,185 names. Tree hygiene passes with nine nonblocking warnings.

The skipped runtime cases provide no coverage here:

- `MenuHierarchySaveTests.RoundTrip_OfVb6sOwnMenuTemplates_ReproducesTheMenuShape`.
- `MenuHierarchySaveTests.Vb6sOwnMenuTemplates_AreNoLongerHeldReadOnly`.
- `Vb6OracleRoundTripTests.HexIDE_output_still_builds_in_vb6`.
- `LocaleCompareTests.StrComp_matches_every_measured_cell_on_Windows`.
- `Vb6TextFileTests.The_one_corpus_file_with_high_bytes_round_trips`.
- `VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text`.
- `VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does`.
- `DateFractionOfASecondTests.Long_date_uses_the_measured_Windows_pattern`.
- `ShellTests.RecordsMeasuredStylesAndDefault`.
- `UiThreadTests.Child_ReportsTheUiThreadsStack`.
- `ContainerHierarchySaveTests.RoundTrip_OfVb6sOwnNestedTemplates_ReproducesTheBeginStructure`.
- `FileSystemTests.Windows_name_rows`.
- `FileSystemTests.Windows_numeric_rows`.
- `FileSystemTests.Windows_attribute_rows`.
- `FileSystemTests.Windows_error_rows`.
- `FileSystemTests.CurDir_uses_only_the_first_drive_character`.
- `WindowsDateLocaleTests.Era_date_output_matches_GetDateFormatEx_and_reads_without_a_wrong_date`.
- `WindowsDateLocaleTests.Date_output_matches_GetDateFormatEx`.
- `ClassHeaderPreservationTests.A_real_vb6_class_round_trips_byte_for_byte`.
- `ClassHeaderPreservationTests.A_renamed_module_retargets_VB_Name_but_keeps_everything_else`.
- `ClassHeaderPreservationTests.Instancing_and_data_binding_settings_survive`.

These require Windows, missing VB6 templates, opt-in VB6 compilation, or the child-only
UI-thread probe. Runtime integration skips `ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent`
and `ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements`: they require
Windows GDI or the oracle’s installed font build.

Full IDE suite: 9,825 passed, 70 skipped, zero failures (9,895 total, 38m30s), with zero
hidden xUnit errors. The skipped cases require Windows/macOS, the missing VB6 template tree,
or opt-in wire stress. They provide no coverage for this delivery.

<details>
<summary>Skipped IDE cases (70)</summary>

- `Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception`.
- `IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves`.
- `Infrastructure.ProjectWriteSafetyTests.A_copy_that_cannot_be_removed_is_kept_and_named`.
- `Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess`.
- `LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches`.
- `LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath`.
- `LspClient.TypeLibraryServicePathTests.GuidOnlyReferenceLoadsWindowsOleAutomationLibrary`.
- `LspClient.WireStressTests.ConcurrentConnectionsKeepEveryFrameValid`.
- `LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile`.
- `LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole`.
- `Processes.ChildProcessJobTests.A_closed_job_refuses_a_process`.
- `Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it`.
- `Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it`.
- `Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead`.
- `Profiles.ProfileContainmentTests.JunctionEscapeIsRefused`.
- `Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused`.
- All 3 cases in `Projects.ClassModuleSaveFidelityTests`.
- `Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce`.
- `Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag`.
- `Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them`.
- `Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step`.
- `Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact`.
- `Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity`.
- `Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write`.
- `Projects.ContributedProjectTests.A_contributed_project_already_open_is_refused_when_opened_again_with_different_drive_and_folder_case`.
- `Projects.DuplicateProjectOpenTests.Opening_the_same_project_with_different_drive_and_folder_case_is_refused`.
- `Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main`.
- `Repack.LinuxRepackerTests.A_linux_build_is_refused_on_windows`.
- `Repack.MacRepackerTests.A_mac_build_is_repacked_as_one`.
- `Repack.MacRepackerTests.macOS_accepts_the_property_list_signature_and_icon`.
- `Repack.MacRepackerTests.A_mac_build_is_refused_on_windows`.
- `Repack.MacRepackerTests.A_signature_macOS_rejects_stops_the_run_and_leaves_nothing`.
- `Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: \"revocations.json\")`.
- `Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: \"nested/HEXIDE-FIRSTPARTY.PUB\")`.
- `Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: \"acme-ide.exe\")`.
- `Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: \"revocations-cache.json\")`.
- `Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: \"LICENSE\")`.
- `Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: \"revocations.sig\")`.
- `Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: \"brand.json\")`.
- `Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: \"revocations-cache.signed\")`.
- `Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: \"hexide-root.pub\")`.
- `Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: \"branding/logo.png\")`.
- `Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: \"HexIDE.exe\")`.
- `Repack.WindowsRepackerTests.Included_protected_files_are_refused_without_output(relative: \"revocations-cache.sig\")`.
- `Repack.WindowsRepackerTests.A_repacked_executable_says_it_is_the_brand_when_Windows_reads_it`.
- `Repack.WindowsRepackerTests.Every_file_but_the_executable_is_exactly_as_published`.
- `Repack.WindowsRepackerTests.The_icon_is_the_brands_with_nothing_left_of_stocks`.
- `Repack.WindowsRepackerTests.Included_files_reach_the_installable_payload_and_report`.
- `Repack.WindowsRepackerTests.A_default_profile_folder_travels_with_the_brand`.
- `Repack.WindowsRepackerTests.Included_output_ancestor_is_refused_before_staging`.
- `Repack.WindowsRepackerTests.A_brand_with_no_copyright_keeps_stocks_rather_than_claiming_none`.
- `Repack.WindowsRepackerTests.Included_missing_folder_is_refused`.
- `Repack.WindowsRepackerTests.The_installer_inputs_are_written_beside_the_build_and_not_inside_it`.
- `Repack.WindowsRepackerTests.A_run_that_fails_partway_leaves_nothing_behind`.
- `Repack.WindowsRepackerTests.A_branded_process_declares_that_identity_to_Windows`.
- `Repack.WindowsRepackerTests.The_brands_files_land_where_the_IDE_will_look_for_them`.
- `Repack.WindowsRepackerTests.Included_links_are_refused_at_the_root_or_inside`.
- `Repack.WindowsRepackerTests.Included_folder_below_a_link_is_allowed`.
- `Repack.WindowsRepackerTests.A_signed_stock_executable_comes_out_unsigned_and_the_run_says_so`.
- `Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet`.
- `Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible`.
- `Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it`.
- `Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it`.
- `Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn`.
- `Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests`.
- `Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot`.
- `Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation`.
- `UserDataPathTests.OnWindowsItIsUnderAppData`.

</details>
