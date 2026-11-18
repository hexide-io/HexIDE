# Reproduce #4688's teeth with the supervisor's mutation-check.ps1 tool.
param([Parameter(Mandatory)] [string] $MutationTool)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$file = 'IDE/HexIDE.Runtime/Interpreter/BasicInterpreter.CallByName.cs'
$source = [IO.File]::ReadAllText((Join-Path $repo $file))
$arity = [regex]::Match($source, '(?m)^                if \(\(count && values.Count != 0\)[\s\S]*?VBStandardError.WrongNumberOfArgumentsOrInvalidPropertyAssignment\);\r?\n').Value
$return = [regex]::Match($source, '(?m)^                    return await collection.CallAsync\(name, values\)[^\r\n]*;').Value
$objectRefusal = @'
                {
                    var result = await collection.CallAsync(name, values) ?? new Vb6Value(Vb6Value.ValueType.EmptyVariant);
                    if (result.Type == Vb6Value.ValueType.Object || result.Type == Vb6Value.ValueType.CSharpProxyObject
                        || result.Type == Vb6Value.ValueType.Control)
                        throw new NotImplementedException("CallByName object results are not implemented (#2519)");
                    return result;
                }
'@
# Use the file's own newline convention for the replacement as well as the matched text.
$newline = if ($source.Contains("`r`n")) { "`r`n" } else { "`n" }
$mutations = @(
    @{ Name = 'arity'; Find = $arity; Replace = '' },
    @{ Name = 'object-results'; Find = $return; Replace = $objectRefusal.ReplaceLineEndings($newline) },
    @{ Name = 'members'; Find = '            if (kind == 1)'; Replace = '            if (kind == 1 && (name.Equals("Add", StringComparison.OrdinalIgnoreCase) || name.Equals("Item", StringComparison.OrdinalIgnoreCase)))' }
)
foreach ($mutation in $mutations) {
    if (-not $mutation.Find) { throw "No source matched $($mutation.Name)" }
    Write-Host "#4688 mutation: $($mutation.Name)"
    & pwsh -NoProfile -File $MutationTool -Repo $repo -File $file -Find $mutation.Find -Replace $mutation.Replace `
        -Project IDE/HexIDE.Runtime.Tests -TestArgs '--filter-method,*Collection_MeasuredMembers'
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
