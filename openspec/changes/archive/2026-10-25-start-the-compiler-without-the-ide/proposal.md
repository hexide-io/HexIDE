## Why

Every live run of the compiler adapter so far started the compiler through a copy of the stock IDE's native host (#1204). A bridge (#1690) must start it itself. The launch was the missing piece: started as `--ide=PID`, the executable is the IDE front end's page server, and its root refused every key (#1689). The documentation contributor's launcher, tblaunch 9.0, showed the compiler's own mode. HexIDE now uses it, and the whole exchange has been measured on BETA 1024 with HexIDE's own client.

## What Changes

- `CompilerProcess` starts `bin\STUB.exe --compiler=0` and reads the port and key from the 42-byte record. It chooses the stub from the project's `Settings`, holds the compiler in a kill-on-close job object, and waits for it to exit after a shutdown request.
- `CompilerRootSession` and `CompilerPreparedProject` send the shutdown request, command 9, on the root's next request id. Project preparation takes its ids from the root, too.
- `ChildProcessJob` in `HexIDE.Core` is the job object. Any child process HexIDE launches can use it, and #406 will use it for language servers.

Only facts are taken from tblaunch. Its own sources state no licence, and none of its code is in this tree.

## Impact

- The compiler adapter's project now references `IDE/HexIDE.Core`, which has no dependencies of its own.
- `LiveCompilerTests` is opt-in through an environment variable naming an install folder, and CI skips it visibly.
