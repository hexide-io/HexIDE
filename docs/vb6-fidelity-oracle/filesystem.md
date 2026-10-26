## Filesystem (#1561) — vb6.exe 6.00.8176 (original release), MSVBVM60 6.00.9848 (SP6-level)

All rows are run-time results of `/make` EXEs (#1524).

**Native and p-code agree on every row**, so each one below holds for both. Every row is a run-time result from a compiled EXE run in session 0 in the oracle guest, on NTFS. Each operation ran under `On Error Resume Next`, and its `Err.Number` and `Err.Description` were logged.

### The fixture

The probe made `App.Path\fx` itself with `MkDir`. Inside it:
- `a.txt` holds 3 bytes and `b.txt` 2. The other files hold a byte or two each: `c.htm`, `d.html`, `x.tmp`, `y.tmp`.
- `h.txt` is set `vbHidden`, `r.txt` `vbReadOnly` and `s.txt` `vbSystem`.
- The directories are `sub`, which holds `in.txt`, and `empty`.

A file "open" below means open in the same program.

### CurDir

| Call | Result |
|---|---|
| `CurDir` | String, equal to `App.Path` (the process's working directory). `CurDir$` is String too |
| `CurDir("C")`, `CurDir("c")`, `CurDir("C:\Windows")` | `C:\…`: only the first character counts, in either case |
| `CurDir("")` | the same as `CurDir` |
| `CurDir("Q")`, a drive that doesn't exist | **68** Device unavailable |
| `CurDir("1")` | **5** Invalid procedure call or argument |

### MkDir and RmDir

| Call | Result |
|---|---|
| `MkDir` a new directory | made |
| `MkDir base & "\slash\"`, with a trailing backslash | made |
| `MkDir` an existing directory | **75** Path/File access error |
| `MkDir` onto an existing file | **75** |
| `MkDir` with a missing parent | **76** Path not found. No intermediate directories are made |
| `MkDir ""` | **76** |
| `RmDir` an empty directory | removed |
| `RmDir` a directory that isn't empty | **75** |
| `RmDir` a missing directory | **76** |
| `RmDir` a file | **76** |
| `RmDir base & "\w*"`, with a wildcard | **76**, and the directory survives |
| `RmDir ""` | **76** |

### FileLen, FileDateTime and GetAttr

| Call | Result |
|---|---|
| `FileLen("a.txt")` | Long 3 |
| `FileLen` of a hidden file | Long 1: hidden files are not skipped |
| `FileLen` of a directory | Long 0 |
| `FileLen` of a missing file, or of `""` | **53** File not found |
| `FileLen` with a wildcard | **52** Bad file name or number |
| `FileLen` of a file open for Output with 5 bytes `Print`ed, before `Close` | Long **0**: what is on disk, not what is buffered |
| … after `Close` | Long 5 |
| `FileDateTime(file)` | **Date**, whole seconds, today's date |
| `FileDateTime` of a directory | Date |
| `FileDateTime` of a missing file | **53** |
| `FileDateTime` with a wildcard | **52** |
| `GetAttr` of a normal file just written | Long **32** (`vbArchive`) |
| `GetAttr` of a directory | Long 16 |
| `GetAttr` of `sub\`, with a trailing backslash | Long 16 |
| `GetAttr` of hidden, read-only and system files | Long 2, 1 and 4. `SetAttr` replaces the whole set, so Archive is gone |
| `GetAttr("C:\")` | Long 22 (Directory, System and Hidden) |
| `GetAttr` of a missing file, or of `""` | **53** |
| `GetAttr` with a wildcard | **52** |

### SetAttr

| Call | Result |
|---|---|
| `SetAttr f, vbNormal`, then `GetAttr` | 0 |
| `vbArchive` | 32 |
| `vbReadOnly Or vbHidden Or vbSystem Or vbArchive` | 39 |
| `SetAttr` back to `vbNormal` | 0 |
| `SetAttr f, vbDirectory` or `vbVolume` | **5** |
| `SetAttr` a directory to `vbHidden` | `GetAttr` gives 18: the Directory bit is kept |
| … then to `vbNormal` | 16 |
| `SetAttr` a missing file | **53** |
| `SetAttr` with a wildcard | **52** |
| `SetAttr` a file open for **Append** | **55** File already open |
| `SetAttr` a file open for **Input** | allowed |

### FileCopy

| Call | Result |
|---|---|
| `FileCopy a.txt, e.txt` | copied, length 3 |
| onto an existing file | overwritten |
| onto a read-only target | **75** |
| onto a **hidden** target | **75** |
| from a read-only source | the copy is **32**: the attributes are not copied, and the copy gets Archive |
| from a missing source | **53** |
| into a missing directory | **76** |
| onto a directory, or from a directory | **75** |
| onto itself | **70** Permission denied |
| with a wildcard in the source | **52** |
| from a file open for **Output** | **55** |
| onto a file open for Output | **55** |
| from a file open for **Input** | allowed |

### Name … As …

| Call | Result |
|---|---|
| `Name e.txt As f.txt` | renamed |
| onto an existing file | **58** File already exists |
| from a missing file | **53** |
| onto itself | no error |
| `f.txt As F.TXT` | renamed: `Dir` then lists `F.TXT` |
| a directory | renamed |
| into another directory | moved |
| into a **missing** directory | **53**, not 76 |
| with a wildcard | **52** |
| a file open for Input | **55** |
| a read-only file | renamed |

### Kill

| Call | Result |
|---|---|
| a file | deleted |
| a missing file, or `""` | **53** |
| `*.tmp`, matching 2 files | both deleted |
| `*.zzz`, matching nothing | **53** |
| `q?.dat` | both deleted |
| a **read-only** file | **75** |
| a **hidden** file, or a **system** file | **53**: `Kill` does not see them |
| `*.log` matching a hidden and a normal file | the normal one deleted, the hidden one kept, no error |
| a directory | **53** |
| a file open for Input | **55** |

### Dir

| Call | Result |
|---|---|
| `Dir()` before any `Dir(pattern)` | **5** Invalid procedure call or argument |
| `Dir()` after a listing has returned `""` | **5** |
| `Dir("*.txt")` | read-only files included, hidden and system left out. Order: `a,b,i,ko,n,n2,r,w,w2` |
| `Dir(p, 0)` | the same as `vbNormal` |
| `Dir(p, vbHidden)` | adds hidden files |
| `Dir(p, vbSystem)` | adds system files |
| `Dir(p, vbReadOnly)` | the same as `vbNormal` |
| `Dir(base & "\*", vbDirectory)` | `.`, `..`, then files and directories together, in name order |
| `Dir("*.htm")` | **`c.htm` and `d.html`**: the extension matches through the 8.3 short name |
| `Dir("?.txt")` | one-character names only |
| `Dir("A.TXT")` | `a.txt`: no case, and the name comes back as stored |
| `Dir` and `Dir$` | both String |
| `Dir(base, vbDirectory)` | `fx`, the directory's own name |
| `Dir(base)`, with no `vbDirectory` | `""` |
| `Dir(base & "\")` | the first file, `a.txt` |
| `Dir(base & "\", vbDirectory)` | `.` |
| no match, or a missing directory | `""`, not an error |
| `Dir("")` | the first file in `CurDir`. Here that is `err.log`, the `/make` log |
| `Dir("fx\a.txt")` | resolves against `CurDir` |
| `Dir("C:\", vbVolume)` | `""` here, but the guest's volume has no label, so this decides nothing |
| `Dir(base & "\*", vbVolume Or vbDirectory)` | `.`: with another bit, `vbVolume` acts as `vbDirectory` |
| attribute 1024, or -1 | **5** |
| `Dir(Null)` | **13** Type mismatch |
| `Dir("*.txt")`, then `Dir("*.htm")`, then `Dir()` | `d.html`: a new pattern restarts the listing |
| the listing's file `Kill`ed mid-listing, then `Dir()` | carries on to the next file |

### Deliberate host divergences

| Case | HexIDE behaviour |
|---|---|
| `*.htm` | Matches long names only: `c.htm`, not `d.html`; NTFS 8.3 aliases are not consulted. |
| Unix hidden/system | Uses .NET `File.GetAttributes`: leading-dot names are Hidden, System is never reported. `GetAttr` reports those host bits; `Kill` skips dot-files, and `Dir` excludes them unless vbHidden is supplied. |
| Unix `SetAttr` | Sets attributes the host can hold and drops unsupported bits. Hidden cannot be set or removed by this call (it follows the name); System and Archive are dropped. ReadOnly follows host permissions. |
| Name order | `StringComparer.OrdinalIgnoreCase` on every host. |
| Current directory | Shared with `Open`: project folder initially, then the interpreter-local directory selected by `ChDir`; backslashes become host separators on Unix. Drive-specific `CurDir` is unavailable on Unix (68). |
| Root attributes | `GetAttr` reports host attributes, so roots need not have the oracle guest's Hidden/System bits. |

Named arguments to the five new intrinsics were not measured by this probe and remain explicitly refused pending a probe. An unsaved project still has no initial folder; relative paths and `CurDir` are refused until `ChDir` selects an absolute directory, as with `Open`.

