using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class NonObjectMemberTests : BaseVBTestFixture
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var (setup, error) in new[]
        {
            ("", 424L),
            ("Dim v", 424L),
            ("Dim v: v = Null", 424L),
            ("Dim v: v = 5", 424L),
            ("Dim v: v = \"s\"", 424L),
            ("Dim v: v = Array(1, 2)", 424L),
            ("Dim v: Set v = Nothing", 91L),
            ("Dim v As Object", 91L),
            ("Dim v As Collection", 91L),
            ("Dim v: Set v = New Collection", 438L),
        })
        foreach (var statement in new[] { "v.Foo", "v.Foo = 1", "x = v.Foo", "x = v.Foo(1)", "Call v.Foo", "Set x = v.Foo" })
            if (setup.Length != 0 || statement == "v.Foo")
                yield return [setup, statement, error];
    }

    // vb6.exe SP6, native and p-code: a-member-used-on-something-that-is-not-an-object.md (#257).
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ReceiverDeterminesTrappableError(string setup, string statement, long error)
    {
        await Run($"{setup}\nDim x\nOn Error Resume Next\n{statement}\nDebug.Print Err.Number\nDebug.Print \"continued\"");
        AssertDebugLog([new Vb6Value(error), new Vb6Value("continued")]);
    }

    [Theory]
    [InlineData("x = v.NoSuch")]
    [InlineData("v.NoSuch = 1")]
    [InlineData("v.NoSuch")]
    [InlineData("Call v.NoSuch")]
    [InlineData("x = v.NoSuch(1)")]
    [InlineData("Set x = v.NoSuch")]
    public async Task FormThroughObjectRaises438(string statement)
    {
        var code = $"Dim v As Object\nDim x\nSet v = Me\nOn Error Resume Next\n{statement}\nDebug.Print Err.Number";
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, code,
            primaryKind: InterpreterModuleKind.Form);
        context.AllocVariable(rootEnv, "Me", Vb6Value.NewObject(vb.NewFormInstance(vb.PrimaryModule)));
        await vb.Execute();
        AssertDebugLog([new Vb6Value(438L)]);
    }
}
