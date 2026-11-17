using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Interpreter.ConditionalCompilation;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Tests;

// Excel x86 rows in https://github.com/hexide-io/HexIDE/issues/4399 (oracle measuring PR #4398).
public class VbaBitness32Tests : BaseVBTestFixture
{
    private void Target() => Compilation = CompilationConstants.ForProject(
        new ProjectDefinition(VBProjectType.EXE, "Test") { VbaBitness = VbaBitness.Bit32 });

    [Fact]
    public void ForProjectAccepts32Bit()
    {
        Target();
        Compilation!.VbaBitness.Should().Be(VbaBitness.Bit32);
    }

    [Theory]
    [InlineData("T1", "Dim q As LongPtr\nq = 5", "TypeName(q) & \",\" & VarType(q) & \",\" & LenB(q)", "Long,3,4")]
    [InlineData("V1", "", "VarType(CLngPtr(5))", "3")]
    [InlineData("C1", "", "CLngPtr(2.5)", "2")]
    [InlineData("C2", "", "CLngPtr(3.5)", "4")]
    [InlineData("C3", "", "CLngPtr(\"2147483647\")", "2147483647")]
    [InlineData("C5", "", "CLngPtr(-2147483648#)", "-2147483648")]
    [InlineData("A1", "", "CLngPtr(5) + CInt(1)", "6")]
    [InlineData("A3", "", "CLngPtr(5) * CDbl(2)", "10")]
    [InlineData("H1", "", "Hex$(CLngPtr(-1))", "FFFFFFFF")]
    [InlineData("K2", "", "5^ + 1", "5")]
    [InlineData("K3", "", "2^3", "8")]
    [InlineData("K4", "Dim y As Long\ny = 3", "y^2", "9")]
    [InlineData("D2", "DefLngPtr A-Z\nx = 5", "TypeName(x)", "Long")]
    public async Task MeasuredRow(string row, string setup, string expression, string value)
    {
        Target();
        await Run($"' {row}\n{setup}\nDebug.Print CStr({expression})\n");
        AssertDebugLog([new Vb6Value(value)]);
    }

    [Theory]
    [InlineData("C1", "CLngPtr(2.5)", "Long")]
    [InlineData("C2", "CLngPtr(3.5)", "Long")]
    [InlineData("C5", "CLngPtr(-2147483648#)", "Long")]
    [InlineData("A1", "CLngPtr(5) + CInt(1)", "Long")]
    [InlineData("A3", "CLngPtr(5) * CDbl(2)", "Double")]
    [InlineData("K2", "5^ + 1", "Double")]
    [InlineData("K3", "2^3", "Double")]
    [InlineData("K4", "y^2", "Double")]
    public async Task MeasuredType(string row, string expression, string type)
    {
        Target();
        await Run($"' {row}\nDim y As Long\ny = 3\nv = {expression}\nDebug.Print TypeName(v)\n");
        AssertDebugLog([new Vb6Value(type)]);
    }

    [Theory]
    [InlineData("C4", "v = CLngPtr(\"2147483648\")")]
    [InlineData("A2", "Dim q As LongPtr\nq = 2147483647\nq = q + 1")]
    public async Task MeasuredOverflow(string row, string code)
    {
        Target();
        await Run($"' {row}\nOn Error Resume Next\n{code}\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(6L)]);
    }

    [Theory]
    [InlineData("T2", "Dim x As LongLong", "User-defined type not defined*")]
    [InlineData("C6", "Debug.Print CLngLng(5)", "Sub or Function not defined*")]
    // Parse diagnostics retain HexIDE's existing wording (#4415); VBA reports Syntax error.
    [InlineData("K1", "v = 5^", "*")]
    [InlineData("K5", "Dim z^", "*")]
    // D1's text was not read in Excel: preserve the VB6 path, reasoned rather than measured.
    [InlineData("D1", "DefLngLng A-Z", "Invalid outside procedure*")]
    public async Task Refusal(string row, string code, string message)
    {
        Target();
        var act = () => Run($"' {row}\n{code}\n");
        await act.Should().ThrowAsync<VBCompileErrorException>().WithMessage("*" + message);
    }

    [Theory]
    [InlineData("W1", "Win64", 0)]
    [InlineData("W2 VBA7", "VBA7", 1)]
    [InlineData("W2 Win32", "Win32", 1)]
    [InlineData("W3", "VBA6", 1)]
    [InlineData("W5", "Mac", 0)]
    [InlineData("W6", "Win16", 0)]
    public async Task MeasuredConstant(string row, string name, int value)
    {
        Target();
        await Run($"' {row}\n#If {name} = {value} Then\nDebug.Print \"yes\"\n#Else\nDebug.Print \"no\"\n#End If\n");
        AssertDebugLog([new Vb6Value("yes")]);
    }

    [Theory]
    [InlineData("P1", "", "Long")]
    [InlineData("P2", "PtrSafe ", "Long")]
    [InlineData("P3", "PtrSafe ", "LongPtr")]
    public async Task MeasuredDeclareLoads(string row, string modifier, string type)
    {
        Target();
        // Native GetTickCount is Windows-only. Loading the real Declare is portable; no synthetic native result.
        await Run($"' {row}\nPrivate Declare {modifier}Function GetTickCount Lib \"kernel32\" () As {type}\nDebug.Print \"loaded\"\n");
        AssertDebugLog([new Vb6Value("loaded")]);
    }

    [Fact]
    public async Task AllDeclarationSitesUseLong()
    {
        Target();
        // Declaration routing required by #4399; values reasoned from LongPtr = Long (T1).
        await Run("""
            Type T
                n As LongPtr
            End Type
            Const N As LongPtr = 5
            Function Echo(ByVal arg As LongPtr) As LongPtr
                Debug.Print TypeName(arg)
                Echo = arg
            End Function
            Dim x As T
            x.n = N
            Debug.Print TypeName(N)
            Debug.Print TypeName(x.n)
            Debug.Print TypeName(Echo(x.n))
            """);
        AssertDebugLog([new Vb6Value("Long"), new Vb6Value("Long"), new Vb6Value("Long"), new Vb6Value("Long")]);
    }
}
