# #2908 round 2 validation

Scope: `window/showMessageRequest`. The loaded-member `showDocument` slice was already on
`upstream/main` (`b1b483c5`); merging it reported already up to date. No runtime language semantics
or oracle measurements changed, and MISSING_LANGUAGE's rows and counts remain unchanged.

## Regression tests and teeth

- `ShowMessageRequestWireTests`: **8 passed, 0 skipped** with the implementation. Frames prove the
  capability, original chosen item (including extra JSON fields), dismissal, cancellation error -32800,
  diagnostics during a pending prompt, queue order, and deferral for a running program or modal.
- `ServerMessagePromptTests`: **5 passed, 0 skipped** with the implementation. Headless windows prove
  ownership, title, action buttons, choice/dismissal and cancellation; the native-picker state clears
  on completion and failure.
- **13 failed, 0 passed, 0 skipped** in the before-feature replay: eight wire and five headless cases.
  `python3 scripts/probes/2908-prove-teeth.py` reproduces it and restores the files in `finally`.
  It retains the new signatures/contracts so tests compile, removes the request handler and capability
  to restore the pre-fix receiver behavior, and makes the new UI entry points inert. This is an explicit
  replay with compilation scaffolding, rather than a claim that new-API tests compile against untouched
  main. The action reply tests see -32601; the capability and UI assertions also fail.

## Environment

Windows entries were removed from PATH before every test. `HEXIDE_FOREIGN_LSP_DOWNLOAD=0` was set.
The installed cached JRE was added to PATH for ANTLR. `XDG_RUNTIME_DIR` and `NUGET_HTTP_CACHE_PATH`
pointed into this clone's ignored `artifacts/2908/` directory. Builds used `-m:1 -nodeReuse:false
-p:UseSharedCompilation=false` after shared compilation stopped making visible progress. No package
pins or dependencies changed. The unit suite also exports `UseSharedCompilation=false` and
`MSBUILDDISABLENODEREUSE=1`, so builds launched inside tests inherit the same isolation. A prior
invocation's nested build failed with CS2012 (read-only output); both nested-build cases passed with
those environment settings. That incomplete invocation is not counted as a completed suite. A first
silent unit invocation was stopped without a result; the final command disables MTP output capture
and enables live diagnostics. Two early overlapping builds collided in Avalonia's generated output;
they are not validation. `dotnet clean` on the local runtime project and a sequential rebuild recovered it.

## Remaining policy and live verification

`workspace/applyEdit` remains undeclared and external `showDocument` remains refused, as directed by
the latest issue comment, pending the existing policy decision. No VB6 behavior was guessed.

The supervisor's running-IDE check should use the scripted server to confirm the server display name,
message and both action buttons, then inspect the chosen item in the server log. Check dismissal,
cancellation closing the box with -32800, two queued questions, diagnostics arriving during a question,
and questions waiting until a program, IDE modal or native Open/Save picker ends. Confirm RDCore project
open and first save still import silently with `overwrite: true`. No live verification was performed here.

The coverage table was regenerated from the pinned model using
`python3 scripts/probes/2908-regenerate-lsp-coverage.py path/to/metaModel.json`: **40 of 93**, up from 39.
The live requirements are in `openspec/specs/lsp-client/spec.md`, inside `## Requirements`.

## Files changed

- `IDE/HexIDE.Core/Lsp/Messages/LspMessages.cs`
- `IDE/HexIDE.Integration.Tests/Views/ServerMessagePromptTests.cs`
- `IDE/HexIDE.Lsp/LanguageServerRegistrationFactory.cs`
- `IDE/HexIDE.Lsp/Messages/LspJsonContext.cs`
- `IDE/HexIDE.Lsp/VBLspClient.cs`
- `IDE/HexIDE.Tests/LspClient/ShowMessageRequestWireTests.cs`
- `IDE/HexIDE/DISetup.cs`
- `IDE/HexIDE/IDE/IWindowManager.cs`
- `IDE/HexIDE/IDE/ServerMessagePrompt.cs`
- `IDE/HexIDE/IDE/WindowManager.cs`
- `changelog.d/2908.md`
- `docs/lsp-client.md`
- `openspec/specs/lsp-client/spec.md`
- `scripts/probes/2908-prove-teeth.py`
- `scripts/probes/2908-regenerate-lsp-coverage.py`
- `scripts/probes/2908-validation.md`

## Full checks

| Check | Passed | Skipped | Failed |
|---|---:|---:|---:|
| HexIDE.Tests | 5677 | 35 | 0 |
| HexIDE.Integration.Tests | 4886 | 2 | 0 |

The full unit run passed its 5378-test floor and `scripts/check-xunit-report.sh`, with no xUnit errors.
All eight new wire cases passed in it. Its final duration was 17m 50s. The full Integration run passed its 4599-test floor and `scripts/check-xunit-report.sh`, with no xUnit
errors. All five new headless cases passed in it. Its two existing skips prove nothing in this sandbox:

- `ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent` — requires Windows GDI.
- `ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements` — requires the oracle's Windows
  Segoe UI font build.

The explicit Desktop Debug build passed with **zero warnings and zero errors**.

## Existing unit skips

These 35 skips provide no coverage here. No new regression case skipped.

- `Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `Profiles.ProfileContainmentTests.JunctionEscapeIsRefused` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main` — A made program is started on Windows only: elsewhere it needs a desktop session.
- `UserDataPathTests.OnWindowsItIsUnderAppData` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it` — Job objects exist only on Windows.
- `Processes.ChildProcessJobTests.A_closed_job_refuses_a_process` — Job objects exist only on Windows.
- `Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it` — Job objects exist only on Windows.
- `Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag` — No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact` — No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write` — No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce` — No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity` — No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them` — No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step` — No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical` — No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header` — No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched` — No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath` — A link to a share is a Windows path.
- `Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile` — Type libraries are read only on Windows.
- `LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole` — Type libraries are read only on Windows.

## Other checks

Tree hygiene passed (nine existing warnings, none blocking); `git diff --check` passed.
Strict OpenSpec validation reports 44 existing requirement-length warnings and no errors. All prior
requirement bodies were checked unchanged against HEAD, and neither new requirement has a warning.

The final unit invocation used the already rebuilt assemblies (no product source changed afterward):

```sh
cd IDE
HEXIDE_FOREIGN_LSP_DOWNLOAD=0 dotnet test HexIDE.Tests/ --no-build --no-restore -m:1 -nodeReuse:false -p:TestingPlatformCaptureOutput=false -- --minimum-expected-tests 5378 --results-directory ../../artifacts/2908/ide-final-results --report-xunit --report-xunit-filename ide.xml --long-running 30 --xunit-diagnostics on --output Detailed --timeout 20m
```

It inherited `UseSharedCompilation=false`, `MSBUILDDISABLENODEREUSE=1`,
`DOTNET_CLI_USE_MSBUILD_SERVER=0` and the environment paths described above.
