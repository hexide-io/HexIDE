using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class MissingBuiltInObjectTests : BaseVBTestFixture
{
    public static IEnumerable<object[]> NamesAndDeclarationModes() => Modes("Printer", "Printers", "pRiNtEr");

    // Forms is implemented (#2496), and a declared name still claims it.
    public static IEnumerable<object[]> DeclarableNamesAndDeclarationModes() => Modes("Printer", "Printers", "Forms", "pRiNtEr");

    private static IEnumerable<object[]> Modes(params string[] names)
    {
        foreach (var name in names)
            foreach (var explicitDeclarations in new[] { false, true })
                yield return [name, explicitDeclarations];
    }

    [Theory]
    [MemberData(nameof(NamesAndDeclarationModes))]
    public async Task Missing_objects_are_refused_instead_of_reading_Empty(string name, bool explicitDeclarations)
    {
        var code = (explicitDeclarations ? "Option Explicit\n" : "")
            + $"On Error Resume Next\nDebug.Print TypeName({name})\nDebug.Print \"continued\"\n";
        var run = () => Run(code);
        (await run.Should().ThrowAsync<NotImplementedException>()).Which.Message
            .Should().Contain(name).And.Contain("built-in object").And.Contain("#871");
        debug.Should().BeEmpty("On Error cannot skip an engine limitation");
    }

    [Theory]
    [MemberData(nameof(NamesAndDeclarationModes))]
    public async Task A_member_read_reports_the_missing_object(string name, bool explicitDeclarations)
    {
        var code = (explicitDeclarations ? "Option Explicit\n" : "")
            + $"Debug.Print {name}.Count\n";
        var run = () => Run(code);
        (await run.Should().ThrowAsync<NotImplementedException>()).Which.Message.Should().Contain(name);
    }

    [Theory]
    [MemberData(nameof(DeclarableNamesAndDeclarationModes))]
    public async Task A_declared_variable_with_the_same_name_still_wins(string name, bool explicitDeclarations)
    {
        await Run((explicitDeclarations ? "Option Explicit\n" : "")
            + $"Dim {name} As String\n{name} = \"local\"\nDebug.Print {name}\n");
        AssertDebugLog([new Vb6Value("local")]);
    }

    [Theory]
    [InlineData("Screen")]
    [InlineData("Clipboard")]
    [InlineData("Printer")]
    [InlineData("Printers")]
    [InlineData("Forms")]
    public async Task A_user_function_with_the_same_name_still_wins(string name)
    {
        await Run($"Function {name}() As String\n{name} = \"function\"\nEnd Function\nDebug.Print {name}\n");
        AssertDebugLog([new Vb6Value("function")]);
    }

    [Fact]
    public async Task Forms_in_a_program_with_no_form_holds_none()
    {
        await Run("Option Explicit\nDebug.Print TypeName(Forms)\nDebug.Print Forms.Count\n");
        AssertDebugLog([new Vb6Value("Object"), new Vb6Value(0)]);
    }

    [Fact]
    public async Task Ordinary_undeclared_names_and_implemented_objects_are_unchanged()
    {
        await Run("Debug.Print TypeName(ordinaryName)\nDebug.Print TypeName(App)\nDebug.Print TypeName(Err)\n");
        AssertDebugLog([new Vb6Value("Empty"), new Vb6Value("App"), new Vb6Value("ErrObject")]);
    }
}
