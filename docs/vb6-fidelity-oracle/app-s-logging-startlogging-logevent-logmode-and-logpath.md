## App's logging: StartLogging, LogEvent, LogMode and LogPath (2026-10-28, #2552)

Measured on vb6.exe SP6, every probe built native and as p-code, which logged the same lines. Each probe is a Standard EXE `Sub Main` under `On Error Resume Next`, logging each statement with `Err.Number`; `Show(v)` logs a value as `[value] TypeName/VarType`.

**What the rows say:**
- Before any logging, `LogMode` is **1**, a Long, and `LogPath` is "". An `App.LogEvent` with no `StartLogging` before it raises nothing, and afterwards `LogMode` is **3** (`vbLogToNT`): the event went to the Windows event log.
- `App.StartLogging path, vbLogToFile` (2) sets `LogMode` 2 and `LogPath` to the path as given. Each `LogEvent` then appends one line: the type word padded to 12 characters (`Information `, `Warning     `, `Error       `), `Application `, the log file's path, `: Thread ID: `, the thread's id, ` ,Logged: `, the message, and CRLF. With no type it is Information. A number is logged as its text. **Type 9 is 380.**
- A file that already exists is appended to.
- **`vbLogToFile Or vbLogOverwrite` (18) does not overwrite.** `LogMode` reads 1, and nothing is written: the old line is all the file holds. That is not what VB6's documentation says; it is what vb6.exe did, in both builds.
- `vbLogOff` (1): `LogMode` 1, `LogEvent` writes nothing, and no file is made.
- `StartLogging "", vbLogToFile`: `LogMode` 2 and `LogPath` **`vbevents.log`**.
- `StartLogging "", vbLogToNT` (3): `LogMode` 3 and `LogPath` "".
- `App.LogPath = "x.txt"` and `App.StartMode = 1` are compile errors, "Can't assign to read-only property".

`<dir>` stands for `App.Path`, replaced in the probe before logging.

**StartLogging to a file, and five events:**

```
StartLogging to file err 0
LogMode [2] Long/3 err 0
LogPath [<dir>\applog.txt] String/8 err 0
LogEvent info err 0
LogEvent warning err 0
LogEvent error err 0
LogEvent no type err 0
LogEvent type 9 err 380
LogEvent number err 0
file err 0 [Information Application <dir>\applog.txt: Thread ID: <id> ,Logged: hello<CRLF>Warning     Application <dir>\applog.txt: Thread ID: <id> ,Logged: careful<CRLF>Error       Application <dir>\applog.txt: Thread ID: <id> ,Logged: boom<CRLF>Information Application <dir>\applog.txt: Thread ID: <id> ,Logged: plain<CRLF>Information Application <dir>\applog.txt: Thread ID: <id> ,Logged: 42<CRLF>]
app-log-file: done
```

**An existing file:**

```
StartLogging to file, existing err 0
LogEvent err 0
file err 0 [old line<CRLF>Information Application <dir>\applog.txt: Thread ID: <id> ,Logged: new<CRLF>]
app-log-append: done
```

**`vbLogToFile Or vbLogOverwrite`:**

```
StartLogging to file Or vbLogOverwrite err 0
LogMode [1] Long/3 err 0
LogEvent err 0
file err 0 [old line<CRLF>]
app-log-overwrite: done
```

**`vbLogOff`:**

```
StartLogging vbLogOff err 0
LogMode [1] Long/3 err 0
LogEvent err 0
file exists False err 0
app-log-off: done
```

**An empty path to a file, and to the event log:**

```
StartLogging " to file err 0
LogMode [2] Long/3 err 0
LogPath [vbevents.log] String/8 err 0
LogEvent err 0
app-log-empty: done
```

```
StartLogging to NT err 0
LogMode [3] Long/3 err 0
LogPath [] String/8 err 0
LogEvent err 0
app-log-nt: done
```

**The defaults, read before logging** (from the App properties probe, on its own page):

```
HelpFile [] String/8 err 0
HelpFile set err 0
HelpFile after [probe.hlp] String/8 err 0
LogMode [1] Long/3 err 0
LogPath [] String/8 err 0
StartMode [0] Integer/2 err 0
TaskVisible [False] Boolean/11 err 0
TaskVisible set False err 0
TaskVisible after [False] Boolean/11 err 0
TaskVisible set True err 0
IMEStatus [0] Long/3 err 0
LogEvent with no StartLogging err 0
LogMode after a LogEvent [3] Long/3 err 0
app-props: done
```
