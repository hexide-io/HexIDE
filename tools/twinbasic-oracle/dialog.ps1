<#
.SYNOPSIS
  Answers the next native file dialog the twinBASIC IDE opens by pressing its default button (#5139).

.DESCRIPTION
  Waits up to -TimeoutSeconds for a common file dialog (window class #32770) belonging to a process run from
  -Install, reports its title and the file name it offers, and presses its default button (Save or Open). Runs in
  the desktop session that shows the dialog (vm.ps1 gui), since UI Automation can't see across sessions.

  It keeps the name the dialog offers. For a new project's Save that is <project name>.twinproj in the user's
  Documents folder, so the harness picks the path through the project's name. Text put into the file name box with
  WM_SETTEXT was ignored there (measured 2026-11-20, Windows 11 26300): the dialog saved under its own default.

  The dialog belongs to the IDE's host window; the compiler is not involved.
#>
param(
  [Parameter(Mandatory)] [string] $Install,
  [int] $TimeoutSeconds = 60
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$T = [System.Windows.Automation.TreeScope]
$P = [System.Windows.Automation.PropertyCondition]

# The dialog is found by window handle. UI Automation's list of top-level windows sometimes leaves it out, since it
# is owned by the IDE's window (seen 2026-11-20), and its controls expose no UI Automation patterns here (Windows 11
# 26300 with twinBASIC's host window), so the default button is pressed with BM_CLICK by window handle too.
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Text; using System.Collections.Generic;
public static class DialogWin32 {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  public static List<IntPtr> Dialogs(uint[] pids) {
    var r = new List<IntPtr>();
    EnumWindows((h, l) => {
      uint p; GetWindowThreadProcessId(h, out p);
      var c = new StringBuilder(64); GetClassName(h, c, 64);
      if (Array.IndexOf(pids, p) >= 0 && IsWindowVisible(h) && c.ToString() == "#32770") r.Add(h);
      return true; }, IntPtr.Zero);
    return r;
  }
}
"@

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$dialog = $null
while (-not $dialog -and (Get-Date) -lt $deadline) {
  $ids = [uint32[]]@(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith("$Install\", [StringComparison]::OrdinalIgnoreCase) } | ForEach-Object { $_.ProcessId })
  $handle = [DialogWin32]::Dialogs($ids) | Select-Object -First 1
  if ($handle) { $dialog = $A::FromHandle($handle) } else { Start-Sleep -Milliseconds 300 }
}
if (-not $dialog) { throw "No file dialog from $Install within $TimeoutSeconds s" }
$byId = { param($id, $class) $dialog.FindAll($T::Descendants, (New-Object $P ($A::AutomationIdProperty, $id))) |
  Where-Object { $_.Current.ClassName -eq $class -and $_.Current.NativeWindowHandle } | Select-Object -First 1 }
$name = & $byId '1001' 'Edit'
"dialog: '$($dialog.Current.Name)', offering '$(if ($name) { $name.Current.Name })'"
$ok = & $byId '1' 'Button'
if (-not $ok) { throw 'The dialog has no default button (a Button with AutomationId 1)' }
"button: '$($ok.Current.Name)'"
$null = [DialogWin32]::PostMessage([IntPtr]$ok.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)  # BM_CLICK
Start-Sleep -Seconds 1
$still = [DialogWin32]::Dialogs([uint32[]]@($dialog.Current.ProcessId))
if ($still) { "still open: $(@($still).Count) dialog(s)" } else { 'answered' }
