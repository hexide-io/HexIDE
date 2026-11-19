using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Regression for the interpreter doc-debt sweep: three declaration-section directives that real VB6 modules
/// routinely carry (`Option Compare`, `Option Private Module`, `Declare … Lib`) used to throw in <c>PrePass</c> and
/// fail the WHOLE module load. They now load so the module can run its non-API code.
/// Compare behavior is tested separately; Declare calls report missing Windows libraries off Windows (#4820),
/// while native invocation remains deferred (docs/interpreter-gaps.md).
/// </summary>
public class OptionDeclareToleranceTests : BaseVBTestFixture
{
    [Fact]
    public async Task OptionCompareText_ModuleLoadsAndRuns()
    {
        await Run("Option Compare Text\nDim s As String\ns = \"hi\"\nDebug.Print s\n");
        AssertDebugLog([new Vb6Value("hi")]);
    }

    [Fact]
    public async Task OptionPrivateModule_ModuleLoadsAndRuns()
    {
        await Run("Option Private Module\nDebug.Print 42\n");
        AssertDebugLog([new Vb6Value(42)]);
    }

    [Fact]
    public async Task DeclareLib_Uncalled_ModuleLoadsAndRuns()
    {
        // A module that DECLARES a Win32 API but doesn't call it must load — the near-universal VB6 pattern.
        await Run(
            "Declare Function GetTickCount Lib \"kernel32\" () As Long\n" +
            "Debug.Print 7\n");
        AssertDebugLog([new Vb6Value(7)]);
    }
}
