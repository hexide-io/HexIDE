<#
.SYNOPSIS
    Ask a real vb6.exe whether each snippet in a corpus is LEGAL VB6. A compile-only oracle.

.DESCRIPTION
    vb6-oracle.ps1 answers "what does this expression evaluate to". This answers a different and
    more basic question — "would VB6 accept this at all" — and the difference matters mechanically:

      * It compiles ONLY. Legality is a compile question, and not running the result avoids every
        way a probe can hang on a modal.
      * It compiles each case in ISOLATION. The value oracle puts every probe in one program, so a
        single illegal case aborts the whole batch. A conformance corpus is mostly *about* illegal
        cases, which makes that batching exactly backwards here.
      * It expects to be WRONG sometimes. Each case carries a prediction; the output is the
        agreement between prediction and compiler, and a disagreement is the interesting result.

    One VM session serves the whole corpus, so the per-case cost is a compile rather than a
    round-trip.

    This is the "legality oracle" named in docs/vb6-grammar-fixes.md, which the clean-room corpus
    method depends on: it is what makes an independently authored corpus authoritative rather than
    merely plausible.

.PARAMETER CorpusPath
    A directory of .json case files, or a single .json file. Each file is an object with a `cases`
    array of { id, code (array of source lines), expect (legal|illegal|unsure), scope
    (statement|module), why }.

.PARAMETER OutFile
    Where to write the results as JSON. Defaults beside the corpus as `results.json`.

.EXAMPLE
    ./scripts/vb6-legality.ps1 -CorpusPath corpus/conformance

.NOTES
    Source is written CRLF + ASCII, for the reason vb6-oracle.ps1 documents: VB6 will not load an
    LF-terminated module and blames it on a user-defined type.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [string] $CorpusPath,

    [string] $OutFile,
    [switch] $Local,
    [string] $VMName = 'Win10',
    [string] $CredentialPath = (Join-Path $env:USERPROFILE '.hexide\win10.cred'),
    [string] $Vb6Exe,
    [int]    $TimeoutSec = 60,

    # Also RUN each legal case and record what it printed. Off by default, because running is a
    # strictly weaker guarantee than compiling: a compile cannot hang, and an unhandled VB6 runtime
    # error in a compiled exe puts up a modal and waits forever. See the capture notes below.
    [switch] $CaptureOutput
)

$ErrorActionPreference = 'Stop'

if (-not $Vb6Exe) {
    $Vb6Exe = if ($env:VB6_EXE) { $env:VB6_EXE }
              else { 'C:\Program Files (x86)\Microsoft Visual Studio\VB98\VB6.EXE' }
}

# ---- Load the corpus ---------------------------------------------------------------------------

if (-not (Test-Path $CorpusPath)) { throw "Corpus not found: $CorpusPath" }
$files = if (Test-Path $CorpusPath -PathType Container) {
    Get-ChildItem -Path $CorpusPath -Filter *.json -File | Where-Object { $_.Name -ne 'results.json' }
} else { Get-Item $CorpusPath }

$cases = [System.Collections.Generic.List[object]]::new()
$skipped = [System.Collections.Generic.List[object]]::new()
foreach ($f in $files) {
    $doc = Get-Content $f.FullName -Raw | ConvertFrom-Json
    foreach ($c in $doc.cases) {
        if (-not $c.id -or -not $c.code) { continue }
        # A case may declare itself undeliverable by THIS harness — the line-ending cases cannot be,
        # because every module is written ASCII+CRLF on purpose. Recording a result for one would be a
        # confident answer that measured nothing, which is worse than no answer.
        if ($c.skip) {
            $skipped.Add([pscustomobject]@{ Key = "$($f.BaseName)/$($c.id)"; Reason = $c.skip })
            continue
        }
        # A case is identified by file AND id: ids are only unique within an area, and a collision
        # would silently drop a case from the report rather than fail.
        $cases.Add([pscustomobject]@{
            Key    = "$($f.BaseName)/$($c.id)"
            Area   = $f.BaseName
            Id     = $c.id
            Expect = if ($c.expect) { $c.expect } else { 'unsure' }
            Scope  = if ($c.scope)  { $c.scope }  else { 'statement' }
            Why    = $c.why
            Source = @($c.code) -join "`r`n"
            # FURTHER standard modules, for the questions one module cannot ask: cross-module visibility,
            # a name declared twice, Private scoping. Written as Module2.bas, Module3.bas … beside
            # Module1 and named in the .vbp. Without them "what does VB6 do when two modules declare the
            # same Type" is simply unmeasurable, and the only alternative is to guess — which is the one
            # thing this corpus exists to avoid. A list rather than a single extra, because the decisive
            # case needs THREE: a user and two exporters, so that nothing local can disambiguate.
            Extra  = $(if ($c.modules) { @($c.modules | ForEach-Object { @($_) -join "`r`n" }) } else { @() })
        })
    }
}
if ($cases.Count -eq 0) { throw "No cases found under $CorpusPath" }
Write-Verbose "Loaded $($cases.Count) cases from $($files.Count) file(s)"

# A statement-scope case is the body of a procedure; a module-scope case is the whole module. Both
# need a Sub Main, because the .vbp names it as the startup and will not build without one.
#
# A module-scope case may define its OWN Sub Main, and appending a second one unconditionally is a
# mistake that costs real results: VB6 answers "Ambiguous name detected: Main" and the case is recorded
# as ILLEGAL for a reason with nothing to do with what it was testing. That silently converted a large
# share of the declaration cases — Declare continued across lines, a continued procedure signature,
# `Option _ Explicit` — into confident wrong facts about VB6.
foreach ($c in $cases) {
    # Line by line rather than one multiline regex. This decides whether a second Sub Main is appended,
    # and getting it wrong is expensive but SILENT: VB6 answers "Ambiguous name detected: Main" and the
    # case is recorded illegal for a reason unrelated to what it tested, which quietly turned a whole
    # family of declaration cases into confident wrong facts about VB6.
    $needsMain = $true
    foreach ($srcLine in ($c.Source -split "`r`n")) {
        if ($srcLine.Trim() -match '^(Public |Private |Friend )?Sub +Main') { $needsMain = $false; break }
    }
    Write-Verbose "[$($c.Key)] scope=$($c.Scope) needsMain=$needsMain"
    $c | Add-Member -NotePropertyName Module -NotePropertyValue $(
        if ($c.Scope -eq 'module') {
            if ($needsMain) { $c.Source + "`r`n`r`nSub Main()`r`nEnd Sub`r`n" } else { $c.Source + "`r`n" }
        } else { "Sub Main()`r`n" + $c.Source + "`r`nEnd Sub`r`n" })
}

# ---- Behaviour capture (optional) --------------------------------------------------------------
#
# `Debug.Print` is INERT in a compiled exe — there is no Immediate window to receive it — so a case
# that prints tells us nothing unless the print is redirected somewhere observable. vb6-oracle.ps1
# already proves the mechanism: a compiled exe can `Print #` to a file.
#
# So each capturable case gets a SECOND module, built from the same source with `Debug.Print`
# rewritten to a helper that appends to a file, and that module is compiled and run. Open/append/
# close per call rather than a module-level handle, so no startup or shutdown hook is needed and a
# case defining its own `Sub Main` needs no special handling.
#
# Three honesty constraints, all load-bearing:
#
#   * The rewrite CHANGES THE PROGRAM, and some cases are *about* the construct being rewritten. So
#     the probe's legality is checked against the original's; if they disagree the rewrite broke the
#     case and NO behaviour is recorded. A divergence the harness introduced must never be reported
#     as a divergence in VB6 — that is the mistake this whole corpus exists to avoid.
#   * A print carrying `;` or `,` is not deliverable by a single-argument helper. Those are excluded
#     BY NAME, so the captured count never overstates what was measured.
#   * The helper records TypeName alongside the value. A gate that compares only rendered text would
#     miss every Integer-for-Long and Single-for-Double, which is precisely where a wrong value hides.
$guestDir = 'C:\hexide-legality'
$notCapturable = [System.Collections.Generic.List[object]]::new()

if ($CaptureOutput) {
    $helper = @"

Public Sub HXP(ByVal hxV As Variant)
    Dim hxF As Integer
    Dim hxS As String
    If IsObject(hxV) Then
        hxS = "<object>"
    ElseIf IsArray(hxV) Then
        hxS = "<array>"
    ElseIf IsNull(hxV) Then
        hxS = "Null"
    Else
        hxS = CStr(hxV)
    End If
    hxF = FreeFile
    Open "$guestDir\out.txt" For Append As #hxF
    Print #hxF, TypeName(hxV) & Chr`$(9) & hxS
    Close #hxF
End Sub
"@

    foreach ($c in $cases) {
        $all = @($c.Source) + @($c.Extra)
        $reason = $null
        if (($all -join "`r`n") -notmatch 'Debug\.Print') {
            $reason = 'nothing printed, so nothing to observe'
        } elseif (($all -join "`r`n") -match 'Debug\.Print[^\r\n]*[;,]') {
            $reason = 'print uses a ; or , separator; the single-argument helper cannot carry it'
        } elseif (($all -join "`r`n") -match '(?m)^\s*(Public\s+)?Sub\s+HXP\b') {
            $reason = 'case already defines HXP'
        }

        if ($reason) {
            $notCapturable.Add([pscustomobject]@{ Key = $c.Key; Reason = $reason })
            $c | Add-Member -NotePropertyName Probe      -NotePropertyValue $null
            $c | Add-Member -NotePropertyName ProbeExtra -NotePropertyValue @()
            continue
        }

        $rewritten = $c.Source -replace 'Debug\.Print', 'HXP'
        $c | Add-Member -NotePropertyName ProbeExtra -NotePropertyValue @(
            @($c.Extra) | ForEach-Object { $_ -replace 'Debug\.Print', 'HXP' })
        $c | Add-Member -NotePropertyName Probe -NotePropertyValue $(
            if ($c.Scope -eq 'module') {
                $needsMain = $true
                foreach ($srcLine in ($rewritten -split "`r`n")) {
                    if ($srcLine.Trim() -match '^(Public |Private |Friend )?Sub +Main') { $needsMain = $false; break }
                }
                if ($needsMain) { $rewritten + "`r`n`r`nSub Main()`r`nEnd Sub`r`n" + $helper }
                else { $rewritten + "`r`n" + $helper }
            } else { "Sub Main()`r`n" + $rewritten + "`r`nEnd Sub`r`n" + $helper })
    }
    Write-Verbose "Capture: $($cases.Count - $notCapturable.Count) capturable, $($notCapturable.Count) not"
}

$payload = $cases | ForEach-Object {
    @{ Key = $_.Key; Module = $_.Module; Extra = $_.Extra; Probe = $_.Probe; ProbeExtra = $_.ProbeExtra }
}

# ---- Compile each case, in one session ---------------------------------------------------------

$work = {
    param($dir, $items, $exePath, $timeoutSec)

    if (-not (Test-Path $exePath)) { return @{ Stage = 'vb6-missing'; Detail = $exePath } }
    $null = New-Item -ItemType Directory -Force -Path $dir

    # The .vbp is written PER CASE, not once, because a case may bring further modules and the project
    # file has to name every one. Cheap — it is a few lines of text next to a compiler invocation.
    $vbpFor = {
        param($extraCount)
        $lines = @("Module=Module1; Module1.bas")
        for ($i = 2; $i -le $extraCount + 1; $i++) { $lines += "Module=Module$i; Module$i.bas" }
        $mods = $lines -join "`r`n"
        @"
Type=Exe
$mods
Reference=*\G{00020430-0000-0000-C000-000000000046}#2.0#0#..\..\Windows\SysWOW64\stdole2.tlb#OLE Automation
Startup="Sub Main"
ExeName32="verify.exe"
Name="verify"
"@
    }

    $toCrLf = { param($t) ($t -replace "`r`n", "`n") -replace "`n", "`r`n" }

    $results = @()
    foreach ($item in $items) {
        Remove-Item "$dir\verify.exe", "$dir\err.log" -ErrorAction SilentlyContinue
        # Stale extra modules from a PREVIOUS case would be compiled into this one and the result would
        # be about the wrong program, so they are removed rather than overwritten.
        Get-ChildItem "$dir\Module*.bas" -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -ne 'Module1.bas' } | Remove-Item -ErrorAction SilentlyContinue

        $extra = @($item.Extra)
        [System.IO.File]::WriteAllText("$dir\verify.vbp",
            (& $toCrLf (& $vbpFor $extra.Count)), [System.Text.Encoding]::ASCII)
        for ($i = 0; $i -lt $extra.Count; $i++) {
            [System.IO.File]::WriteAllText("$dir\Module$($i + 2).bas",
                (& $toCrLf $extra[$i]), [System.Text.Encoding]::ASCII)
        }
        [System.IO.File]::WriteAllText("$dir\Module1.bas", (& $toCrLf $item.Module), [System.Text.Encoding]::ASCII)

        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $exePath
        $psi.Arguments = "/make `"$dir\verify.vbp`" /out `"$dir\err.log`""
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        $psi.WorkingDirectory = $dir

        $timedOut = $false
        $p = [System.Diagnostics.Process]::Start($psi)
        if (-not $p.WaitForExit($timeoutSec * 1000)) {
            try { $p.Kill() } catch { }
            $timedOut = $true
        }

        $err = if (Test-Path "$dir\err.log") { (Get-Content "$dir\err.log" -Raw) } else { '' }
        $built = Test-Path "$dir\verify.exe"
        $actual = if ($timedOut) { 'timeout' } elseif ($built) { 'legal' } else { 'illegal' }

        # Behaviour: only for a case that compiled, and only where a probe was built for it. The .vbp
        # and the extra modules are already on disk from the compile above and are rewritten in place,
        # so the probe is the same PROJECT, differing only in how its prints are delivered.
        $ran = $null; $output = $null
        if ($actual -eq 'legal' -and $item.Probe) {
            Remove-Item "$dir\verify.exe", "$dir\err.log", "$dir\out.txt" -ErrorAction SilentlyContinue
            $pe = @($item.ProbeExtra)
            for ($i = 0; $i -lt $pe.Count; $i++) {
                [System.IO.File]::WriteAllText("$dir\Module$($i + 2).bas",
                    (& $toCrLf $pe[$i]), [System.Text.Encoding]::ASCII)
            }
            [System.IO.File]::WriteAllText("$dir\Module1.bas", (& $toCrLf $item.Probe), [System.Text.Encoding]::ASCII)

            $pp = [System.Diagnostics.Process]::Start($psi)   # identical /make invocation
            if (-not $pp.WaitForExit($timeoutSec * 1000)) { try { $pp.Kill() } catch { } }

            if (-not (Test-Path "$dir\verify.exe")) {
                # The rewrite changed the program's legality. Report the artefact, never a behaviour.
                $ran = 'rewrite-broke-case'
                $ren = if (Test-Path "$dir\err.log") { (Get-Content "$dir\err.log" -Raw) } else { '' }
                $output = ($ren -replace "`r?`n", ' ').Trim()
            } else {
                $rpsi = New-Object System.Diagnostics.ProcessStartInfo
                $rpsi.FileName = "$dir\verify.exe"
                $rpsi.UseShellExecute = $false
                $rpsi.CreateNoWindow = $true
                $rpsi.WorkingDirectory = $dir

                $r = [System.Diagnostics.Process]::Start($rpsi)
                if (-not $r.WaitForExit($timeoutSec * 1000)) {
                    # An unhandled VB6 runtime error raises a modal and waits. Killing it is the only
                    # way out; whatever reached the file first is still a real observation, recorded
                    # as such but never as a clean run.
                    try { $r.Kill() } catch { }
                    $ran = 'hung'
                } else {
                    $ran = if ($r.ExitCode -eq 0) { 'ok' } else { "exit:$($r.ExitCode)" }
                }
                # ONE newline-joined string, never an array: ConvertTo-Json silently collapses a
                # single-element array to a scalar, so a one-print case and a two-print case would
                # deserialise to different SHAPES and a reader that indexes it would walk a string
                # character by character. Joining makes the shape uniform and the split explicit.
                $output = if (Test-Path "$dir\out.txt") { ((Get-Content "$dir\out.txt") -join "`n") } else { '' }
            }
        }

        $results += @{
            Key     = $item.Key
            Actual  = $actual
            Error   = ($err -replace "`r?`n", ' ').Trim()
            Ran     = $ran
            Output  = $output
        }
    }

    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    return @{ Stage = 'ok'; Results = $results }
}

if ($Local) {
    Write-Verbose "Compiling locally against $Vb6Exe"
    $outcome = & $work $guestDir $payload $Vb6Exe $TimeoutSec
} else {
    if (-not (Test-Path $CredentialPath)) {
        throw "No credential at $CredentialPath. Create it with:`n" +
              "    Get-Credential | Export-Clixml `"$CredentialPath`""
    }
    $cred = Import-Clixml $CredentialPath
    Write-Verbose "Opening PowerShell Direct session to $VMName ($($cases.Count) compiles)"
    $session = New-PSSession -VMName $VMName -Credential $cred
    try {
        $outcome = Invoke-Command -Session $session -ScriptBlock $work `
                     -ArgumentList $guestDir, $payload, $Vb6Exe, $TimeoutSec
    } finally { Remove-PSSession $session }
}

if ($outcome.Stage -eq 'vb6-missing') {
    throw "VB6.EXE not found at: $($outcome.Detail)`nPass -Vb6Exe, or set `$env:VB6_EXE."
}

# ---- Report ------------------------------------------------------------------------------------

$byKey = @{}
foreach ($r in $outcome.Results) { $byKey[$r.Key] = $r }

$rows = foreach ($c in $cases) {
    $r = $byKey[$c.Key]
    $actual = if ($r) { $r.Actual } else { 'no-result' }
    [pscustomobject]@{
        Key     = $c.Key
        Area    = $c.Area
        Expect  = $c.Expect
        Actual  = $actual
        # "unsure" is a question, not a prediction, so it can never disagree — it RESOLVES.
        Verdict = if ($c.Expect -eq 'unsure') { 'resolved' }
                  elseif ($actual -eq $c.Expect) { 'agrees' }
                  else { 'DISAGREES' }
        Error   = if ($r) { $r.Error } else { '' }
        Why     = $c.Why
        Ran     = if ($r) { $r.Ran } else { $null }
        Output  = if ($r -and $null -ne $r.Output) { [string]$r.Output } else { $null }
    }
}

if (-not $OutFile) {
    $base = if (Test-Path $CorpusPath -PathType Container) { $CorpusPath } else { Split-Path $CorpusPath }
    $OutFile = Join-Path $base 'results.json'
}
$rows | ConvertTo-Json -Depth 6 | Set-Content $OutFile -Encoding UTF8

$agree    = @($rows | Where-Object Verdict -eq 'agrees').Count
$disagree = @($rows | Where-Object Verdict -eq 'DISAGREES').Count
$resolved = @($rows | Where-Object Verdict -eq 'resolved').Count
$legal    = @($rows | Where-Object Actual  -eq 'legal').Count

Write-Host ""
Write-Host "  cases      $($rows.Count)"
Write-Host "  legal      $legal   illegal $($rows.Count - $legal)"
Write-Host "  agrees     $agree"
Write-Host "  DISAGREES  $disagree   <- the interesting ones"
Write-Host "  resolved   $resolved   (predicted 'unsure')"
if ($skipped.Count) { Write-Host "  skipped    $($skipped.Count)   (undeliverable by this harness)" }
if ($CaptureOutput) {
    Write-Host ""
    Write-Host "  captured   $(@($rows | Where-Object { $_.Ran -eq 'ok' }).Count)   (ran cleanly; output recorded)"
    Write-Host "  hung       $(@($rows | Where-Object { $_.Ran -eq 'hung' }).Count)   (killed; partial output kept)"
    Write-Host "  nonzero    $(@($rows | Where-Object { $_.Ran -like 'exit:*' }).Count)"
    Write-Host "  rewrite    $(@($rows | Where-Object { $_.Ran -eq 'rewrite-broke-case' }).Count)   <- harness artefact, NOT a VB6 result"
    Write-Host "  no probe   $($notCapturable.Count)"
}
Write-Host "  written to $OutFile"
Write-Host ""

# Disagreements to the pipeline, so a caller can act on them without re-reading the file.
$rows | Where-Object Verdict -ne 'agrees'
