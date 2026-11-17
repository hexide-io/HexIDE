<#
.SYNOPSIS
Prints the test classes and tests that took longest, from an xunit v3 XML report (#2128).

.DESCRIPTION
A test step that runs past its limit keeps none of the test host's output: dotnet test holds it until the run ends. So
the per-class times of runs that do finish are what the next timeout is compared against. The tables go to standard
output and, when GITHUB_STEP_SUMMARY is set, to the job summary.

A missing report is not an error. The run may have ended before xunit wrote it.

.EXAMPLE
pwsh scripts/slowest-tests.ps1 IDE/HexIDE.Tests/TestResults/viewmodel-results.xml -Top 15
#>
param(
    [Parameter(Mandatory = $true)][string]$Report,
    [int]$Top = 15
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Report)) {
    Write-Output "No xunit report at $Report, so there are no timings: the run ended before xunit wrote it."
    exit 0
}

$xml = [xml](Get-Content -LiteralPath $Report -Raw)
$tests = @($xml.SelectNodes('//test') | ForEach-Object {
    [pscustomobject]@{
        Class   = ($_.type -split '\.')[-1]
        Name    = $_.name
        # xunit writes seconds with an invariant decimal point.
        Seconds = [double]::Parse($_.time, [System.Globalization.CultureInfo]::InvariantCulture)
    }
})
if ($tests.Count -eq 0) {
    Write-Output "The xunit report at $Report lists no tests."
    exit 0
}

$total = ($tests | Measure-Object Seconds -Sum).Sum
$classes = $tests | Group-Object Class | ForEach-Object {
    [pscustomobject]@{
        Class   = $_.Name
        Tests   = $_.Count
        Seconds = ($_.Group | Measure-Object Seconds -Sum).Sum
    }
} | Sort-Object Seconds -Descending | Select-Object -First $Top
$slowest = $tests | Sort-Object Seconds -Descending | Select-Object -First $Top

function Format-Seconds([double]$seconds) { $seconds.ToString('0.0', [System.Globalization.CultureInfo]::InvariantCulture) }

$minutes = ($total / 60).ToString('0.0', [System.Globalization.CultureInfo]::InvariantCulture)
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("### Slowest test classes")
$lines.Add("")
$lines.Add("$($tests.Count) tests took $minutes minutes in all.")
$lines.Add("")
$lines.Add("| Seconds | Tests | Class |")
$lines.Add("|---:|---:|---|")
foreach ($c in $classes) { $lines.Add("| $(Format-Seconds $c.Seconds) | $($c.Tests) | $($c.Class) |") }
$lines.Add("")
$lines.Add("### Slowest tests")
$lines.Add("")
$lines.Add("| Seconds | Test |")
$lines.Add("|---:|---|")
# A theory row's name can hold a pipe; escape it so the table keeps its columns.
foreach ($t in $slowest) { $lines.Add("| $(Format-Seconds $t.Seconds) | $($t.Name -replace '\|', '\|') |") }

$lines | Write-Output
if ($env:GITHUB_STEP_SUMMARY) {
    $lines | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8
}
