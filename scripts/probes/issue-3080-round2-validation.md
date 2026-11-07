# #3080 round 2 validation

The base was round one's protocol slice plus `upstream/main` at `3f5001d2`, merged as
`f2754e0f`. The merge keeps both call-hierarchy and range-formatting capabilities and wire tests.

## Environment

WSL/Linux; drop `/mnt/` entries from PATH before every run. This sandbox also needs:

```sh
export NUGET_HTTP_CACHE_PATH=/tmp/hexide-3080-nuget-http
export XDG_RUNTIME_DIR=/tmp/hexide-3080-runtime
export UseSharedCompilation=false
export MSBUILDDISABLENODEREUSE=1
export HEXIDE_FOREIGN_LSP_DOWNLOAD=0
```

The first two directories must exist. The default NuGet HTTP cache and the named mutex directory
under the inherited runtime directory are read-only in this sandbox. An initial shared compiler
also could not write this clone's test-adapter output. These are build-environment settings, not
product changes.

## Supervisor live check

Use a server advertising `callHierarchyProvider`. Open a code window, put the caret on a callable
symbol, and invoke Call Hierarchy from Edit and from the code context menu. Check the pane renders
in Immediate's group; Calls To and Calls From are labelled and mutually exclusive. Inspect the
protocol log: prepare uses the caret, and incoming/outgoing requests occur only when opening a node,
returning its original opaque data to its owning server. Enter and double-click should open a node's
selection range or a call site's range (outgoing sites belong to the parent document). Rerun at a
second symbol: the same pane must replace the roots. Check empty replies, per-server failures, and
command disabling without a provider. Inspect a non-English pack and pseudo localization as well.

No VB6 runtime values were inferred or measured. `MISSING_LANGUAGE` counts do not change.

## Tests with implementations removed

Retained the new API declarations, record types, pane/view declarations and test scaffolding so tests
could compile. Temporarily removed search-result population, the context-menu entry, the shell's
pane-show subscription and the registry's prepare aggregation. This is an implementation-removal
probe, **not a build of the unchanged round-one commit**.

- `dotnet test HexIDE.Integration.Tests/ -- --filter-class '*CallHierarchyTests'`:
  **15 failed, 0 passed, 0 skipped**.
- `dotnet test HexIDE.Tests/ -- --filter-method '*CallHierarchy*'`:
  **3 failed, 15 passed, 0 skipped**. The three failures were the two new registry cases and
  `CallHierarchyRerunsReuseTheImmediateGroupPane`; the 15 existing protocol cases passed.

Total: **18 new cases fail without the implementations**. Restored all product implementations
before the full checks. The pane tests include node/call-site navigation in both directions,
provider gating, both lazy modes, reruns, empty replies, per-server errors and preservation of a
leaf's call sites when it has no further calls. Both modes are selected through the view's radio buttons.

The base's server-feature summary had no call-hierarchy row. Added an explicit **■ boundary** row;
the bundled server still neither advertises nor computes call relationships.

## Full checks

- `HexIDE.Tests`: **5,776 passed, 35 skipped, 0 failed; total 5,811** (20m 41s).
  The 5,378-test floor passed. `check-xunit-report.sh` confirms zero runner errors; all three new
  unit cases passed. The skipped cases below provide no coverage in this Linux run.

- `HexIDE.Integration.Tests`: **4,955 passed, 2 skipped, 0 failed; total 4,957** (5m 19s).
  The 4,599-test floor passed. `check-xunit-report.sh` confirms zero runner errors; all **15** new
  pane cases passed on Linux. Together with the three unit cases, all **18** new cases pass.
- Desktop Debug build: **0 warnings, 0 errors**.
- `check-tree-hygiene.sh`: passed with nine pre-existing warnings; identity scan not configured.
- `git diff --check` and conflict-marker scan: clean.

The two Integration skips provide no coverage here:

- `ToolbarCaptionTests.Segoe_UI_nine_point_matches_console_measurements`: requires the measured
  Windows Segoe UI font build.
- `ToolbarCaptionTests.Windows_caption_width_follows_installed_fonts_Gdi_extent`: requires Windows GDI.

### Unit skips: Windows-only or native Windows resources (25)

- `Runtime.VbSettingsStoreTests.The_registry_keeps_each_setting_as_text_as_vb6_exe_does`
- `Runtime.VbSettingsStoreTests.A_value_another_program_wrote_that_is_not_text_reads_as_no_text`
- `Profiles.ProfileContainmentTests.JunctionEscapeIsRefused`
- `Profiles.ProfileContainmentTests.JunctionSwappedInAfterPreflightIsRefusedDuringRead`
- `Projects.MadeProgramTests.A_made_program_starts_and_runs_its_Sub_Main`
- `Addins.PackageVerificationTests.Locked_signature_file_yields_Untrusted_not_an_exception`
- `Infrastructure.WindowStateUnconfirmedTests.On_Windows_the_probe_does_not_guess`
- `UserDataPathTests.OnWindowsItIsUnderAppData`
- `LspClient.WorkspaceArtifactProjectTests.ALocalTypeLibraryIsNamedFromItsFile`
- `LspClient.WorkspaceArtifactProjectTests.TheTypeLibraryNamesOleAutomationStdole`
- `Tools.ProjectTreeBuilderTests.AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation`
- `Tools.ProjectTreeBuilderTests.ForwardSlashPath_NormalizesAndNests`
- `Tools.ProjectTreeBuilderTests.CrossDriveMember_RendersAtRoot`
- `Sidecar.UserSidecarSaveTests.TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn`
- `LspClient.TypeLibraryNamesTests.ALocalLinkToAShareIsNotALocalPath`
- `IDE.EditorServiceNavigationTests.AFileUriWhoseDriveLetterTheServerNormalisedStillResolves`
- `Profiles.ProfileReaderSafetyTests.WindowsPipeHandleIsRefused`
- `Processes.ChildProcessJobTests.A_closed_job_refuses_a_process`
- `Processes.ChildProcessJobTests.A_process_left_out_of_the_job_outlives_it`
- `Processes.ChildProcessJobTests.Closing_the_job_ends_the_process_put_in_it`
- `Server.ProcessWindowsToolTests.A_running_process_with_no_window_is_said_to_have_none_yet`
- `Server.ProcessWindowsToolTests.A_window_of_a_process_the_started_one_started_is_read_through_it`
- `Server.ProcessWindowsToolTests.A_program_started_hidden_is_read_as_hidden_and_the_note_says_vbHide_did_it`
- `Server.ProcessWindowsToolTests.A_minimized_program_is_read_as_minimized_and_visible`
- `LspClient.LspDocumentSessionTests.AWindowsDriveLetterInTheOtherCaseStillMatches`

### Unit skips: VB6 templates unavailable (10)

- `Projects.ClassModuleSaveFidelityTests.Editing_the_body_leaves_the_header_untouched`
- `Projects.ClassModuleSaveFidelityTests.Saving_a_project_leaves_an_untouched_class_byte_identical`
- `Projects.ClassModuleSaveFidelityTests.A_reload_from_disk_also_preserves_the_header`
- `Projects.CompanionBinaryPreservationTests.AFormWhoseBlobsAreAllModelled_DoesNotRaiseTheLossFlag`
- `Projects.CompanionBinaryPreservationTests.A_second_save_still_leaves_the_companion_intact`
- `Projects.CompanionBinaryPreservationTests.The_form_records_that_it_lost_binary_fidelity`
- `Projects.CompanionBinaryPreservationTests.Saving_never_shrinks_or_deletes_a_companion_it_cannot_reproduce`
- `Projects.CompanionBinaryPreservationTests.A_form_whose_records_HexIDE_does_not_model_saves_both_halves_as_VB6_wrote_them`
- `Projects.CompanionBinaryPreservationTests.Dropping_a_blob_bearing_control_keeps_both_halves_in_step`
- `Projects.CompanionBinaryPreservationTests.A_form_that_reproduces_its_companion_is_never_denied_the_write`

## Files changed

The implementation touches connection abstractions/routing; the new call-hierarchy pane, event and
editor command; shared results-tool docking; both menus; DI/view registration; keymap registration
and both keymaps; all 39 neutral language packs; pane/routing/docking tests (plus one constructor
call-site adjustment); the live client spec, LSP client/server docs, `MISSING_FEATURES`, and the
unreleased `changelog.d/3080.md` fragment. `MISSING_LANGUAGE` and oracle pages are unchanged by this run.

The merge is committed as permitted; this run's implementation and documentation remain uncommitted
for supervisor review. No push or pull request was made. The supervisor live check above remains open.
