# Tasks

## 1. The shared rule

- [x] 1.1 Add `IDE/HexIDE.Lsp/LspLaunchDirectory.cs`: explicit setting wins unchecked; otherwise the
  workspace directory if it exists; otherwise the empty string, with a log line naming the directory and
  the consequence. Information rather than Warning: it fires on every unsaved project that opens a code
  window, and a warning that says "this is expected" teaches its reader to skip warnings.
- [x] 1.2 Record in its docstring why the directory is not created, why the explicit branch is not checked,
  and why `Directory.Exists` needs no try/catch (it answers false rather than throwing for a malformed,
  over-long or unauthorised path).
- [x] 1.3 `StdioProcessLspTransport.WorkingDirectory()` calls it; keep the existing argument in the docstring
  and add the does-not-exist case.
- [x] 1.4 `NamedPipeLspTransport.WorkingDirectory()` calls it; note in `WorkspaceDirectory()`'s remark that
  the asymmetry is deliberate, so a future reader does not "fix" it.

## 2. Say what went wrong

- [x] 2.1 Stdio: log the working directory beside the launch, with the same `<inherited>` spelling the pipe
  transport already uses.
- [x] 2.2 Stdio: `LastFailure` names the working directory, because the OS message does not.
- [x] 2.3 Pipe: `LastFailure` names it too, recorded at launch rather than recomputed — recomputing would
  log the warning twice and would name a directory when no child was launched at all.

## 3. Correct the record

- [x] 3.1 `ProjectLspWorkspace`'s documentation describes the pre-#260 shared path and calls the directory a
  place that "may hold another project's leavings". Replace it with what the code now does, and with why
  answering with a directory that does not exist is right for the workspace and wrong for the launch.

## 4. Prove it

- [x] 4.1 `StdioProcessLspTransportTests`: a workspace directory that does not exist still starts the server,
  and the transport does not create it. Needs a `Workspace` double and an `LspServerInfo` with no explicit
  working directory — every existing one in the file passes `Path.GetTempPath()`, which is why this branch
  had no coverage.
- [x] 4.2 `StdioProcessLspTransportTests`: an explicit working directory that does not exist still fails,
  and `LastFailure` names it.
- [x] 4.3 `NamedPipeWorkspacePlaceholderTests`: the same for the pipe transport, asserting the child's own
  stderr echo rather than that the call returned.
- [x] 4.4 `ScratchDirectoryIsolationTests`: asking for the scratch directory does not create it — pinned at
  the source, where the tempting one-line fix would go.
- [x] 4.5 Mutation-check: with the existence test removed, exactly one test in each transport suite fails,
  and it is the new one.

## 5. Design record

- [x] 5.1 Modify the `lsp-client` requirement so the stated behaviour is conditional where it was absolute.
- [x] 5.2 `openspec validate --strict`, then archive.
