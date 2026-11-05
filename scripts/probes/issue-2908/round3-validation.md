# #2908 round 3: command edits and external documents

Merged the already-resolved merge of `upstream/main` (`4b89ce2e`) as `019b5330` before implementation.
Main includes #3078 and #3093; the branch retains #3095's focus change.

## Scope

`workspace/applyEdit` now declares support and uses the workspace rename applier, including the
requesting connection's sent text/version snapshots. Authorization belongs to the one user-command
sender, per connection. The raw RPC task determines whether a command has answered, independently of
when the UI resumes. Internal project-import commands do not authorize writes.

External `showDocument` requests use the same `ServerMessagePrompt` instance and queue as server
questions. Every request shows the whole URI, with localized Yes/No actions. Only Yes invokes the
substitutable system launcher, and its success determines the response.

No VB6 runtime semantics changed or were inferred. `MISSING_LANGUAGE.md` counts are unchanged.
The inspected main has the code lens user path, but no Quick Fix command sender in the editor yet;
the shared sender is ready for that path without another send or authorization mechanism.

## Reproducing the teeth check

Build both test projects with the fix, then run:

```sh
python3 scripts/probes/issue-2908/check-round3-teeth.py
```

The probe restores the pre-round-3 dispatch behaviour while retaining constructor/DTO test seams:
`applyEdit` is neither advertised nor dispatched, and external `showDocument` returns false before
navigation. It builds only the LSP assembly, substitutes it into the two existing test outputs, runs
the focused tests, and restores all modified source and binaries byte-for-byte in `finally`.
Temporary logs are in `artifacts/2908-teeth/`; the measured results belong below.

## Supervisor live check

- Through the scripted server, run a lens command that requests edits before answering. Check the edit
  in two modules, with one independent undo step in each.
- Request the same edit with no command running and after the command's answer. Check unchanged buffers
  and `applied: false` with a readable `failureReason` in the server's log.
- Ask to open an external URI. Check the server's display name, full URI, localized Yes/No buttons,
  and that No launches nothing. Ask twice to verify consent is not remembered; Yes launches once.
- Send the external request while a program runs or another modal is open. Check it waits in the shared
  server-question queue and diagnostics continue to arrive.
- Retain #3095: a fresh server question focuses its first action, Enter chooses it, and Escape dismisses.

The supervising session owns this running-IDE check. No MCP tools or compiler binaries were inspected.

## Measured focused results (WSL, 2026-11-04)

- Fixed wire/unit slice: 14 passed, 0 failed, 0 skipped.
- Fixed headless slice (`ExternalServerDocumentTests` and `ServerMessagePromptTests`):
  13 passed, 0 failed, 0 skipped, including #3095's Enter-focus regression.
- Restored pre-round-3 receiver behaviour: wire/unit slice 11 failed, 3 passed, 0 skipped;
  headless external slice 2 failed, 0 passed, 0 skipped. **13 cases have teeth.**
- OpenSpec normal validation passes. Strict mode reports the existing spec's long-requirement warnings;
  the requirements added or rewritten here are each below that length limit.

## Complete integration and build checks

- `HexIDE.Integration.Tests`: **4,978 passed, 2 skipped, 0 failed**, 4,980 total, in 5m 25s.
  The 4,599 CI floor passed. `check-xunit-report.sh` confirms no xUnit errors or missing tests.
- The skipped tests are `ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements`
  (requires the oracle's Windows Segoe UI font build) and
  `ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent` (requires Windows GDI).
  They are not coverage for this run. All new tests ran on Linux.
- Final Debug desktop build: **0 warnings, 0 errors**.
- Tree hygiene: passed, with nine existing advisory warnings; identity scan skipped because its
  environment pattern is unset. `git diff --check` passed and there are no unmerged index entries.

The first full Integration attempt crashed before tests: the hang monitor's named mutex tried to create
its state under the sandbox's read-only default runtime directory. That attempt provides no coverage.
The successful full run set `XDG_RUNTIME_DIR` to a private writable directory under `artifacts/` in this
clone and retained `--hangdump --hangdump-timeout 10m --hangdump-type Mini`.

## Changed files

- `IDE/HexIDE.Core/Lsp/Messages/LspMessages.cs`
- `IDE/HexIDE.Integration.Tests/Views/ExternalServerDocumentTests.cs`
- `IDE/HexIDE.Lsp/LanguageServerRegistrationFactory.cs`
- `IDE/HexIDE.Lsp/Messages/LspJsonContext.cs`
- `IDE/HexIDE.Lsp/VBLspClient.cs`
- `IDE/HexIDE.Tests/LspClient/ShowDocumentWireTests.cs`
- `IDE/HexIDE.Tests/ViewModels/ServerApplyEditTests.cs`
- `IDE/HexIDE/DISetup.cs`
- `IDE/HexIDE/Forms/ViewModels/CodeEditorViewModel.cs`
- `IDE/HexIDE/IDE/EditorService.cs`
- `IDE/HexIDE/IDE/IEditorService.cs`
- `IDE/HexIDE/IDE/ServerDocumentNavigation.cs`
- `changelog.d/2908-prompt-focus.md`
- `docs/MISSING_FEATURES.md`
- `docs/lsp-client.md`
- `openspec/specs/lsp-client/spec.md`
- `scripts/probes/issue-2908/check-round3-teeth.py`
- `scripts/probes/issue-2908/round3-validation.md`
- `IDE/HexIDE/Localization/Packs/*.json`: 39 neutral packs, adding the external-URI question.

## Complete IDE suite

`HexIDE.Tests`: **5,809 passed, 35 skipped, 0 failed**, 5,844 total, in 15m 57s.
The 5,378 CI floor passed; the xUnit report records no errors. Windows PATH entries were removed
before all runs, and `HEXIDE_FOREIGN_LSP_DOWNLOAD=0` was set. No foreign-server tests were skipped.

### Skipped IDE tests (not coverage)

- `HexIDE.Tests.Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main` — A made program is started on Windows only: elsewhere it needs a desktop session.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical` — No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched` — No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header` — No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity` — No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them` — No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce` — No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step` — No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag` — No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact` — No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write` — No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_closed_job_refuses_a_process` — Job objects exist only on Windows.
- `HexIDE.Tests.Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it` — Job objects exist only on Windows.
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it` — Job objects exist only on Windows.
- `HexIDE.Tests.Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.UserDataPathTests.OnWindowsItIsUnderAppData` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile` — Type libraries are read only on Windows.
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole` — Type libraries are read only on Windows.
- `HexIDE.Tests.Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionEscapeIsRefused` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath` — A link to a share is a Windows path.
