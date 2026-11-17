# TestScript adapter for mutation-check.ps1 -Project IDE/HexIDE.Runtime.
# Build HexIDE.Integration.Tests first. These mutations change method bodies only, so its
# prebuilt test host can load each rebuilt runtime without recompiling the IDE's DI graph.
# mutation-check still builds baseline, mutant and restored runtime assemblies itself.
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$runtime = Join-Path $root 'IDE/HexIDE.Runtime/bin/Debug/net10.0/HexIDE.Runtime.dll'
$hostRuntime = Join-Path $root 'IDE/HexIDE.Integration.Tests/bin/Debug/net10.0/HexIDE.Runtime.dll'
Copy-Item -LiteralPath $runtime -Destination $hostRuntime -Force
if ((Get-FileHash $runtime).Hash -ne (Get-FileHash $hostRuntime).Hash) {
    throw 'The test host did not receive the rebuilt runtime.'
}
& pwsh -NoProfile -File (Join-Path $root 'scripts/test.ps1') IDE/HexIDE.Integration.Tests --no-build '--' --filter-class '*ProgramBoxCaptionTests'
exit $LASTEXITCODE
