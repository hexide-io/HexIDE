using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Regression for the interpreter doc-debt sweep: three declaration-section directives that real VB6 modules
/// routinely carry (`Option Compare`, `Option Private Module`, `Declare … Lib`) used to throw in <c>PrePass</c> and
/// fail the WHOLE module load. They are now tolerated (accepted no-ops) so the module loads and its real code runs.
/// Their unsupported semantics are documented divergences (docs/interpreter-gaps.md): `Option Compare Text` is
/// always treated as Binary, and a `Declare`'d API is not registered (a call to it raises the clean "Sub or
/// Function not defined").
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
