# File safety audit (#2504)

Audit of `4ef7aaa3` (2026-10-28), plus the export fixes in this change. This is a command audit,
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
| Save project to directory (`--newproject`) | `DesktopStartup` passes a GUID scratch directory; `ProjectService.SaveProjectToDirectory` itself replaces colliding members/project/companions with no question. | HexIDE addition: protect the public service's non-fresh directory case. |
| Make interpreter program | Picker for executable; silently replaces executable and **derived `.dll` archive**, not covered by that picker. `ProjectService.MakeProjectInternal` (`runner.CopyTo`, archive `File.Move`). | Measure VB6 Make executable behavior; derived interpreter archive is HexIDE-only and needs protection. |
| Make/Run with VB6 | `Vb6ToolchainService.MakeWithVb6Async/RunVb6Async` invokes `/make`; compiler output uses the project's output name; a log uses a GUID temp path. Host adds no replacement question. | Measure VB6 `/make` and IDE Make output collisions separately. |
| Rename document/control; language-server Rename | Changes names/model or undoable editor text, not filesystem names. `PropertiesToolViewModel`, `CodeEditorViewModel.ApplyRename`; disk write waits for Save (MCP property writes save immediately). | VB6 document/control rename measurement; LSP refactoring is HexIDE-only, already editor-undoable. |
| Encoding conversion | No standalone conversion command found. `ProjectService.EncodePackageSources` converts only disposable Make copies; ordinary Save preserves document encoding and refuses unencodable text. | No new VB6 semantics inferred. |
| Add-in `SetContent`, `ApplyEdits`, editor writes; AI chat edits | Routes through editor replacement/undo groups and read-only-region guards. Does not itself save to disk. `AddinEditorService`, `CodeEditorViewModel.ReplaceContent/ApplyEdits`. | HexIDE additions: buffer undo exists; a later Save is the writer above. |
| MCP `set_file_content` | Resolves a loaded document by identity (no arbitrary path), then replaces code and saves. Open buffer has undo; closed document updates model directly, without equivalent undo; neither provides durable disk recovery. `HexIdeTools.SetFileContentAsync`, `DocumentContent.Write`. | HexIDE addition: add consistent recoverability or consent, including closed documents. |
| MCP `set_control_property`, `add_control` | Changes model/designer and immediately saves whole file; unrelated unsaved edits go with it. Designer undo is not disk undo. `HexIdeTools.SetControlPropertyAsync/AddControlAsync`, `SaveQuietlyAsync`. | HexIDE additions: protect disk writes; enforce containment of resolved targets, including loaded external members/links. |
| MCP `move_control`, format/type/key/reflection/menu tools | Buffer/model undo for normal edits; generic tools can invoke every UI Save/export/Make path. `HexIdeTools`, `UiAutomationDriver`. They inherit those writers' gaps. | Protect shared writers; an armed picker is not an overwrite confirmation. |
| MCP path boundary | `add_file` rejects separators; source/property tools resolve loaded identities. A loaded `.vbp` can name members outside its directory; scripted save paths are arbitrary. No uniform project-root/link containment gate on these writes. | Security work remains for source/property/control writes; explicit UI Save As/export destinations must be distinguished from tool-supplied project-member paths. |
| Export conversation in Protocol Inspector | Picker for messages, silently replaces messages **and derived manifest**. `ProtocolInspectorToolViewModel.ExportAsync`. | **Fixed in this slice:** explicit Yes/No naming every existing output before either write; No/cancel writes neither. |
| MCP `export_lsp_conversation` | Fixed temp filenames silently destroy the previous export. `HexIdeTools.ExportLspConversationAsync`. | **Fixed in this slice:** fresh private temporary directory per call; earlier exports survive. Tool accepts no destination path. |
| Export picture property | Picker then direct replacement of chosen file. `PropertiesToolViewModel.ExportPicture`. | HexIDE addition: explicit consent/recovery needed, including scripted picker route. |
| Save carried document | Atomic replacement of original; editor text undo exists but no disk recovery. Fixed `.hexide-tmp` sibling can overwrite another file. `RelatedDocumentEditorViewModel.SaveAsync`. | HexIDE addition: recoverability/consent and safe staging needed. |
| Workspace artefact generation/removal | Consent before first write; confines paths including links, refuses foreign/edited files using ownership hashes. Deletes only owned unchanged output. `WorkspaceArtifactService`, `WorkspaceArtifactConsent`. | HexIDE addition: ownership guards exist; removing an owned generated output is regenerable. |
| Archive source-folder synchronization | Refuses unrelated/edited destinations; retains previous trees for recovery and uses a journal. `ArchiveSourceFolder.Synchronize`. Compiler preparation uses `CreateNew` in staging. | HexIDE addition: recovery already exists. Archive inputs are read-only; no compiler binaries inspected. |
| Project `.vbw` sidecar writes | Merges known fields, preserves unknown ones, handles changed/unrelated sidecars, then replaces via fixed `.tmp`. `UserSidecarService.SaveAsync`. | HexIDE addition: audit recovery of replaced user metadata; fixed staging collision remains. |
| Translation override edits / reset | Writes per-language override file. Reset All asks; individual override edits/reset persist immediately. `UserTranslationsService`, `TranslationEditorViewModel`. | HexIDE addition: recoverability of individual override changes needs follow-up. |
| Settings, layout/reset, recents, add-in consent/state/revocations, chat history | Writes/removes application profile data, not source files; settings/layout use `AtomicFileWriter` with fixed `.tmp`. `SettingsService`, `WindowStateService`, `RecentProjectsService`, add-in stores, `ChatSession`, `LoggingSetup` (old application-log pruning). | Profile-data recovery and staging collisions remain; cache/revocation refresh is regenerable. |
| Add-in packer `pack`, `dev-init`, `revoke` (CLI) | `PackageSigner.Pack` recursively deletes the requested output directory; key/trust/revocation generation overwrites fixed names. | HexIDE tooling: destructive behavior remains; needs separate tests and explicit consent or retained output. |
| DAP parity harness (CLI) | `HexIDE.Dap.Parity/Program.cs` overwrites `results.jsonl`, `summary.json`, per-case modules and `.rdproj` files in the chosen output directory. Default output is timestamped, but an explicit `--out` or same-second run can collide. | Developer tooling: retain or ask about existing output; not an IDE menu command. |
| Temporary staging and cleanup | `ProjectService.AtomicWriteText/Bytes` overwrites/removes `<target>.tmp`; carried and sidecar saves have similar fixed siblings. GUID Make trees/logs and compiler preparation are scratch-owned. | Replace fixed staging names with exclusively created scratch files; ordinary source Save being a VB6 command does not justify deleting unrelated `.tmp` files. |

Locations without a prefix above are under `IDE/HexIDE/`; project definitions are under
`IDE/HexIDE.Core/ProjectElements/`, MCP tools under `IDE/HexIDE.Desktop/Server/`, workspace artefacts
under `IDE/HexIDE.Lsp/`, archive preparation under `IDE/HexIDE.TwinBasic/`, the parity harness under `IDE/HexIDE.Dap.Parity/`, and the packer under
`IDE/HexIDE.AddinPacker/`.

Programs run by the interpreter can intentionally use `Open`, `Kill`, `FileCopy`, `Name`, etc.; these
are language semantics, not IDE commands. Their existing oracle-backed behavior is unchanged. Likewise,
external compilers and third-party add-ins can write independently of host APIs; this audit does not
claim to sandbox those processes.

## Remaining

This run delivers the audit and conversation-export slice. The rows above identify the remaining
HexIDE-only fixes, especially MCP write recovery/containment, derived Make archives, picture export,
carried-file saves, staging collisions, packer output and parity-harness output. No VB6 replacement behavior was invented.
Do not close #2504 or the beta safety gate on this slice.
