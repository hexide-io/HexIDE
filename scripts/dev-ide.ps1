<#
.SYNOPSIS
    Start a Debug HexIDE for the MCP dev loop on this session's own port and profile, and wait until it is the
    process answering there. With -Stop, stop the HexIDE on that port instead.

.DESCRIPTION
    Several agent sessions often run the dev loop at once, each with its own build. On one shared port they
    collide: the second IDE exits with code 3, and its session goes on driving the first session's build without
    knowing (hexide-io/HexIDE#1212). Two environment variables keep them apart:

      HEXIDE_MCP_PORT   The port to serve on. .mcp.json reads the same variable, so a session's client and its
                        IDE agree. Unset, it is 5123, which is what a single developer gets with no setup.
      HEXIDE_SESSION    A name for the session. When set, the IDE keeps its settings, layout, recents and
                        lsp-servers.json in <LocalApplicationData>/HexIDE/sessions/<name>, so no two sessions share
                        a profile and none touches the user's own. Unset, the user's own profile, as before.

    Set both where the session is started, so the session, its shell and the IDE it launches all see them.

    A start waits for /health to answer with the PID of the process it launched, not merely for a 200: another
    HexIDE still holding the port answers too (hexide-io/HexIDE#53). It refuses to launch at all while the port
    already answers, and says whose process that is.

.PARAMETER Stop
    Stop the HexIDE serving on the port, found from /health or, when that does not answer, from the port's
    listener. Only a HexIDE.Desktop process started for this session is stopped, as its command line's profile
    directory says; anything else holding the port, another session's IDE included, is reported and left.
    For when shutdown_ide cannot be called. Never stop HexIDE by process name: other sessions' IDEs share it.

.PARAMETER Exe
    The HexIDE.Desktop executable. Default: this checkout's Debug build.

.PARAMETER TimeoutSeconds
    How long to wait for the IDE to answer. Default 30.

.PARAMETER IdeArguments
    Anything after the script's own parameters is passed to HexIDE. Default: --newproject.

.EXAMPLE
    pwsh scripts/dev-ide.ps1
    Starts this checkout's Debug build with a new Standard EXE project, on $env:HEXIDE_MCP_PORT or 5123.

.EXAMPLE
    pwsh scripts/dev-ide.ps1 --capture-lsp demo/spring-tide/SpringTide.vbp
    Starts it on a project instead, recording every language-server conversation from its first handshake.

.EXAMPLE
    pwsh scripts/dev-ide.ps1 -Stop
#>
# No positional parameters: a project path given first would otherwise bind to -Exe and be shell-executed as one.
[CmdletBinding(PositionalBinding = $false)]
param(
    [switch] $Stop,
    [string] $Exe,
    [int] $TimeoutSeconds = 30,
    [Parameter(ValueFromRemainingArguments)] [string[]] $IdeArguments
)

$ErrorActionPreference = 'Stop'

$port = 5123
if ($env:HEXIDE_MCP_PORT) {
    # Refused rather than defaulted: .mcp.json would expand the same text into a URL that reaches nothing.
    if (-not [int]::TryParse($env:HEXIDE_MCP_PORT, [ref] $port) -or $port -lt 1 -or $port -gt 65535) {
        throw "HEXIDE_MCP_PORT is '$env:HEXIDE_MCP_PORT', which is not a port from 1 to 65535."
    }
}
$health = "http://localhost:$port/health"

$profileDirectory = $null
if ($env:HEXIDE_SESSION) {
    # The name becomes one directory under HexIDE/sessions, never a path that leads out of it. Nor one ending in
    # a dot, which Windows drops, so 'a.' and 'a' would share a profile.
    if ($env:HEXIDE_SESSION -notmatch '^[A-Za-z0-9]([A-Za-z0-9._-]*[A-Za-z0-9_-])?$') {
        throw "HEXIDE_SESSION is '$env:HEXIDE_SESSION'. Use letters, digits, '.', '_' and '-', starting with a " +
            "letter or digit and not ending in '.'."
    }
    $profileDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) "HexIDE/sessions/$env:HEXIDE_SESSION"
}

function Get-Health {
    try { Invoke-RestMethod -Uri $health -TimeoutSec 2 } catch { $null }
}

if ($Stop) {
    $answering = Get-Health
    $ideId = if ($answering) { [int] $answering.pid } else { $null }
    if (-not $ideId -and (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue)) {
        # A hung IDE holds the port without answering, which is when this is most needed.
        $ideId = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
            Select-Object -First 1 -ExpandProperty OwningProcess
    }
    if (-not $ideId) {
        Write-Output "Nothing is listening on port $port."
        return
    }

    $process = Get-Process -Id $ideId -ErrorAction SilentlyContinue
    if (-not $process) {
        Write-Output "Process $ideId held port $port and has already exited."
        return
    }
    if ($process.ProcessName -ne 'HexIDE.Desktop') {
        throw "Port $port is held by $($process.ProcessName) (PID $ideId), not by HexIDE. It has been left running."
    }

    # Only this session's IDE. A session that forgot its port lands on another's, and stopping what answers there
    # would end that session's run. The profile directory on the IDE's command line is what says whose it is.
    $commandLine = if ([System.Environment]::OSVersion.Platform -eq 'Win32NT') {
        (Get-CimInstance Win32_Process -Filter "ProcessId = $ideId").CommandLine
    } else {
        $process.CommandLine
    }
    $normalised = "$commandLine".Replace('/', '\')
    $owner = [regex]::Match($normalised, '\\HexIDE\\sessions\\([^\\\s"]+)')
    $ours = if ($profileDirectory) {
        $normalised.IndexOf($profileDirectory.Replace('/', '\'), [StringComparison]::OrdinalIgnoreCase) -ge 0
    } else {
        -not $owner.Success
    }
    if (-not $ours) {
        $whose = if ($owner.Success) { "session '$($owner.Groups[1].Value)'" } else { 'a run with no HEXIDE_SESSION' }
        throw "The HexIDE on port $port (PID $ideId) was started for $whose, not for this one, and has been left " +
            "running. Check HEXIDE_MCP_PORT and HEXIDE_SESSION."
    }

    Stop-Process -Id $ideId -Force
    $null = $process.WaitForExit(10000)
    Write-Output "Stopped HexIDE (PID $ideId) on port $port."
    return
}

if (-not $Exe) {
    $name = if ([System.Environment]::OSVersion.Platform -eq 'Win32NT') { 'HexIDE.Desktop.exe' } else { 'HexIDE.Desktop' }
    $Exe = Join-Path (Split-Path $PSScriptRoot -Parent) "IDE/HexIDE.Desktop/bin/Debug/net10.0/$name"
}
if ((Split-Path $Exe -Leaf) -notmatch '^HexIDE\.Desktop(\.exe)?$') {
    throw "-Exe is '$Exe', which is not a HexIDE.Desktop executable."
}
if (-not (Test-Path $Exe -PathType Leaf)) {
    throw "There is no build at $Exe. Build it first: cd IDE; dotnet build HexIDE.Desktop/HexIDE.Desktop.csproj -c Debug"
}

# Before launching, not after: a second IDE on a held port exits with code 3, and a caller that missed the exit
# would carry on driving whichever process does hold it.
if ($answering = Get-Health) {
    throw "Port $port already answers, as HexIDE PID $($answering.pid). If it is yours, call shutdown_ide or run " +
        "'scripts/dev-ide.ps1 -Stop'. If it is not, it belongs to another session: set HEXIDE_MCP_PORT to your own port."
}

$arguments = @('--server-port', "$port")
if ($profileDirectory) {
    $arguments += '--user-data-dir', $profileDirectory
}
$arguments += if ($IdeArguments) { $IdeArguments } else { @('--newproject') }

# Start-Process joins an argument array with spaces and quotes nothing, so a profile path with a space in it
# would arrive as two arguments.
$commandLine = ($arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
$ide = Start-Process -FilePath $Exe -ArgumentList $commandLine -PassThru
# Read now: Windows PowerShell reports no exit code for a process whose handle was never opened.
$null = $ide.Handle

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while ((Get-Date) -lt $deadline) {
    if ($ide.HasExited) {
        $why = switch ($ide.ExitCode) {
            2 { 'its command line was refused' }
            3 { "port $port is held by something that does not answer /health" }
            4 { 'its automation server could not start' }
            default { "exit code $($ide.ExitCode)" }
        }
        throw "HexIDE exited before answering on port ${port}: $why. Its log is under the HexIDE logs directory."
    }

    $answering = Get-Health
    if ($answering -and [int] $answering.pid -eq $ide.Id) {
        [pscustomobject] @{
            Pid             = $ide.Id
            Port            = $port
            Session         = $env:HEXIDE_SESSION
            UserDataDir     = $profileDirectory
            Exe             = $Exe
        }
        return
    }
    Start-Sleep -Milliseconds 500
}

throw "HexIDE (PID $($ide.Id)) did not answer on port $port within $TimeoutSeconds s and is still running. " +
    "Stop it with 'scripts/dev-ide.ps1 -Stop' or Stop-Process -Id $($ide.Id)."
