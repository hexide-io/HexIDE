# Makes C:\corpus\locked.xlsm: a project locked for viewing, password "corpus". Runs at the interactive console,
# since VBA protection has no COM call: it drives the editor's Project Properties dialog with keys.
$ErrorActionPreference = 'Stop'
$log = 'C:\corpus-state\build-lock.log'
"started $(Get-Date -Format o) session=$([Diagnostics.Process]::GetCurrentProcess().SessionId)" | Set-Content $log
function L($s) { "$(Get-Date -Format HH:mm:ss) $s" | Add-Content $log }
$crlf = "`r`n"
$std = @('Option Explicit', '', 'Public Const Secret As String = "visible only with the password"', '',
  'Public Sub SayHello()', '    Debug.Print Secret', 'End Sub') -join $crlf
$before = @(Get-Process EXCEL -ErrorAction SilentlyContinue | ForEach-Object Id)
$xl = New-Object -ComObject Excel.Application
$wsh = New-Object -ComObject WScript.Shell
try {
  $xl.Visible = $true; $xl.DisplayAlerts = $false; $xl.EnableEvents = $false
  $wb = $xl.Workbooks.Add()
  $wb.VBProject.Name = 'LockedProject'
  $c = $wb.VBProject.VBComponents.Add(1); $c.Name = 'Module1'; $c.CodeModule.AddFromString($std)
  $wb.RemovePersonalInformation = $true
  $wb.SaveAs('C:\corpus\locked.xlsm', 52)
  L 'saved unlocked'
  $xl.VBE.MainWindow.Visible = $true
  Start-Sleep 3
  $title = $xl.VBE.MainWindow.Caption
  L "VBE caption length $($title.Length); activate=$($wsh.AppActivate($title))"
  Start-Sleep 2
  $wsh.SendKeys('%t'); Start-Sleep 1; $wsh.SendKeys('e'); Start-Sleep 3
  L 'properties dialog requested'
  $wsh.SendKeys('^{TAB}'); Start-Sleep 2
  L 'protection tab'
  Start-Sleep 8
  $wsh.SendKeys('%v'); Start-Sleep 1
  $wsh.SendKeys('%p'); Start-Sleep 1; $wsh.SendKeys('corpus'); Start-Sleep 1
  $wsh.SendKeys('%c'); Start-Sleep 1; $wsh.SendKeys('corpus'); Start-Sleep 1
  L 'filled; waiting before OK'
  Start-Sleep 8
  $wsh.SendKeys('{ENTER}'); Start-Sleep 3
  L 'OK sent'
  $xl.VBE.MainWindow.Visible = $false
  $wb.Save()
  L 'saved'
  $wb.Close($false)
  $wb2 = $xl.Workbooks.Open('C:\corpus\locked.xlsm')
  L "reopened: protection=$($wb2.VBProject.Protection) (1 = locked)"
  $wb2.Close($false)
} catch { L "ERROR: $($_.Exception.Message)" } finally {
  try { $xl.Quit() } catch {}
  [void][Runtime.InteropServices.Marshal]::ReleaseComObject($xl)
  Start-Sleep 2
  Get-Process EXCEL -ErrorAction SilentlyContinue | Where-Object { $before -notcontains $_.Id } | ForEach-Object { Stop-Process -Id $_.Id -Force }
  L 'finished'
}
