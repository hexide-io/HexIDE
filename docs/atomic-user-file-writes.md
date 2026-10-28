# Atomic user-file writes (#2503)

Project saves, carried-document saves, Make outputs, exports and sidecars use
`HexIDE.IDE.AtomicFileWriter`. It writes a unique sibling temporary file, calls
`FileStream.Flush(flushToDisk: true)`, closes the stream, then replaces the destination.
A write or replacement failure propagates to the existing caller; the old target is
untouched until replacement. Cleanup is attempted even on failure. If the filesystem
also refuses deletion, a disposable temporary file can remain; that cleanup error does
not replace the original save error. Carried-document saves keep their dirty marker
and do not announce a successful save to the language server. Existing fire-and-forget
save commands log the error through `ListenErrors`; this change adds no I/O-error dialog.
This is per-file protection, not a transaction
across a form and its companion, or across a project group. It does not establish a
power-loss guarantee for directory metadata on every filesystem.

## Writer inventory

| Path | Entry points / destinations | Status |
|---|---|---|
| Project saves | `ProjectService.AtomicWriteText`: designer sources (`.frm`, `.ctl`, `.pag`), modules (`.bas`, `.cls`), `.vbp`, `.vbg`; Save As and temporary Make source copies share these paths | Shared flushed writer; baseline recorded only after replacement |
| Companion saves | `ProjectService.AtomicWriteBytes`: `.frx`, `.ctx`, `.pgx` | Shared flushed writer |
| Package source encoding | `ProjectService.EncodePackageSources`: disposable source copies converted to UTF-8 | Shared flushed writer |
| Carried-document saves | `RelatedDocumentEditorViewModel.SaveAsync`: arbitrary user-owned text files | Shared flushed writer; dirty state cleared only after replacement |
| Make archive | `ProjectService.MakeProject`: zipped project beside executable | Shared flushed writer; zip is completed before flushing |
| Make runner | `ProjectService.MakeProject`: standalone executable copied beside archive | Shared flushed writer; copies Unix permissions before replacement |
| Resource export | `PropertiesToolViewModel`: selected binary property payload | Shared flushed writer |
| Protocol export | `ProtocolInspectorToolViewModel`: conversation and manifest | Shared flushed writer, separately for each file |
| Automation protocol export | `HexIdeTools`: conversation and manifest | Shared flushed writer, separately for each file |
| User sidecar | `UserSidecarService`: project-associated user metadata | Shared flushed writer; failed saves remain owed and are retried |

The serializers render text and byte arrays; they do not themselves open destination
files. The shared writer also serves existing layout/settings callers, but IDE-owned
settings, translations, caches, consent, recents and logs are outside this issue's
user-file audit. Compiler workspaces and extracted archives are disposable staging,
and interpreter `Open`/`Put`/`Print`/`FileCopy` operations retain their language semantics;
this change makes no claim about them.

`Vb6ToolchainService.RunVb6Async` still invokes `vb6.exe /make` against the saved
project, allowing that external compiler to write its output beside the project. This
path remains unprotected: the oracle records neither output redirection nor the complete
set of compiler output files. Redirecting it without that evidence would guess VB6
behaviour. It needs a measured staging/publication contract in a later slice. The
twinBASIC compiler is not inspected or executed by this work; its existing preparation
copies are disposable workspace files.

## Portable refusal tests

A refused replacement propagates the filesystem exception: `IOException` or
`UnauthorizedAccessException` (including Windows access-denied refusals). The writer
attempts temporary-file cleanup in either case; it does not translate one exception
into the other. The replacement test uses an existing directory containing a file,
checks that its bytes survive, and accepts either refusal type.

The three sidecar migration retry tests inject an `IOException` through
`AtomicFileWriter.OpenStream`, restoring the previous factory in `finally` before
retrying the save. They do not depend on file locks, read-only attributes or the old
fixed `.tmp` name. The unchanged sidecar and the later migrated keys establish that
the failed write stays owed without migrating consumed entries twice. On Windows,
the same injected exception should take the same migration retry path; the real
replacement test is expected to receive `UnauthorizedAccessException` there.

The portable-test follow-up passes all 53 cases in `AtomicFileWriterTests`,
`SidecarMigrationTests` and `MissingLanguageDocTests`, with no skips. Restoring the
original migration tests produces exactly three failures (40 pass); restoring the
corrected tests passes again. The replacement test's Windows exception mismatch is not reproduced on Linux,
so its exception correction has no Linux before-failure claim. Windows execution remains for supervisor verification. No product code,
language-coverage rows or counts change in this follow-up.

This follow-up's full runtime executable run completed with **6,755 passed,
13 skipped, zero errors or failures** (6,768 total), in 674.919 seconds. No test
hung. The skipped tests are the same thirteen named below and are not coverage.
The requested `dotnet test` launch reached “Run tests” without naming a test before
it was stopped; the full result comes from the built executable with
`-parallel none -verbose`. The initial ANTLR build failure was avoided by adding
the existing cached JRE to the build process's `PATH`; no build configuration changed.
Tree hygiene and `git diff --check` passed.

## Verification

The helper tests exercise a partial stream write followed by an I/O error, preserving
old bytes and removing the temporary file, a refused replacement, a refused disk flush,
and an unrelated pre-existing `.tmp` file. Existing save and encoding tests cover
successful callers. Failure-injection tests for this slice exercise each remaining in-process caller,
including both destinations of each protocol export. Earlier project/carried-file
callers still have helper-level failure coverage only. No VB6 measurement
is required or introduced: this is a HexIDE persistence policy.

A failed Make reports the existing localized error dialog and rethrows. Resource export
reports its existing error dialog; protocol export shows its existing failure status;
automation propagates the I/O error; sidecar saves log a warning and keep the save owed.
No successful export status or sidecar baseline is recorded for a refused replacement.
The temporary file is removed unless deletion itself is refused, as described above.

The final targeted run passed 133 tests with one skip: `UserFileWriteFailureTests`,
`AtomicFileWriterTests`, `PictureExportTests`, `ProtocolInspectorToolViewModelTests`,
`UserSidecarSaveTests`, `MakeProjectMessageTests` and `MissingLanguageDocTests`.
All eight new caller failure cases failed against the previous callers with only the
injection/destination seams retained, then passed after routing through the writer.
The ninth new case checks successful archive creation and Unix runner permissions;
it passed before the change too. The skipped test is
`TheProjectsOwnSidecarSpelledInAnotherCaseIsStillItsOwn` (Windows-only path casing),
and is not claimed as coverage. Language-coverage rows and counts are unchanged.

`dotnet build IDE/HexIDE.Tests/` passed with zero warnings or errors. Both requested
`dotnet test` launches stalled after “Run tests”, before naming any test (the runtime
host log was empty). They were stopped; tests were run through the built xUnit
executables instead. The runtime executable completed its full run with verbose reporting: **6,678 passed,
13 skipped, zero failures** (6,691 total), in 1,677.963 seconds. No test hung. The
10,000-term expression tests were slow (the module constant took 712 seconds and the
`If` statement 606 seconds), but finished. The full result exceeds the CI runtime floor
of 5,849. The targeted IDE run used the built executable with seven `-class` filters;
its 134 cases include both language-document guard tests.

Runtime skips (not coverage):

- `ClassHeaderPreservationTests.Instancing_and_data_binding_settings_survive`
- `ClassHeaderPreservationTests.A_renamed_module_retargets_VB_Name_but_keeps_everything_else`
- `ClassHeaderPreservationTests.A_real_vb6_class_round_trips_byte_for_byte`
- `MenuHierarchySaveTests.RoundTrip_OfVb6sOwnMenuTemplates_ReproducesTheMenuShape`
- `MenuHierarchySaveTests.Vb6sOwnMenuTemplates_AreNoLongerHeldReadOnly`
- `ContainerHierarchySaveTests.RoundTrip_OfVb6sOwnNestedTemplates_ReproducesTheBeginStructure`
- `Vb6TextFileTests.The_one_corpus_file_with_high_bytes_round_trips`
- `Vb6OracleRoundTripTests.HexIDE_output_still_builds_in_vb6`
- `FileSystemTests.Windows_attribute_rows`
- `FileSystemTests.Windows_error_rows`
- `FileSystemTests.CurDir_uses_only_the_first_drive_character`
- `FileSystemTests.Windows_name_rows`
- `FileSystemTests.Windows_numeric_rows`

The first seven require the unavailable VB6 Template tree; the eighth requires the
opt-in VB6 oracle compiler; the last five require Windows filesystem behaviour.

Tree hygiene passed with nine nonblocking warnings; `git diff --check` passed.
This run leaves its changes uncommitted for supervisor review.
