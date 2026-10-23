using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// The four settings statements' parameters (#1562), as vb6.exe compiles them: each row below is one project /make'd on
/// vb6.exe. SettingsProgramTests has what they do.
/// </summary>
public class SettingsTests : BaseVBTestFixture
{
    [Fact]
    public async Task Each_takes_its_parameters_by_name_and_DeleteSetting_needs_only_the_program()
    {
        await Run("""
            Dim v As Variant
            On Error Resume Next
            v = GetSetting(AppName:="HexProbe1562Nope", Section:="s", Key:="k", Default:="d")
            Debug.Print "GetSetting " & Err.Number & " " & v
            v = GetAllSettings(AppName:="HexProbe1562Nope", Section:="s")
            Debug.Print "GetAllSettings " & Err.Number & " " & IsEmpty(v)
            SaveSetting AppName:="HexProbe1562Named", Section:="s", Key:="k", Setting:="v"
            Debug.Print "SaveSetting " & Err.Number & " " & GetSetting("HexProbe1562Named", "s", "k")
            DeleteSetting AppName:="HexProbe1562Named", Section:="s", Key:="k"
            Debug.Print "DeleteSetting " & Err.Number & " " & GetSetting("HexProbe1562Named", "s", "k", "gone")
            DeleteSetting "HexProbe1562Nope"
            Debug.Print "DeleteSetting alone " & Err.Number
            Err.Clear
            GetSetting "HexProbe1562Nope", "s", "k"
            Debug.Print "as a statement " & Err.Number
            """);

        AssertDebugLog(new List<Vb6Value>
        {
            new("GetSetting 0 d"), new("GetAllSettings 0 True"), new("SaveSetting 0 v"), new("DeleteSetting 0 gone"),
            new("DeleteSetting alone 5"), new("as a statement 0"),
        });
    }

    [Theory]
    [InlineData("v = GetSetting(\"a\", \"s\")", "*Argument not optional*")]
    [InlineData("v = GetSetting(AppName:=\"a\", Section:=\"s\", Key:=\"k\", DefaultValue:=\"d\")", "*Named argument not found*")]
    [InlineData("v = GetSetting(AppName:=\"a\", Key:=\"k\")", "*Argument not optional*")]
    [InlineData("v = GetAllSettings(\"a\")", "*Argument not optional*")]
    [InlineData("SaveSetting AppName:=\"a\", Section:=\"s\", Key:=\"k\", Value:=\"v\"", "*Named argument not found*")]
    public async Task What_vb6_refuses_to_compile_is_refused(string statement, string message)
    {
        var run = () => Run("Dim v As Variant\n" + statement + "\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage(message);
    }

    // Refused as the program loads, as vb6.exe refuses to build them, but by the grammar, in the parser's words rather than
    // VB6's: vb6.exe says "Argument not optional" for the first and "Syntax error" for the second. SaveSetting's rule takes
    // exactly four arguments, so the language server, which counts no arguments, marks a short one too.
    [Theory]
    [InlineData("SaveSetting \"a\", \"s\", \"k\"")]
    [InlineData("SaveSetting(\"a\", \"s\", \"k\", \"v\")")]
    public async Task What_the_grammar_refuses_is_refused_as_the_program_loads(string statement)
    {
        var run = () => Run("Dim v As Variant\n" + statement + "\n");

        await run.Should().ThrowAsync<VBCompileErrorException>();
    }
}
