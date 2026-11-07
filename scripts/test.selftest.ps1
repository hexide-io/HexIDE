# Exercise the local runner without building or starting a test host.
$ErrorActionPreference = 'Stop'
$fixture = Join-Path (Split-Path $PSScriptRoot -Parent) ('artifacts/test-runner-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
$child = Join-Path $fixture 'child.ps1'
@'
$ErrorActionPreference = 'Stop'
$config = Get-Content (Join-Path $PSScriptRoot 'invocation.json') -Raw | ConvertFrom-Json
function global:dotnet {
    ConvertTo-Json -InputObject @($args) | Set-Content (Join-Path $PSScriptRoot 'captured.json')
    'Passed! - fake test host summary'
    $global:LASTEXITCODE = $config.result
}
$invokeArgs = @($config.arguments)
& $config.runner @invokeArgs
exit $LASTEXITCODE
'@ | Set-Content $child
$powerShell = if ($PSVersionTable.PSEdition -eq 'Desktop') { 'powershell.exe' } else { 'pwsh' }
$fakeResult = 0
$runner = Join-Path $PSScriptRoot 'test.ps1'
$root = Split-Path $PSScriptRoot -Parent
$workflow = Get-Content (Join-Path $root '.github/workflows/build.yml') -Raw
$checks = 0
function Check($project, $arguments, $expected, $status = 0) {
    $capturePath = Join-Path $fixture 'captured.json'
    Remove-Item $capturePath -ErrorAction SilentlyContinue
    @{ runner = $runner; arguments = @($project) + @($arguments); result = $fakeResult } |
        ConvertTo-Json | Set-Content (Join-Path $fixture 'invocation.json')
    $output = & $powerShell -NoProfile -File $child 2>&1 | Out-String
    if ($LASTEXITCODE -ne $status) { throw "Wrong exit code for ${project}: $LASTEXITCODE; $output" }
    $captured = @()
    if (Test-Path $capturePath) { $captured = @(Get-Content $capturePath -Raw | ConvertFrom-Json) }
    if (($captured -join '|') -cne ($expected -join '|')) {
        throw "Wrong arguments: $($captured -join '|'); expected $($expected -join '|')"
    }
    if ($status -eq 0 -and $output -notmatch 'test.ps1: PASS .*floor') { throw "Missing PASS: $output" }
    if ($status -eq 1 -and $output -notmatch 'test.ps1: FAIL .*floor') { throw "Missing FAIL: $output" }
    $script:checks++
}
try {
    foreach ($pair in @(
        @('IDE/HexIDE.Runtime.Tests', 'RUNTIME'), @('IDE/HexIDE.Tests', 'IDE'),
        @('IDE/HexIDE.Integration.Tests', 'INTEGRATION'), @('LspServer/HexIDE.VbLspServer.Tests', 'LSP_SERVER'),
        @('IDE/HexIDE.TwinBasic.Tests', 'TWINBASIC'))) {
        $floor = [regex]::Match($workflow, "(?m)^  MIN_TESTS_$($pair[1]): *([0-9]+)").Groups[1].Value
        Check $pair[0] @() @('test', (Join-Path $root $pair[0]), '--', '--minimum-expected-tests', $floor)
    }
    foreach ($filter in @('--filter-class', '--filter-method', '--filter-query', '--filter-not-method')) {
        Check 'IDE/HexIDE.Tests/' @('--no-build', '--', $filter, '*Example*') @(
            'test', (Join-Path $root 'IDE/HexIDE.Tests'), '--no-build', '--', '--minimum-expected-tests', '1', $filter, '*Example*')
    }
    Check 'IDE/HexIDE.Tests/HexIDE.Tests.csproj' @('--', '--minimum-expected-tests', '7') @(
        'test', (Join-Path $root 'IDE/HexIDE.Tests/HexIDE.Tests.csproj'), '--', '--minimum-expected-tests', '7')
    Check 'IDE/HexIDE.Tests' @('--', '--minimum-expected-tests=7') @(
        'test', (Join-Path $root 'IDE/HexIDE.Tests'), '--', '--minimum-expected-tests=7')
    $fakeResult = 1
    Check 'IDE/HexIDE.Tests' @('--', '--filter-class', '*Example*') @(
        'test', (Join-Path $root 'IDE/HexIDE.Tests'), '--', '--minimum-expected-tests', '1', '--filter-class', '*Example*') 1
    Check 'IDE/Unknown.Tests' @() @() 2
    # A broken workflow must fail closed before invoking dotnet.
    $originalRunner = $runner
    $fakeRoot = Join-Path $fixture 'repo'
    New-Item -ItemType Directory -Path (Join-Path $fakeRoot 'scripts'),
        (Join-Path $fakeRoot 'IDE/HexIDE.Tests'), (Join-Path $fakeRoot '.github/workflows') -Force | Out-Null
    $runner = Join-Path $fakeRoot 'scripts/test.ps1'
    Copy-Item $originalRunner $runner
    foreach ($content in @('', '  MIN_TESTS_IDE: 0')) {
        Set-Content (Join-Path $fakeRoot '.github/workflows/build.yml') $content
        Check 'IDE/HexIDE.Tests' @() @() 2
    }
    Write-Host "$checks checks passed"
} finally {
    Remove-Item $fixture -Recurse -Force
}
