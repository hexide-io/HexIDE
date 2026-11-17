# Makes C:\corpus\cp932.xlsm and cp932.xls: a module with a Japanese name, and Japanese in its source, saved while
# the system's ANSI code page is 932. Japanese text is built from code points, so this file's own encoding can't
# change it. Runs at the interactive console, as build.ps1 does.
$ErrorActionPreference = 'Stop'
$log = 'C:\corpus-state\build-cp932.log'
"started $(Get-Date -Format o) session=$([Diagnostics.Process]::GetCurrentProcess().SessionId)" | Set-Content $log
function L($s) { "$(Get-Date -Format HH:mm:ss) $s" | Add-Content $log }
function J([int[]] $codes) { -join ($codes | ForEach-Object { [char]$_ }) }
$acp = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Nls\CodePage').ACP
L "ACP $acp"
if ($acp -ne '932') { L 'ERROR: the ANSI code page is not 932'; return }
$moduleName = J 0x65E5, 0x672C, 0x8A9E, 0x30E2, 0x30B8, 0x30E5, 0x30FC, 0x30EB      # a Japanese word for "Japanese module"
$hello = J 0x3053, 0x3093, 0x306B, 0x3061, 0x306F                                  # "hello"
$comment = J 0x65E5, 0x672C, 0x8A9E, 0x306E, 0x30B3, 0x30E1, 0x30F3, 0x30C8        # "a Japanese comment"
$crlf = "`r`n"
$code = @('Option Explicit', '', "' $comment", '',
  "Public Const Greeting As String = ""$hello""", '',
  'Public Sub SayHello()', '    Debug.Print Greeting', 'End Sub') -join $crlf
$before = @(Get-Process EXCEL -ErrorAction SilentlyContinue | ForEach-Object Id)
$xl = New-Object -ComObject Excel.Application
try {
  $xl.DisplayAlerts = $false; $xl.EnableEvents = $false
  $wb = $xl.Workbooks.Add()
  $wb.VBProject.Name = 'CodePageProject'
  $c = $wb.VBProject.VBComponents.Add(1); $c.Name = $moduleName; $c.CodeModule.AddFromString($code)
  L "module named as asked: $($c.Name -eq $moduleName); source lines $($c.CodeModule.CountOfLines)"
  $wb.RemovePersonalInformation = $true
  $wb.SaveAs('C:\corpus\cp932.xlsm', 52)
  $wb.SaveAs('C:\corpus\cp932.xls', 56)
  $wb.Close($false)
  $wb2 = $xl.Workbooks.Open('C:\corpus\cp932.xlsm')
  $names = @($wb2.VBProject.VBComponents | ForEach-Object Name)
  L "reopened: the Japanese module is there: $($names -contains $moduleName); its source round-trips: $(($wb2.VBProject.VBComponents.Item($moduleName).CodeModule.Lines(1, 9)) -eq $code)"
  $wb2.Close($false)
} catch { L "ERROR: $($_.Exception.Message)" } finally {
  try { $xl.Quit() } catch {}
  [void][Runtime.InteropServices.Marshal]::ReleaseComObject($xl)
  Start-Sleep 2
  Get-Process EXCEL -ErrorAction SilentlyContinue | Where-Object { $before -notcontains $_.Id } | ForEach-Object { Stop-Process -Id $_.Id -Force }
  L 'finished'
}
