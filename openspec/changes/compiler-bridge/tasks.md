## 1. Design

- [x] 1.1 Record how an archive's documents reach a language server (design.md, Decision 1)
- [x] 1.2 Record the bridge's process shape and lifetime (Decision 2)
- [x] 1.3 Record the first LSP mapping from the compiler's language channel (Decision 3)
- [x] 1.4 Record where the bridge lives (Decision 4)
- [x] 1.5 Record what core keeps (Decision 5)

## 2. The boundary

- [ ] 2.1 Move the compiler-facing files into an assembly only the bridge references, with the hygiene allowlist updated
- [ ] 2.2 Add the test that fails when the shell's assembly references that assembly

## 3. Archive entries in core

- [ ] 3.1 Mint, compare and redact archive entry URIs beside `file:` and `untitled:`
- [ ] 3.2 Give archive tabs a document session and markers, recreated on reload, and kept read-only
- [ ] 3.3 Give `ILspWorkspace` archive containers as workspace folders
- [ ] 3.4 Resolve archive entry URIs in `EditorService.NavigateTo`
- [ ] 3.5 Stop a workspace move restarting a server whose registration doesn't depend on the VB6 workspace

## 4. The bridge

- [ ] 4.1 The `lsp` mode: compilers per container, staging, lifetime, and no edit capabilities
- [ ] 4.2 Diagnostics, once the mapping exists
- [ ] 4.3 The `dap` mode's relay
