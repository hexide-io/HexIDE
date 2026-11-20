# twinBASIC oracle harness

Makes the stock twinBASIC IDE save a project after one known change, so the saved `.twinproj` files can be measured
(#5139) and the container written down from them (#5140, `docs/twinbasic-oracle/`). It is a developer tool. Nothing
here ships, and CI never runs it.

## The compiler is off-limits

**No decompiling, disassembly, reading its binaries, instrumenting, hooking, patching or shimming the twinBASIC
compiler, for any reason.** The oracle is the files the IDE saves. The harness launches the compiler only because
the IDE does, and never looks at it: it clicks the IDE's front end, puts text in through the editor's API, answers
the IDE's file dialogs and reads the files that land on disk. The maintainer decided on 2026-11-20 that the IDE may
serve as an oracle, as `vb6.exe` does, and twinBASIC's author has said that's fine (#5138).

## How it reaches the IDE

The IDE's front end is a WebView2 page. `tb.ps1` gives it a DevTools port without touching the IDE's files:

- It makes a copy of the WebView2 runtime under a work folder, with junctions and hard links, and puts `shim/` in
  place of its `msedgewebview2.exe`.
- It starts the IDE with `WEBVIEW2_BROWSER_EXECUTABLE_FOLDER` pointing at the copy.
- The shim starts the real browser with `--remote-debugging-port` added. It reads the real browser's path and the port
  from `tboracle-shim.cfg` beside it, because the browser doesn't inherit the launcher's environment.
- It is built as a GUI program (`WinExe`). A console build opens a terminal window for every browser process.

`act.mjs` and `run.mjs` then attach with Playwright's `connectOverCDP`, from `playwright-core` 1.65.0 (Apache-2.0, read
from the npm registry at that version; it downloads no browsers). `dialog.ps1` answers the IDE's native file
dialogs through UI Automation.

## Where it runs

In a Hyper-V VM, never on a desktop someone is using: the IDE's windows open on whichever desktop runs it. `vm.ps1`
copies the harness, `node.exe` and a pinned twinBASIC zip into `C:\tbo` in the VM over PowerShell Direct. It runs
GUI steps in the signed-in user's session through a one-off scheduled task, and the Node drivers in session 0, since
DevTools answers on the VM's loopback.

```powershell
dotnet publish shim/shim.csproj -c Release -o shim/bin/publish
npm ci
.\vm.ps1 -Action push -CredentialFile <guest credential> -Payload <folder with node.exe and the zip> -Beta 1044
```

Pin a beta, and record its number with every fixture. Releases are at `twinbasic/twinbasic` as
`twinBASIC_IDE_BETA_<n>.zip`. Check the download against the digest the release reports.

On a beta's first start the IDE shows its licence, an error-reporting question and its options. A session may
accept that licence for oracle work in the VM only, and notes the beta and date on #5139 (Jen, Desk card
q-5138-twinbasic-oracle). Decline error reporting, so nothing leaves the VM, and save the options at their
defaults. Then checkpoint the VM.

## Making saves

`scenario.ps1` is one run. It copies a baseline project to where the IDE expects it, starts the IDE on it, and has
`run.mjs` make each change in turn, saving after each. It keeps every save, the bytes it started from, and a
`run.json` with the beta, the WebView2 and Windows builds, and each save's SHA-256. With no baseline, the first change
must be `new`, which makes a project from the template; `dialog.ps1` answers its first Save dialog.

The committed fixtures 02 to 10 were made from `IDE/HexIDE.TwinBasic.Tests/Fixtures/tb1044-01-new-project.twinproj`,
so put that file in the VM as the baseline to make them again; a fresh `new` run gives a project with other GUIDs.

```powershell
.\vm.ps1 -Action put -CredentialFile <guest credential> -From ..\..\IDE\HexIDE.TwinBasic.Tests\Fixtures\tb1044-01-new-project.twinproj -To C:\tbo\runs\1044\baseline.twinproj
$run = '& C:\tbo\harness\scenario.ps1 -Install C:\tbo\tb\beta-1044 -Baseline C:\tbo\runs\1044\baseline.twinproj ' +
       '-Changes none,add-module,rename-module,remove-module,add-folder,add-form -Out C:\tbo\runs\1044\tree'
.\vm.ps1 -Action gui -CredentialFile <guest credential> -Command $run
.\vm.ps1 -Action fetch -CredentialFile <guest credential> -From C:\tbo\runs\1044\tree -To <host folder>
```

`run.mjs` lists the changes it knows. A session's first save also carries what reopening does, so run `none` first
and diff each save against the one before it.

## Measuring

`tbdump.py dump <file>` prints every field of a container with its offset and width. `tbdump.py diff <a> <b>` shows
what changed between two saves, by entry and field. The layout it walks is the hypothesis being tested, not a source.
