## SaveSetting from an IDE run, read by the made EXE, and back (2026-11-16, #4070)

**The question.** Does a value a project saves with `SaveSetting` during an F5 run come back from `GetSetting` in the same
project's made EXE, native and p-code, and the reverse? And does `App.Title` or the project's name play any part? #4070
needs the answer before HexIDE's runs and made programs share one settings store off Windows.

**The probe.**
- **Build:** VB6 SP6. The IDE runs were `VB6.EXE /runexit verify.vbp`, which runs the project as F5 does. The made runs
  were `/make`, native code and p-code.
- **The run:** every step ran in the same guest user, one after another, so `HKCU` carried over from step to step.
- **The calls:** each used section `S` and an explicit appname, `HexIdeProbe4070` unless a row says otherwise.
- **Reading the registry directly:** `WScript.Shell.RegRead` of
  `HKCU\Software\VB and VBA Program Settings\<appname>\S\<key>`.
- **The projects:** the IDE project had `Name="ProbeIde"` and `Title="IdeTitle4070"`. The made one had
  `Name="ProbeExe"` and `Title="ExeTitle4070"`. Both had `ExeName32="verify.exe"`.

| Step | Call | Result |
|---|---|---|
| IDE run | `SaveSetting "HexIdeProbe4070", "S", "FromIde", "ide-value"` | `RegRead` of `…\HexIdeProbe4070\S\FromIde` gives "ide-value" |
| made EXE, native | `GetSetting("HexIdeProbe4070", "S", "FromIde", "<missing>")` | **"ide-value"** |
| made EXE, native | the same key under appname `OtherApp4070` | "<missing>": the default |
| made EXE, native | `SaveSetting "HexIdeProbe4070", "S", "FromExeNative", "exe-native"` | saved |
| made EXE, p-code | `GetSetting` of `FromIde`; of `FromExeNative` | "ide-value"; "exe-native" |
| made EXE, p-code | `SaveSetting … "FromExePcode", "exe-pcode"` | saved |
| IDE run | `GetSetting` of `FromExeNative`; of `FromExePcode` | **"exe-native"; "exe-pcode"** |

**`App.Title`:**

| Project | `App.Title` in the IDE | `App.Title` in the made EXE |
|---|---|---|
| `Title="IdeTitle4070"` | IdeTitle4070 | — |
| `Title="ExeTitle4070"` | ExeTitle4070 | ExeTitle4070 |
| no `Title` line, `Name="ProbeNoTitle"` | verify | verify |

- The IDE run saved `SaveSetting App.Title, "S", "TitleKey", …` under `IdeTitle4070`.
- The made EXE, whose title differs, read it back with the explicit appname `IdeTitle4070`, but **not** through its own
  `App.Title` ("<missing>").
- The EXE saved under its `App.Title`, `ExeTitle4070`. An IDE run of the same project, with the same `Title`, read it
  back through `App.Title`.

**Readings:**
- **The store is the appname argument's key under `HKCU\Software\VB and VBA Program Settings`, whoever writes it.** An
  F5 run, a native EXE and a p-code EXE all read what the others wrote, in both directions.
- **Neither the project's name nor its title plays any part** beyond what the program passes as the appname. A different
  appname misses.
- **A program that passes `App.Title` shares only while the IDE and the EXE agree on the title.** With the same project
  file they do: `App.Title` is the `Title` line in both.
- **Without a `Title` line, `App.Title` was "verify" in both,** not the project's `Name`. That matches both the `.vbp`'s
  file name and `ExeName32` here, and these rows don't tell which one it came from.

**Not measured:**
- a second Windows user;
- a made EXE whose `ExeName32` differs from the `.vbp`'s file name, which would separate where `App.Title` comes from
  without a `Title` line.
