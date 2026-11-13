# File safety audit (#2504)

Audit of `4ef7aaa3` (2026-10-28), plus the conversation/picture export, Make-archive consent, unique-staging,
parity-output, signing-generator, directory-save, carried/profile recovery and packer-output fixes (2026-11-10). This is a command audit,
not a claim that the beta file-safety gate is satisfied. “Picker” means the application delegates
replacement confirmation to the platform; its native prompt has not been verified here. Scripted
picker answers bypass that platform UI. Atomic replacement prevents truncation, not data loss or undo.

VB6 prompts, buttons, defaults and cancellation effects for a **collision with a different existing
destination** remain **unmeasured** for the IDE commands below. The oracle's CommonDialog `OverwritePrompt` measurement concerns a running
program's control, not VB6's IDE commands, and does not establish their behavior.

| Command / entry point | Behavior at audit baseline; code location | VB6 model / next action |
|---|---|---|
| Remove form / module / carried item; Remove Project | Model removal only; files stay on disk. `Tools/Projects/ProjectToolViewModel.DeleteForm`, `ProjectDefinition.DeleteForm/DeleteModule/DeleteRelatedDocument`, `ProjectService.UnloadProject` (the latter offers Save Changes). Only form removal is exposed by the explorer command. | Measure VB6's unsaved-item removal prompt; no disk-delete fix needed. |
| Save form/module/project/group; Save As; Save Changes on close/unload | Existing paths replace silently; new paths and Save As use a picker. Writes source, companion and project/group files; no disk undo. `ProjectService.SaveFiles`, `DestinationOf`, `Write`, `WriteCompanionBinary`. Faithfulness and encoding gates refuse some writes. | Measured: ordinary Save Project rewrites existing members and `.vbp` without a prompt; cancelling a pathless member picker abandons the save ([oracle](vb6-fidelity-oracle/when-a-document-gets-a-file-and-what-a-dangling-startup-does.md)). Save Project As retargets relative paths without copying members ([oracle](vb6-fidelity-oracle/what-save-project-as-does-to-the-paths-in-a-project-file.md)). Measure collisions with another existing destination/companion. |
| New form/module/class/UserControl/PropertyPage; MCP `add_file` | Adds only to the model; no file until Save. Generated names avoid occupied source/companion files; MCP validates VB6 names and duplicates. `ProjectService.AddNew*`, `NextFree*Name`; `HexIdeTools.AddFileAsync`. | Measured: new documents have no disk file until Save ([oracle](vb6-fidelity-oracle/when-a-document-gets-a-file-and-what-a-dangling-startup-does.md)). Measure first Save onto an existing name; no immediate overwrite. |
| New project / templates | Built-in and add-in templates create a project model, not a folder of files. `ProjectManager.NewProject`, `IAddinProjectTemplate.Create`. Its Save reaches the normal writer. | Measure VB6 template/Save collision behavior. Third-party add-in arbitrary filesystem code is outside host enforcement. |
| Save project to directory (`--newproject`) | `DesktopStartup` passes a GUID scratch directory; `ProjectService.SaveProjectToDirectory` asks once, naming existing source, companion and project destinations before writing any output. Only Yes proceeds; No/cancel preserves the files and model paths. | **Fixed for this HexIDE addition:** protects the public service's non-fresh directory case (`UserFileWriteFailureTests.DirectorySaveAsksBeforeReplacingAnyOutput`). |
| Make interpreter program | Picker for executable; derived `.dll` archive now has a separate explicit Yes/No question naming its path. No/cancel leaves both outputs untouched. Both outputs are staged and flushed through `AtomicFileWriter`. `ProjectService.MakeProjectInternal`. | **Fixed for the HexIDE-only archive.** Measure VB6 Make executable behavior; executable picker behavior is unchanged. |
| Make/Run with VB6 | `Vb6ToolchainService.MakeWithVb6Async/RunVb6Async` invokes `/make`; compiler output uses the project's output name; a log uses a GUID temp path. Host adds no replacement question. | Measure VB6 `/make` and IDE Make output collisions separately. |
| Rename document/control; language-server Rename | Changes names/model or undoable editor text, not filesystem names. `PropertiesToolViewModel`, `CodeEditorViewModel.ApplyRename`; disk write waits for Save (MCP property writes save immediately). | VB6 document/control rename measurement; LSP refactoring is HexIDE-only, already editor-undoable. |
| Encoding conversion | No standalone conversion command found. `ProjectService.EncodePackageSources` converts only disposable Make copies; ordinary Save preserves document encoding and refuses unencodable text. | No new VB6 semantics inferred. |
| Add-in `SetContent`, `ApplyEdits`, editor writes; AI chat edits | Routes through editor replacement/undo groups and read-only-region guards. Does not itself save to disk. `AddinEditorService`, `CodeEditorViewModel.ReplaceContent/ApplyEdits`. | HexIDE additions: buffer undo exists; a later Save is the writer above. |
| MCP `set_file_content` | Resolves a loaded document by identity (no arbitrary path), then replaces code and saves. Before editing, `ProjectWriteSafety` checks the owning project folder, destination and companion. Existing bytes are flushed to unique, exclusively created recovery siblings; success names each backup and gives copy-back instructions, for closed documents too. `HexIdeTools.SetFileContentAsync`, `DocumentContent.Write`. | **Fixed for this direct writer:** outside-folder, parent-traversal and linked destinations are refused before editing; replacements preserve source and companion bytes. |
| MCP `set_control_property`, `add_control` | Changes model/designer and immediately saves whole file; unrelated unsaved edits go with it. Designer undo is not disk undo. `HexIdeTools.SetControlPropertyAsync/AddControlAsync`, `SaveQuietlyAsync`. | **Fixed for these direct writers:** the same `ProjectWriteSafety` gate and byte recovery as `set_file_content`, before property changes or control placement. |
| MCP `move_control`, format/type/key/reflection/menu tools | Buffer/model undo for normal edits; generic tools can invoke every UI Save/export/Make path. `HexIdeTools`, `UiAutomationDriver`. They inherit those writers' gaps. | Protect shared writers; an armed picker is not an overwrite confirmation. |
| MCP path boundary | `add_file` rejects separators; source/property tools resolve loaded identities. A loaded `.vbp` can name members outside its directory; scripted save paths are arbitrary. The three direct member writers now check their owning project folder and all path ancestors, source and companion alike. Armed first-save destinations pass the same check; a project without a folder is refused. | Direct member writes are contained and recoverable. `add_file` remains model-only with VB6 identifier validation. Generic UI invocation and armed UI Save As/export destinations remain to audit at shared writers; they are not covered by this direct-tool gate. |
| Export conversation in Protocol Inspector | Picker for messages, silently replaces messages **and derived manifest**. `ProtocolInspectorToolViewModel.ExportAsync`. | **Fixed in this slice:** explicit Yes/No naming every existing output before either write; No/cancel writes neither. |
| MCP `export_lsp_conversation` | Fixed temp filenames silently destroy the previous export. `HexIdeTools.ExportLspConversationAsync`. | **Fixed in this slice:** fresh private temporary directory per call; earlier exports survive. Tool accepts no destination path. |
| Export picture property | Picker followed by an explicit Yes/No question naming an existing destination, including scripted picker answers. Only Yes replaces it, through `AtomicFileWriter`; a new destination is created exclusively. `PropertiesToolViewModel.ExportPicture`. | **Fixed:** No/cancel preserves the file (`PictureExportTests.ExistingDestinationRequiresExplicitConsent`). |
| Save carried document | Atomic replacement of original; each save now retains the previous disk bytes in a unique recovery sibling. Unique, exclusively created staging names preserve a user file at the former `.hexide-tmp` name. `RelatedDocumentEditorViewModel.SaveAsync`. | **Recovery fixed:** `CarriedFileSaveTests.SavingRetainsPreviousDiskBytesForRecovery`; **staging collision fixed** (regression: `CarriedFileSaveTests.SavingPreservesFileAtOldStagingName`). |
| Workspace artefact generation/removal | Consent before first write; confines paths including links, refuses foreign/edited files using ownership hashes. Deletes only owned unchanged output. `WorkspaceArtifactService`, `WorkspaceArtifactConsent`. | HexIDE addition: ownership guards exist; removing an owned generated output is regenerable. |
| Archive source-folder synchronization | Refuses unrelated/edited destinations; retains previous trees for recovery and uses a journal. `ArchiveSourceFolder.Synchronize`. Compiler preparation uses `CreateNew` in staging. | HexIDE addition: recovery already exists. Archive inputs are read-only; no compiler binaries inspected. |
| Project user sidecar writes (`.user.hexproj`) | Merges known fields, preserves unknown ones, handles changed/unrelated sidecars, then stages and flushes through `AtomicFileWriter` with unique names. `UserSidecarService.SaveAsync`. | **Recovery fixed:** previous sidecar bytes are retained (`UserSidecarSaveTests.SavingRetainsPreviousSidecarBytesForRecovery`); **staging collision fixed** (regression: `UserSidecarSaveTests.SavingPreservesFileAtOldStagingName`). |
| Translation override edits / reset | Writes per-language override file atomically with recovery. Reset All asks; individual override edits/reset persist immediately. `UserTranslationsService`, `TranslationEditorViewModel`. | **Recovery fixed:** edits, clearing an override (including the last), and Reset All retain previous file bytes (`UserTranslationsServiceTests.ChangesRetainPreviousDiskBytesForRecovery`). |
| Settings, layout/reset, recents, add-in consent/state/revocations, chat history | Writes/removes application profile data, not source files; settings/layout/recents/consent/state/chat use `AtomicFileWriter` with unique, exclusively created temporary names. `SettingsService`, `WindowStateService`, `RecentProjectsService`, add-in stores, `ChatSession`, `LoggingSetup` (old application-log pruning). | **Recovery fixed:** settings, current/named layouts (including reset), recents, add-in consent/state, and chat history retain recovery siblings (`SettingsServiceTests`, `ProfileFileRecoveryTests`, `ProfileRecoveryTests`, `ChatSessionTests`); staging collisions are fixed (`AtomicFileWriterTests`, `SettingsServiceTests.SavingPreservesFileAtOldStagingName`); cache/revocation refresh is regenerable. |
| Keymap overrides / export | Options persists edits and exports selected bindings through the shared atomic writer. `KeymapService.SaveOverrides`, `KeymapPageViewModel.Export`. | **Recovery fixed:** both preserve previous destination bytes (`KeymapServiceTests`, `KeymapEditingTests`). |
| Open-document session persistence | Saves per-project tab state in the profile. `DocumentSessionStore.Save`. | **Recovery fixed:** previous session bytes retained (`ProfileFileRecoveryTests.SavingRetainsPreviousSessionBytesForRecovery`). |
| Add-in packer `pack`, `genkeys`, `sign-revocations` (CLI) | `PackageSigner.Pack` accepts new or empty output folders, stages a complete replacement and refuses other existing output unless its signature verifies with the current signing key, its payload hashes match, its trust files match the supplied chain, and its exact file/directory inventory has no additions or links. `GenerateDevTrust` and `SignRevocations` now create every output exclusively and refuse a collision before writing generated content. | **Key/trust/revocation replacement fixed:** all eleven output collisions preserve the existing file (`PackerFileSafetyTests`, Linux-running). **Package replacement fixed:** unrelated or edited output is refused by path; an unchanged prior signed package is replaced, and a failed staged pack preserves it (`PackerFileSafetyTests`, Linux-running). Failed reservations or writes remove only the files this call created, including when the last output collides. |
| DAP parity harness (CLI) | `HexIDE.Dap.Parity/Program.cs` overwrites `results.jsonl`, `summary.json`, per-case modules and `.rdproj` files in the chosen output directory. Default output is timestamped, but an explicit `--out` or same-second run can collide. | **Fixed:** every output is created exclusively; a collision fails without replacing that file (`ProgramParityTests.Existing_output_is_preserved`, four Linux-running cases). Failed runs remove only files created by that invocation; existing files survive, including a collision on the final output. Retries reuse their already-created workspace. Not an IDE menu command. |
| Temporary staging and cleanup | `ProjectService.AtomicWriteText/Bytes`, carried saves, sidecars, settings and layout now use `AtomicFileWriter` with GUID siblings opened with `CreateNew` and flushed before replacement. GUID Make trees/logs and compiler preparation are scratch-owned. | **Fixed:** old `.tmp` and `.hexide-tmp` files survive; project-save regression: `WriteAtSaveTests.SavingPreservesFilesAtOldStagingNames`. Layout regression: `AtomicFileWriterTests.LayoutSavePreservesFileAtOldStagingName`. |

Locations without a prefix above are under `IDE/HexIDE/`; project definitions are under
`IDE/HexIDE.Core/ProjectElements/`, the shared atomic writer is under `IDE/HexIDE.Core/IDE/`, MCP tools under `IDE/HexIDE.Desktop/Server/`, workspace artefacts
under `IDE/HexIDE.Lsp/`, archive preparation under `IDE/HexIDE.TwinBasic/`, the parity harness under `IDE/HexIDE.Dap.Parity/`, and the packer under
`IDE/HexIDE.AddinPacker/`.

Programs run by the interpreter can intentionally use `Open`, `Kill`, `FileCopy`, `Name`, etc.; these
are language semantics, not IDE commands. Their existing oracle-backed behavior is unchanged. Likewise,
external compilers and third-party add-ins can write independently of host APIs; this audit does not
claim to sandbox those processes.

## Remaining

The branch delivers the audit, conversation/picture exports, Make-archive and directory-save consent and unique staging.
The rows above identify the remaining
shared-writer questions, including generic MCP UI-triggered writes. Packer package output now refuses unrelated/edited folders and stages replacement. Profile stores and project sidecars now retain recovery versions. Key/trust/revocation outputs now refuse replacement. Parity-harness output now refuses replacement. No VB6 replacement behavior was invented.
Do not close #2504 or the beta safety gate on this slice.

## Direct MCP member writes (next slice)

`WorkingCopyReplyTests` exercises all three writers against external loaded members, parent traversal,
linked directories and an external armed first-save destination, and checks recovery of existing source
bytes for chosen files and working copies. `ProjectWriteSafetyTests` checks binary companion recovery
(`.frx`, `.ctx`, `.pgx`), including refusing linked and dangling companion paths before any backup is made.
Links are conservatively refused even when they lead inside the project. The boundary is the owning
project file's directory, or its working directory when no project file exists; another loaded project's
folder does not grant permission. Recovery files remain beside the original and are never automatically
pruned. This is a preflight guard: concurrent filesystem changes and overlapping scripted picker calls
between checking and saving remain unaudited (source review, not a concurrency probe).
No VB6 prompt or overwrite behavior was inferred or changed.

## Carried files and profile recovery

Carried-document Save, settings Save, layout persistence/reset, named-layout edits, recent-project
add/remove/clear/startup preference, project sidecars, add-in consent/state, chat history, document
sessions, keymap overrides/export, and translation edits/clear/reset retain the previous file as
`<original-name>.<unique-id>.recovery` beside the original. Recovery files are created exclusively,
flushed before replacement, and never automatically pruned. Reset moves the original to a unique
recovery sibling without replacing an existing file. To restore, close the IDE, choose the recovery
version wanted, and copy it back to the original name. Translation loaders only enumerate `*.json`,
so recovery siblings are not loaded as languages. No prompt wording or VB6 behavior was inferred.

The synchronous writers do not provide a transaction against another process changing the file
between recovery and replacement. A failed recovery copy refuses the write; a failed staged write
keeps the original and any completed recovery. Retaining versions can consume disk space; cleanup is
manual so earlier user bytes are not silently removed. Packer output replacement is now guarded as described below. Revocation caches and application logs keep their existing refresh/retention
policy; neither is a user-edit command.


## Packer package replacement

`pack` recognises its own previous output using the signed `addin.json`, verified with the supplied
signing key, payload hashes, byte-identical supplied trust-chain files, and an exact inventory including
payload parent directories. Unrelated, modified, missing, or extra entries (including empty directories)
are refused; links in the output and the output itself being a link are refused. Links above the output
are allowed. The CLI names the refused path, explains the reason, confirms nothing was changed, and
asks the caller to choose another folder or move this one aside.
No ownership marker or package-format change is needed, so existing signed build output remains replaceable.
Changing signing keys or trust files requires choosing a fresh output directory or explicitly moving the
old one aside. Empty pre-existing directories are accepted.

The new package is built in a unique sibling before the old output moves. Ownership is checked again,
the old directory is renamed to a unique sibling, and the staged directory takes its place; a failed
installation rename restores the old directory. Only after installation succeeds is the unchanged old package deleted. Cleanup is best-effort after
that commit: failure prints the leftover sibling path but does not report the completed pack as failed.
The sibling contains the old package beneath `package/`, with no root manifest, so leftovers are not
loaded as another add-in.
A build/signing/copy failure leaves the old package untouched. This is not a transaction against a
concurrent process changing directories between the last check and rename, or a power-loss journal.
The packer row is resolved; the audit still records unmeasured VB6 collision prompts and shared UI-writer
questions above, so this does not establish the whole beta safety gate.

## Refused direct writes (#3902)

The three direct member writers discard recovery copies when a call writes nothing, on refusal or
exception alike. Recovery pruning happens only after a write, so five refusals cannot evict a real
write's previous bytes. An unsaveable-form property reply says the value is set in the IDE while the
file on disk is unchanged. See [the closed gap](mcp-gaps/closed/3902.md).
