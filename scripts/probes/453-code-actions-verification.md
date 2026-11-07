# Issue 453: code-action relay verification

Measured under WSL on 2026-11-04, after merging `upstream/main` into the interrupted round-4 branch.

## Results

| Check | Passed | Failed | Skipped | xUnit errors |
|---|---:|---:|---:|---:|
| Full HexIDE.Tests (floor 5378) | 5700 | 1 | 35 | 0 |
| Full HexIDE.Integration.Tests (floor 4599) | 4910 | 0 | 2 | 0 |
| Final CodeActionTests slice | 6 | 0 | 0 | — |
| BundledServerBuildTests retry with shared compilation disabled in the environment | 2 | 0 | 0 | — |

The full unit run was **not green**. Its sole failure was
`BundledServerBuildTests.DependencyFollowsReferencedOutputForBuildAndPublish(artifactsLayout: True)`:
the nested publish received C# error CS2012, a read-only-filesystem error writing its fixture assembly.
The parent build's `-p:UseSharedCompilation=false` does not set that property for the fixture's child
process. Both fixture cases passed on retry with `UseSharedCompilation=false` and
`MSBUILDDISABLENODEREUSE=1` exported in the environment. No product or test code was changed for that retry.
The full unit run was not repeated after this environment correction.

The final Desktop Debug build passed with **zero warnings and zero errors**.
`check-tree-hygiene.sh` passed with nine existing nonblocking warnings; the identity scan was not
configured. Both full-suite XML reports passed `check-xunit-report.sh`.

## Regression teeth

**14 tests failed before implementation and pass after it:** six `CodeActionTests` protocol/routing
cases against the pre-feature client assembly, and eight headless `QuickFixTests` against the unfinished
view/model slice with temporary no-op baseline extensions. Those extensions have been deleted.
The final full reports record all six protocol tests and all eight UI tests as passing, without skips.
The protocol tests inspect requests received by a separate JSON-RPC peer, including diagnostic data's
JSON spelling, initialization capabilities, resolution payloads and executed commands.

## Running the checks in this sandbox

Drop Windows entries from PATH before invoking dotnet. The existing output files and shared caches were
read-only in this sandbox, so builds used fresh `artifacts/453-build` and `artifacts/453-ui-build`
output trees. The unchanged generated grammars from this clone were reused; the merge changed no `.g4`
files. Set a writable `XDG_RUNTIME_DIR` inside the clone for the hang-dump helper's mutex. NuGet's HTTP
cache can likewise be redirected inside the clone for restore.

The build/test commands used `--artifacts-path`, `-m:1`, `-nr:false`, `-p:UseSharedCompilation=false`
and `-p:NuGetAudit=false`; the final runs used `--no-restore`. Set `HEXIDE_FOREIGN_LSP_DOWNLOAD=0`.
For nested fixture builds, also export `UseSharedCompilation=false` and `MSBUILDDISABLENODEREUSE=1`.

The full unit run included `--minimum-expected-tests 5378 --long-running 120 --diagnostic --report-xunit`.
Integration included `--minimum-expected-tests 4599 --report-xunit --hangdump --hangdump-timeout 10m
--hangdump-type Mini`. The coverage table was regenerated with
`scripts/probes/2908-regenerate-lsp-coverage.py` and the already-cached pinned metamodel.

## Skipped tests: not coverage

### HexIDE.Tests

- `HexIDE.Tests.IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath`: A link to a share is a Windows path.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.UserDataPathTests.OnWindowsItIsUnderAppData`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact`: VB6 template tree unavailable.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write`: VB6 template tree unavailable.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity`: VB6 template tree unavailable.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce`: VB6 template tree unavailable.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them`: VB6 template tree unavailable.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step`: VB6 template tree unavailable.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag`: VB6 template tree unavailable.
- `HexIDE.Tests.Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched`: VB6 template tree unavailable.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical`: VB6 template tree unavailable.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header`: VB6 template tree unavailable.
- `HexIDE.Tests.Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main`: A made program is started on Windows only: elsewhere it needs a desktop session.
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_closed_job_refuses_a_process`: Job objects exist only on Windows.
- `HexIDE.Tests.Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it`: Job objects exist only on Windows.
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it`: Job objects exist only on Windows.
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionEscapeIsRefused`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile`: Type libraries are read only on Windows.
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole`: Type libraries are read only on Windows.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.

### HexIDE.Integration.Tests

- `HexIDE.Integration.Tests.Views.ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements`: Requires the oracle's Windows Segoe UI font build
- `HexIDE.Integration.Tests.Views.ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent`: Requires Windows GDI

## Supervisor live check and remaining scope

Live verification remains with the supervisor: place the caret on a diagnostic, open Quick Fixes,
confirm one request and server groups, and check that disabled reasons are visible and unpickable.
Select a lazy edit-and-command action; inspect resolve, the edit, then executeCommand in that order.
Undo should restore each changed module in one step. Verify protected or changed targets refuse with
localized wording. The initialization frame should contain both code-action capabilities and
`publishDiagnostics.dataSupport`.

The optional keymap route was deferred; no command or default binding was added. Server-initiated
`workspace/applyEdit` remains the separate issue's scope. The bundled server still computes no fixes.
No VB6 behavior was newly pinned or guessed; MISSING_LANGUAGE counts are unchanged.
