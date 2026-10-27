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

    // The For counter rows below were measured for #739 on vb6.exe, native and p-code, in the oracle entry "A For counter
    // written with a type character".
    public static IEnumerable<object[]> NumericCharacters =>
        [["%", "Integer"], ["&", "Long"], ["!", "Single"], ["#", "Double"], ["@", "Currency"]];

    public static IEnumerable<object[]> CounterScopes()
    {
        foreach (var row in NumericCharacters)
        foreach (var scope in new[] { "local", "module", "public" })
        foreach (var next in new[] { false, true })
            yield return [row[0], row[1], scope, next];
    }

    [Theory]
    [MemberData(nameof(CounterScopes))]
    public async Task A_counter_through_its_character_is_the_declared_variable(string character, string type, string scope, bool next)
    {
        var loop = $"""
            Sub Main()
                {(scope == "local" ? $"Dim c As {type}" : "")}
                Dim s As String
                For c{character} = 1 To 2
                    s = s & c & ","
                Next{(next ? $" c{character}" : "")}
                Debug.Print s & TypeName(c) & " " & c
            End Sub
            """;
        if (scope == "public")
            await RunModules("Option Explicit\n" + loop + "\nMain\n", ("Module2", $"Option Explicit\nPublic c As {type}\n"));
        else
            await Run($"Option Explicit\n{(scope == "module" ? $"Private c As {type}" : "")}\n{loop}\nMain\n");

        AssertDebugLog([new Vb6Value($"1,2,{type} 3")]);
    }

    [Theory]
    [MemberData(nameof(NumericCharacters))]
    public async Task An_undeclared_counter_takes_the_characters_type(string character, string type)
    {
        await Run($"""
            Sub Main()
                For u{character} = 1 To 2
                Next u{character}
                Debug.Print TypeName(u) & " " & u
            End Sub
            Main
            """);

        AssertDebugLog([new Vb6Value($"{type} 3")]);
    }

    [Fact]
    public async Task Next_with_a_character_may_close_a_bare_counter_and_the_reverse()
    {
        await Run("""
            Sub Main()
                Dim i As Integer, g As Single, s As String
                For i = 1 To 2: Next i%
                s = s & i & ","
                For i% = 1 To 2: Next i
                s = s & i & ","
                For g! = 0.5 To 1.5 Step 0.5
                    s = s & g & ","
                Next g!
                Debug.Print s & TypeName(g) & " " & g
            End Sub
            Main
            """);

        AssertDebugLog([new Vb6Value("3,3,0.5,1,1.5,Single 2")]);
    }

    [Theory]
    [InlineData("Dim i As Long", "For i% = 1 To 2: Next", "*Type-declaration character does not match declared data type*")]
    [InlineData("Dim v", "For v% = 1 To 2: Next", "*Type-declaration character does not match declared data type*")]
    [InlineData("Dim i As Integer", "For i = 1 To 2: Next i&", "*Type-declaration character does not match declared data type*")]
    [InlineData("Dim v As Variant", "For v = 1 To 2: Next v%", "*Type-declaration character does not match declared data type*")]
    [InlineData("Dim s As String", "For s$ = 1 To 2: Next", "*Type mismatch*")]
    [InlineData("", "For k% = 1 To 2: Next", "*Variable not defined (k)*")]
    [InlineData("Dim i As Integer", "For i% = 1 To 2: Next j%", "*Variable not defined (j)*")]
    public async Task A_counter_character_VB6_refuses_is_its_compile_error(string declaration, string loop, string message)
    {
        var run = () => Run($"""
            Option Explicit
            Sub Main()
                {declaration}
                {loop}
            End Sub
            Main
            """);

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage(message);
    }
}
