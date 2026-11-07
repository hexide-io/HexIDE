# Run one test project with CI's floor. Arguments after the project go to dotnet test.
#   pwsh scripts/test.ps1 IDE/HexIDE.Tests -- --filter-class '*ExampleTests'
# Supports PowerShell 7 and Windows PowerShell 5.1; paths are relative to the repository root.
$ErrorActionPreference = 'Stop'
try {
    if ($args.Count -lt 1) { throw 'usage: scripts/test.ps1 <project> [dotnet test arguments...]' }
    $root = Split-Path $PSScriptRoot -Parent
    $project = ([string]$args[0]).Replace('\', '/').TrimEnd('/')
    $suite = $project -replace '/[^/]+\.csproj$', ''
    $keys = @{
        'IDE/HexIDE.Runtime.Tests' = 'RUNTIME'
        'IDE/HexIDE.Tests' = 'IDE'
        'IDE/HexIDE.Integration.Tests' = 'INTEGRATION'
        'IDE/HexIDE.TwinBasic.Tests' = 'TWINBASIC'
        'LspServer/HexIDE.VbLspServer.Tests' = 'LSP_SERVER'
    }
    if (-not $keys.ContainsKey($suite)) { throw "no CI test floor for $project" }
    $projectPath = Join-Path $root $project
    if (-not (Test-Path $projectPath)) { throw "no project at $project" }
    $testArgs = @($args | Select-Object -Skip 1)
    $separator = [Array]::IndexOf($testArgs, '--')
    $runnerArgs = @()
    if ($separator -ge 0) { $runnerArgs = @($testArgs | Select-Object -Skip ($separator + 1)) }
    $explicitFloor = @($runnerArgs | Where-Object { $_ -eq '--minimum-expected-tests' -or $_ -like '--minimum-expected-tests=*' })
    if ($explicitFloor.Count -gt 0) {
        $option = [string]$explicitFloor[0]
        if ($option.Contains('=')) {
            $floor = $option.Substring($option.IndexOf('=') + 1)
        } else {
            $index = [Array]::IndexOf($runnerArgs, $option)
            $floor = '<missing>'
            if ($index + 1 -lt $runnerArgs.Count) { $floor = $runnerArgs[$index + 1] }
        }
    } else {
        $workflow = Get-Content (Join-Path $root '.github/workflows/build.yml') -Raw
        $row = [regex]::Match($workflow, "(?m)^  MIN_TESTS_$($keys[$suite]): *([1-9][0-9]*)\b")
        if (-not $row.Success) { throw "missing or invalid MIN_TESTS_$($keys[$suite]) in build.yml" }
        $floor = $row.Groups[1].Value
        if (@($runnerArgs | Where-Object { $_ -like '--filter-*' }).Count -gt 0) { $floor = '1' }
        if ($separator -lt 0) {
            $testArgs += '--'
            $separator = $testArgs.Count - 1
        }
        $testArgs = @($testArgs | Select-Object -First ($separator + 1)) +
            @('--minimum-expected-tests', $floor) + $runnerArgs
    }
    Write-Host "test.ps1: $project; minimum expected tests: $floor"
    & dotnet test $projectPath @testArgs
    $result = $LASTEXITCODE
    # MTP can print Passed! even when its minimum-test policy fails. The exit code is authoritative.
    if ($result -eq 0) {
        Write-Host "test.ps1: PASS (tests and floor policy; minimum expected tests: $floor)"
    } else {
        Write-Host "test.ps1: FAIL (tests or floor policy; minimum expected tests: $floor; exit code: $result)"
    }
    exit $result
} catch {
    Write-Host "test.ps1: FAIL ($($_.Exception.Message))"
    exit 2
}
