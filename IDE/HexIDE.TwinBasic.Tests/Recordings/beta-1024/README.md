# BETA 1024 root initialization

`root-initialization.jsonl` was recorded against a real compiler, BETA 1024, Community Edition, started without the
IDE by `CompilerProcess` (#1689). `LiveCompilerTests.Root_initialization_and_shutdown_are_recorded` made it, with its
recording-folder variable set to this folder. The format is the one `../synthetic/README.md` describes.

| Frame | Direction | What |
|---|---|---|
| 1 | send | pre-initialize, command 22: `96` then `0,1,10000,0,`, with no licence key |
| 2 | receive | its reply: the compiler's process id |
| 3 | send | initialize, command 0: the install's package folder names, each followed by `?`, then the language data |
| 4 | receive | its reply: five ports, then the instance identity |
| 5 | send | shutdown, command 9, request id 2. It is not answered, and the compiler exited with code 0 |

What it leaves out, and why:
- **No project is loaded.** The packages a project asks for would cross in full, and they are not ours to keep.
- **`{language}` is the install's language pack**, sent as a JSON object, for the same reason. A one-entry object in its
  place never completed initialization, as an empty one did not in #1200.
- `{pid}`, the five `{port:…}` values and `{instance}` are run values, replaced after the run, since the ports and the
  instance are known only from frame 4.

`CompilerRecordingTests.Root_initialization_and_shutdown_recorded_on_beta_1024_replay_against_the_root_session` plays
the compiler's side of it against `CompilerRootSession`. It supplies its own language object, and reads the package
list back from frame 3. A change to how HexIDE encodes any of the three requests fails that test.

To record again, for another beta: run the live test with the two variables its documentation names, one for an
install folder and one for a new folder beside this one. Decode the result and read it before keeping it.

The same live test compares each recording it makes with the newest one kept here for that beta or an earlier one,
and fails on a difference, naming each frame that differs. The daily beta check (`.github/workflows/compiler-beta-check.yml`,
#1219) runs it against every new beta. A beta that exchanges what an earlier one did needs no folder of its own: keep
a recording only when it differs, so the folders here are the betas where the exchange changed.
