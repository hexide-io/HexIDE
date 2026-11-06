# #3114 round 2 validation

Merged `upstream/main` first, retaining the higher test-count floors in the workflow conflict.
The regression run used the real `MainViewViewModel.DockFactory` in `DockFactoryTests`.

Seven cases failed against the pre-round-2 product code:

- `ResultsHeightSurvivesSavedLayout(true, 380, false, false)` and
  `(true, 380, true, false)`: a sibling's visible height was 0 DIP, below the fallback floor.
  The latter also restores a saved Locals proportion of zero.
- `ResultsHeightSurvivesSavedLayout(true, 649, false, true)`: with a larger-font Locals view,
  its measured title/headers plus two rows required 166.67 DIP; the dock minimum was unset.
- `CollapsedToolLoadsWithFloor`: the restored tool minimum was unset.
- `ShowingCollapsedLocalsRestoresItsFloor`: View > Locals left its proportion at zero.
- `CollapsedDocumentsAreNotSaved(false, true)` and `(true, true)`: both synchronous and
  debounced saves replaced the last valid layout despite a zero-height tool.

The larger-font case was then strengthened with an expanded Locals root. Before the header-only
measurement correction, its subtree measured 359.33 DIP and the helper incorrectly assigned an
820-DIP floor. The strengthened assertion failed, proving that descendants must not count as one row.

The unchanged ordinary 649-DIP fixture passes before the fix; it is regression coverage, not a
claimed failing reproduction. These factory height fixtures run at 150% scaling; the existing generic height matrix covers 100% and 150%. The fallback floor is an
IDE layout policy, not a VB6 runtime measurement.

Live review still needs the actual IDE at 1200×380 and 150%, and the maximized #3094 saved layout:
check complete sibling title strips, rows when measurable, fewer than six results rows when needed,
recovery from a profile containing zero-height tools, View > Locals/Call Stack/Watches, and relaunch.

## Completed checks

- Factory tests: 56 passed, no skips.
- Generic height matrix: 9 passed, no skips.
- Desktop Debug build: 0 warnings and 0 errors.
- Tree hygiene and `git diff --check`: passed (nine non-blocking hygiene advisories).
- Full Integration suite: 5021 passed, 2 skipped, 5023 total; test floor passed.
  `check-xunit-report.sh` confirms all 5023 are present with no xUnit errors.
  Skips are `ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent`
  (Windows GDI) and `ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements`
  (the Windows oracle's Segoe UI font). Neither is claimed as coverage here.

The Integration hang-dump helper required a writable `XDG_RUNTIME_DIR`; this run used
`artifacts/3114-runtime` inside the checkout. Both suites drop `/mnt/` PATH entries and set
`HEXIDE_FOREIGN_LSP_DOWNLOAD=0`.

- Full unit suite: 5975 passed, 35 skipped, 6010 total; test floor passed.
  `check-xunit-report.sh` confirms all 6010 are present with no xUnit errors.
- CI floors raised to 5710 unit and 4772 Integration (95% of these totals).
- `MISSING_LANGUAGE.md` counts are unchanged; this change affects IDE layout only.

The unit skips comprise 25 Windows-only cases and 10 cases requiring an external VB6 Template tree.
They are not claimed as coverage. Exact skipped unit cases:

- `Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet` — Windows only.
- `Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible` — Windows only.
- `Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it` — Windows only.
- `Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it` — Windows only.
- `Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does` — Windows only.
- `Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text` — Windows only.
- `IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves` — Windows only.
- `LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches` — Windows only.
- `Processes.ChildProcessJobTests.A_closed_job_refuses_a_process` — Windows only.
- `Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it` — Windows only.
- `Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it` — Windows only.
- `Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write` — VB6 Template tree absent.
- `Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce` — VB6 Template tree absent.
- `Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag` — VB6 Template tree absent.
- `Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact` — VB6 Template tree absent.
- `Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step` — VB6 Template tree absent.
- `Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity` — VB6 Template tree absent.
- `Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them` — VB6 Template tree absent.
- `LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole` — Windows only.
- `LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile` — Windows only.
- `Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation` — Windows only.
- `Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests` — Windows only.
- `Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot` — Windows only.
- `UserDataPathTests.OnWindowsItIsUnderAppData` — Windows only.
- `Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main` — Windows only.
- `Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header` — VB6 Template tree absent.
- `Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical` — VB6 Template tree absent.
- `Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched` — VB6 Template tree absent.
- `LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath` — Windows only.
- `Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead` — Windows only.
- `Profiles.ProfileContainmentTests.JunctionEscapeIsRefused` — Windows only.
- `Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception` — Windows only.
- `Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess` — Windows only.
- `Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn` — Windows only.
- `Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused` — Windows only.

## Changed files

- `.github/workflows/build.yml`
- `IDE/HexIDE/MainView.axaml.cs`
- `IDE/HexIDE/MainViewViewModel.cs`
- `IDE/HexIDE/Tools/ResultsToolHeight.cs`
- `IDE/HexIDE/Tools/ToolPaneHeight.cs`
- `IDE/HexIDE.Tests/ViewModels/DockFactoryTests.cs`
- `IDE/HexIDE.Integration.Tests/Views/FindReferencesHeightTests.cs`
- `openspec/specs/lsp-client/spec.md`
- `docs/MISSING_FEATURES.md`
- `changelog.d/3114.md`
- `scripts/probes/3114-round2-validation.md`
