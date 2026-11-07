# Issue 3073 validation (2026-11-04)

Resumed the interrupted implementation in `15afbede`, then merged `upstream/main`
(`17d96a10`) in `45fad8e3`. The spec conflict retains both preparation and server-question requirements.

## Regression teeth

`check-teeth.py` temporarily removes preparation requests, routing, capability declaration
and the pre-prompt preparation flow, retaining API stubs so the tests compile. It restores
the source bytes in `finally`. With Windows PATH entries removed and foreign downloads disabled:

| Suite, filtered to preparation | Failed | Passed | Skipped |
| --- | ---: | ---: | ---: |
| HexIDE.Tests | 12 | 4 | 0 |
| HexIDE.Integration.Tests | 1 | 1 | 0 |

The 13 failures cover ranges/placeholders, null/error refusal, default behavior,
client capability declaration, registry aggregation including requests to two real wire
peers after a refusal, and withholding the prompt. The five already-passing guards
cover unsupported capabilities and protected lines; they are not claimed as teeth.

## Build environment

ANTLR's default Java setup failed on its shared setup lock in this sandbox. A temporary,
ignored MSBuild target supplies the existing cached Java executable and ANTLR jar
through `Antlr4` item metadata before `Antlr4Compile`; no dependency or tracked build
configuration changes. `DOTNET_CLI_HOME` points into ignored build artifacts. Builds
use `-m:1 -p:UseSharedCompilation=false`. Integration's hang-dump setup uses a writable
`XDG_RUNTIME_DIR` under ignored artifacts. Initial build attempts were interrupted to
change the sandbox setup and enable diagnostic logging; only completed runs below count.

## Live check for the supervisor

With a server advertising `renameProvider.prepareProvider: true`, invoke Rename on
an allowed name. Confirm preparation precedes the prompt in the protocol inspector,
and the prompt uses the returned placeholder or range text. For null/error replies,
confirm no prompt opens and an error reason is displayed when supplied. A protected
line must send no preparation, and a server without preparation must keep the existing
prompt. With two supporting servers, both must be asked even when the first refuses.

No new UI strings or commands were introduced. No VB6 measurements or language-surface
status changed; `MISSING_LANGUAGE.md` counts are unchanged.

## Completed checks

- Full `HexIDE.Tests`: **5697 passed, 35 skipped, 0 failed; 5732 total**,
  above main's 5378 floor. Duration 16m04s. The xUnit report guard records no
  errors. All 16 preparation cases passed, including all 12 pre-fix failures.
- Full `HexIDE.Integration.Tests`: **4904 passed, 2 skipped, 0 failed; 4906 total**,
  above main's 4599 floor. Duration 4m57s. The xUnit report guard records no
  errors. Both preparation cases passed, including the pre-fix failure.
- Desktop Debug build: **0 warnings, 0 errors**.
- `scripts/check-tree-hygiene.sh`: passed (9 nonblocking existing warnings).
- `git diff --check`: passed.

### IDE skips (no coverage claimed)

- `HexIDE.Tests.IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_closed_job_refuses_a_process`: Job objects exist only on Windows.
- `HexIDE.Tests.Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it`: Job objects exist only on Windows.
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it`: Job objects exist only on Windows.
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole`: Type libraries are read only on Windows.
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile`: Type libraries are read only on Windows.
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionEscapeIsRefused`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath`: A link to a share is a Windows path.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched`: No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main`: A made program is started on Windows only: elsewhere it needs a desktop session.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.UserDataPathTests.OnWindowsItIsUnderAppData`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot`: Windows-only: this pins behaviour the product deliberately varies by host filesystem.

### Integration skips (no coverage claimed)

- `HexIDE.Integration.Tests.Views.ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent`: Requires Windows GDI
- `HexIDE.Integration.Tests.Views.ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements`: Requires the oracle's Windows Segoe UI font build
