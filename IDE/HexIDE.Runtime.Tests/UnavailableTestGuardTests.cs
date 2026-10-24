using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace HexIDE.Runtime.Tests;

public class UnavailableTestGuardTests
{
    [Theory]
    [InlineData("SerializationCorpusTests.cs")]
    [InlineData("Vb6OracleRoundTripTests.cs")]
    public void Corpus_gates_cannot_pass_by_returning_early(string file)
    {
        var source = File.ReadAllText(Path.Join(SourceDirectory(), file)).ReplaceLineEndings();
        Regex.IsMatch(source, @"\breturn\s*;").Should().BeFalse(
            "an unavailable corpus or compiler must report a skip rather than an unearned pass");
    }

    private static string SourceDirectory([CallerFilePath] string source = "") => Path.GetDirectoryName(source)!;
}
