<#
.SYNOPSIS
  Starts or stops the stock twinBASIC IDE with DevTools open on its front end, for the oracle harness (#5139).

.DESCRIPTION
  start: makes a copy of the WebView2 runtime under -Work (junctions for folders, hard links for files, falling back
  to copies across volumes), replaces its msedgewebview2.exe with the shim from ./shim, points the IDE at the copy
  through WEBVIEW2_BROWSER_EXECUTABLE_FOLDER and starts the IDE, optionally on a project file. The IDE's own page
  then answers DevTools on 127.0.0.1:-Port, and its web panel on -Port + 1.

  stop: stops what start started, by PID, then anything still running from the install folder (the compiler the
  IDE launched outlives it) and the browser processes of this runtime copy, by image path. Never by name alone.

  The compiler is launch-and-watch only. Nothing here reads, hooks or instruments it.
#>
param(
  [Parameter(Mandatory)] [ValidateSet('start', 'stop')] [string] $Action,
  [Parameter(Mandatory)] [string] $Install,
  [Parameter(Mandatory)] [string] $Work,
  [string] $Project,
  [string] $Shim,
  [int] $Port = 9341
)
$ErrorActionPreference = 'Stop'
$Install = (Resolve-Path $Install).Path.TrimEnd('\')
New-Item -ItemType Directory -Force $Work | Out-Null
$Work = (Resolve-Path $Work).Path.TrimEnd('\')
$pidFile = Join-Path $Work 'ide.pid'
$wvRoot = Join-Path $Work 'wv'

function Get-Ours {
  # Processes run from the install folder or the runtime copy, and the real browsers the shim started (which carry
  # its DevTools port on their command line). Their child processes end with them.
  $ports = @("--remote-debugging-port=$Port ", "--remote-debugging-port=$($Port + 1) ")
  Get-CimInstance Win32_Process | Where-Object {
    $path = $_.ExecutablePath
    $line = "$($_.CommandLine) "
    $path -and (
      $path.StartsWith("$Install\", [StringComparison]::OrdinalIgnoreCase) -or
      $path.StartsWith("$wvRoot\", [StringComparison]::OrdinalIgnoreCase) -or
      ($_.Name -ieq 'msedgewebview2.exe' -and ($line.Contains($ports[0]) -or $line.Contains($ports[1]))))
  }
}

if ($Action -eq 'stop') {
  if (Test-Path $pidFile) {
    $id = [int](Get-Content $pidFile)
    $p = Get-Process -Id $id -ErrorAction SilentlyContinue
    if ($p -and $p.Path -and $p.Path.StartsWith("$Install\", [StringComparison]::OrdinalIgnoreCase)) {
      $null = $p.CloseMainWindow()
      if (-not $p.WaitForExit(10000)) { Stop-Process -Id $id -Force }
    }
    Remove-Item $pidFile
  }
  Start-Sleep -Seconds 2
  foreach ($round in 1..3) {
    $left = @(Get-Ours)
    if (-not $left) { break }
    $left | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 2
  }
  "stopped; left running: $(@(Get-Ours).Count)"
  return
}

if (@(Get-Ours).Count) { throw "Something from $Install or this runtime copy is already running; stop it first." }
if (-not $Shim) { $Shim = Join-Path $PSScriptRoot 'shim\bin\publish\msedgewebview2.exe' }
if (-not (Test-Path $Shim)) { throw "No shim at $Shim. Publish it: dotnet publish shim/shim.csproj -c Release -o shim/bin/publish" }

# The newest installed Evergreen runtime is what the IDE would have used.
$apps = "${env:ProgramFiles(x86)}\Microsoft\EdgeWebView\Application"
$real = Get-ChildItem $apps -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'msedgewebview2.exe') } |
  Sort-Object { [version]$_.Name } | Select-Object -Last 1
if (-not $real) { throw "No WebView2 runtime under $apps" }
$copy = Join-Path $wvRoot $real.Name
if (-not (Test-Path (Join-Path $copy 'msedgewebview2.exe'))) {
  New-Item -ItemType Directory -Force $copy | Out-Null
  foreach ($e in Get-ChildItem $real.FullName -Force) {
    $to = Join-Path $copy $e.Name
    if ($e.Name -ieq 'msedgewebview2.exe') { continue }
    if ($e.PSIsContainer) { New-Item -ItemType Junction -Path $to -Target $e.FullName | Out-Null; continue }
    try { New-Item -ItemType HardLink -Path $to -Target $e.FullName -ErrorAction Stop | Out-Null }
    catch { Copy-Item $e.FullName $to }
  }
}
Copy-Item $Shim (Join-Path $copy 'msedgewebview2.exe') -Force

# The browser doesn't inherit this environment, so the shim reads its settings from a file beside it.
"real=$(Join-Path $real.FullName 'msedgewebview2.exe')", "port=$Port" | Set-Content (Join-Path $copy 'tboracle-shim.cfg')
$env:WEBVIEW2_BROWSER_EXECUTABLE_FOLDER = $copy
$argList = @()
if ($Project) { $argList += '"' + (Resolve-Path $Project).Path + '"' }
$start = @{ FilePath = "$Install\twinBASIC.exe"; WorkingDirectory = $Install; PassThru = $true }
if ($argList) { $start.ArgumentList = $argList }
$p = Start-Process @start
$p.Id | Set-Content $pidFile

# Ready when the IDE's page answers DevTools.
$deadline = (Get-Date).AddSeconds(90)
while ((Get-Date) -lt $deadline) {
  try {
    $targets = Invoke-RestMethod "http://127.0.0.1:$Port/json/list" -TimeoutSec 2
    if ($targets | Where-Object { $_.type -eq 'page' -and $_.url -like '*main.htm*' }) {
      "started IDE $($p.Id); DevTools on $Port; runtime $($real.Name)"
      return
    }
  } catch { }
  if ($p.HasExited) { throw "The IDE exited with code $($p.ExitCode) before its page answered DevTools" }
  Start-Sleep -Milliseconds 500
}
throw "The IDE's page did not answer DevTools on $Port within 90 s"
