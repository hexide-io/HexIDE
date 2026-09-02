<#
.SYNOPSIS
    Ask the real VB6 compiler what an expression does. The fidelity oracle, scripted.

.DESCRIPTION
    HexIDE's interpreter aims for runtime-execution fidelity, and the only trustworthy source for
    "what does VB6 actually do here" is vb6.exe itself. This script automates the loop documented in
    docs/vb6-fidelity-oracle.md: build a tiny Sub Main program, compile it with /make, run it, and
    read back what it wrote.

    You give it expressions. It gives you `value | TypeName`, or `ERR<n>` when VB6 raised instead.

    VB6 is Windows-only and often lives in a VM. Both are supported:
      -Local          run against a VB6 install on this machine
      (default)       run inside a Hyper-V guest over PowerShell Direct

.PARAMETER Expression
    One or more VB6 expressions to evaluate. Accepts pipeline input.

.PARAMETER Path
    A file of expressions, one per line. Blank lines and lines starting with ' are ignored.
    A line may be `label: expression` to name the row; otherwise the expression is its own label.

.PARAMETER Local
    Use a VB6 install on this machine instead of the VM.

.PARAMETER VMName
    Hyper-V guest holding VB6. Default: Win10.

.PARAMETER CredentialPath
    Export-Clixml'd PSCredential for the guest, DPAPI-encrypted to the current user.
    Default: ~\.hexide\win10.cred. Create with:
        Get-Credential | Export-Clixml "$env:USERPROFILE\.hexide\win10.cred"

.PARAMETER Vb6Exe
    Path to VB6.EXE *as seen by whoever runs it* (the guest, unless -Local).
    Defaults to $env:VB6_EXE, else the standard install location.

.PARAMETER Raw
    Emit the probe's raw output lines instead of objects.

.PARAMETER KeepWorkDir
    Leave the generated .bas/.vbp/.exe in place for inspection.

.EXAMPLE
    .\vb6-oracle.ps1 'CByte(100) + CByte(100)', 'CByte(200) + CByte(100)'

    Expression                  Value  Type  Err
    ----------                  -----  ----  ---
    CByte(100) + CByte(100)     200    Byte
    CByte(200) + CByte(100)                    6

.EXAMPLE
    .\vb6-oracle.ps1 -Path probes\division.txt | Format-Table

.NOTES
    Every probe runs under `On Error Resume Next`. This is not tidiness: an unguarded runtime error
    pops a MODAL dialog inside the VM and the run hangs until someone dismisses it. Capturing
    Err.Number turns an overflow into data (ERR6) instead of a stuck session.

    Source is written CRLF + ASCII. VB6 fails to load LF-terminated modules and reports it as
    "User-defined type not defined", which is a memorably unhelpful way to learn about line endings.
#>
[CmdletBinding(DefaultParameterSetName = 'Expression')]
param(
    [Parameter(ParameterSetName = 'Expression', Mandatory, Position = 0, ValueFromPipeline)]
    [string[]] $Expression,

    [Parameter(ParameterSetName = 'Path', Mandatory)]
    [string] $Path,

    # Lines for the module's DECLARATIONS section, before Sub Main. Replaces the default
    # `Option Explicit` — pass it yourself if you want it. This is how a directive that is not an
    # expression gets probed at all: Option Base, Option Compare, DefInt/DefStr, module-level Const.
    # DefType in particular CANNOT be measured with Option Explicit on, because it is a rule about
    # undeclared variables.
    [string[]] $Declarations,

    # Class modules to compile alongside the probe, as name -> body. The canonical .cls header is
    # supplied; give only the members. Without this the whole object model is unmeasurable — Implements,
    # As New, Set type-enforcement, default members and parameterized properties all need at least one
    # class, and several need two.
    [hashtable] $Classes,

    [switch] $Local,
    [string] $VMName = 'Win10',
    [string] $CredentialPath = (Join-Path $env:USERPROFILE '.hexide\win10.cred'),
    [string] $Vb6Exe,
    [switch] $Raw,
    [switch] $KeepWorkDir
)

begin {
    $ErrorActionPreference = 'Stop'
    $collected = [System.Collections.Generic.List[string]]::new()

    if (-not $Vb6Exe) {
        $Vb6Exe = if ($env:VB6_EXE) { $env:VB6_EXE }
                  else { 'C:\Program Files (x86)\Microsoft Visual Studio\VB98\VB6.EXE' }
    }
}

process {
    if ($PSCmdlet.ParameterSetName -eq 'Expression') { $collected.AddRange($Expression) }
}

end {
    if ($PSCmdlet.ParameterSetName -eq 'Path') {
        if (-not (Test-Path $Path)) { throw "Probe file not found: $Path" }
        Get-Content $Path | ForEach-Object {
            $line = $_.Trim()
            if ($line -and -not $line.StartsWith("'")) { $collected.Add($line) }
        }
    }
    if ($collected.Count -eq 0) { throw 'No expressions to evaluate.' }

    # ---- Build the probe -----------------------------------------------------------------------
    # `label: expression` splits on the FIRST colon, but only when what precedes it is a plain
    # identifier-ish token. Otherwise the colon belongs to the VB6 (`a: b` is a statement separator,
    # and `Type:=` is a named argument), so the whole line is the expression.
    # NB: not $raw as the loop variable. PowerShell variable names are case-insensitive, so `$raw`
    # IS the `-Raw` switch parameter, and assigning a string to it throws "Cannot convert String to
    # SwitchParameter" during the loop — an error that reads like a parameter-binding failure at the
    # call site and sends you hunting in entirely the wrong place.
    $cases = foreach ($entry in $collected) {
        if ($entry -match '^\s*([A-Za-z_][A-Za-z0-9_ ]*?)\s*:\s*(?!=)(.+)$') {
            [pscustomobject]@{ Label = $Matches[1].Trim(); Expr = $Matches[2].Trim() }
        } else {
            [pscustomobject]@{ Label = $entry.Trim(); Expr = $entry.Trim() }
        }
    }

    $guestDir = 'C:\hexide-oracle'
    $outFile  = "$guestDir\out.txt"

    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine('Attribute VB_Name = "Module1"')
    if ($Declarations) {
        foreach ($d in $Declarations) { [void]$sb.AppendLine($d) }
    } else {
        [void]$sb.AppendLine('Option Explicit')
    }
    [void]$sb.AppendLine('')
    # Value and type in one line, or the error number if VB6 raised. Variant `v` so the probe never
    # imposes a type of its own — the whole point is to observe the type VB6 chose.
    # Every path through WT must Print exactly once. A row that renders badly has to come back as a row
    # saying so — a missing line looks like a case you never ran, and in an oracle that is how a wrong
    # answer gets believed. `On Error Resume Next` is per-procedure, so Main's does not cover this one.
    [void]$sb.AppendLine('Private Sub WT(f As Integer, label As String, v As Variant, en As Long)')
    [void]$sb.AppendLine('    Dim s As String')
    [void]$sb.AppendLine('    On Error Resume Next')
    [void]$sb.AppendLine('    If en <> 0 Then')
    [void]$sb.AppendLine('        Print #f, label & vbTab & "ERR" & en')
    [void]$sb.AppendLine('        Exit Sub')
    [void]$sb.AppendLine('    End If')
    # CStr(Null) raises 94, CStr(an object) raises 438. Both are legitimate probe results, so render
    # them rather than letting the row evaporate.
    [void]$sb.AppendLine('    If IsNull(v) Then')
    [void]$sb.AppendLine('        s = "Null"')
    [void]$sb.AppendLine('    ElseIf IsObject(v) Then')
    [void]$sb.AppendLine('        s = "<object>"')
    [void]$sb.AppendLine('    ElseIf IsArray(v) Then')
    [void]$sb.AppendLine('        s = "<array>"')
    [void]$sb.AppendLine('    Else')
    [void]$sb.AppendLine('        Err.Clear')
    [void]$sb.AppendLine('        s = CStr(v)')
    [void]$sb.AppendLine('        If Err.Number <> 0 Then s = "<unrenderable:" & Err.Number & ">"')
    [void]$sb.AppendLine('    End If')
    [void]$sb.AppendLine('    Print #f, label & vbTab & s & vbTab & TypeName(v)')
    [void]$sb.AppendLine('End Sub')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('Sub Main()')
    [void]$sb.AppendLine('    Dim f As Integer')
    [void]$sb.AppendLine('    Dim v As Variant')
    [void]$sb.AppendLine('    f = FreeFile')
    [void]$sb.AppendLine("    Open ""$outFile"" For Output As #f")
    [void]$sb.AppendLine('    On Error Resume Next')
    foreach ($c in $cases) {
        # Labels are indices, not data — they come back to match rows up, so they must survive the
        # trip. Escape embedded quotes for the VB6 literal.
        $lbl = $c.Label -replace '"', '""'
        [void]$sb.AppendLine("    Err.Clear: v = Empty: v = $($c.Expr): WT f, ""$lbl"", v, Err.Number")
    }
    [void]$sb.AppendLine('    On Error GoTo 0')
    [void]$sb.AppendLine('    Close #f')
    [void]$sb.AppendLine('End Sub')

    $bas = $sb.ToString()
    $classLines = ''
    if ($Classes) {
        foreach ($name in $Classes.Keys) { $classLines += "Class=$name; $name.cls`r`n" }
    }
    $vbp = @"
Type=Exe
Module=Module1; Module1.bas
$classLines
Reference=*\G{00020430-0000-0000-C000-000000000046}#2.0#0#..\..\Windows\SysWOW64\stdole2.tlb#OLE Automation
Startup="Sub Main"
ExeName32="verify.exe"
Name="verify"
"@

    # ---- Where the compile happens -------------------------------------------------------------
    # One scriptblock, run either here or in the guest. VB6 must be driven through ProcessStartInfo
    # with an ABSOLUTE .vbp path and CreateNoWindow — a relative path, or launching via a shell,
    # makes VB6.EXE open its GUI instead of doing a headless /make.
    $work = {
        param($dir, $basText, $vbpText, $exePath, $timeoutSec, $classText)

        $null = New-Item -ItemType Directory -Force -Path $dir
        $toCrLf = { param($t) ($t -replace "`r`n", "`n") -replace "`n", "`r`n" }
        [System.IO.File]::WriteAllText("$dir\Module1.bas", (& $toCrLf $basText), [System.Text.Encoding]::ASCII)
        [System.IO.File]::WriteAllText("$dir\verify.vbp", (& $toCrLf $vbpText), [System.Text.Encoding]::ASCII)
        if ($classText) { foreach ($clsName in $classText.Keys) {
            # The header VB6 writes itself. Without VB_Name the class has no identity and the project
            # will not load; MultiUse/Creatable make it instantiable with New from the module.
            $hdr = "VERSION 1.0 CLASS`r`nBEGIN`r`n  MultiUse = -1  'True`r`nEND`r`n" + "Attribute VB_Name = `"$clsName`"`r`n" + "Attribute VB_GlobalNameSpace = False`r`nAttribute VB_Creatable = True`r`n" + "Attribute VB_PredeclaredId = False`r`nAttribute VB_Exposed = False`r`n"
            [System.IO.File]::WriteAllText("$dir\$clsName.cls", (& $toCrLf ($hdr + $classText[$clsName])), [System.Text.Encoding]::ASCII)
        } }
        foreach ($stale in "$dir\out.txt", "$dir\err.log", "$dir\verify.exe") {
            if (Test-Path $stale) { [System.IO.File]::Delete($stale) }
        }

        if (-not (Test-Path $exePath)) { return @{ Stage = 'vb6-missing'; Detail = $exePath } }

        function Start-Quiet($file, $arguments, $seconds) {
            $psi = [System.Diagnostics.ProcessStartInfo]::new()
            $psi.FileName = $file
            $psi.Arguments = $arguments
            $psi.UseShellExecute = $false
            $psi.CreateNoWindow = $true
            $psi.WorkingDirectory = Split-Path $file
            $p = [System.Diagnostics.Process]::Start($psi)
            if (-not $p.WaitForExit($seconds * 1000)) {
                try { $p.Kill() } catch { }
                return $null   # timeout: almost always a modal waiting for someone
            }
            return $p.ExitCode
        }

        $compileExit = Start-Quiet $exePath "/make `"$dir\verify.vbp`" /out `"$dir\err.log`"" $timeoutSec
        $errLog = if (Test-Path "$dir\err.log") { Get-Content "$dir\err.log" -Raw } else { '' }
        if ($null -eq $compileExit) { return @{ Stage = 'compile-timeout'; Detail = $errLog } }
        if (-not (Test-Path "$dir\verify.exe")) { return @{ Stage = 'compile-failed'; Detail = $errLog } }

        $runExit = Start-Quiet "$dir\verify.exe" '' $timeoutSec
        if ($null -eq $runExit) { return @{ Stage = 'run-timeout'; Detail = 'probe hung — an unguarded modal?' } }
        if (-not (Test-Path "$dir\out.txt")) { return @{ Stage = 'no-output'; Detail = "exit $runExit" } }

        return @{ Stage = 'ok'; Lines = @(Get-Content "$dir\out.txt"); ErrLog = $errLog }
    }

    if ($Local) {
        Write-Verbose "Running locally against $Vb6Exe"
        $result = & $work $guestDir $bas $vbp $Vb6Exe 120 $Classes
    } else {
        if (-not (Test-Path $CredentialPath)) {
            throw "No credential at $CredentialPath. Create it with:`n" +
                  "    Get-Credential | Export-Clixml `"$CredentialPath`""
        }
        $cred = Import-Clixml $CredentialPath
        Write-Verbose "Opening PowerShell Direct session to $VMName as $($cred.UserName)"
        $session = New-PSSession -VMName $VMName -Credential $cred
        try {
            $result = Invoke-Command -Session $session -ScriptBlock $work `
                        -ArgumentList $guestDir, $bas, $vbp, $Vb6Exe, 120, $Classes
        } finally {
            Remove-PSSession $session
        }
    }

    # ---- Report --------------------------------------------------------------------------------
    switch ($result.Stage) {
        'vb6-missing' {
            throw "VB6.EXE not found at: $($result.Detail)`nPass -Vb6Exe, or set `$env:VB6_EXE."
        }
        'compile-failed' {
            throw "VB6 /make failed. Compiler log:`n$($result.Detail)`n" +
                  "A probe that names your own class needs the OLE Automation reference; " +
                  "one that fails to load at all is usually line endings."
        }
        'compile-timeout' {
            throw "VB6 /make did not finish. It may be showing a dialog.`n$($result.Detail)"
        }
        'run-timeout' {
            throw "The compiled probe hung: $($result.Detail) Every case must be guarded by On Error Resume Next."
        }
        'no-output' {
            throw "The probe produced no output ($($result.Detail))."
        }
    }

    if (-not $KeepWorkDir -and $Local) { Remove-Item $guestDir -Recurse -Force -ErrorAction SilentlyContinue }

    # A row that never came back is the dangerous failure: it reads as "I didn't ask that" rather than
    # "that went wrong", and an oracle you quietly under-answer is worse than one that errors.
    $returned = @($result.Lines | Where-Object { $_ })
    if ($returned.Count -ne $cases.Count) {
        Write-Warning ("Sent $($cases.Count) case(s), got $($returned.Count) row(s) back. " +
                       'Missing rows are a harness fault, not a VB6 answer — rerun with -Raw -KeepWorkDir.')
    }

    if ($Raw) { return $result.Lines }

    foreach ($line in $result.Lines) {
        if (-not $line) { continue }
        $parts = $line -split "`t"
        if ($parts.Count -ge 3) {
            [pscustomobject]@{ Expression = $parts[0]; Value = $parts[1]; Type = $parts[2]; Err = $null }
        } elseif ($parts.Count -eq 2 -and $parts[1] -match '^ERR(\d+)$') {
            [pscustomobject]@{ Expression = $parts[0]; Value = $null; Type = $null; Err = [int]$Matches[1] }
        } else {
            [pscustomobject]@{ Expression = $line; Value = $null; Type = $null; Err = $null }
        }
    }
}
