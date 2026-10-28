# VB6 fidelity oracle — behaviour verified against `vb6.exe`

HexIDE's in-box interpreter aims for **runtime-execution fidelity**: it reproduces VB6's *observable* behaviour
without depending on `MSVBVM60.DLL` (see the CST-not-AST boundary in `CLAUDE.md`). The only trustworthy source
of truth for "what does VB6 actually do here" is **the real compiler**. This document records the facts pinned
against real `vb6.exe` during the interpreter type-system build-out (interpreter-core Phase 2), plus the
**harness** for running the oracle cleanly — future phases (intrinsics: `CInt`/`CLng`/`CDate`/`Format`/`Rnd`;
date functions; graphics) should extend it the same way.

> **Why this matters:** the oracle repeatedly overturned "documented" assumptions. Building from memory / the
> language reference alone would have shipped several wrong behaviours (see *Assumptions overturned* below).

- [A UserControl's focus and access keys](usercontrol-focus-and-keys.md) — #1631 part B, SP6 native and p-code, real keystrokes.
- [A UserControl’s extender members at run time](usercontrol-extender-members.md) — #1631 part A, SP6 native and p-code; includes the supervisor’s handle decision.
- [Key events](key-events.md) — #2325, real input, SP6 native and p-code.
- [A Timer’s default Interval](timer-default-interval.md) — #2068, SP6 native and p-code.

---

## The oracle

- **Binary:** `C:\Program Files (x86)\Microsoft Visual Studio\VB98\VB6.EXE` (the `Vb6ToolchainService`
  auto-discovers it; the `VB6_EXE` env var overrides). Windows + a VB6 install required — this is a dev-time
  fidelity check, not part of the shipped product. The install does **not** have to be on your own machine:
  see [the scripted harness](#the-scripted-harness--scriptsvb6-oracleps1), which drives a copy in a Hyper-V
  guest just as happily.
- **Method:** compile a tiny `Sub Main` console-less program with `VB6.EXE /make`, run the produced `.exe`, and
  read back a file it wrote with `TypeName(expr)` + the value for each case.

### Which VB6 the oracle is (#1524)

**Until 2026-10-23 the oracle was VB6's original release, with an SP6-level runtime. From 2026-10-24 it is SP6
throughout.** Sections dated before 2026-10-24 that call the oracle "6.00.8176 (the original release)" were wrong about the compiler.
6.00.8176 is the original release build, and every other file in the guest's VB98 folder agreed. They now say "the
original release". SP6 went in on 2026-10-24, from the Microsoft-signed `Vs6sp6B.exe` the maintainer supplied, and the
`VisualStudio\6.0\ServicePacks` key now reads `latest = 6`.

| File | Until 2026-10-23: the original release | From 2026-10-24: SP6 |
|---|---|---|
| `VB6.EXE`, the IDE and `/make` | 6.00.8176 | **6.00.9782** |
| `VBA6.DLL`, the engine the IDE runs code with | 6.0.8169 | **6.0.9782** |
| `C2.EXE`, the native code generator | 12.00.8041.0 | **12.00.8783.0** |
| `LINK.EXE` | 6.00.8168.0 | **6.00.8447.0** |
| `MSVBVM60.DLL` (SysWOW64), the runtime a built EXE runs on | 6.00.9848, SP6-level, supplied by Windows | 6.00.9848, unchanged |

**So which build a row measured depends on what it read**, not on its date alone:

- **What a built EXE printed at run time**, native or p-code, came from MSVBVM60 6.00.9848, which SP6 did not change.
  Those rows stand as written, whenever they were measured.
- **A compile error** comes from VB6.EXE and VBA6.DLL, not the runtime, even when the program was built with `/make`.
  Before 2026-10-24, that was the release build.
- **Anything run in the IDE** (`/runexit`, the Immediate window, `Debug.Assert`, break mode, the reset prompt), and
  everything the designer drew or saved, ran on the release VB6.EXE and VBA6.DLL before 2026-10-24.
- **Native code** came from the release C2.EXE and LINK.EXE before 2026-10-24. Where a native result could depend on
  code generation rather than the runtime, the row measured the release build.

Measuring the release-build rows again on SP6 is #1663, and what it has measured so far is below. A row SP6 changes
keeps both answers, since a release-versus-SP6 difference is a finding in its own right. The release state is kept as
the Hyper-V checkpoint *VB6 6.00.8176 release, before SP6 (#1524)*, so a comparison can still be made. A section
measured from 2026-10-24 on names the SP6 builds.

#### Measured again on SP6 (2026-10-24, #1663)

**One property changed, and nothing else.** A new Data control's `Connect` is `"Access 2000;"` on SP6, where the
release build wrote `"Access"` (*The size of a control added from the toolbox*, #1473). Every other row below gave the
release build's answer, compile errors, the IDE and the designer included. There were five kinds of run.

**Every probe project kept from the sections below, rebuilt with `/make` on SP6: 2,572 projects**, native and p-code.
- Each printed what it printed on the release build, line for line, and each compile error was the same message at the
  same line.
- 2,550 were compared with their release-build logs by script. The other 22 had no saved log and were read against their
  section's tables instead: #1386's and #1431's compiled probes, two of #435's, and #1471's text-compare probe.
- Two draft folders of #1562's, superseded a minute after they were made, and three of #1471's bisection projects were
  left out.
- **So the compile-error tables stand**: *Named arguments* (#1465), *CVar* (#1466) and *Too few arguments to an
  intrinsic* (#1512) among them. So do the native-code rows of every section here.
- A rerun has to give a probe its rig back:
  - #1562's settings probes read registry keys its harness seeds first;
  - #1521's locale probe runs under de-DE.
  Without those, each differed, and with them each was identical.

| Section | Projects |
|---|---|
| Named arguments (#1465) | 686 |
| Conditional compilation (#1112) | 360 |
| Collection (#1471), with *An empty Collection, and assigning an item* (#1492) | 220 |
| A name declared twice (#1622, #1499) | 178 |
| The intrinsics signature help had no entry for (#1475) | 144 |
| DefType: default typing by first letter (#169) | 102 |
| Too few arguments to an intrinsic (#1512) | 100 |
| Static (#1491) | 88 |
| As New (#435) | 72 |
| A D exponent in a source literal (#1611) | 72 |
| Random and Binary files, Get, Put and Len (#1646) | 58 |
| A numeric literal ending in its point (#1615) | 52 |
| Indexing a call's result (#1476) | 42 |
| GetSetting, SaveSetting, DeleteSetting and GetAllSettings (#1562) | 39 |
| Chr, Asc, ChrW and AscW (#1134) | 38 |
| LOF, Loc, Seek and the Input function (#1645) | 38 |
| A Let into an array variable (#998, #1588) | 26 |
| Write # and Input # (#1521) | 22 |
| StrComp, StrConv, Environ, Switch and Error() (#1560) | 21 |
| A Const declared with a type character (#1614) | 21 |
| ReDim Preserve (#1459) | 20 |
| InStr and InStrRev at the ends, and Option Compare Text (#1567) | 18 |
| CVar (#1466) | 16 |
| IIf and Choose (#1461) | 16 |
| A String read as a Date (#967) | 16 |
| Like (#1547) | 14 |
| An array element passed ByRef (#1455) | 14 |
| A control's Name and Index, at run time (#1431) | 14 |
| A UDT field passed ByRef (#1451) | 12 |
| When Err is cleared, and what a running handler traps (#877) | 12 |
| A Let into an object variable (#1493) | 10 |
| `Like` in `#If` and `#Const` (#1598) | 7 |
| A Function's return, converted where it is assigned (#1444) | 6 |
| A member indexed through its object (#1386) | 4 |
| An undeclared variable passed ByRef (#1505) | 4 |
| A store into a UDT field (#221) | 2 |
| Print # and the ANSI best fit (#1511) | 2 |
| Filesystem (#1561) | 2 |
| `Time`, and eight Dies rows that already ran (#1633) | 2 |
| CCur and CDec of a String (#1531), recorded on the issue | 2 |

**The IDE, with `/runexit` in session 0.** *Debug.Assert* (#1467): all 25 IDE runs. Each logged what it logged on the
release build, waited where it waited (break mode) and exited with the same code. Its compile errors and its compiled EXE
were rebuilt too.

**A spot check of IDE rows whose probes were not kept.**
- **The programs.** 17, rebuilt from the tables of *An object used as a value* (#174), *Resume Next into a block*
  (#839), *What a condition is* (#847), *Overflow edges* (#234), *A String in arithmetic* (#1047) and *An unhandled
  run-time error* (#1011). Each was run native, as p-code and in the IDE.
- **The result.** Every one gave its table's answer in each mode. That includes every row where native code and the IDE
  differ:
  - For's order of evaluation, and a raised start, limit or step;
  - Select Case on a selector that raises, or on an object with no default member;
  - unary minus at a type's minimum;
  - `Debug.Print k`, which raises 438 only in the IDE;
  - a class's unhandled error under *Break in Class Module*: the run stops in the class in the IDE, and the caller's
    handler catches it when compiled.
- **Measured for the first time:** a p-code build drops `Debug.Print k` as native code does, so it raises nothing there.

**The console: the Immediate window, the reset prompt and Save Project As.**
- *A `?` line in the VB6 IDE's Immediate window* (#1386): every row the same. Three rows were measured for the first
  time, and that section now carries them.
- *The reset prompt* (#1329): the three scripts behind its tables, run again. Each step logged the same windows, owners,
  texts, buttons and enabled states, and the same mode and Timer behaviour, as on the release build.
- *What Save Project As does to the paths in a project file* (#1406) and *Where and how VB6 writes a project's
  description* (#42): every project file VB6 saved is byte-identical to the release build's.

**The designer and the IDE's saves,** run again on the console with each section's own rig. Where a section had been
driven by hand, its steps were replayed from the console agent's log.

| Section | SP6 |
|---|---|
| *What VB6's Properties window lists for a UserControl and a PropertyPage* (#1585) | the same: 51 and 33 rows, every capture byte-identical |
| *What VB6's Add Form saves* (#1532) | the same: every saved file byte-identical |
| *A control VB6 knows and HexIDE carries stacks with the rest* (#1498) | the same saves, byte-identical; that section records what SP6's IDE does on opening VB6's Web Browser template |
| *The size of a control added from the toolbox* (#1473) | every size and position the same; a new Data control's `Connect` is `"Access 2000;"`, not `"Access"` |
| *The name VB6's designer gives a new control* (#540) | the same, every row |
| *Make Project Group* (#746) | the same |
| *The TabIndex a new or pasted control takes* (#925) | the same: the save is byte-identical |
| *Where VB6's designer puts a control's block* (#1481) | the same: every control block |
| *Drawn in the VB6 IDE's designer* (#926) | the same, every row |

**Not measured again:**
- *The reset prompt*'s rows with *Notify Before State Loss* changed, since setting that option changes the IDE for
  everyone using the guest;
- the IDE rows of sections whose probes were not kept, beyond the spot check above, *Several forms* (#822) and
  *Unload Me* (#973) among them, and what the IDE writes for member attributes (#273).

Both are #1675.

### Reaching the VB6 *IDE*, not just the compiler (2026-09-20)

Everything above answers "what does VB6 **accept**", which `VB6.EXE /make` can do with no desktop at all.
"What does VB6 **write**" needs the IDE, and the IDE needs a window — which the obvious route does not
give you.

**The trap.** A GUI process started through PowerShell Direct lands in the guest's **session 0**. It has no
window (`MainWindowHandle` is `0`, `MainWindowTitle` empty) and `AppActivate` reports the process as not
found, so SendKeys reaches nothing. Nothing about this is visible from the host: the process starts, stays
running, and does nothing.

**What works, in two parts.** First there has to be a console session at all — on a VM nobody logs into,
there is none (`query user` answers `No User exists for *`). Autologon provides one:

```powershell
$k = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'
Set-ItemProperty $k -Name AutoAdminLogon    -Value '1'
Set-ItemProperty $k -Name DefaultUserName   -Value '<guest account>'
Set-ItemProperty $k -Name DefaultDomainName -Value '<guest computer name>'
New-ItemProperty  $k -Name DefaultPassword  -Value '<password>' -PropertyType String -Force
```

**Restart from inside the guest** (`shutdown /r`), never with `Restart-VM`. That is not a preference: a
host-side restart discarded every one of these writes, twice, and the reverted values read exactly like
Windows rejecting the logon and disabling autologon itself — the wrong diagnosis, and a convincing one. The
registry hive had simply not been flushed. `RegistryKey.Flush()` before restarting is belt and braces.

Then run the GUI work **in** that session, which is the part that needs care.

**Do not drive it with a per-step interactive scheduled task.** That is the obvious route, it is what this
section used to recommend, and it is the one that fails. It worked once, immediately after the autologon
reboot. Later the same task stopped running: `schtasks /run` reported success, `TaskScheduler/Operational`
logged **325** (queued) and **110** (launched) and then *nothing* — no 129, no 200, no failure event — and
the action never executed. A trivial control task (`cmd /c echo hi > C:\probe\hello.txt`) behaved
identically, which is what ruled out the probe script. From PowerShell Direct the guest looked entirely
healthy: `query session` reported `console  <account>  1  Active`, Explorer was running in session 1, and
`LogonUI` was not.

**The cause was visible only from the host.** Hyper-V hands out the guest's framebuffer with no cooperation
from inside it:

```powershell
$vm  = Get-CimInstance -Namespace root\virtualization\v2 -ClassName Msvm_ComputerSystem -Filter "ElementName='Win10'"
$set = Get-CimAssociatedInstance -InputObject $vm -ResultClassName Msvm_VirtualSystemSettingData |
       Where-Object VirtualSystemType -like 'Microsoft:Hyper-V:System:Realized'
$svc = Get-CimInstance -Namespace root\virtualization\v2 -ClassName Msvm_VirtualSystemManagementService
$r = Invoke-CimMethod -InputObject $svc -MethodName GetVirtualSystemThumbnailImage -Arguments @{
    TargetSystem = [Microsoft.Management.Infrastructure.CimInstance] $set
    WidthPixels  = [uint16] 1024
    HeightPixels = [uint16] 768
}
# $r.ImageData is raw RGB565, row-major. Copy it row by row into a Format16bppRgb565 Bitmap
# (mind the stride, which is not width * 2) and save it as PNG.
```

The console was sitting on the **OOBE privacy wizard** — *"Let Microsoft and apps use your location"* —
which Windows layers over the desktop on a first interactive sign-in. The session was genuine and the shell
was running underneath it, but nothing a new process did could reach the screen. **Reach for the framebuffer
first whenever a guest looks alive from PowerShell Direct and behaves as though it is not.** It is one call,
and it answers a question no amount of probing from inside the guest will.

Suppress the wizard before it costs a second session:

```powershell
New-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\OOBE' -Name DisablePrivacyExperience `
                 -Value 1 -PropertyType DWord -Force
```

**Then start one long-lived agent at logon, rather than a task per step.** A `.vbs` in the account's Startup
folder launches it with no console window:

```vbs
CreateObject("WScript.Shell").Run "powershell -NoProfile -ExecutionPolicy Bypass -File C:\probe\agent.ps1", 0, False
```

`agent.ps1` loops: write a heartbeat, look for an action file, perform the action it names, append to a log,
repeat. The host drops an action in and tails the log. Beyond working at all, this beats the task on three
counts — the interactive session is established once instead of per step; an `exec:<powershell>` action lets
the rig itself be changed without another reboot; and the heartbeat distinguishes *the agent is not running*
from *the action ran and did nothing*, which the task route could not.

Three traps in the agent, all measured:

- **Register exactly one autostart.** A Startup-folder shim *and* an `HKCU\…\Run` value both fire, and two
  pollers then race for the same action file.
- **Round-trip anything the rig compares against itself.** The inventory writer and reader disagreed about
  one character:

  ```powershell
  '{0}`t{1}' -f $k, $v     # single-quoted: a literal backtick followed by t
  $line.IndexOf("`t")      # double-quoted: a real tab
  ```

  So every baseline loaded empty and the first diff reported every file on the machine as new. That reads as
  a dramatic result and measures nothing. The rig now logs *N files in, N read back* and says loudly when the
  two disagree.
- **A window started within 200 seconds of the last input never gets the foreground** (measured 2026-10-22,
  #1340). That is Windows' `ForegroundLockTimeout`, which is 200000 ms by default. A probe that drives itself with
  `SendKeys` then types into whatever had the foreground, so it gets **nothing, and no error**. Its log shows
  every step and none of the events the keys should have raised. The probe's own `AppActivate` doesn't help, and
  neither does WScript.Shell's from the agent, since the agent has no window of its own and can't hand over a
  foreground it doesn't hold. A batch of probes run back to back shows it plainly: the first works, and the rest,
  each started within 200 s of the one before, log nothing. `SystemParametersInfo(SPI_SETFOREGROUNDLOCKTIMEOUT)`
  fails for the same reason. So the value is set in the account's registry instead, which takes effect at the
  next sign-in:

  ```powershell
  Set-ItemProperty 'HKCU:\Control Panel\Desktop' -Name ForegroundLockTimeout -Value 0 -Type DWord
  ```

  It was set on 2026-10-22, from an `exec:` action in the console session. Until the guest has signed in again,
  leave 200 s without input before each probe that sends keys.

Two details of the scheduled-task route are kept, because they cost a run each and the route is still worth
having for a genuine one-shot: the user must be domain-qualified (`COMPUTER\account`, backslash included) or
`schtasks /run` fails with `ERROR: Element not found.`, which names nothing; and `/it` needs **no** `/rp` —
passing a password alongside it is what produced that error. Keeping the password off the command line is
what this file tells you to do everywhere else anyway.

The guest is a disposable, isolated oracle VM; autologon on a machine that is not is a different decision.

### The scripted harness — `scripts/vb6-oracle.ps1`

Everything below this section is what the script automates. **Prefer the script**; keep reading only if you
need to do something it does not cover, or to understand why it does what it does.

```powershell
.\scripts\vb6-oracle.ps1 'CByte(100) + CByte(100)', 'CByte(200) + CByte(100)', '5 / 2'

Expression              Value Type    Err
----------              ----- ----    ---
CByte(100) + CByte(100) 200   Byte
CByte(200) + CByte(100)                 6
5 / 2                   2.5   Double
```

You give it expressions; it compiles them into a `Sub Main`, runs the real `vb6.exe`, and reports
`value | TypeName` — or `ERR<n>` where VB6 raised. `-Path probes.txt` reads one expression per line
(`'` comments and blanks skipped, `label: expression` to name a row). Objects come back on the pipeline,
so `| Format-Table`, `| Export-Csv`, `| Where-Object Err` all work.

**Two ways to reach VB6.** `-Local` uses an install on this machine. With no switch it opens a
**PowerShell Direct** session to a Hyper-V guest (`-VMName`, default `Win10`) — which is how the
maintainers run it, since VB6 is a 1998 Windows-only installer that few people want on their daily
driver. PowerShell Direct runs over the VMBus, so the guest needs **no network at all** — an isolated
switch with no IP route is fine.

Guest side, once:

```powershell
# in the guest, as an existing admin
$pw = Read-Host "password" -AsSecureString
New-LocalUser -Name hexoracle -Password $pw -PasswordNeverExpires
Add-LocalGroupMember -Group Administrators -Member hexoracle   # PowerShell Direct admits admins only
```

Host side, once:

```powershell
New-Item -ItemType Directory -Force "$env:USERPROFILE\.hexide" | Out-Null
Get-Credential | Export-Clixml "$env:USERPROFILE\.hexide\win10.cred"
```

`Export-Clixml` encrypts under DPAPI, so the file is readable only by that Windows account on that
machine — the password never lands in a script, a command line or a shell history. Override the location
with `-CredentialPath`.

**Getting the corpus out of the guest.** The round-trip tests need VB6's `VB98\Template` tree, and
`Copy-VMFile` only goes host→guest. Pull it the other way through the same session:

```powershell
$s = New-PSSession -VMName Win10 -Credential (Import-Clixml "$env:USERPROFILE\.hexide\win10.cred")
Copy-Item -FromSession $s -Recurse `
  -Path 'C:\Program Files (x86)\Microsoft Visual Studio\VB98\Template' `
  -Destination "$env:USERPROFILE\.hexide\vb6-template"
Remove-PSSession $s
```

Then set `HEXIDE_ROUNDTRIP_CORPUS` to that path. **This matters more than it looks:** without it
`SerializationCorpusTests` falls back to `demo/`, whose files are HexIDE-authored and filtered out by
`IsHexIdeAuthored`, so the gate passes **vacuously** — a serialization regression goes green. The corpus
is Microsoft's redistributable-restricted content and must stay out of the repository.

> **Sanity-check the harness before trusting a new answer.** Run a handful of rows that are already in
> the tables below — `CByte(100) + CByte(100)` → `200 | Byte` and `CByte(200) + CByte(100)` → `ERR6` are
> a good pair, because the second proves `Byte + Byte` really does stay `Byte`. If those reproduce, the
> loop is sound and a surprising new result is VB6 being surprising rather than the harness lying.

### Harness (reusable recipe) — the manual version

What `vb6-oracle.ps1` does internally, kept because the reasons are worth knowing when a probe misbehaves.

Two lessons learned the hard way:

1. **Use absolute paths + `CreateNoWindow`.** A relative `.vbp` path (or launching via a shell) makes `VB6.EXE`
   pop its GUI instead of doing a headless `/make`. Drive it through `System.Diagnostics.ProcessStartInfo`
   with `UseShellExecute=false`, `CreateNoWindow=true`, and an **absolute** `.vbp` path + `/out <errlog>`.
2. **Wrap every probe in `On Error Resume Next`.** A runtime error inside the probe (e.g. an overflow) pops a
   **modal error dialog on the user's screen** and blocks the run until it's dismissed. Capture `Err.Number`
   per case instead, so overflow is *data* (`ERR6`), not a modal. (This is exactly how the `Byte+Byte`
   overflow was found — the first, un-guarded probe hung on a `Runtime Error 6` modal.)
3. **The writer sub needs its own guard, and must print on every path.** `On Error Resume Next` is
   **per-procedure**, so `Main`'s guard does not extend into the helper that formats the row. `CStr(Null)`
   raises 94 and `CStr(<object>)` raises 438 — both perfectly good probe results — and an unguarded helper
   simply returns without printing. The case then **vanishes from the output**, which reads like a case you
   never ran rather than one that went wrong. Test `Null + 1` against any new harness: it should come back
   `Null | Null`, not disappear. Count the rows you get against the cases you sent.

`Module1.bas` shape:

```vb
Attribute VB_Name = "Module1"
Sub WT(f As Integer, label As String, v As Variant, en As Long)
    If en <> 0 Then Print #f, label & "=ERR" & en Else Print #f, label & "=" & v & "|" & TypeName(v)
End Sub
Sub Main()
    Dim f As Integer, v As Variant
    f = FreeFile
    Open "C:\...\out.txt" For Output As #f
    On Error Resume Next
    Err.Clear: v = 5.5 Mod 2: WT f, "mod", v, Err.Number   ' one line per case
    Close #f
End Sub
```

`verify.vbp` shape: `Type=Exe`, `Module=Module1; Module1.bas`, `Startup="Sub Main"`, `ExeName32="verify.exe"`.

Driver (PowerShell): start `VB6.EXE /make "<abs>\verify.vbp" /out "<abs>\err.log"` with `CreateNoWindow`, wait +
kill-on-timeout, then `Start-Process verify.exe -WindowStyle Hidden -PassThru -Wait`, then read `out.txt`.

**Probes that use a class (`Class_Initialize`/`Terminate`, objects) — three gotchas** (learned building the
Phase-4.2 refcount oracle):
1. The `.vbp` **must** carry the OLE Automation reference line, or the compiler reports *"User-defined type not
   defined"* for your own class: `Reference=*\G{00020430-0000-0000-C000-000000000046}#2.0#0##OLE Automation`.
   Copy a known-good `.vbp`/`.cls` header from `demo/*/` rather than hand-authoring the class-header bytes.
2. VB6 needs **CRLF + ASCII** source. The `Write` tool emits LF; convert before `/make`
   (`($c -replace "\`r\`n","\`n") -replace "\`n","\`r\`n"` then `WriteAllText(..., ASCII)`), or the module fails to
   load (same misleading *"User-defined type not defined"*).
   *Narrowed 2026-10-22 (#1020):* this holds for a class and a form, whose designer block must be CRLF. A `.bas` with
   lone-LF or lone-CR line ends loads and runs; see "A lone CR or LF in VB6 source".
3. Launching `vb6.exe` trips the shell sandbox — run the make/run PowerShell with `dangerouslyDisableSandbox:
   true` (it only writes to the scratch dir; this is the authorized dev-time toolchain). Avoid `Remove-Item` near
   the `C:\Program Files (x86)\…` path in the same command — the path-guard blocks it; let `/make` overwrite and
   `For Output` truncate instead.

---

## Extending the oracle (future phases)

Phase 3 (intrinsics) and beyond should verify, at minimum:
- Conversions: `CInt`/`CLng`/`CByte`/`CCur`/`CDec`/`CDbl`/`CSng`/`CDate` — rounding, ranges, overflow codes.
- `Rnd`/`Randomize` — the exact 24-bit LCG seed + first outputs (pin constants only after oracle confirmation).
- `Format`/`Format$` — the date/number/currency mask mini-language.
- Date library: `DateAdd`/`DateDiff`/`DatePart`/`Year`/`Month`/`Weekday`/`Now`, and how they treat the epoch.
- String funcs edge cases (`InStr` start arg, `Mid` boundaries, `Val` parsing).

Reuse the `On Error Resume Next` + `TypeName` harness above; keep the probe `.bas`/`.vbp` under a scratch dir,
never in the tree.

---


## Recording a measurement

Record the probe, value, type and error, along with the compiler/runtime versions, date and provenance.
Keep overturned conclusions and surprising rows; record what resisted explanation and what remains
unmeasured. Never replace a measured oddity with an inferred rule.

Build both native code and p-code wherever their answers could differ. HexIDE follows p-code, the
build the VB6 IDE runs; a native-only row remains provisional until p-code has answered too. See
[the two build modes and their probes](resume-next-into-a-block.md#a-new-axis-native-code-and-p-code)
and [native compiler switches](the-native-builds-hidden-switches-and-where-vb6exe-finds-c2-and-link.md).
Add new findings to the subject file, and add a subject to the index when needed.

This in-tree split prepares the move decided in #1216: `hexide-io/vb6-behaviour`, with measurements
under CC BY 4.0 and probe code under MIT. The neutral repository will be created after this split lands.

The migration audit is `python3 scripts/check-oracle-split.py` from the repository root. It compares
all table rows verbatim and checks the original prose and probes against the pinned pre-split revision;
an optional revision argument selects another baseline. This audits the split, rather than new measurements
added afterwards.

## Subject index

- [Verified findings](verified-findings.md#verified-findings)
- [Assumptions overturned by the oracle](assumptions-overturned-by-the-oracle.md#assumptions-overturned-by-the-oracle)
- [`Class_Terminate` lifecycle (interpreter-advanced Phase 4.2)](class_terminate-lifecycle.md#class_terminate-lifecycle-interpreter-advanced-phase-42)
- [Custom events — `Event` / `RaiseEvent` / `WithEvents` (interpreter-advanced Phase 5)](custom-events--event--raiseevent--withevents.md#custom-events--event--raiseevent--withevents-interpreter-advanced-phase-5)
- [`End` fires no `Class_Terminate` (interpreter-debugger reset)](end-fires-no-class_terminate.md#end-fires-no-class_terminate-interpreter-debugger-reset)
- [Relational comparison + `ChDir`/`ChDrive` (2026-08-03 gap-audit fixes)](relational-comparison--chdirchdrive.md#relational-comparison--chdirchdrive-2026-08-03-gap-audit-fixes)
- [Control arrays (2026-08-10)](control-arrays.md#control-arrays-2026-08-10)
- [Option buttons — `Value` is a Boolean, and the group can be empty (2026-08-22, #95)](option-buttons--value-is-a-boolean-and-the-group-can-be-empty.md#option-buttons--value-is-a-boolean-and-the-group-can-be-empty-2026-08-22-95)
- [Two controls of one name (2026-10-12, #854)](two-controls-of-one-name.md#two-controls-of-one-name-2026-10-12-854)
- [A form-based game's runtime surface (2026-10-12)](a-form-based-games-runtime-surface.md#a-form-based-games-runtime-surface-2026-10-12)
- [A form as an object: its names, its lifecycle and its predeclared instance (2026-10-14, #822)](a-form-as-an-object-its-names-its-lifecycle-and-its-predeclared-instance.md#a-form-as-an-object-its-names-its-lifecycle-and-its-predeclared-instance-2026-10-14-822)
- [Assigning to a control property (2026-10-12, #768)](assigning-to-a-control-property.md#assigning-to-a-control-property-2026-10-12-768)
- [A list's members: ListCount, List(i), AddItem's index and RemoveItem (2026-10-22, #821)](a-lists-members-listcount-list.md#a-lists-members-listcount-listi-additems-index-and-removeitem-2026-10-22-821)
- [A ComboBox's Text, at each Style, from code (2026-10-22, #926)](a-comboboxs-text-at-each-style-from-code.md#a-comboboxs-text-at-each-style-from-code-2026-10-22-926)
- [A ComboBox's Click, Change and DropDown, with real input (2026-10-22, #1429)](a-comboboxs-click-change-and-dropdown-with-real-input.md#a-comboboxs-click-change-and-dropdown-with-real-input-2026-10-22-1429)
- [A Simple Combo's list, drawn and clicked (2026-10-23, #1603)](a-simple-combos-list-drawn-and-clicked.md#a-simple-combos-list-drawn-and-clicked-2026-10-23-1603)
- [Out of stack space — Error 28 (2026-08-22, #80)](out-of-stack-space--error-28.md#out-of-stack-space--error-28-2026-08-22-80)
- [Infinity, NaN, and division by zero (2026-08-22)](infinity-nan-and-division-by-zero.md#infinity-nan-and-division-by-zero-2026-08-22)
- [Declared type, Variant subtype, and overflow promotion (2026-08-22)](declared-type-variant-subtype-and-overflow-promotion.md#declared-type-variant-subtype-and-overflow-promotion-2026-08-22)
- [The `Empty` literal (2026-08-22, #126)](the-empty-literal.md#the-empty-literal-2026-08-22-126)
- [The `App` object — and the difference between F5 and a compiled exe (2026-09-01, #136)](the-app-object--and-the-difference-between-f5-and-a-compiled-exe.md#the-app-object--and-the-difference-between-f5-and-a-compiled-exe-2026-09-01-136)
- [`IsMissing`, and the shape of an omitted argument (2026-09-01)](ismissing-and-the-shape-of-an-omitted-argument.md#ismissing-and-the-shape-of-an-omitted-argument-2026-09-01)
- [A control VB6 cannot load renders as an empty box (2026-09-01, design-time)](a-control-vb6-cannot-load-renders-as-an-empty-box.md#a-control-vb6-cannot-load-renders-as-an-empty-box-2026-09-01-design-time)
- [Bitwise And / Or / Xor / Not — the result-type ladder (2026-09-02, #166)](bitwise-and--or--xor--not--the-result-type-ladder.md#bitwise-and--or--xor--not--the-result-type-ladder-2026-09-02-166)
- [DefType — default typing by first letter (2026-09-02, #169)](deftype--default-typing-by-first-letter.md#deftype--default-typing-by-first-letter-2026-09-02-169)
- [Variant arithmetic promotes; a declared type is a ceiling (2026-09-02, #122)](variant-arithmetic-promotes-a-declared-type-is-a-ceiling.md#variant-arithmetic-promotes-a-declared-type-is-a-ceiling-2026-09-02-122)
- [Member chains, including module-qualified ones (2026-09-02, #173)](member-chains-including-module-qualified-ones.md#member-chains-including-module-qualified-ones-2026-09-02-173)
- [Implements, and interface-typed variables (2026-09-02, #186)](implements-and-interface-typed-variables.md#implements-and-interface-typed-variables-2026-09-02-186)
- [Static locals (2026-09-02)](static-locals.md#static-locals-2026-09-02)
- [Omitted optional arguments, and string operands to numeric intrinsics (2026-09-02)](omitted-optional-arguments-and-string-operands-to-numeric-intrinsics.md#omitted-optional-arguments-and-string-operands-to-numeric-intrinsics-2026-09-02)
- [Line continuations and statement separators (2026-09-02)](line-continuations-and-statement-separators.md#line-continuations-and-statement-separators-2026-09-02)
- [Enums (2026-09-03)](enums.md#enums-2026-09-03)
- [Module scope for Types and Enums (2026-09-03)](module-scope-for-types-and-enums.md#module-scope-for-types-and-enums-2026-09-03)
- [What the corpus PRINTS, not just whether it parses (2026-09-03)](what-the-corpus-prints-not-just-whether-it-parses.md#what-the-corpus-prints-not-just-whether-it-parses-2026-09-03)
- [`Select Case` layout — colons, blank lines, and continuations (2026-09-03)](select-case-layout--colons-blank-lines-and-continuations.md#select-case-layout--colons-blank-lines-and-continuations-2026-09-03)
- [`Select Case` matching — the coercion rule (2026-09-03)](select-case-matching--the-coercion-rule.md#select-case-matching--the-coercion-rule-2026-09-03)
- [In-box constants: the library qualifier selects (2026-09-03)](in-box-constants-the-library-qualifier-selects.md#in-box-constants-the-library-qualifier-selects-2026-09-03)
- [String literals: `""` is the only escape (2026-09-04)](string-literals--is-the-only-escape.md#string-literals--is-the-only-escape-2026-09-04)
- [`Sub Main` as a startup object (2026-09-04, #210)](sub-main-as-a-startup-object.md#sub-main-as-a-startup-object-2026-09-04-210)
- [What a `.vbp` tolerates, and the one key for a non-code file (2026-09-04)](what-a-vbp-tolerates-and-the-one-key-for-a-non-code-file.md#what-a-vbp-tolerates-and-the-one-key-for-a-non-code-file-2026-09-04)
- [Serialization / file-format fidelity (2026-08-11)](serialization--file-format-fidelity.md#serialization--file-format-fidelity-2026-08-11)
- [Unsuffixed float literals, and comparing Double with Single (2026-09-03)](unsuffixed-float-literals-and-comparing-double-with-single.md#unsuffixed-float-literals-and-comparing-double-with-single-2026-09-03)
- [Numeric type pairs — five arithmetic tables, and comparison is a sixth (2026-09-03)](numeric-type-pairs--five-arithmetic-tables-and-comparison-is-a-sixth.md#numeric-type-pairs--five-arithmetic-tables-and-comparison-is-a-sixth-2026-09-03)
- [Module-name uniqueness is per-project, and filenames are free (2026-09-07)](module-name-uniqueness-is-per-project-and-filenames-are-free.md#module-name-uniqueness-is-per-project-and-filenames-are-free-2026-09-07)
- [Qualified type names: a `Type` never shadows the qualifier (2026-09-19)](qualified-type-names-a-type-never-shadows-the-qualifier.md#qualified-type-names-a-type-never-shadows-the-qualifier-2026-09-19)
- [What `/make` writes to `/out`, and what "Line N" counts (2026-09-19)](what-make-writes-to-out-and-what-line-n-counts.md#what-make-writes-to-out-and-what-line-n-counts-2026-09-19)
- [A `.vbp` item line has two shapes, and the keys do not agree on which (2026-09-20)](a-vbp-item-line-has-two-shapes-and-the-keys-do-not-agree-on-which.md#a-vbp-item-line-has-two-shapes-and-the-keys-do-not-agree-on-which-2026-09-20)
- [When a document gets a file, and what a dangling `Startup=` does (2026-09-20, #489)](when-a-document-gets-a-file-and-what-a-dangling-startup-does.md#when-a-document-gets-a-file-and-what-a-dangling-startup-does-2026-09-20-489)
- [A type character on a name the program also declares: `Left$` beside a user `Left` (2026-10-11)](a-type-character-on-a-name-the-program-also-declares-left-beside-a-user-left.md#a-type-character-on-a-name-the-program-also-declares-left-beside-a-user-left-2026-10-11)
- [A running program's locale is the user's regional settings (2026-10-12, #777)](a-running-programs-locale-is-the-users-regional-settings.md#a-running-programs-locale-is-the-users-regional-settings-2026-10-12-777)
- [Fixed number masks beyond Decimal range (#2118)](format-of-a-number-beyond-decimals-range.md)
- [Turning a number or a date into text (2026-10-12, #772, #771)](turning-a-number-or-a-date-into-text.md#turning-a-number-or-a-date-into-text-2026-10-12-772-771)
- [A control where a value is wanted is its default member (2026-10-12, #790)](a-control-where-a-value-is-wanted-is-its-default-member.md#a-control-where-a-value-is-wanted-is-its-default-member-2026-10-12-790)
- [A Label's colours, read back and painted (2026-10-12, #795)](a-labels-colours-read-back-and-painted.md#a-labels-colours-read-back-and-painted-2026-10-12-795)
- [Seven system colours (2026-10-22, #908)](seven-system-colours.md#seven-system-colours-2026-10-22-908)
- [The fonts a form names (2026-10-17, #1132, #1133)](the-fonts-a-form-names.md#the-fonts-a-form-names-2026-10-17-1132-1133)
- [A Label's border (2026-10-17, #1077)](a-labels-border.md#a-labels-border-2026-10-17-1077)
- [A caption's access key (2026-10-22, #920)](a-captions-access-key.md#a-captions-access-key-2026-10-22-920)
- [MousePointer's names and pointers (2026-10-22, #120)](mousepointers-names-and-pointers.md#mousepointers-names-and-pointers-2026-10-22-120)
- [RGB, QBColor and MsgBox's style (2026-10-12)](rgb-qbcolor-and-msgboxs-style.md#rgb-qbcolor-and-msgboxs-style-2026-10-12)
- [Identifier case (2026-10-12, #811)](identifier-case.md#identifier-case-2026-10-12-811)
- [Member attributes: where VB6 attaches them, and what its IDE writes (2026-10-13, #273)](member-attributes-where-vb6-attaches-them-and-what-its-ide-writes.md#member-attributes-where-vb6-attaches-them-and-what-its-ide-writes-2026-10-13-273)
- [Resume Next into a block (2026-10-12, #839)](resume-next-into-a-block.md#resume-next-into-a-block-2026-10-12-839)
- [A typed selector and a Variant one (2026-10-13, #839 review round two)](a-typed-selector-and-a-variant-one.md#a-typed-selector-and-a-variant-one-2026-10-13-839-review-round-two)
- [Attribute lines inside a procedure (2026-10-12, #832)](attribute-lines-inside-a-procedure.md#attribute-lines-inside-a-procedure-2026-10-12-832)
- [What a condition is (2026-10-12, #847)](what-a-condition-is.md#what-a-condition-is-2026-10-12-847)
- [A typed parameter given a value of another type (2026-10-13, #874)](a-typed-parameter-given-a-value-of-another-type.md#a-typed-parameter-given-a-value-of-another-type-2026-10-13-874)
- [A member call inside While...Wend (2026-10-13, #882)](a-member-call-inside-whilewend.md#a-member-call-inside-whilewend-2026-10-13-882)
- [Subscripts, bounds, and a method called from Class_Initialize (2026-10-12, #760, #849, #850)](subscripts-bounds-and-a-method-called-from-class_initialize.md#subscripts-bounds-and-a-method-called-from-class_initialize-2026-10-12-760-849-850)
- [An object used as a value (2026-10-12, #174)](an-object-used-as-a-value.md#an-object-used-as-a-value-2026-10-12-174)
- [The native build's hidden switches, and where `VB6.EXE` finds C2 and LINK (2026-10-13)](the-native-builds-hidden-switches-and-where-vb6exe-finds-c2-and-link.md#the-native-builds-hidden-switches-and-where-vb6exe-finds-c2-and-link-2026-10-13)
- [A Function that returns an array (2026-10-13, #975)](a-function-that-returns-an-array.md#a-function-that-returns-an-array-2026-10-13-975)
- [Arrays copy wherever VB6 stores one (2026-10-14, #970)](arrays-copy-wherever-vb6-stores-one.md#arrays-copy-wherever-vb6-stores-one-2026-10-14-970)
- [What a store into a Const is (2026-10-14, #1003)](what-a-store-into-a-const-is.md#what-a-store-into-a-const-is-2026-10-14-1003)
- [Another module's Public names, bare (2026-10-14, #880)](another-modules-public-names-bare.md#another-modules-public-names-bare-2026-10-14-880)
- [A control's position at run time (2026-10-14, #933)](a-controls-position-at-run-time.md#a-controls-position-at-run-time-2026-10-14-933)
- [Mouse events (2026-10-14, #974)](mouse-events.md#mouse-events-2026-10-14-974)
- [A click with real input: where Click and DblClick fall (2026-10-22, #1364)](a-click-with-real-input-where-click-and-dblclick-fall.md#a-click-with-real-input-where-click-and-dblclick-fall-2026-10-22-1364)
- [A ListBox's Click: a left press on an item, or code or a key that moves the selection (2026-10-22, #1374)](a-listboxs-click-a-left-press-on-an-item-or-code-or-a-key-that-moves-the-selection.md#a-listboxs-click-a-left-press-on-an-item-or-code-or-a-key-that-moves-the-selection-2026-10-22-1374)
- [A ListBox's Selected, SelCount and Text, with MultiSelect and without (2026-10-22, #1395)](a-listboxs-selected-selcount-and-text-with-multiselect-and-without.md#a-listboxs-selected-selcount-and-text-with-multiselect-and-without-2026-10-22-1395)
- [One handler at a time (2026-10-14, #766)](one-handler-at-a-time.md#one-handler-at-a-time-2026-10-14-766)
- [Unload Me (2026-10-14, #973)](unload-me.md#unload-me-2026-10-14-973)
- [Overflow edges (2026-10-14, #234)](overflow-edges.md#overflow-edges-2026-10-14-234)
- [Thousands separators in numeric Strings (2026-10-24, #1446)](thousands-separators-in-numeric-strings.md#thousands-separators-in-numeric-strings-2026-10-24-1446)
- [A String in arithmetic (2026-10-14, #1047, #770)](a-string-in-arithmetic.md#a-string-in-arithmetic-2026-10-14-1047-770)
- [Several forms: load, show, hide, modal, and when the run ends (2026-10-14, #822)](several-forms-load-show-hide-modal-and-when-the-run-ends.md#several-forms-load-show-hide-modal-and-when-the-run-ends-2026-10-14-822)
- [An unhandled run-time error (2026-10-14, #1011)](an-unhandled-run-time-error.md#an-unhandled-run-time-error-2026-10-14-1011)
- [Operator precedence (2026-10-15, #1074)](operator-precedence.md#operator-precedence-2026-10-15-1074)
- [The library's `$` forms (2026-10-15, #742, #1071)](the-librarys--forms.md#the-librarys--forms-2026-10-15-742-1071)
- [Move, ScaleWidth and ScaleHeight (2026-10-15, #1071)](move-scalewidth-and-scaleheight.md#move-scalewidth-and-scaleheight-2026-10-15-1071)
- [Menus at run time, and the menu bar (2026-10-17, #1126, #1127)](menus-at-run-time-and-the-menu-bar.md#menus-at-run-time-and-the-menu-bar-2026-10-17-1126-1127)
- [When a menu's Click is raised (2026-10-22, #1344)](when-a-menus-click-is-raised.md#when-a-menus-click-is-raised-2026-10-22-1344)
- [Choosing a menu item leaves Checked to the program (2026-10-18, #1158; measured by #1159)](choosing-a-menu-item-leaves-checked-to-the-program.md#choosing-a-menu-item-leaves-checked-to-the-program-2026-10-18-1158-measured-by-1159)
- [SetFocus (2026-10-15, #1061)](setfocus.md#setfocus-2026-10-15-1061)
- [The Line control (2026-10-15, #163)](the-line-control.md#the-line-control-2026-10-15-163)
- [Sequential files (2026-10-17, #1145)](sequential-files.md#sequential-files-2026-10-17-1145)
- [The CommonDialog control (2026-10-17, #1084)](the-commondialog-control.md#the-commondialog-control-2026-10-17-1084)
- [A font's size, and the default font (2026-10-22, #910)](a-fonts-size-and-the-default-font.md#a-fonts-size-and-the-default-font-2026-10-22-910)
- [A font's FontSize, FontBold and the rest (2026-10-22, #1343, #915)](a-fonts-fontsize-fontbold-and-the-rest.md#a-fonts-fontsize-fontbold-and-the-rest-2026-10-22-1343-915)
- [Command, and Left$ in a form's code (2026-10-17, #742)](command-and-left-in-a-forms-code.md#command-and-left-in-a-forms-code-2026-10-17-742)
- [A TextBox's selection (2026-10-17, #1150)](a-textboxs-selection.md#a-textboxs-selection-2026-10-17-1150)
- [A form's WindowState (2026-10-17, #1152)](a-forms-windowstate.md#a-forms-windowstate-2026-10-17-1152)
- [File-number hashes and date literals (2026-10-17, #1146)](file-number-hashes-and-date-literals.md#file-number-hashes-and-date-literals-2026-10-17-1146)
- [Missing built-in objects and shadowing (#871, 2026-10-17)](missing-built-in-objects-and-shadowing.md#missing-built-in-objects-and-shadowing-871-2026-10-17)
- [Text Clipboard (#1156, 2026-10-17)](text-clipboard.md#text-clipboard-1156-2026-10-17)
- [A TextBox's MultiLine and ScrollBars (2026-10-22, #1340)](a-textboxs-multiline-and-scrollbars.md#a-textboxs-multiline-and-scrollbars-2026-10-22-1340)
- [The reset prompt: which edits raise it, and which windows it holds (2026-10-22, #1329)](the-reset-prompt-which-edits-raise-it-and-which-windows-it-holds.md#the-reset-prompt-which-edits-raise-it-and-which-windows-it-holds-2026-10-22-1329)
- [A form whose two names disagree (2026-10-22, #473)](a-form-whose-two-names-disagree.md#a-form-whose-two-names-disagree-2026-10-22-473)
- [A quote inside a .frm string (2026-10-22, #218)](a-quote-inside-a-frm-string.md#a-quote-inside-a-frm-string-2026-10-22-218)
- [ToolTipText: which controls have it (2026-10-23, #1403)](tooltiptext-which-controls-have-it.md#tooltiptext-which-controls-have-it-2026-10-23-1403)
- [What Save Project As does to the paths in a project file (2026-10-22, #1406)](what-save-project-as-does-to-the-paths-in-a-project-file.md#what-save-project-as-does-to-the-paths-in-a-project-file-2026-10-22-1406)
- [Where and how VB6 writes a project's description (2026-10-22, #42)](where-and-how-vb6-writes-a-projects-description.md#where-and-how-vb6-writes-a-projects-description-2026-10-22-42)
- [A control's Name and Index, at run time (2026-10-22, #1431)](a-controls-name-and-index-at-run-time.md#a-controls-name-and-index-at-run-time-2026-10-22-1431)
- [A lone CR or LF in VB6 source (2026-10-22, #1020)](a-lone-cr-or-lf-in-vb6-source.md#a-lone-cr-or-lf-in-vb6-source-2026-10-22-1020)
- [A member indexed through its object, and a `?` line in the Immediate window (2026-10-22, #1386)](a-member-indexed-through-its-object-and-a--line-in-the-immediate-window.md#a-member-indexed-through-its-object-and-a--line-in-the-immediate-window-2026-10-22-1386)
- [Another module's Enum members, bare (2026-10-22, #889)](another-modules-enum-members-bare.md#another-modules-enum-members-bare-2026-10-22-889)
- [A String read as a Date (2026-10-22, #967, #800)](a-string-read-as-a-date.md#a-string-read-as-a-date-2026-10-22-967-800)
- [A Function's return, converted where it is assigned (2026-10-22, #1444)](a-functions-return-converted-where-it-is-assigned.md#a-functions-return-converted-where-it-is-assigned-2026-10-22-1444)
- [A store into a UDT field (2026-10-22, #221)](a-store-into-a-udt-field.md#a-store-into-a-udt-field-2026-10-22-221)
- [A UDT field passed ByRef (2026-10-22, #1451)](a-udt-field-passed-byref.md#a-udt-field-passed-byref-2026-10-22-1451)
- [An array element passed ByRef (2026-10-22, #1455)](an-array-element-passed-byref.md#an-array-element-passed-byref-2026-10-22-1455)
- [For Each locks its array (#1460)](for-each-locks-its-array.md#for-each-locks-its-array-1460)
- [The References dialog's fixed rows (2026-10-22, #1026, design-time)](the-references-dialogs-fixed-rows.md#the-references-dialogs-fixed-rows-2026-10-22-1026-design-time)
- [A form's StartUpPosition when its file has no line (2026-10-13, #932)](a-forms-startupposition-when-its-file-has-no-line.md#a-forms-startupposition-when-its-file-has-no-line-2026-10-13-932)
- [A form's Attribute block, and a form without one (2026-10-22, #516, #931)](a-forms-attribute-block-and-a-form-without-one.md#a-forms-attribute-block-and-a-form-without-one-2026-10-22-516-931)
- [VB_Ext_KEY on a form survives VB6's save (2026-10-26, #1117, design-time)](vb-ext-key-on-a-form-survives-vb6s-save.md#vb_ext_key-on-a-form-survives-vb6s-save-2026-10-26-1117-design-time)
- [ReDim Preserve (2026-10-22, #1459)](redim-preserve.md#redim-preserve-2026-10-22-1459)
- [IIf and Choose (2026-10-22, #1461, #1464)](iif-and-choose.md#iif-and-choose-2026-10-22-1461-1464)
- [Where the Format commands put a control (2026-10-22, #929, design-time)](where-the-format-commands-put-a-control.md#where-the-format-commands-put-a-control-2026-10-22-929-design-time)
- [Named arguments (2026-10-22, #1465)](named-arguments.md#named-arguments-2026-10-22-1465)
- [The TabIndex a new or pasted control takes (2026-10-22, #925, design-time)](the-tabindex-a-new-or-pasted-control-takes.md#the-tabindex-a-new-or-pasted-control-takes-2026-10-22-925-design-time)
- [Drawing a control into a Frame or PictureBox (2026-10-26, #2039, design-time)](drawing-a-control-into-a-frame-or-picturebox.md#drawing-a-control-into-a-frame-or-picturebox-2026-10-26-2039-design-time)
- [Collection (2026-10-22, #1471)](collection.md#collection-2026-10-22-1471)
- [As New (2026-10-22 and 23, #435)](as-new.md#as-new-2026-10-22-and-23-435)
- [Where VB6's designer puts a control's block (2026-10-23, #1481, design-time)](where-vb6s-designer-puts-a-controls-block.md#where-vb6s-designer-puts-a-controls-block-2026-10-23-1481-design-time)
- [A control VB6 knows and HexIDE carries stacks with the rest (2026-10-23, #1498, design-time, IDE, release build)](a-control-vb6-knows-and-hexide-carries-stacks-with-the-rest.md#a-control-vb6-knows-and-hexide-carries-stacks-with-the-rest-2026-10-23-1498-design-time-ide-release-build)
- [Static (2026-10-23, #1491)](static.md#static-2026-10-23-1491)
- [A Let into an object variable (2026-10-23, #1493)](a-let-into-an-object-variable.md#a-let-into-an-object-variable-2026-10-23-1493)
- [The size of a control added from the toolbox (2026-10-23, #1473, design-time, IDE, release build)](the-size-of-a-control-added-from-the-toolbox.md#the-size-of-a-control-added-from-the-toolbox-2026-10-23-1473-design-time-ide-release-build)
- [An undeclared variable passed ByRef (2026-10-23, #1505)](an-undeclared-variable-passed-byref.md#an-undeclared-variable-passed-byref-2026-10-23-1505)
- [Chr, Asc, ChrW and AscW (2026-10-23, #1134)](chr-asc-chrw-and-ascw.md#chr-asc-chrw-and-ascw-2026-10-23-1134)
- [The name VB6's designer gives a new control (2026-10-23, #540, design-time, IDE, release build)](the-name-vb6s-designer-gives-a-new-control.md#the-name-vb6s-designer-gives-a-new-control-2026-10-23-540-design-time-ide-release-build)
- [Debug.Assert (2026-10-23, #1467)](debugassert.md#debugassert-2026-10-23-1467)
- [Indexing a call's result (2026-10-23, #1476)](indexing-a-calls-result.md#indexing-a-calls-result-2026-10-23-1476)
- [What VB6's Add Form saves (2026-10-23, #1532, design-time, IDE, release build)](what-vb6s-add-form-saves.md#what-vb6s-add-form-saves-2026-10-23-1532-design-time-ide-release-build)
- [What VB6's Properties window lists for a UserControl and a PropertyPage (2026-10-23, #1585, design-time, IDE)](what-vb6s-properties-window-lists-for-a-usercontrol-and-a-propertypage.md#what-vb6s-properties-window-lists-for-a-usercontrol-and-a-propertypage-2026-10-23-1585-design-time-ide)
- [What VB6's Properties window shows a position in (2026-10-23, #1636, design-time, IDE)](what-vb6s-properties-window-shows-a-position-in.md#what-vb6s-properties-window-shows-a-position-in-2026-10-23-1636-design-time-ide)
- [A UserControl's and a PropertyPage's own events (2026-10-24, #1632, design-time, IDE, release build and SP6)](a-usercontrols-and-a-propertypages-own-events.md#a-usercontrols-and-a-propertypages-own-events-2026-10-24-1632-design-time-ide-release-build-and-sp6)
- [Write # and Input # (2026-10-23, #1521)](write--and-input.md#write--and-input--2026-10-23-1521)
- [Make Project Group (2026-10-23, #746, design-time, IDE, release build)](make-project-group.md#make-project-group-2026-10-23-746-design-time-ide-release-build)
- [How a running form stacks its controls (2026-10-23, #1497, IDE, release build)](how-a-running-form-stacks-its-controls.md#how-a-running-form-stacks-its-controls-2026-10-23-1497-ide-release-build)
- [Too few arguments to an intrinsic (2026-10-23, #1512)](too-few-arguments-to-an-intrinsic.md#too-few-arguments-to-an-intrinsic-2026-10-23-1512)
- [Print # and the ANSI best fit (2026-10-23, #1511)](print--and-the-ansi-best-fit.md#print--and-the-ansi-best-fit-2026-10-23-1511)
- [CVar (2026-10-23, #1466)](cvar.md#cvar-2026-10-23-1466)
- [Filesystem (#1561) — vb6.exe 6.00.8176 (original release), MSVBVM60 6.00.9848 (SP6-level)](filesystem.md#filesystem-1561--vb6exe-6008176-original-release-msvbvm60-6009848-sp6-level)
- [What ZOrder does at run time (2026-10-23, #1503, p-code, IDE, release build)](what-zorder-does-at-run-time.md#what-zorder-does-at-run-time-2026-10-23-1503-p-code-ide-release-build)
- [A form's Controls collection, and TypeOf on a control (2026-10-23, #1060)](a-forms-controls-collection-and-typeof-on-a-control.md#a-forms-controls-collection-and-typeof-on-a-control-2026-10-23-1060)
- [GotFocus and LostFocus on each control, and Enabled saved False (2026-10-23, #1101)](gotfocus-and-lostfocus-on-each-control-and-enabled-saved-false.md#gotfocus-and-lostfocus-on-each-control-and-enabled-saved-false-2026-10-23-1101)
- [Tab and the arrow keys among option buttons (2026-10-23, #1583)](tab-and-the-arrow-keys-among-option-buttons.md#tab-and-the-arrow-keys-among-option-buttons-2026-10-23-1583)
- [Where the arrow keys go from a button (2026-10-23, #1625)](where-the-arrow-keys-go-from-a-button.md#where-the-arrow-keys-go-from-a-button-2026-10-23-1625)
- [Conditional compilation (2026-10-23, #1112)](conditional-compilation.md#conditional-compilation-2026-10-23-1112)
- [A form's Width and Height: the frame round its client area (2026-10-23, #1072)](a-forms-width-and-height-the-frame-round-its-client-area.md#a-forms-width-and-height-the-frame-round-its-client-area-2026-10-23-1072)
- [A form's window styles: BorderStyle, ControlBox, MinButton and MaxButton (2026-10-23, #1575)](a-forms-window-styles-borderstyle-controlbox-minbutton-and-maxbutton.md#a-forms-window-styles-borderstyle-controlbox-minbutton-and-maxbutton-2026-10-23-1575)
- [A form's window icon: no Icon line, (None), and an icon of its own (2026-10-24, #1594, SP6)](a-forms-window-icon-no-icon-line.md#a-forms-window-icon-no-icon-line-none-and-an-icon-of-its-own-2026-10-24-1594-sp6)
- [A form's Resize, Activate and Deactivate (2026-10-23, #994)](a-forms-resize-activate-and-deactivate.md#a-forms-resize-activate-and-deactivate-2026-10-23-994)
- [Like (2026-10-23, #1547)](like.md#like-2026-10-23-1547)
- [InStr and InStrRev at the ends (2026-10-23, #1567)](instr-and-instrrev-at-the-ends.md#instr-and-instrrev-at-the-ends-2026-10-23-1567)
- [Option Compare Text, and VB6's text compare (2026-10-23, #1567)](option-compare-text-and-vb6s-text-compare.md#option-compare-text-and-vb6s-text-compare-2026-10-23-1567)
- [`Like` in `#If` and `#Const`, and a directive's text order (2026-10-23, #1598)](like-in-if-and-const-and-a-directives-text-order.md#like-in-if-and-const-and-a-directives-text-order-2026-10-23-1598)
- [A Let into an array variable (2026-10-23, #998, #1588)](a-let-into-an-array-variable.md#a-let-into-an-array-variable-2026-10-23-998-1588)
- [StrComp, StrConv, Environ, Switch and Error(), and the case of a character (2026-10-23, #1560)](strcomp-strconv-environ-switch-and-error.md#strcomp-strconv-environ-switch-and-error-and-the-case-of-a-character-2026-10-23-1560)
- [Err.Source after a run-time error and after Err.Raise (#1483)](when-err-is-cleared-and-what-a-running-handler-traps.md#errsource-after-a-run-time-error-and-after-errraise-1483)
- [When Err is cleared, and what a running handler traps (2026-10-23, #877)](when-err-is-cleared-and-what-a-running-handler-traps.md#when-err-is-cleared-and-what-a-running-handler-traps-2026-10-23-877)
- [Err.Source after a run-time error and after Err.Raise (2026-10-27, #1483)](err-source-after-a-run-time-error-and-err-raise.md#errsource-after-a-run-time-error-and-after-errraise-2026-10-27-1483)
- [`Time`, and eight Dies rows that already ran (2026-10-23, #1633)](time-and-eight-dies-rows-that-already-ran.md#time-and-eight-dies-rows-that-already-ran-2026-10-23-1633)
- [A Const declared with a type character (2026-10-23, #1614)](a-const-declared-with-a-type-character.md#a-const-declared-with-a-type-character-2026-10-23-1614)
- [A For counter written with a type character (2026-10-27, #739)](a-for-counter-written-with-a-type-character.md#a-for-counter-written-with-a-type-character-2026-10-27-739)
- [A Function declared with a type character (2026-10-27, #740)](a-function-declared-with-a-type-character.md#a-function-declared-with-a-type-character-2026-10-27-740)
- [A name read with a type character (2026-10-27, #742)](a-name-read-with-a-type-character.md#a-name-read-with-a-type-character-2026-10-27-742)
- [A member used on something that is not an object (2026-10-27, #257)](a-member-used-on-something-that-is-not-an-object.md#a-member-used-on-something-that-is-not-an-object-2026-10-27-257)
- [A compare argument, and when it is checked (2026-10-27, #881)](a-compare-argument-and-when-it-is-checked.md#a-compare-argument-and-when-it-is-checked-2026-10-27-881)
- [A standard module's Property procedures (2026-10-27, #977)](a-standard-modules-property-procedures.md#a-standard-modules-property-procedures-2026-10-27-977)
- [A control's Font object (2026-10-27, #934)](a-controls-font-object.md#a-controls-font-object-2026-10-27-934)
- [AutoSize on a Label and a PictureBox (2026-10-27, #922)](autosize-on-a-label-and-a-picturebox.md#autosize-on-a-label-and-a-picturebox-2026-10-27-922)
- [The Screen object (2026-10-28, #2431)](the-screen-object.md#the-screen-object-2026-10-28-2431)
- [The control a shown form focuses (2026-10-28, #2436)](the-control-a-shown-form-focuses.md#the-control-a-shown-form-focuses-2026-10-28-2436)
- [A form's own ActiveControl (2026-10-28, #2437)](a-forms-own-activecontrol.md#a-forms-own-activecontrol-2026-10-28-2437)
- [A ParamArray parameter (2026-10-28, #2462)](a-paramarray-parameter.md#a-paramarray-parameter-2026-10-28-2462)
- [The MaskedEdBox: its mask, its Text and its keystrokes (2026-10-28, #1098)](the-maskededbox-mask-text-and-keystrokes.md#the-maskededbox-its-mask-its-text-and-its-keystrokes-2026-10-28-1098)
- [A D exponent in a source literal (2026-10-23, #1611)](a-d-exponent-in-a-source-literal.md#a-d-exponent-in-a-source-literal-2026-10-23-1611)
- [GetSetting, SaveSetting, DeleteSetting and GetAllSettings (2026-10-23, #1562)](getsetting-savesetting-deletesetting-and-getallsettings.md#getsetting-savesetting-deletesetting-and-getallsettings-2026-10-23-1562)
- [A name declared twice (2026-10-23, #1622, #1499)](a-name-declared-twice.md#a-name-declared-twice-2026-10-23-1622-1499)
- [Next closing several loops (2026-10-27, #2393)](next-closing-several-loops.md#next-closing-several-loops-2026-10-27-2393)
- [LOF, Loc, Seek and the Input function (2026-10-23, #1645)](lof-loc-seek-and-the-input-function.md#lof-loc-seek-and-the-input-function-2026-10-23-1645)
- [A declaration written with a type character (2026-10-27, #2409)](a-declaration-written-with-a-type-character.md#a-declaration-written-with-a-type-character-2026-10-27-2409)
- [A numeric literal ending in its point (2026-10-23, #1615)](a-numeric-literal-ending-in-its-point.md#a-numeric-literal-ending-in-its-point-2026-10-23-1615)
- [Random and Binary files, Get, Put and Len (2026-10-24, #1646)](random-and-binary-files-get-put-and-len.md#random-and-binary-files-get-put-and-len-2026-10-24-1646)
- [Unknown library statement calls (#867)](unknown-library-statement-calls.md#unknown-library-statement-calls-867)
- [Len of declared scalars and numeric expressions (#780)](len-of-declared-scalars-and-numeric-expressions.md#len-of-declared-scalars-and-numeric-expressions-780)
- [A For loop with a fraction (2026-10-24, #983)](a-for-loop-with-a-fraction.md#a-for-loop-with-a-fraction-2026-10-24-983)
- [In-box controls: the ProgressBar (2026-10-25, #1082)](in-box-controls-the-progressbar.md#in-box-controls-the-progressbar-2026-10-25-1082)
- [TabIndex and TabStop at run time (2026-10-26, #1974)](tabindex-and-tabstop-at-run-time.md#tabindex-and-tabstop-at-run-time-2026-10-26-1974)
- [The Components dialog and an in-box library's lines (2026-10-26, #1103, design-time)](the-components-dialog-and-an-in-box-librarys-lines.md#the-components-dialog-and-an-in-box-librarys-lines-2026-10-26-1103-design-time)
- [Data-bound TextBox startup value (#859, #856)](data-bound-textbox-startup-value.md#data-bound-textbox-startup-value-859-856)
- [The byte-string functions (2026-10-25, #1510)](the-byte-string-functions.md#the-byte-string-functions-2026-10-25-1510)
- [A form's Move (2026-10-26, #1569)](a-forms-move.md#a-forms-move-2026-10-26-1569)
- [A date's fraction of a second (2026-10-26, #783)](a-dates-fraction-of-a-second.md#a-dates-fraction-of-a-second-2026-10-26-783)
- [Rnd and Randomize (2026-10-26, #2117)](rnd-and-randomize.md#rnd-and-randomize-2026-10-26-2117)
- [Format's AMPM token (2026-10-26, #2040)](formats-ampm-token.md#formats-ampm-token-2026-10-26-2040)
- [Format of a number beyond Decimal's range (2026-10-26, #2118)](format-of-a-number-beyond-decimals-range.md#format-of-a-number-beyond-decimals-range-2026-10-26-2118)
- [A GlobalMultiUse class in a referenced library (2026-10-26, #1107)](a-globalmultiuse-class-in-a-referenced-library.md#a-globalmultiuse-class-in-a-referenced-library-2026-10-26-1107)
- [Controls.Add and Controls.Remove (2026-10-26, #1556)](controls-add-and-controls-remove.md#controlsadd-and-controlsremove-2026-10-26-1556)
- [A control without a Font block takes the form's font (#2062)](form-font-inheritance.md)
- [Compile refusals: Continue Do, With Debug and a private field through Me](compile-refusals-continue-with-debug-and-me.md) — SP6 native and p-code measurements supplied in #2121.

- [A UserControl's containers, drawing and events](usercontrol-containers-drawing-and-events.md) (#1631, part C).
- [A UserControl instance's saved properties](usercontrol-files.md) (#1631, group 2A file-save subset).
- [A UDT copied whole, and arrays of a UDT (#178)](a-udt-copied-whole-and-arrays-of-a-udt.md)
- [A UDT copied whole, and arrays of a UDT (2026-10-26, #178)](a-udt-copied-whole-and-arrays-of-a-udt.md#a-udt-copied-whole-and-arrays-of-a-udt-2026-10-26-178)
- [A breakpoint on a procedure's End line or its header (2026-10-26, #2172, IDE)](a-breakpoint-on-a-procedures-end-line-or-its-header.md#a-breakpoint-on-a-procedures-end-line-or-its-header-2026-10-26-2172-ide)
- [A multiple selection in the designer, and Align (2026-10-26, #2181, design-time, IDE)](a-multiple-selection-in-the-designer-and-align.md#a-multiple-selection-in-the-designer-and-align-2026-10-26-2181-design-time-ide)
- [A UserControl's own properties at run time (2026-10-26, #1631)](a-usercontrols-own-properties-at-run-time.md#a-usercontrols-own-properties-at-run-time-2026-10-26-1631)
- [A UserControl's own properties in the designer and the files (2026-10-26, #1631, design-time, IDE)](a-usercontrols-own-properties-in-the-designer-and-the-files.md#a-usercontrols-own-properties-in-the-designer-and-the-files-2026-10-26-1631-design-time-ide)
- [In-box controls: the ImageList (2026-10-26, #1083)](in-box-controls-the-imagelist.md#in-box-controls-the-imagelist-2026-10-26-1083)
- [In-box controls: the StatusBar (2026-10-26, #1087)](in-box-controls-the-statusbar.md#in-box-controls-the-statusbar-2026-10-26-1087)
- [In-box controls: the Toolbar (2026-10-26, #1088)](in-box-controls-the-toolbar.md#in-box-controls-the-toolbar-2026-10-26-1088)
- [In-box controls: the ListView (2026-10-26, #1085)](in-box-controls-the-listview.md#in-box-controls-the-listview-2026-10-26-1085)
- [In-box controls: the TreeView (2026-10-27, #1086)](in-box-controls-the-treeview.md#in-box-controls-the-treeview-2026-10-27-1086)
- [In-box controls: the CommonDialog (2026-10-27, #1084)](in-box-controls-the-commondialog.md#in-box-controls-the-commondialog-2026-10-27-1084)
- [In-box controls: the SSTab (2026-10-27, #1090)](in-box-controls-the-sstab.md#in-box-controls-the-sstab-2026-10-27-1090)
- [A control's Enabled inside a disabled container (2026-10-27)](a-controls-enabled-inside-a-disabled-container.md#a-controls-enabled-inside-a-disabled-container-2026-10-27)
- [In-box controls: the MSFlexGrid (2026-10-27, #1099)](in-box-controls-the-msflexgrid.md#in-box-controls-the-msflexgrid-2026-10-27-1099)
- [In-box controls: the RichTextBox (2026-10-27, #1100)](in-box-controls-the-richtextbox.md#in-box-controls-the-richtextbox-2026-10-27-1100)
- [In-box controls: the TabStrip (2026-10-27, #1089)](in-box-controls-the-tabstrip.md#in-box-controls-the-tabstrip-2026-10-27-1089)
- [In-box classes: a bare name binds to the first library (2026-10-27, #2265)](in-box-classes-a-bare-name-binds-to-the-first-library.md#in-box-classes-a-bare-name-binds-to-the-first-library-2026-10-27-2265)
- [A reference's LCID is lowercase hex, unpadded (2026-10-27, #2273)](a-references-lcid-is-lowercase-hex.md#a-references-lcid-is-lowercase-hex-unpadded-2026-10-27-2273)
- [A reference's path, and the line order a save writes (2026-10-27, #2274)](a-references-path-and-the-line-order-a-save-writes.md#a-references-path-and-the-line-order-a-save-writes-2026-10-27-2274)
- [In-box controls: the Slider (2026-10-27, #1091)](in-box-controls-the-slider.md#in-box-controls-the-slider-2026-10-27-1091)
- [In-box controls: the UpDown (2026-10-27, #1093)](in-box-controls-the-updown.md#in-box-controls-the-updown-2026-10-27-1093)
- [In-box controls: the DTPicker (2026-10-27, #1094)](in-box-controls-the-dtpicker.md#in-box-controls-the-dtpicker-2026-10-27-1094)
- [What a Timer has at run time (2026-10-27, #801)](what-a-timer-has-at-run-time.md#what-a-timer-has-at-run-time-2026-10-27-801)
- [A watch and its context, in the IDE's Watches window (2026-10-27, #2290)](a-watch-and-its-context.md#a-watch-and-its-context-in-the-ides-watches-window-2026-10-27-2290)
- [In-box controls: the MonthView (2026-10-27, #1095)](in-box-controls-the-monthview.md#in-box-controls-the-monthview-2026-10-27-1095)
- [Key events: KeyDown, KeyPress and KeyUp (2026-10-27, #2325)](key-events-keydown-keypress-and-keyup.md#key-events-keydown-keypress-and-keyup-2026-10-27-2325)
- [In-box controls: the ImageCombo (2026-10-27, #1092)](in-box-controls-the-imagecombo.md#in-box-controls-the-imagecombo-2026-10-27-1092)
- [In-box controls: the FlatScrollBar (2026-10-27, #1097)](in-box-controls-the-flatscrollbar.md#in-box-controls-the-flatscrollbar-2026-10-27-1097)
- [In-box controls: the Animation (2026-10-27, #1096)](in-box-controls-the-animation.md#in-box-controls-the-animation-2026-10-27-1096)
- [The Forms collection (2026-10-28, #2496)](the-forms-collection.md#the-forms-collection-2026-10-28-2496)

- [A compare argument, and when it is checked](compare-argument-validation.md) — #881, supplied SP6 native/p-code measurements.
- [FormatNumber, FormatCurrency and FormatPercent (2026-10-28, #2515)](formatnumber-formatcurrency-and-formatpercent.md#formatnumber-formatcurrency-and-formatpercent-2026-10-28-2515)
- [FormatDateTime (2026-10-28, #2516)](formatdatetime.md#formatdatetime-2026-10-28-2516)
- [LSet and RSet into a String (2026-10-28, #2517)](lset-and-rset-into-a-string.md#lset-and-rset-into-a-string-2026-10-28-2517)
- [LSet between two user-defined types (2026-10-28, #2518)](lset-between-two-user-defined-types.md#lset-between-two-user-defined-types-2026-10-28-2518)
- [CallByName (2026-10-28, #2519)](callbyname.md#callbyname-2026-10-28-2519)
- [CVErr and the Error subtype (2026-10-28, #2520)](cverr-and-the-error-subtype.md#cverr-and-the-error-subtype-2026-10-28-2520)
- [Reset and FileAttr (2026-10-28, #2521)](reset-and-fileattr.md#reset-and-fileattr-2026-10-28-2521)
- [Shell (2026-10-28, #2522)](shell.md#shell-2026-10-28-2522)
- [Pmt, FV, PV, NPer, IPmt, PPmt and Rate (2026-10-28, #2523)](pmt-fv-pv-nper-ipmt-ppmt-and-rate.md#pmt-fv-pv-nper-ipmt-ppmt-and-rate-2026-10-28-2523)
- [DDB, SLN and SYD (2026-10-28, #2524)](ddb-sln-and-syd.md#ddb-sln-and-syd-2026-10-28-2524)
- [NPV, IRR and MIRR (2026-10-28, #2525)](npv-irr-and-mirr.md#npv-irr-and-mirr-2026-10-28-2525)
- [Partition (2026-10-28, #2526)](partition.md#partition-2026-10-28-2526)
- [Err.HelpFile, Err.HelpContext and Err.LastDllError (2026-10-28, #2527)](err-helpfile-err-helpcontext-and-err-lastdllerror.md#errhelpfile-errhelpcontext-and-errlastdllerror-2026-10-28-2527)
- [Erl: the line number an error reports (2026-10-28, #2528)](erl-the-line-number-an-error-reports.md#erl-the-line-number-an-error-reports-2026-10-28-2528)
- [A line number on a closing or middle line of a block (2026-10-28, #2529)](a-line-number-on-a-closing-or-middle-line-of-a-block.md#a-line-number-on-a-closing-or-middle-line-of-a-block-2026-10-28-2529)
- [App.HelpFile, App.StartMode, App.TaskVisible and IMEStatus (2026-10-28, #2551)](app-helpfile-app-startmode-app-taskvisible-and-imestatus.md#apphelpfile-appstartmode-apptaskvisible-and-imestatus-2026-10-28-2551)
- [App's logging: StartLogging, LogEvent, LogMode and LogPath (2026-10-28, #2552)](app-s-logging-startlogging-logevent-logmode-and-logpath.md#apps-logging-startlogging-logevent-logmode-and-logpath-2026-10-28-2552)
- [Lock and Unlock (2026-10-28, #2553)](lock-and-unlock.md#lock-and-unlock-2026-10-28-2553)
- [Width # (2026-10-28, #2554)](width.md#width--2026-10-28-2554)
- [A class with VB_PredeclaredId = True (2026-10-28, #2555)](a-class-with-vb-predeclaredid-true.md#a-class-with-vb_predeclaredid--true-2026-10-28-2555)
