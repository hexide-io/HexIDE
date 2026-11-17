# Office corpus for #4330, built in Microsoft 365 (x64) through Office's COM object model.
# Runs inside the guest. Every file goes to C:\corpus. Office processes this starts are stopped by PID.
param([string[]] $Only)
$ErrorActionPreference = 'Stop'
$dir = 'C:\corpus'
$crlf = "`r`n"

$std = @(
  'Option Explicit', '',
  'Public Const Greeting As String = "Hello from the corpus"', '',
  '#If VBA7 Then',
  'Private Const Target As String = "VBA7"',
  '#Else',
  'Private Const Target As String = "VBA6"',
  '#End If', '',
  'Public Sub SayHello()',
  '    Debug.Print Greeting & " (" & Target & ")"',
  'End Sub') -join $crlf
$cls = @(
  'Option Explicit', '',
  'Private mValue As Long', '',
  'Public Property Get Value() As Long',
  '    Value = mValue',
  'End Property', '',
  'Public Property Let Value(ByVal NewValue As Long)',
  '    mValue = NewValue',
  'End Property') -join $crlf
# Over 4 KB of source, so its compressed form spans several 4096-byte chunks.
$bigLines = @('Option Explicit', '', "' A module whose source is over 4 KB, for #4330's decompressor.")
for ($i = 1; $i -le 120; $i++) { $bigLines += ("Public Const Line{0:000} As String = ""The quick brown fox jumps over the lazy dog, line {0:000}""" -f $i) }
$bigLines += @('', 'Public Function CountLines() As Long', '    CountLines = 120', 'End Function')
$big = $bigLines -join $crlf

function NewProcesses([string] $name, [int[]] $before) {
  @(Get-Process $name -ErrorAction SilentlyContinue | Where-Object { $before -notcontains $_.Id })
}
function Code($component, [string] $text) {
  $cm = $component.CodeModule
  if ($cm.CountOfLines -gt 0) { $cm.DeleteLines(1, $cm.CountOfLines) }
  $cm.AddFromString($text)
}
function AddModule($project, [int] $kind, [string] $name, [string] $text) {
  $c = $project.VBComponents.Add($kind); $c.Name = $name; Code $c $text; $c
}

function Excel([scriptblock] $body) {
  $before = @(Get-Process EXCEL -ErrorAction SilentlyContinue | ForEach-Object Id)
  $xl = New-Object -ComObject Excel.Application
  $xl.DisplayAlerts = $false; $xl.EnableEvents = $false
  try { & $body $xl } finally {
    try { $xl.Quit() } catch {}
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($xl)
    Start-Sleep 2
    NewProcesses 'EXCEL' $before | ForEach-Object { Stop-Process -Id $_.Id -Force }
  }
}

$wbHandler = @('Option Explicit', '', 'Private Sub Workbook_Open()', '    Debug.Print "opened"', 'End Sub') -join $crlf

if (-not $Only -or $Only -contains 'excel') {
  Excel {
    param($xl)
    # The other project, which basic.xlsm references.
    $other = $xl.Workbooks.Add()
    $other.VBProject.Name = 'OtherProject'
    AddModule $other.VBProject 1 'OtherModule' (@('Option Explicit', '', 'Public Function Twice(ByVal n As Long) As Long', '    Twice = n * 2', 'End Function') -join $crlf) | Out-Null
    $other.RemovePersonalInformation = $true
    $other.SaveAs("$dir\other.xlsm", 52)

    $wb = $xl.Workbooks.Add()
    $wb.VBProject.Name = 'CorpusProject'
    AddModule $wb.VBProject 1 'Module1' $std | Out-Null
    AddModule $wb.VBProject 2 'Class1' $cls | Out-Null
    AddModule $wb.VBProject 1 'BigModule' $big | Out-Null
    Code $wb.VBProject.VBComponents.Item($wb.CodeName) $wbHandler
    [void]$wb.VBProject.References.AddFromFile("$env:windir\System32\scrrun.dll")   # Microsoft Scripting Runtime, a type library
    [void]$wb.VBProject.References.AddFromFile("$dir\other.xlsm")
    $wb.RemovePersonalInformation = $true
    $wb.SaveAs("$dir\basic.xlsm", 52)
    $wb.SaveAs("$dir\basic.xlsb", 50)
    $wb.SaveAs("$dir\basic.xls", 56)
    $wb.Close($false); $other.Close($false)

    # A UserForm, with a label and a button.
    $uf = $xl.Workbooks.Add()
    $uf.VBProject.Name = 'FormProject'
    AddModule $uf.VBProject 1 'Module1' $std | Out-Null
    AddModule $uf.VBProject 2 'Class1' $cls | Out-Null
    Code $uf.VBProject.VBComponents.Item($uf.CodeName) $wbHandler
    $form = $uf.VBProject.VBComponents.Add(3)
    $form.Name = 'UserForm1'
    $form.Properties.Item('Caption').Value = 'Corpus form'
    $label = $form.Designer.Controls.Add('Forms.Label.1'); $label.Name = 'Label1'; $label.Caption = 'A label'; $label.Left = 12; $label.Top = 12
    $button = $form.Designer.Controls.Add('Forms.CommandButton.1'); $button.Name = 'CommandButton1'; $button.Caption = 'OK'; $button.Left = 12; $button.Top = 40
    Code $form (@('Option Explicit', '', 'Private Sub CommandButton1_Click()', '    Unload Me', 'End Sub') -join $crlf)
    $uf.RemovePersonalInformation = $true
    $uf.SaveAs("$dir\userform.xlsm", 52)
    $uf.Close($false)
  }
  "excel done"
}

if (-not $Only -or $Only -contains 'word') {
  $before = @(Get-Process WINWORD -ErrorAction SilentlyContinue | ForEach-Object Id)
  $wd = New-Object -ComObject Word.Application
  $wd.Visible = $true
  $wd.DisplayAlerts = 0
  try {
    'w: adding document'
    $doc = $wd.Documents.Add()
    "w: added [$($doc.Name)], VBProject null=$($null -eq $doc.VBProject)"
    $doc.VBProject.Name = 'CorpusProject'
    'w: named'
    AddModule $doc.VBProject 1 'Module1' $std | Out-Null
    AddModule $doc.VBProject 2 'Class1' $cls | Out-Null
    Code $doc.VBProject.VBComponents.Item('ThisDocument') (@('Option Explicit', '', 'Private Sub Document_Open()', '    Debug.Print "opened"', 'End Sub') -join $crlf)
    'w: code in'
    [void]$doc.Content.InsertAfter('A document for the corpus.')
    $doc.RemovePersonalInformation = $true
    $doc.SaveAs2("$dir\basic.docm", 13)
    'w: saved docm'
    $doc.SaveAs2("$dir\basic.doc", 0)
    'w: saved doc'
    $doc.Close(0)
  } finally {
    try { $wd.Quit(0) } catch {}
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($wd)
    Start-Sleep 2
    NewProcesses 'WINWORD' $before | ForEach-Object { Stop-Process -Id $_.Id -Force }
  }
  "word done"
}

if (-not $Only -or $Only -contains 'powerpoint') {
  $before = @(Get-Process POWERPNT -ErrorAction SilentlyContinue | ForEach-Object Id)
  $pp = New-Object -ComObject PowerPoint.Application
  $pp.Visible = -1
  try {
    $pres = $pp.Presentations.Add(0)
    $pres.VBProject.Name = 'CorpusProject'
    AddModule $pres.VBProject 1 'Module1' $std | Out-Null
    AddModule $pres.VBProject 2 'Class1' $cls | Out-Null
    [void]$pres.Slides.Add(1, 12)
    $pres.RemovePersonalInformation = -1
    $pres.SaveAs("$dir\basic.pptm", 25)
    $pres.Close()
  } finally {
    try { $pp.Quit() } catch {}
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($pp)
    Start-Sleep 2
    NewProcesses 'POWERPNT' $before | ForEach-Object { Stop-Process -Id $_.Id -Force }
  }
  "powerpoint done"
}

Get-ChildItem $dir | ForEach-Object { "{0}`t{1}" -f $_.Name, $_.Length }
