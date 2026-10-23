using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class BarePrintTests : BaseVBTestFixture
{
    // Unmeasured module kinds refuse at execution, never while loading (#993).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BarePrint_OutsideForm_RefusesNamingIssue(bool inClass)
    {
        const string body = "Sub Foo()\nPrint \"hello\"\nEnd Sub";
        Func<Task> run = inClass
            ? () => RunClasses("Dim c As New C\nc.Foo", ("C", body))
            : () => Run(body + "\nFoo");
        await run.Should().ThrowAsync<NotImplementedException>().WithMessage("*not implemented*#993*");
    }
}
