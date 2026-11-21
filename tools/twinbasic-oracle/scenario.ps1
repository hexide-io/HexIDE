<#
.SYNOPSIS
  One oracle run in the VM: open a copy of a baseline project, make known changes, keep every save's bytes (#5139).

.DESCRIPTION
  Runs in the signed-in desktop session (vm.ps1 gui), since it starts the IDE. Stops any IDE from -Install, copies
  -Baseline over the project path, starts the IDE on it, and has run.mjs make each of -Changes in turn, saving after
  each. Keeps before.twinproj, <n>-<change>.twinproj for every save, and run.json under -Out. Stops the IDE afterwards.

  With no -Baseline, the first change must be "new": the IDE starts on its front page with no project file, and
  dialog.ps1 runs alongside to answer the first save's file dialog, which saves to -Project's default place.
#>
param(
  [Parameter(Mandatory)] [string] $Install,
  [string] $Baseline,
  [Parameter(Mandatory)] [string[]] $Changes,
  [Parameter(Mandatory)] [string] $Out,
  [string] $Project = "$env:USERPROFILE\Documents\NewProject.twinproj",
  [string] $Work = 'C:\tbo\work',
  [string] $Node = 'C:\tbo\node\node.exe'
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
& "$here\tb.ps1" -Action stop -Install $Install -Work $Work | Out-Null
if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
New-Item -ItemType Directory -Force $Out | Out-Null
if ($Baseline) {
  Copy-Item $Baseline $Project -Force
  Copy-Item $Baseline (Join-Path $Out 'before.twinproj') -Force
  $started = & "$here\tb.ps1" -Action start -Install $Install -Work $Work -Project $Project
} else {
  if ($Changes[0] -ne 'new') { throw 'With no -Baseline the first change must be new' }
  if (Test-Path $Project) { Remove-Item $Project -Force }
  $started = & "$here\tb.ps1" -Action start -Install $Install -Work $Work
  $log = Join-Path $Out 'dialog.log'
  Start-Process powershell.exe -WindowStyle Hidden -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command',
    "& '$here\dialog.ps1' -Install '$Install' -TimeoutSeconds 180 *> '$log'"
}
$started
Push-Location $here
try {
  $steps = & $Node run.mjs $Project $Out @Changes
  if ($LASTEXITCODE) { throw "run.mjs exited with $LASTEXITCODE" }
} finally {
  Pop-Location
  & "$here\tb.ps1" -Action stop -Install $Install -Work $Work | Out-Null
}
$beta = if ($Install -match 'beta-(\d+)') { [int]$Matches[1] } else { $null }
[ordered]@{
  beta = $beta
  baseline = if ($Baseline) { Split-Path $Baseline -Leaf } else { $null }
  before = if ($Baseline) { (Get-FileHash (Join-Path $Out 'before.twinproj') -Algorithm SHA256).Hash.ToLower() } else { $null }
  webView2 = ($started -replace '^.*runtime ', '')
  windows = (Get-CimInstance Win32_OperatingSystem).BuildNumber
  steps = @($steps | ForEach-Object {
    $s = $_ | ConvertFrom-Json
    [ordered]@{
      step = $s.step; change = $s.change; savedBy = $s.savedBy; file = (Split-Path $s.kept -Leaf); bytes = $s.bytes
      sha256 = (Get-FileHash $s.kept -Algorithm SHA256).Hash.ToLower()
      keptAtUtc = (Get-Item $s.kept).LastWriteTimeUtc.ToString('o'); note = $s.note
    }
  })
} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $Out 'run.json')
Get-Content (Join-Path $Out 'run.json')
