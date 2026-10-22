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
    Stop the HexIDE started for the port, found by the --server-port on its command line, so a hung IDE and one
    whose server has gone down without it are found too. Only one started for this session is stopped, as the
    profile directory on its command line says; another session's IDE, or anything else holding the port, is
    reported and left running. For when shutdown_ide cannot be called. Never stop HexIDE by process name: other
    sessions' IDEs share it.

.PARAMETER Exe
    The HexIDE.Desktop executable. Default: this checkout's Debug build.

.PARAMETER TimeoutSeconds
    How long to wait for the IDE to answer. Default 30.

.PARAMETER IdeArguments
    Anything after the script's own parameters is passed to HexIDE, in place of the default --newproject, so pass
    --newproject again along with other flags if you still want it. --server-port is refused (set
    HEXIDE_MCP_PORT), and so is --user-data-dir while HEXIDE_SESSION is set. From Git Bash, use the -- forms of
    HexIDE's flags: it rewrites an argument such as /newproject into a Windows path before the script sees it.

.EXAMPLE
    pwsh scripts/dev-ide.ps1
    Starts this checkout's Debug build with a new Standard EXE project, on $env:HEXIDE_MCP_PORT or 5123.

.EXAMPLE
    pwsh scripts/dev-ide.ps1 --capture-lsp --newproject
    The same, recording every language-server conversation from its first handshake.

.EXAMPLE
    pwsh scripts/dev-ide.ps1 demo/spring-tide/SpringTide.vbp
    Starts it on a project instead.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\dev-ide.ps1
    The same from Windows PowerShell 5.1, where PowerShell 7 is not installed.

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

# Read so that a variable set to nothing is told apart from one not set at all. .mcp.json expands an empty
# HEXIDE_MCP_PORT into a URL with no port, so defaulting here would put the IDE on 5123 while the client reaches
# nothing. The whole block is read because Windows PowerShell's GetEnvironmentVariable returns null for both.
$environment = [Environment]::GetEnvironmentVariables()
function Read-Variable([string] $name) {
    if ($environment.Contains($name)) { [string] $environment[$name] } else { $null }
}

$portText = Read-Variable 'HEXIDE_MCP_PORT'
$port = 5123
if ($null -ne $portText) {
    # Only a bare number: .mcp.json substitutes the text as it stands, so ' 5140' or '+5140' would reach no one.
    if ($portText -notmatch '^[1-9][0-9]{0,4}\z' -or [int] $portText -gt 65535) {
        throw "HEXIDE_MCP_PORT is '$portText'. Set it to a port number from 1 to 65535 and nothing else, or unset " +
            "it for 5123."
    }
    $port = [int] $portText
}
$health = "http://localhost:$port/health"

$session = Read-Variable 'HEXIDE_SESSION'
$profileDirectory = $null
if ($null -ne $session) {
    # One directory under HexIDE/sessions and nothing else: no path that leads out of it, no trailing dot, which
    # Windows drops, no name Windows keeps for a device, and lower case only, because Windows folds case and
    # 'Docs' and 'docs' would share a profile.
    if ($session -cnotmatch '^[a-z0-9]([a-z0-9._-]*[a-z0-9_-])?\z' -or
        $session -match '^(con|prn|aux|nul|com[0-9]|lpt[0-9])(\..*)?\z') {
        throw "HEXIDE_SESSION is '$session'. Use lower-case letters, digits, '.', '_' and '-', starting with a " +
            "letter or digit and not ending in '.', and not a Windows device name such as 'con' or 'nul'."
    }
    $profileDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) "HexIDE/sessions/$session"
}

$onWindows = [System.Environment]::OSVersion.Platform -eq 'Win32NT'

function Get-Health {
    try { Invoke-RestMethod -Uri $health -TimeoutSec 2 } catch { $null }
}

if ($Stop) {
    # Every HexIDE started for this port, found by its command line rather than by whatever answers on the port. A
    # hung IDE holds the port without answering, and one whose server went down without it (#644) holds nothing at
    # all, and those are the cases this is for.
    $hexides = @(
        if ($onWindows) {
            Get-CimInstance Win32_Process -Filter "Name = 'HexIDE.Desktop.exe'" |
                ForEach-Object { [pscustomobject] @{ Id = [int] $_.ProcessId; CommandLine = $_.CommandLine } }
        } else {
            # pwsh reads /proc on Linux and offers nothing on macOS, where ps does.
            Get-Process -Name HexIDE.Desktop -ErrorAction SilentlyContinue | ForEach-Object {
                $line = if ($_.CommandLine) { $_.CommandLine } else { ps -o command= -p $_.Id 2>$null }
                [pscustomobject] @{ Id = $_.Id; CommandLine = $line }
            }
        })
    foreach ($unreadable in $hexides | Where-Object { -not $_.CommandLine }) {
        Write-Warning "Could not read the command line of HexIDE PID $($unreadable.Id), so whether it serves port $port cannot be told. It has been left running."
    }
    $portFlag = "(^|\s)(--|/)server-port\s+`"?$port`"?(\s|\z)"
    $onPort = @($hexides | Where-Object { $_.CommandLine -and $_.CommandLine -match $portFlag })

    if (-not $onPort) {
        $listener = if (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue) {
            Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
                Select-Object -First 1 -ExpandProperty OwningProcess
        } elseif (Get-Command lsof -ErrorAction SilentlyContinue) {
            lsof -nP -t "-iTCP:$port" -sTCP:LISTEN 2>$null | Select-Object -First 1
        }
        if ($listener -and ($holder = Get-Process -Id ([int] $listener) -ErrorAction SilentlyContinue)) {
            throw "No HexIDE was started for port $port. The port is held by $($holder.ProcessName) (PID " +
                "$($holder.Id)), which has been left running."
        }
        Write-Output "No HexIDE started for port $port is running."
        return
    }

    $refused = @()
    foreach ($ide in $onPort) {
        # Only this session's IDE. A session that forgot its port lands on another's, and stopping what serves there
        # would end that session's run. The profile directory on the command line says whose it is, and the whole
        # name is compared rather than searched for, because 'a' is a prefix of 'ab'.
        $owner = [regex]::Match("$($ide.CommandLine)".Replace('/', '\'), '\\HexIDE\\sessions\\([^\\\s"]+)')
        $ownerName = if ($owner.Success) { $owner.Groups[1].Value } else { $null }
        if ($ownerName -ne $session) {
            $whose = if ($ownerName) { "session '$ownerName'" } else { 'a run with no HEXIDE_SESSION' }
            $refused += "PID $($ide.Id), started for $whose"
            continue
        }

        $process = Get-Process -Id $ide.Id -ErrorAction SilentlyContinue
        if ($process) {
            Stop-Process -Id $ide.Id -Force
            $null = $process.WaitForExit(10000)
        }
        Write-Output "Stopped HexIDE (PID $($ide.Id)) on port $port."
    }
    if ($refused) {
        throw "Left running, as not this session's: the HexIDE on port $port with $($refused -join '; '). Check " +
            "HEXIDE_MCP_PORT and HEXIDE_SESSION."
    }
    return
}

# HexIDE takes the last of a repeated flag, so one passed here would quietly win over the script's own: the IDE
# would serve a port the session's client is not pointed at, or keep its files where -Stop cannot tell whose it is.
foreach ($argument in $IdeArguments) {
    if ($argument -match '^(--|/)server-port\z') {
        throw "Set HEXIDE_MCP_PORT instead of passing --server-port: .mcp.json reads the variable, so the session's " +
            "client follows it, and it would not follow the flag."
    }
    if ($profileDirectory -and $argument -match '^(--|/)user-data-dir\z') {
        throw "HEXIDE_SESSION already puts this session's files in $profileDirectory. Drop --user-data-dir, or " +
            "unset HEXIDE_SESSION to choose the directory yourself."
    }
}

if (-not $Exe) {
    $name = if ($onWindows) { 'HexIDE.Desktop.exe' } else { 'HexIDE.Desktop' }
    $Exe = Join-Path (Split-Path $PSScriptRoot -Parent) "IDE/HexIDE.Desktop/bin/Debug/net10.0/$name"
}
if ((Split-Path $Exe -Leaf) -notmatch '^HexIDE\.Desktop(\.exe)?\z') {
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

# Start-Process takes one command line and quotes nothing, so each argument is quoted here by the rules the
# runtime splits it back by: backslashes are literal except before a quote, so those are doubled, and so are any
# before the closing quote. A path ending in '\', as tab completion leaves one, would otherwise swallow the
# arguments after it.
function ConvertTo-Argument([string] $value) {
    if ($value -and $value -notmatch '[\s"]') { return $value }
    $escaped = [regex]::Replace($value, '(\\*)"', { param($m) ($m.Groups[1].Value * 2) + '\"' })
    $escaped = [regex]::Replace($escaped, '(\\+)\z', { param($m) $m.Groups[1].Value * 2 })
    '"' + $escaped + '"'
}
$commandLine = ($arguments | ForEach-Object { ConvertTo-Argument $_ }) -join ' '
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
            Pid         = $ide.Id
            Port        = $port
            Session     = $session
            UserDataDir = $profileDirectory
            Exe         = $Exe
        }
        return
    }
    Start-Sleep -Milliseconds 500
}

throw "HexIDE (PID $($ide.Id)) did not answer on port $port within $TimeoutSeconds s and is still running. " +
    "Stop it with 'scripts/dev-ide.ps1 -Stop' or Stop-Process -Id $($ide.Id)."
