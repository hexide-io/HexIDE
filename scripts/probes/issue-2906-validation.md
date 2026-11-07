# Issue #2906 — viewport scheduling and refresh slice

Validated on Linux, 2026-11-04. This is a partial implementation of round 2. No editor instantiates
`CodeLensSession` yet, so no lens is drawn by this slice.

## Implemented

- A document lens session discovers lenses, resolves only starts in its supplied zero-based viewport,
  cancels work leaving view, retains complete lenses, and rejects superseded discovery and late resolutions.
- Workspace refresh capability and parameterless handler land together. The handler answers null and
  raises an invalidation event; the registry preserves the originating connection. Active lens sessions
  refetch and unsubscribe on disposal.
- The coverage table was regenerated from the pinned metaModel using the new regeneration script.

## Validation

All commands dropped `/mnt/` PATH entries first and set `HEXIDE_FOREIGN_LSP_DOWNLOAD=0` for tests.

- New session tests: **7 failed** against an inert API scaffold before implementation, **7 passed** after.
  The class did not exist on the base branch; the scaffold let assertions run rather than just fail compilation.
- New refresh wire/registry tests: **3 failed** with the old transport/registry, **3 passed** after.
- Full `cd IDE && dotnet test HexIDE.Tests/`: **5682 passed, 35 skipped, 0 failed** (5717 total).
- Full `cd IDE && dotnet test HexIDE.Integration.Tests/`: **4881 passed, 2 skipped, 0 failed** (4883 total).
  `check-xunit-report.sh` confirmed 4883 tests and no errors.
- Debug Desktop build: **0 warnings, 0 errors**.
- Tree hygiene: passed, 9 pre-existing warnings; identity scan unavailable without its environment variable.
- `git diff --check`: passed.

The initial full-suite build lost an MSBuild worker during restore, without a diagnostic file. The successful
retry used `DOTNET_CLI_USE_MSBUILD_SERVER=0`, `-m:1 -nodeReuse:false -p:UseSharedCompilation=false`.
Integration's first host crashed before tests because the hang-dump extension could not create its named
mutex in read-only `/run/user/1000`. The successful retry set `XDG_RUNTIME_DIR` to a private, mode-700
`artifacts/runtime-2906` directory. It retained `--hangdump --hangdump-timeout 10m --hangdump-type Mini`
and `--report-xunit --report-xunit-filename issue-2906.xml --results-directory TestResults`.

## Skips — not coverage

The 35 unit-suite skips are Windows-only cases or require the absent VB6 Template tree. Exact cases:

- `HexIDE.Tests.Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn`
- `HexIDE.Tests.IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves`
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does`
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text`
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_closed_job_refuses_a_process`
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it`
- `HexIDE.Tests.Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it`
- `HexIDE.Tests.Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception`
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile`
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole`
- `HexIDE.Tests.Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess`
- `HexIDE.Tests.UserDataPathTests.OnWindowsItIsUnderAppData`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it`
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible`
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionEscapeIsRefused`
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead`
- `HexIDE.Tests.LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step`
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write`
- `HexIDE.Tests.Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused`
- `HexIDE.Tests.Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main`
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation`
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests`
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical`
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header`
- `HexIDE.Tests.LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath`

The two Integration skips require Windows fonts/GDI:

- `ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements`
- `ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent`

None of the ten new tests skipped. No VB6 runtime behavior changed; MISSING_LANGUAGE counts are unchanged.
MISSING_FEATURES still marks code lenses Partial, with updated notes.

## Remaining

- Instantiate the session in editors and supply actual visible lines, including scroll updates.
- Draw smaller, dimmed lens lines above member lines, outside document text, selection, caret and line numbering.
- Route server commands, fill the existing Find References pane from supplied locations, and log unknown commands once.
- Request fresh lenses on the diagnostics edit debounce and handle editor open/close/rename/reconnect lifecycle.
- Add the default-on Options setting and localized toggle in every shipped pack.
- Add drawing, command and toggle tests with before/after failure evidence.
- Supervisor live check: RDCore reference counts, text selection/caret/line numbers, supplied-reference clicks
  without a new references request, updates after edits, scrolling, toggle-off silence and bundled-server silence.

The remaining UI work is deferred to keep this run a tested scheduling/refresh slice. No new fidelity
measurements were made or guessed. Merge commit `70f2ba46` completed the pre-existing merge from main,
retaining both sides of the two conflicts; this run's implementation changes remain uncommitted.

## Files changed in this slice

- `IDE/HexIDE.Core/Lsp/CodeLensSession.cs`
- `IDE/HexIDE.Core/Lsp/ILspClient.cs`
- `IDE/HexIDE.Core/Lsp/Messages/LspMessages.cs`
- `IDE/HexIDE.Lsp/LspClientRegistry.cs`
- `IDE/HexIDE.Lsp/VBLspClient.cs`
- `IDE/HexIDE.Tests/LspClient/CodeLensAndCommandTests.cs`
- `IDE/HexIDE.Tests/LspClient/CodeLensSessionTests.cs`
- `IDE/HexIDE.Tests/LspClient/LspClientRegistryTests.cs`
- `changelog.d/2906.md`
- `docs/MISSING_FEATURES.md`
- `docs/lsp-client.md`
- `openspec/specs/lsp-client/spec.md`
- `scripts/probes/issue-2906-validation.md`
- `scripts/regenerate-lsp-coverage.py`
