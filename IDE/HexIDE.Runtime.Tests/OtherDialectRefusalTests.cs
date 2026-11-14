using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// The closed refusal list and wording are Jen's decision in #3434, not a VB6 diagnostic.
public class OtherDialectRefusalTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("Dim x As Long = 5", "=")]
    [InlineData("Private x As Long = 5", "=")]
    [InlineData("Public x As Long = 5", "=")]
    [InlineData("Static x As Long = 5", "=")]
    [InlineData("Global x As Long = 5", "=")]
    [InlineData("x += 2", "+=")]
    [InlineData("x -= 2", "-=")]
    [InlineData("x *= 2", "*=")]
    [InlineData("x /= 2", "/=")]
    [InlineData("x \\= 2", "\\=")]
    [InlineData("x ^= 2", "^=")]
    [InlineData("x &= 2", "&=")]
    [InlineData("Return 2", "Return")]
    [InlineData("Continue For", "Continue For")]
    [InlineData("Continue Do", "Continue Do")]
    [InlineData("Continue While", "Continue While")]
    [InlineData("x = 1 << 2", "<<")]
    [InlineData("x = 1 >> 2", ">>")]
    [InlineData("x = True AndAlso False", "AndAlso")]
    [InlineData("x = True OrElse False", "OrElse")]
    [InlineData("Foo(Of T)", "(Of T)")]
    [InlineData("Sub Foo(Of T)()\nEnd Sub", "(Of T)")]
    [InlineData("[Description(\"example\")]\nSub Foo()\nEnd Sub", "[Description(\"example\")]")]
    [InlineData("[Description]\nSub Foo()\nEnd Sub", "[Description]")]
    [InlineData("x = True aNdAlSo False", "aNdAlSo")]
    public async Task RefusesBeforeExecution(string statement, string construct)
    {
        Func<Task> run = () => Run("Debug.Print 123\n" + statement + "\n");
        var error = (await run.Should().ThrowAsync<VBCompileErrorException>()).Which;
        error.Message.ReplaceLineEndings("\n").Should().Be(
            $"Compile error:\n\n{construct} isn't part of VB6, VBA or VBScript, so this interpreter doesn't run it.");
        error.Line.Should().Be(2);
        debug.Should().BeEmpty();
        written.Length.Should().Be(0);
    }

    [Fact]
    public async Task ClassicSyntaxAndQuotedTextStillRun()
    {
        await Run("""
                Const x As Long = 5
                Dim AndAlso As Long
                Private Sub OrElse(Optional n As Long = 5)
                End Sub
                Dim [SomeName] As Long
                [SomeName] = x
                [SomeName] = [SomeName] + 1
                ' Continue For AndAlso +=
                Debug.Print [SomeName]
                Debug.Print "Return 2 << (Of T)"
            """);
        AssertDebugLog([new Vb6Value(6L), "Return 2 << (Of T)"]);
    }

    // Classic shapes that sit next to the list: a Long type suffix against its name before `=`, and a variable named Of.
    [Theory]
    [InlineData("n&=5\nDebug.Print n&", 5L)]
    [InlineData("Dim n As Long\nIf n&=0 Then n = 7\nDebug.Print n", 7L)]
    [InlineData("Dim v As Long\nOf = 3\nv = Abs(Of)\nDebug.Print v", 3L)]
    [InlineData("Dim v As Long\nOf = 3\nv = Abs(Of) + Abs(Of + 1)\nDebug.Print v", 7L)]
    public async Task ClassicShapesBesideTheListStillRun(string code, long printed)
    {
        await Run(code + "\n");
        AssertDebugLog([new Vb6Value(printed)]);
    }

    // GoSub's Return before Else, in a one-line If, is VB6's own Return.
    [Fact]
    public async Task GoSubReturnBeforeElseIsNotRefused()
    {
        var error = await Record.ExceptionAsync(() => Run(
            "Sub Go()\nGoSub Here\nExit Sub\nHere:\nIf True Then Return Else Debug.Print 0\nEnd Sub\nGo\n"));
        (error?.Message ?? "").Should().NotContain("isn't part of");
    }

    [Theory]
    [InlineData("Dim x = 5")]
    [InlineData("Dim x As Long, y = 5")]
    public async Task InitializerWithoutItsOwnTypeKeepsParserDiagnostic(string code)
    {
        Func<Task> run = () => Run(code + "\n");
        var error = (await run.Should().ThrowAsync<VBCompileErrorException>()).Which;
        error.Message.Should().Contain("Offending symbol: =");
        error.Message.Should().NotContain("isn't part of");
    }

    [Fact]
    public async Task TypoKeepsParserDiagnostic()
    {
        Func<Task> run = () => Run("Sub Main()\nx = = 1\nEnd Sub");
        var error = (await run.Should().ThrowAsync<VBCompileErrorException>()).Which;
        error.Message.Should().Contain("Offending symbol: =");
        error.Message.Should().NotContain("isn't part of");
        error.Line.Should().Be(2);
    }
}
