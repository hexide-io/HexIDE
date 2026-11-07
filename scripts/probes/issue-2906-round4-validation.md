# Issue #2906 — lens spacer baseline

Round 4, WSL, 2026-11-04. Merged `upstream/main` (`0a164f05`) before investigating the
Windows check's one failure among six lens cases. The brief supplied totals, without the failing
case or assertion. This run reproduced one drawing failure locally using Windows' Courier New font.

## Reproduction and fix

With the original assertion and Linux's normal fallback font, all six cases pass. Making Courier New
available to Fontconfig reproduces a failure in `CodeLensTests.LensInCodeWindow("drawing")`:
the label top is 17.333333333333332 DIPs and the member text top is 17.0828125 DIPs.
The other five original cases pass. This reproduces the reported failure count; a Windows rerun
is still needed to confirm that the automatic check failed for the same reason.

The font came from the installed Windows `cour.ttf`, SHA-256
`6aab1f79264ccda1e7129dba62dbe3750b10eaa27408a327dadb808724e62c84`.
It was copied into an ignored directory in this clone. A private `FONTCONFIG_FILE` included the
system Fontconfig configuration and added that directory; `fc-match 'Courier New'` confirmed the
selection. No font binary is committed and no system configuration was changed.

The spacer's Border had a zero baseline offset. AvaloniaEdit's `InlineObjectRun.Baseline` reads
`TextBlock.GetBaselineOffset`, so the spacer increased descent instead of reserving ascent above
the member. Setting that offset to the spacer height reserves the space above the member text.
The relevant source is `src/AvaloniaEdit/Rendering/InlineObjectRun.cs` at the package's recorded
commit, [`86fdebec`](https://github.com/AvaloniaUI/AvaloniaEdit/blob/86fdebec4cff7affb0e14c7885df71d28edce777/src/AvaloniaEdit/Rendering/InlineObjectRun.cs).

The drawing assertion now requires the **bottom** of the entire lens row to fit above the member,
allowing one device pixel for layout rounding at 150% scaling. This catches the overlap on Linux
without needing the Windows font. With the saved pre-fix `HexIDE.dll` substituted into the updated
test output, that assertion fails: bottom 33.33333333333333 DIPs versus a maximum of
18.220729166666665 DIPs. The other five cases pass. The fixed assembly was restored immediately.
**Teeth count: one regression case.** All six cases pass with the fixed assembly and Courier New.
Temporary LF and CRLF test variants also passed before the fix and were removed as unrelated.

## Checks

Windows mount entries were removed from PATH before every run. The unit suite used
`HEXIDE_FOREIGN_LSP_DOWNLOAD=0`; Integration retained its ten-minute quiet-time hang dump with a private
writable `XDG_RUNTIME_DIR`. Builds disabled shared compilation and MSBuild node reuse.

- Full IDE unit suite: 5,687 passed, 35 skipped, 0 failed (5,722 total), in 17m 08.253s.
  The xUnit report guard records no errors. Skips are 25 Windows-specific cases and 10 cases needing
  the unavailable VB6 Template tree; their names are listed below and are not coverage for this change.
- Full Integration suite with Linux's normal font: 4,903 passed, 2 skipped, 0 failed (4,905 total),
  in 4m 51.870s. The xUnit report guard records no errors. All six lens cases ran and passed.
  The skips are `ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements` (requires the
  oracle's Windows Segoe UI font build) and `Windows_caption_width_follows_installed_fonts_Gdi_extent`
  (requires Windows GDI). Neither skip is coverage for this change.
- Focused Courier New run: all six lens cases passed, none skipped.
- Debug Desktop build: 0 warnings and 0 errors.
- Tree hygiene: passed, with nine existing non-blocking warnings; identity scan skipped because its
  optional pattern is unset. `git diff --check` passed.

## Scope and live verification

The change only corrects visual spacing. Transport, command dispatch and language support are unchanged;
MISSING_LANGUAGE counts do not move. Edit-debounce refetching and the localized Options toggle remain
pending from round 3, outside this failure-repair run.

The supervisor should rerun the Windows lens class, then inspect Greet/Unused in the running IDE at
150% scaling: the whole label must sit above the member with no overlap, line numbers must remain beside
the member text, selection/caret movement must skip the lens, and clicking must still fill the existing
Find References pane without a references request. No live IDE verification was performed in this run.

## Skipped IDE unit cases (not coverage)

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
