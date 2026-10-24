using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Measured in the oracle's 2026-10-11 type-character assignment probes (#739).
public class AssignmentTypeCharacterTests : BaseVBTestFixture
{
    public static IEnumerable<object[]> Characters =>
    [
        ["$", "String", "\"x\""], ["%", "Integer", "5"], ["&", "Long", "70000"],
        ["!", "Single", "1.5"], ["#", "Double", "2.5"], ["@", "Currency", "1.25"]
    ];

    public static IEnumerable<object[]> DeclaredTargets()
    {
        foreach (var row in Characters)
        foreach (var explicitOption in new[] { "", "Option Explicit" })
        foreach (var local in new[] { false, true })
        {
            var value = local ? (string)row[0] switch
            {
                "$" => explicitOption.Length == 0 ? "\"z\"" : "\"y\"",
                "%" => "7", "&" => "9", "!" => "2.5", "#" => "3.5", "@" => "4.25", _ => (string)row[2]
            } : (string)row[0] == "$" && explicitOption.Length == 0 ? "\"w\"" : (string)row[2];
            yield return [row[0], row[1], value, explicitOption, local];
        }
    }

    [Theory]
    [MemberData(nameof(DeclaredTargets))]
    public async Task Declared_target_is_shared_with_its_bare_name(
        string character, string type, string value, string explicitOption, bool local)
    {
        await Run($"""
            {explicitOption}
            {(local ? "" : $"Private target As {type}")}
            Sub Main()
                {(local ? $"Dim target As {type}" : "")}
                target{character} = {value}
                Debug.Print TypeName(target) & "|" & target
            End Sub
            Main
            """);
        AssertDebugLog([new Vb6Value(type + "|" + value.Trim('"'))]);
    }

    [Theory]
    [MemberData(nameof(Characters))]
    public async Task First_suffixed_assignment_declares_the_bare_name_with_its_type(string character, string type, string value)
    {
        value = character switch { "%" => "3", "&" => "8", _ => value };
        await Run($"""
            Sub Main()
                target{character} = {value}
                Debug.Print TypeName(target) & "|" & target
            End Sub
            Main
            """);
        AssertDebugLog([new Vb6Value(type + "|" + value.Trim('"'))]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Option Explicit")]
    public async Task Integer_counter_updates_the_declared_local(string option)
    {
        await Run($"""
            {option}
            Sub Main()
                Dim i As Integer
                For i% = 1 To 2
                Next
                Debug.Print i
            End Sub
            Main
            """);
        AssertDebugLog([new Vb6Value(3)]);
    }

    [Theory]
    [InlineData("Long")]
    [InlineData("Variant")]
    public async Task A_character_contradicting_the_declared_type_is_refused(string type)
    {
        var run = () => Run($"""
            Dim Left As {type}
            Left$ = "x"
            """);
        (await run.Should().ThrowAsync<VBCompileErrorException>())
            .WithMessage("*Type-declaration character does not match declared data type*");
    }
}
