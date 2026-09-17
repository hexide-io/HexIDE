# Tasks

## 1. The redirect

- [x] 1.1 `UserDataRoot`: the platform default until redirected; `RedirectTo` accepts only an absolute path,
  and refuses once the directory has been read or already redirected, so one session cannot be split across
  two directories.
- [x] 1.2 `UserDataPath` holds one for the process and forwards to it, so its ten callers are unchanged.
- [x] 1.3 `UserDataRootTests`: default, redirect, refused after a read, refused twice, relative and empty
  refused, trailing separator normalised. Against a private instance, never the process's own.

## 2. The command line

- [x] 2.1 `--user-data-dir <path>` in `ServerOptions.Options`. A value starting `--` is another flag, not a
  directory; `/` is not treated that way, because it begins every absolute Unix path.
- [x] 2.2 `Program.Main` reads the working directory before `FixCurrentWorkingDictionary` moves it, and
  `ParseArgs` resolves the project path and the data directory against it.
- [x] 2.3 A missing directory prints the reason and exits with status 2 before anything starts.
- [x] 2.4 Redirect before `DesktopStartup.Register`.

## 3. Documentation

- [x] 3.1 A row and a section in `docs/command-line.md`, including what does not move (logs, add-ins) and
  that relative paths follow the shell.
- [x] 3.2 `docs/language-servers.md` says where the file is read from under the option.

## 4. Verified against the running IDE

- [x] 4.1 Launched from a scratch directory with `--user-data-dir demo-profile spring-tide/SpringTide.vbp`:
  the project opened, the profile directory was created there, the language server connected under the id
  from the profile's own `lsp-servers.json`, and the user's real per-user directory was unchanged.
- [x] 4.2 `--user-data-dir`, `--user-data-dir --newproject` and `/user-data-dir` each exit with status 2 and
  the reason, starting nothing.
- [x] 4.3 `--help` lists the option and still fits beside the mark.
