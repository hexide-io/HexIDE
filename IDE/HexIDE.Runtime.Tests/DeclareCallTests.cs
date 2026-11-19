using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Measurements: https://github.com/hexide-io/HexIDE/issues/4820 (including argument evaluation).
public class DeclareCallTests : BaseVBTestFixture
{
    public static bool Windows => OperatingSystem.IsWindows();

    [Theory(Skip = "Off-Windows behavior", SkipWhen = nameof(Windows))]
    [InlineData("kernel32", "Sleep 1")]
    [InlineData("hexide-nosuch", "Sleep 1")]
    [InlineData("kernel32", "Call Sleep(1)")]
    [InlineData("kernel32", "Call Module1.Sleep(1)")]
    public async Task Sub_MissingLibrary_IsTrappable(string library, string call)
    {
        await Run($"""
            Private Declare Sub Sleep Lib "{library}" (ByVal milliseconds As Long)
            On Error Resume Next
            {call}
            Debug.Print Err.Number
            Debug.Print Err.Description
            """);
        AssertDebugLog([new Vb6Value(53L), new Vb6Value("File not found: " + library)]);
    }

    [Theory(Skip = "Off-Windows behavior", SkipWhen = nameof(Windows))]
    [InlineData("NoSuchLib")]
    [InlineData("Module1.NoSuchLib")]
    public async Task Function_EvaluatesArgumentOncePerCall_BeforeTrappableError(string call)
    {
        await Run($"""
            Private Declare Function NoSuchLib Lib "hexide-nosuch.dll" (ByRef n As Long) As Long
            Dim calls As Long
            Function Side() As Long
                calls = calls + 1
                Side = calls
            End Function
            On Error Resume Next
            Dim result As Long
            result = {call}(Side())
            Debug.Print Err.Number
            Debug.Print Err.Description
            Debug.Print calls
            Err.Clear
            result = {call}(Side())
            Debug.Print Err.Number
            Debug.Print Err.Description
            Debug.Print calls
            """);
        AssertDebugLog([new Vb6Value(53L), new Vb6Value("File not found: hexide-nosuch.dll"), new Vb6Value(1L),
            new Vb6Value(53L), new Vb6Value("File not found: hexide-nosuch.dll"), new Vb6Value(2L)]);
    }

    // An Alias doesn't change a missing library's error: vb6.exe raises 53 naming the Lib string, with or without one
    // (measured 2026-11-19, native and p-code, #4820).
    [Fact(Skip = "Off-Windows behavior", SkipWhen = nameof(Windows))]
    public async Task Alias_MissingLibrary_IsTrappable()
    {
        await Run("""
            Private Declare Function MissAlias Lib "hexide-nosuch.dll" Alias "Whatever" () As Long
            Dim x As Long
            On Error Resume Next
            x = MissAlias()
            Debug.Print Err.Number
            Debug.Print Err.Description
            """);
        AssertDebugLog([new Vb6Value(53L), new Vb6Value("File not found: hexide-nosuch.dll")]);
    }

    // VB6 takes a named argument to a Declare and raises 53 (measured, #4869); a Declare has no parameters to place it
    // against yet, so HexIDE refuses it rather than guess.
    [Fact(Skip = "Off-Windows behavior", SkipWhen = nameof(Windows))]
    public async Task NamedArguments_AreRefused()
    {
        var run = () => Run("""
            Private Declare Sub NativeCall Lib "hexide-nosuch.dll" (ByVal n As Long)
            On Error Resume Next
            NativeCall n:=1
            """);
        await run.Should().ThrowAsync<NotImplementedException>().WithMessage("Named arguments on Declare calls are not implemented (#4869)");
    }

    [Theory(Skip = "Off-Windows behavior", SkipWhen = nameof(Windows))]
    [InlineData("native.dylib")]
    [InlineData("native.DYLIB")]
    [InlineData("native.so")]
    [InlineData("native.SO.6")]
    public async Task HostLibrary_KeepsRefusal(string library)
    {
        var run = () => Run($"""
            Private Declare Sub NativeCall Lib "{library}" ()
            On Error Resume Next
            NativeCall
            """);
        await run.Should().ThrowAsync<VBCompileErrorException>().WithMessage("*Sub or Function not defined (NativeCall)*");
    }

    [Fact(Skip = "Windows-only refusal", SkipUnless = nameof(Windows))]
    public async Task Windows_KeepsRefusal()
    {
        var run = () => Run("""
            Private Declare Sub Sleep Lib "kernel32" (ByVal milliseconds As Long)
            On Error Resume Next
            Sleep 1
            """);
        await run.Should().ThrowAsync<VBCompileErrorException>().WithMessage("*Sub or Function not defined (Sleep)*");
    }
}
