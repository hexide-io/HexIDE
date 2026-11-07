# Issue 3076, round 2 validation

Checked on WSL (Linux), 2026-11-04. Merged `upstream/main` into the round-1 branch as
`4080bbdd`; this run's implementation is left uncommitted for supervisor review.

## Commands and results

Windows mount entries were removed from `PATH` before all test/build invocations.
The full unit run uses `HEXIDE_FOREIGN_LSP_DOWNLOAD=0`; installed/cached foreign servers remain usable.

- `HexIDE.Tests`: 5735 passed, 35 skipped, 0 failed; 5770 total, no xUnit errors.
- `HexIDE.Integration.Tests`: 4915 passed, 2 skipped, 0 failed; 4917 total, no xUnit errors.
- Desktop Debug build: succeeded, zero warnings and zero errors.
- `bash scripts/check-tree-hygiene.sh`: passed; nine existing, nonblocking warnings.
- Shipped-pack parity after the mnemonic correction: 18 passed, zero skipped.
- The LSP coverage table was regenerated using `scripts/probes/3076-regenerate-lsp-coverage.py`
  and the pinned, hash-verified `metaModel.json`.

The first full unit run found one Hungarian access-key mismatch (5,734 passed, 35 skipped,
one failed). The translations were corrected, including Dutch and Vietnamese access-key
conventions; the full rerun above is the final result.
The first Integration invocation failed before tests because HangDump's named mutex tried to
write to the sandbox's read-only runtime directory. The successful invocation kept HangDump
and its ten-minute quiet timeout, with `XDG_RUNTIME_DIR` and `TMPDIR` pointing at writable
`artifacts/3076-runtime` and `artifacts/3076-temp` directories inside this clone.
Both final suites requested an xUnit report and were checked with `scripts/check-xunit-report.sh`.

## Teeth

Eight new `SelectionRangeCommandTests` failed against the pre-command implementation because
the routed commands were absent. A ninth regression failed against the first-endpoint-only
implementation: it selected the coarse outer range rather than the tighter server range returned
for the other endpoint. All nine passed in the final full integration run, with no skips.
They drive the routed commands through a shown headless code window:

- expanding twice climbs server parents;
- shrinking restores the previous selection;
- an edit discards the chain;
- an outside caret move discards the chain;
- both menu commands are disabled without a provider;
- a nonempty selection requests both endpoints and skips smaller ranges;
- shrinking the first range restores the original caret;
- a reply arriving after a caret move leaves the selection alone;
- enclosing ranges from both endpoints contribute to the expansion chain.

The existing six protocol cases additionally check command availability for the advertised
boolean/options/false/null provider shapes. Those remain wire/consequence tests, not just
assertions that an API returned.

## Scope and live verification

No VB6 result was inferred or measured in this run. `MISSING_LANGUAGE.md` counts did not move.
`MISSING_FEATURES.md` marks the additive selection commands done; the live LSP client spec,
client documentation and unreleased `changelog.d/3076.md` describe the commands.

The supervisor still needs to check the running IDE: with an advertising backend, open the code
context menu and check both translated labels and enabled state; expand twice and shrink once,
then edit or move the caret and check reset. Disconnect the backend and check both menu items
become disabled. Check a non-English pack and confirm there are no factory shortcuts.

## Skipped tests

These are not cited as coverage for this change. Exact names and reasons from the final reports:

### HexIDE.Tests

- `HexIDE.Tests.Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it` — Job objects exist only on Windows.
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it` — Job objects exist only on Windows.
- `HexIDE.Tests.Processes.ChildProcessJobTests.A_closed_job_refuses_a_process` — Job objects exist only on Windows.
- `HexIDE.Tests.Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath` — A link to a share is a Windows path.
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole` — Type libraries are read only on Windows.
- `HexIDE.Tests.LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile` — Type libraries are read only on Windows.
- `HexIDE.Tests.LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical` — No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched` — No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header` — No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Profiles.ProfileContainmentTests.JunctionEscapeIsRefused` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main` — A made program is started on Windows only: elsewhere it needs a desktop session.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce` — No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity` — No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write` — No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag` — No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step` — No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact` — No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them` — No VB6 Template tree here: VB6_TEMPLATES is not set, and VB6 is not installed at 'C:\\Program Files (x86)\\Microsoft Visual Studio\\VB98\\Template'.
- `HexIDE.Tests.IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.UserDataPathTests.OnWindowsItIsUnderAppData` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.
- `HexIDE.Tests.Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception` — Windows-only: this pins behaviour the product deliberately varies by host filesystem.

### HexIDE.Integration.Tests

- `HexIDE.Integration.Tests.Views.ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements` — Requires the oracle's Windows Segoe UI font build
- `HexIDE.Integration.Tests.Views.ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent` — Requires Windows GDI

