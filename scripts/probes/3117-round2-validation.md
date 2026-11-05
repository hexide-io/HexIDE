# #3117 round 2: shared dialog chrome slice

Follow-up: [the default/focus audit](3117-defaults-validation.md) records the completion run and
remaining unmeasured choices. The results below describe the earlier shared-host slice.

This slice replaces the button-style view allowlist with the shared desktop dialog host's `ideDialog`
class. `DialogWindow` applies the rule for every `IDialog`; `WindowManager.MakeDialogWindow` applies it
for the IDE's direct managed dialog controls. Program MsgBox and InputBox remain excluded. Server prompts
keep their existing class and first-action focus.

The rule reaches Save Changes, Find/Replace, Add Watch, Components, Recovery, Runtime Error, References,
Project Properties, File Conflict, Add Procedure, Language Revert Gate, Add-in Consent, Workspace Artifact
Consent, Export Preview, Trust Chain, Menu Editor, Options, New Project, About, Font, Color, Print,
InputBox and IDE MessageBox, plus server prompts. Native OS file pickers are outside Avalonia styling.

The host gives keyboard focus to an existing enabled, visible default button when no control already has
focus, otherwise the first available tab stop. It preserves explicit opening-focus choices. There is no
change to the default-button assignments in this slice: Find Next, Add Watch OK, Components OK, Recovery
Restore, Runtime Error Debug, Options OK, Color OK, Print OK and IDE MessageBox's first action retain
their existing defaults.

## Remaining

The per-dialog default-button audit from the latest issue comment remains, including New Project Open
and Save Changes. No unrecorded VB6 default choice is asserted here. Opening focus of dialogs with such
missing defaults and no explicit focus currently goes to the first available control. The next slice must
also reconcile explicit focus choices with the requested opening-focus policy. No default is newly assigned to Save Changes, New Project, References, Project
Properties, File Conflict, Add Procedure, Language Revert Gate, Add-in Consent, Workspace Artifact
Consent, Export Preview, Trust Chain, Menu Editor, About, Font or InputBox. Some may have no natural
default; that audit is still open, rather than a claim about VB6. The SingleView MDI host receives the same
cue class; its opening-focus policy is unchanged.

## Supervisor live check

In Classic, Dark and Abyss, open Save Changes and the other newly reached dialogs above. Tab to each
button: its dotted rectangle should be readable, inside the button and unclipped. Existing default buttons
should have the heavier border on opening. Click a button with the mouse: no dotted rectangle. Check
Options, startup, server prompts and IDE message boxes still behave as in round 1, and that a running VB6
program's windows, MsgBox and InputBox are unchanged. Opening focus must not displace an explicitly chosen
focus target.

## Headless validation

The final `DialogButtonCueTests` matrix has 74 cases: 63 rendering cases (21 newly reached views × three
themes), two opening-focus cases, one window-type coverage guard, two factory-origin cases, and the six
round-1 rendering cases. Rendering cases enable one declared button with a mock command and assign it a
default for the purpose of testing host styling, independently of the view model/default-button audit.
They compare pointer/keyboard frames, check the focus rectangle uses the theme brush, and compare the
frame again after removing the default cue.

With this run's product changes reverted, **67 failed, 7 passed, 0 skipped**. All 63 rendering failures
were the expected missing heavier border; the two focus cases, type guard and IDE factory case failed as
well. The program-origin factory case already passed and is preservation coverage, not a new regression.
After restoring the product change, **74 passed, 0 failed, 0 skipped**.

Commands use the Windows-free PATH required by the brief. Builds use `-m:1 -p:UseSharedCompilation=false`
and `MSBUILDDISABLENODEREUSE=1` after a parallel build's MSBuild workers exited before tests began. The
PowerShell wrapper split `-m:1` into two arguments here, so these runs use `dotnet test` directly with the
same explicit CI test floors (1 for the filtered run, 4708 for full Integration, 5585 for full IDE).

Full Integration: **5097 passed, 2 skipped, 0 failed (5099 total)**. The xUnit report guard found no errors.
The skips are `ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent` (Windows
GDI) and `ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements` (the oracle's Windows
font build); neither is coverage from this Linux run. The Integration CI floor is raised to 4844,
95% of the measured total.

The first full Integration attempt stopped before tests: HangDump's named mutex tried to create a
`.dotnet` directory in the sandbox's read-only runtime directory. Setting `XDG_RUNTIME_DIR` to a private
mode-700 `artifacts/3117-runtime` directory allowed the same run, hang timeout included, to pass. This is
only a test-process environment setting; nothing outside the clone was changed.

The requested Debug desktop build passed with **0 warnings and 0 errors**. Tree hygiene passed with nine
existing, non-blocking warnings; `git diff --check` passed. No language surface changed, so
`MISSING_LANGUAGE.md` and its counts are unchanged.

For the IDE suite, `UseSharedCompilation=false` must also be an **environment variable**, so child
`dotnet publish` fixtures inherit it. Passing only the outer MSBuild property left
`BundledServerBuildTests.DependencyFollowsReferencedOutputForBuildAndPublish(artifactsLayout: true)`
failing with CS2012/read-only-filesystem while writing its generated DLL. With the inherited setting,
both build-fixture cases passed (2 passed, no skips). The installed clangd's shutdown test timed out in
the first full attempt, then passed in isolation (1 passed, no skips). That already-failed full attempt
was stopped and the entire IDE suite restarted with the corrected environment and `--long-running 120`.

Full IDE retry: **6028 passed, 35 skipped, 0 failed (6063 total)**, in 19m 33s. The xUnit report guard
found no errors. The IDE CI floor is raised to 5759, 95% of the measured total. Foreign-server downloads
were disabled; no foreign-server test skipped. Of the 35 skips, 10 require the absent VB6 template
corpus and 25 require Windows behaviour. None is coverage from this run. Exact skipped cases:

- `HexIDE.Tests.UserDataPathTests.OnWindowsItIsUnderAppData`
- `HexIDE.Tests.Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step`
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile`
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole`
- `HexIDE.Tests.IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves`
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead`
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionEscapeIsRefused`
- `HexIDE.Tests.Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess`
- `HexIDE.Tests.Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused`
- `HexIDE.Tests.LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches`
- `HexIDE.Tests.LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath`
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests`
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot`
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation`
- `HexIDE.Tests.Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible`
- `HexIDE.Tests.Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it`
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_closed_job_refuses_a_process`
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched`
- `HexIDE.Tests.Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main`
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text`
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does`
