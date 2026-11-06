# Issue 3076: merge validation

Checked on WSL (Linux), 2026-11-04. Completed the already-started merge of
`upstream/main` (`4823d5b7`) into `fix/3076-codex`, merge commit `238a45a7`.
The latest issue comment requested conflict resolution only, with no new behaviour.

## Resolution

Kept both sides of all nine conflicted files:

- `IDE/HexIDE.Core/Lsp/Messages/LspMessages.cs`: both selection-range and range-formatting capabilities.
- `IDE/HexIDE.Lsp/VBLspClient.cs`: advertise both capabilities.
- `IDE/HexIDE/ApplicationCommands.cs`: Expand, Shrink and Format Selection commands.
- `IDE/HexIDE/Forms/ViewModels/CodeEditorViewModel.cs`: both request paths and predicates.
- `IDE/HexIDE/Forms/Views/CodeEditorView.axaml`: all command bindings and context-menu entries.
- `IDE/HexIDE/Forms/Views/CodeEditorView.axaml.cs`: selection-chain reset and code-lens refresh.
- `IDE/HexIDE/Keymaps/KeymapPack.cs`: all command mappings.
- `docs/lsp-client.md`: both features; regenerated the coverage table with
  `scripts/regenerate-lsp-coverage.py` and the cached, hash-verified pinned model.
  Coverage is 43 of 93 messages, including 22 of 41 text-document messages.
- `openspec/specs/lsp-client/spec.md`: both requirements inside Requirements.

No substantive disagreement was found. Main's `CLAUDE.md` was accepted without edits.
The existing `changelog.d/3076.md` remains accurate; a merge adds no release behaviour.
`MISSING_LANGUAGE.md` and its counts are unchanged. No VB6 values were inferred.

## Validation

Windows mount entries were removed from PATH before tests and builds.

- Integration: 4,954 passed, 2 skipped, 0 failed, 4,956 total. xUnit report checked:
  zero errors. All nine `SelectionRangeCommandTests` passed without skips.
- Unit: 5,765 passed, 35 skipped, 0 failed, 5,800 total in 15m 59s; xUnit report
  checked: zero errors. The 5,378-test floor passed.
- Separate selection-range protocol run: 6 passed, 0 skipped, 0 failed.
- Desktop Debug build: passed, zero warnings, zero errors.
- Tree hygiene: passed with nine nonblocking warnings (identity scan unavailable).

The two Integration skips are `ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements`
(Windows Segoe UI measurement) and `ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent`
(Windows GDI). Neither is counted as coverage.

Initial overlapping builds collided on Avalonia's PDB; the unit suite was restarted after
Integration compilation completed. The initial Integration test process failed before executing
because HangDump could not create a named mutex in the sandbox's read-only runtime directory.
The successful invocation used a writable `XDG_RUNTIME_DIR` inside this clone and retained
`--hangdump --hangdump-timeout 10m` plus the 4,599-test floor.

The full unit run completed successfully after a quiet sixteen minutes. A diagnostic full rerun
started during that wait was cancelled (exit 130) once the successful full result was available;
it is not counted as coverage.

No tests or behaviour were added in this merge-only run; no new before-fix teeth claim is made.
Round 2's recorded teeth evidence remains in `3076-round2-validation.md`.
The full suites include every test class in the files modified by the merge; none of the nine
conflicted files is a test file.

## Supervisor live check

With an advertising server, confirm Expand twice climbs its returned ranges, Shrink returns one
level, edits and outside caret moves reset the chain, and the context menu also retains Format
Selection alongside the code-lens display. Without a provider, both selection commands should be
disabled. Live IDE verification remains with the supervising session; no MCP tools were used here.

## Unit skips

These 35 cases did not run and are not counted as coverage. Ten require the VB6 template
tree; the other 25 require Windows filesystem, registry, process, UI or type-library support.

- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them`
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does`
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text`
- `HexIDE.Tests.Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess`
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole`
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile`
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead`
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionEscapeIsRefused`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it`
- `HexIDE.Tests.IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves`
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot`
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests`
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation`
- `HexIDE.Tests.Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header`
- `HexIDE.Tests.Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused`
- `HexIDE.Tests.UserDataPathTests.OnWindowsItIsUnderAppData`
- `HexIDE.Tests.Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main`
- `HexIDE.Tests.LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches`
- `HexIDE.Tests.Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception`
- `HexIDE.Tests.LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath`
- `HexIDE.Tests.Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it`
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it`
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_closed_job_refuses_a_process`
