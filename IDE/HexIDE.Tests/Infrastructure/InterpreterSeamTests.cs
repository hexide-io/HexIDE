using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// Where the IDE's own code names the interpreter (#3411, #3670). Only the run engine's implementation and the composition
/// that binds each seam's implementation do, so the interpreter leaves with the provider when those two places move.
/// </summary>
public class InterpreterSeamTests
{
    [Fact]
    public void Only_the_run_engine_and_the_bindings_name_the_interpreter() =>
        ProductSource.FilesMatching(new Regex(@"\bHexIDE\.Runtime\.Interpreter\b"), "InterpreterRunEngine.cs", "DISetup.cs")
            .Should().BeEmpty("the IDE reaches the interpreter through IRunEngine, IConditionalCodeReader and IBracketReader");
}
