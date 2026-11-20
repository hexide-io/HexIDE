<#
.SYNOPSIS
  Runs the twinBASIC oracle harness in a Hyper-V VM over PowerShell Direct (#5139).

.DESCRIPTION
  push   (-HarnessOnly: just the scripts) copies the harness, node.exe and a pinned twinBASIC zip into C:\tbo in the VM, and unpacks the zip into
         C:\tbo\tb\beta-<n>. -Payload is a folder holding node.exe and twinBASIC_IDE_BETA_<n>.zip.
  gui    runs -Command (PowerShell) in the signed-in user's desktop session through a one-off scheduled task, since
         the IDE is a GUI and PowerShell Direct itself runs in session 0. Waits for it and prints its output, or
         with -NoWait returns at once and leaves the output in C:\tbo\gui.log (to answer a dialog a later step opens).
  run    runs -Command (PowerShell) in session 0. DevTools answers on the VM's loopback, so the Node drivers run here.
  fetch  copies -From (a path in the VM) to -To (a folder on the host).

  Needs an elevated shell on the Hyper-V host and the guest's credential as an Export-Clixml'd PSCredential
  (-CredentialFile). The compiler is launch-and-watch only: nothing here reads, hooks or instruments it.
#>
param(
  [Parameter(Mandatory)] [ValidateSet('push', 'gui', 'run', 'fetch')] [string] $Action,
  [Parameter(Mandatory)] [string] $CredentialFile,
  [string] $VMName = 'twinBASIC-Win11',
  [string] $Payload,
  [int] $Beta,
  [string] $Command,
  [string] $From,
  [string] $To,
  [int] $TimeoutSeconds = 600,
  [switch] $NoWait,
  [switch] $HarnessOnly
)
$ErrorActionPreference = 'Stop'
$cred = Import-Clixml $CredentialFile
$s = New-PSSession -VMName $VMName -Credential $cred
try {
  switch ($Action) {
    'push' {
      Invoke-Command -Session $s { New-Item -ItemType Directory -Force C:\tbo\harness\shim\bin\publish, C:\tbo\node, C:\tbo\tb | Out-Null }
      $h = $PSScriptRoot
      foreach ($f in 'tb.ps1', 'act.mjs', 'run.mjs', 'scenario.ps1', 'dialog.ps1', 'tbdump.py', 'package.json', 'package-lock.json') {
        if (Test-Path (Join-Path $h $f)) { Copy-Item (Join-Path $h $f) C:\tbo\harness\ -ToSession $s -Force }
      }
      if ($HarnessOnly) { 'harness scripts copied'; break }
      if (-not $Payload -or -not $Beta) { throw 'push needs -Payload and -Beta, or -HarnessOnly' }
      $zip = Join-Path $Payload "twinBASIC_IDE_BETA_$Beta.zip"
      foreach ($f in $zip, (Join-Path $Payload 'node.exe')) { if (-not (Test-Path $f)) { throw "missing $f" } }
      Copy-Item (Join-Path $h 'node_modules') C:\tbo\harness\ -ToSession $s -Recurse -Force
      Copy-Item (Join-Path $h 'shim\bin\publish\msedgewebview2.exe') C:\tbo\harness\shim\bin\publish\ -ToSession $s -Force
      Copy-Item (Join-Path $Payload 'node.exe') C:\tbo\node\ -ToSession $s -Force
      Copy-Item $zip C:\tbo\ -ToSession $s -Force
      Invoke-Command -Session $s -ArgumentList $Beta {
        param($n)
        $dir = "C:\tbo\tb\beta-$n"
        if (-not (Test-Path "$dir\twinBASIC.exe")) { Expand-Archive "C:\tbo\twinBASIC_IDE_BETA_$n.zip" $dir -Force }
        "beta $n at $dir; sha256 of zip $((Get-FileHash "C:\tbo\twinBASIC_IDE_BETA_$n.zip" -Algorithm SHA256).Hash.ToLower())"
        "node $(& C:\tbo\node\node.exe --version)"
      }
    }
    'gui' {
      if (-not $Command) { throw 'gui needs -Command' }
      Invoke-Command -Session $s -ArgumentList $Command, $TimeoutSeconds, $cred.UserName, $NoWait.IsPresent {
        param($cmd, $timeout, $user, $noWait)
        $name = 'tbo-gui'
        $log = 'C:\tbo\gui.log'
        $done = 'C:\tbo\gui.done'
        $script = 'C:\tbo\gui-job.ps1'
        # The job writes $done last, with its outcome. Waiting on the task's state instead raced: a task not yet
        # started reads as finished, and LastTaskResult is the previous run's until this one ends.
        Set-Content $script ("`$ErrorActionPreference = 'Continue'`r`n`$ok = `$true`r`n" +
          "try { & { $cmd } *> '$log' } catch { `$ok = `$false; `$_ | Out-String | Add-Content '$log' }`r`n" +
          "Set-Content '$done' `"ok=`$ok`"")
        Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue
        $act = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File $script"
        $pr = New-ScheduledTaskPrincipal -UserId ($user -replace '^.*\\', '') -LogonType Interactive -RunLevel Highest
        Register-ScheduledTask -TaskName $name -Action $act -Principal $pr | Out-Null
        Remove-Item $log, $done -ErrorAction SilentlyContinue
        Start-ScheduledTask -TaskName $name
        if ($noWait) { "gui task started; its output goes to $log"; return }
        $deadline = (Get-Date).AddSeconds($timeout)
        while (-not (Test-Path $done) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
        if (Test-Path $log) { Get-Content $log }
        if (Test-Path $done) { "gui task finished: $(Get-Content $done)" } else { "gui task still running after $timeout s" }
      }
    }
    'run' {
      if (-not $Command) { throw 'run needs -Command' }
      Invoke-Command -Session $s -ScriptBlock ([scriptblock]::Create($Command))
    }
    'fetch' {
      if (-not $From -or -not $To) { throw 'fetch needs -From and -To' }
      New-Item -ItemType Directory -Force $To | Out-Null
      Copy-Item $From $To -FromSession $s -Recurse -Force
      "fetched $From to $To"
    }
  }
} finally {
  Remove-PSSession $s
}
