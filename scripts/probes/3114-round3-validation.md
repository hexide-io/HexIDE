# #3114 round 3 validation

The supervisor narrowed #3114 after the running IDE hit an unhandled "Infinite layout loop detected"
exception in five of five live runs. Item 3 (borrowing from sibling tool panes) is dropped. This run
removes that borrowing, `ToolPaneHeight`, layout-time floor measurement, collapsed-tool save/load
safeguards and View-menu restoration. It keeps the menu access keys and the shared results target,
taking only spare document height above 120 DIP. Short windows accept fewer rows.

The clone arrived mid-merge from `upstream/main`. The localization conflicts retained both the
Call Hierarchy labels and main's external-URI prompt key; merge commit `afdfacfa` completes that merge.
This run's own changes remain uncommitted for supervisor review.

## Regression checks

- Focused height and render classes: **18 passed, 0 skipped**.
- Before-round-3 replay: **8 failed, 10 passed, 0 skipped**, using the four product files from
  `fe3331c8`. `3114-round3-teeth.py` reproduces the replay and restores the working files in `finally`.
- Six height cases fail because round 2 reduces sibling proportions: Find References and Call
  Hierarchy at 380 DIP, each with default and 18-DIP fonts, plus Call Hierarchy at 700 and 600 DIP
  with an 18-DIP font.
- Two real-factory render cases fail because round 2 performs two results-proportion writes per
  show in the saved #3094 arrangement (Find References and Call Hierarchy). The new code performs
  at most one. The render test also covers opening after reset, including an empty layout before
  results arrive. It runs twelve layout/render passes and asserts no exception.
- The live "Infinite layout loop" exception itself did **not** reproduce in this Linux headless
  replay. The guard has teeth through its repeated-adjustment assertion; Windows live verification
  remains necessary. Dock's own arrange-time proportion updates are excluded from that counter by
  inspecting the setter's call stack.
- The previous one-physical-pixel sibling tolerance is retained. The new calculation preserves the
  document/results pair's combined share, rather than reconstructing it from rounded pixel heights.
  The sufficiently-tall fixture now starts with enough document space for the 120 DIP minimum;
  its exact unchanged-results-proportion assertion remains intact.
- The full run with a 40-DIP explicit document minimum exposed proportional rounding in the
  600-DIP, 18-DIP-font Call Hierarchy case: documents ended at 119.33 DIP. The helper now reserves
  one device pixel when exhausting document space, keeping the 120 DIP assertion strict. This
  case failed before that rounding correction; the main eight-case replay count is separate.

The real-factory 649-DIP diagnostic measured a 649.33-DIP panel, four 4-DIP splitters,
a 234-DIP document pane, a 70-DIP results slot, a 16-DIP row and a 21.33-DIP initial viewport.
After a transfer the viewport was still only 95.33 DIP rather than 96: chrome had grown by two
device pixels. Background scheduling alone did not change that result. The target therefore allows
three device pixels for rounding across the panel, tool chrome and list viewport. The document floor
has its own one-pixel reserve. Available panel height excludes splitters, and the document/results
pair keeps its combined proportion. The transfer remains one posted operation per show.


## Live check for the supervisor

In the #3094 saved arrangement, maximized and at 1200×380 with 150% scaling, open Find References and
Call Hierarchy, wait for results, and repeat after Window > Reset Window Layout. Confirm that the IDE
stays running through render passes, documents retain 120 DIP, sibling tools are not squeezed, and
results accept fewer than six rows when space runs out. Bring each results pane forward after a user
resize, then relaunch and check the saved proportions. Retain the already-passed menu access keys,
including `eu`, `nl` and `ro`.

Issue note for the supervisor to post (this run does not use GitHub tools): #3114 item 3 was dropped
because sibling borrowing and its layout-time floor machinery caused repeatable live layout-loop
crashes. Results now take only spare document height and accept fewer rows in short windows.

No VB6 runtime result or oracle measurement changes. `MISSING_LANGUAGE.md` rows and counts are unchanged.

## Completed checks

- `DockFactoryTests` and `NavigationAccessKeyTests`: 293 passed, 0 skipped.
- Final `FindReferencesHeightTests` and `ResultsLayoutRenderTests`: 18 passed, 0 skipped.
- Full unit suite: 6102 passed, 35 skipped, 6137 total; floor 5710 passed.
  `check-xunit-report.sh` confirms all 6137 cases and no xUnit errors.
- Full Integration: 5046 passed, 2 skipped, 5048 total; floor 4772 passed.
  `check-xunit-report.sh` confirms all 5048 cases and no xUnit errors.
- Debug Desktop build: 0 warnings, 0 errors.
- Tree hygiene: passed, with 9 existing non-blocking advisories. `git diff --check`: passed.

The Integration skips are Windows font checks, not coverage here:

- `ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent`.
- `ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements`.

Windows PATH entries were removed and `HEXIDE_FOREIGN_LSP_DOWNLOAD=0` was set for all runs.
The cached JRE was on PATH; NuGet's HTTP cache and `XDG_RUNTIME_DIR` used writable scratch directories.
Shared compilation and MSBuild node reuse were disabled. An initial parallel unit build lost an
MSBuild child node before running tests, with no diagnostic file left behind; the one-worker retry
completed. The initial complete Integration run caught the document-rounding failure described above;
the final complete run is green.

The CI floors are raised to 5830 unit and 4796 Integration, approximately 95% of these measured totals.
`MISSING_LANGUAGE.md` counts do not move.

## Files changed in this round

- `.github/workflows/build.yml`
- `IDE/HexIDE/MainView.axaml.cs`
- `IDE/HexIDE/MainViewViewModel.cs`
- `IDE/HexIDE/Tools/ResultsToolHeight.cs`
- `IDE/HexIDE/Tools/ToolPaneHeight.cs` (deleted)
- `IDE/HexIDE.Tests/ViewModels/DockFactoryTests.cs`
- `IDE/HexIDE.Integration.Tests/Views/FindReferencesHeightTests.cs`
- `IDE/HexIDE.Integration.Tests/Views/ResultsLayoutRenderTests.cs`
- `openspec/specs/lsp-client/spec.md`
- `docs/MISSING_FEATURES.md`
- `changelog.d/3114.md`
- `scripts/probes/3114-round3-teeth.py`
- `scripts/probes/3114-round3-validation.md`

## Skipped unit cases

These 35 cases are not coverage here: Windows-only behavior and the external VB6 Template tree.

- `HexIDE.Tests.Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does`.
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text`.
- `HexIDE.Tests.LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath`.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header`.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical`.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched`.
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole`.
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile`.
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_closed_job_refuses_a_process`.
- `HexIDE.Tests.Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it`.
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it`.
- `HexIDE.Tests.UserDataPathTests.OnWindowsItIsUnderAppData`.
- `HexIDE.Tests.IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves`.
- `HexIDE.Tests.Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception`.
- `HexIDE.Tests.LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches`.
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead`.
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionEscapeIsRefused`.
- `HexIDE.Tests.Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused`.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet`.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible`.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it`.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it`.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation`.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests`.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot`.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write`.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce`.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag`.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact`.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step`.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity`.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them`.
- `HexIDE.Tests.Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess`.
- `HexIDE.Tests.Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn`.
- `HexIDE.Tests.Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main`.
