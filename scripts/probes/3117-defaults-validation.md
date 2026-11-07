# #3117: dialog defaults and opening focus

The latest [issue decision](https://github.com/hexide-io/HexIDE/issues/3117) names startup Open for audit,
records Save Changes Yes, and asks that unrecorded VB6 default choices retain today's behaviour.
Runtime Error's Debug is measured in [the oracle](../../docs/vb6-fidelity-oracle/an-unhandled-run-time-error.md).
No oracle page is copied or modified here. Existing defaults below are retained, not new VB6 measurements.

The shared dialog chrome still covers every IDE desktop dialog and excludes program windows, MsgBox and
InputBox. Native file pickers belong to the OS. An explicit input-focus marker takes priority over the
existing default; otherwise a default receives keyboard focus, then the first text input, then the first
available tab stop. Existing explicit focus choices are preserved; Workspace Artifact Consent’s Deny
now requests keyboard focus.
Disabled and hidden controls cannot receive opening focus.

| Dialog | Default | Opening focus |
|---|---|---|
| Startup / New Project (New, Recent) | None (unchanged; VB6 default unmeasured) | First available tab stop |
| Startup / New Project (Existing) | None (unchanged; VB6 default unmeasured) | File name text box |
| Save Changes | Yes (recorded in #3117) | Yes |
| Find and Replace | Find Next (existing) | Find What text box |
| Add Watch | OK (existing) | Expression text box |
| Components | OK (existing) | OK |
| Options | OK (existing) | OK |
| Recovery | Restore (existing; VB6 counterpart unmeasured) | Restore when enabled, otherwise first available tab stop |
| Runtime Error | Debug (measured) | Debug |
| IDE MessageBox | First action (existing) | First action |
| Server prompt | None: Enter follows focus (existing) | First action; link question keeps No (#3112) |
| Color | OK (existing) | OK |
| Print | OK (existing) | OK |
| References | None (unchanged; VB6 default unmeasured) | First available tab stop |
| Project Properties | None (unchanged; VB6 default unmeasured) | Project Name text box |
| File Conflict | None (unchanged; VB6 counterpart unmeasured) | First available tab stop |
| Add Procedure | None (unchanged; VB6 default unmeasured) | Name text box (existing explicit choice) |
| Language Revert Gate | None (unchanged; no VB6 counterpart recorded) | First available tab stop |
| Add-in Consent | None (unchanged; no VB6 counterpart recorded) | First available tab stop |
| Workspace Artifact Consent | None (unchanged; no VB6 counterpart recorded) | Deny (existing explicit choice) |
| Export Preview | None (unchanged; no VB6 counterpart recorded) | First available tab stop (preview editor) |
| Trust Chain | None (unchanged; no VB6 counterpart recorded) | First available tab stop |
| Menu Editor | None (unchanged; VB6 default unmeasured) | Caption text box |
| About | None (unchanged; VB6 default unmeasured) | First available tab stop |
| Font | None (unchanged; VB6 default unmeasured) | First available tab stop (font list) |
| IDE InputBox | None (unchanged; VB6 default unmeasured) | Input text box (existing explicit choice) |

For defaults marked unmeasured, no new VB6 behaviour is pinned. These are chrome policies rather than
runtime members: no not-implemented member or MISSING_LANGUAGE count change is involved.

## Supervisor live check

In Classic, Dark and Abyss, open the dialogs above and check opening focus against the table. Save Changes
Yes should now have the heavier default border. Startup retains no declared default; its VB6 default is
unmeasured. Find/Replace should type into Find What immediately; Add Watch should type into Expression. A focused button should immediately
show its dotted rectangle. Tab should move it and pointer focus should hide it. Confirm link questions
still start on No, server prompts on their first action, and running programs' windows/MsgBox/InputBox
are unchanged.

## Headless validation

`DialogButtonCueTests` adds 78 policy cases (26 dialog/tab cases × Classic/Dark/Abyss). They leave
`IsDefault` as declared by the view and substitute enabled commands to isolate chrome/focus from command
availability. Each existing default is checked by caption, border thickness and a Skia frame difference
when the default flag is removed. Opening button focus also checks for the dotted adorner. Trust Chain
uses its dialog mode; defaultless views are asserted to remain defaultless.

Before changing product code, **18 failed, 60 passed, 0 skipped**. The failures were Save Changes' missing
Yes default; Find/Replace and Add Watch focusing their defaults rather than their inputs; Project
Properties and startup's Existing tab missing the first-text-input fallback; and Workspace Artifact
Consent's Deny having focus without a keyboard adorner (six policies × three themes). The 60 passing
cases are preservation checks, not new regression teeth.

After the product change, the entire class passed: **152 passed, 0 failed, 0 skipped**, including the
previous 74 cue/host/runtime-exclusion cases.

Full Integration: **5185 passed, 2 skipped, 0 failed (5187 total)** in 6m 31s.
`check-xunit-report.sh` confirms zero errors and the same 5187 tests. The Integration CI floor increases
to 4927 (95% of the measured total). The skips are
`ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent` and
`ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements`; neither is coverage on Linux.

Commands run from the repository root, after dropping `/mnt/` entries from PATH as the brief requires.
Builds use `-m:1 -p:UseSharedCompilation=false`, with `MSBUILDDISABLENODEREUSE=1` and
`UseSharedCompilation=false` inherited by child build fixtures (the earlier slice records why).
Foreign-server downloads are disabled with `HEXIDE_FOREIGN_LSP_DOWNLOAD=0`.
HangDump uses a mode-700 `artifacts/3117-runtime` as `XDG_RUNTIME_DIR`, keeping its runtime files writable
inside this clone. The direct `dotnet test` calls use the explicit CI floors; filtered calls use one.
The full Integration run requests `--report-xunit --hangdump --hangdump-timeout 10m --hangdump-type Mini`.

Full IDE: **6046 passed, 35 skipped, 0 failed (6081 total)** in 17m 57s.
`check-xunit-report.sh` confirms zero errors and the same 6081 tests. The IDE CI floor increases to 5776
(95% of the measured total). No foreign-server test skipped, with downloads disabled. Of the 35 skips,
25 require Windows and 10 require the absent VB6 template corpus; none is coverage from this run.
Exact skipped cases:

- `HexIDE.Tests.Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess`
- `HexIDE.Tests.Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn`
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text`
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched`
- `HexIDE.Tests.Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it`
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_closed_job_refuses_a_process`
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it`
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead`
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionEscapeIsRefused`
- `HexIDE.Tests.LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath`
- `HexIDE.Tests.Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception`
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests`
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot`
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation`
- `HexIDE.Tests.Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible`
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile`
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole`
- `HexIDE.Tests.Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused`
- `HexIDE.Tests.LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches`
- `HexIDE.Tests.UserDataPathTests.OnWindowsItIsUnderAppData`
- `HexIDE.Tests.IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves`

The final Debug desktop build passed with **0 warnings and 0 errors**. Tree hygiene passed with nine
existing non-blocking warnings, and `git diff --check` passed. No language surface or MISSING_LANGUAGE
counts changed. This run's edits remain uncommitted for the supervisor; the pre-existing main merge was
completed as `1e0a34bf`, preserving main's selected prompt action and the branch's keyboard-focus cue.
