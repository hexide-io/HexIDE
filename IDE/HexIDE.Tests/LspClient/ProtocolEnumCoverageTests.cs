using HexIDE.Lsp.Messages;

namespace HexIDE.Tests.LspClient;

public class ProtocolEnumCoverageTests
{
    [LspModelFact]
    public void WireEnumsCoverTheSpecificationValues()
    {
        var model = LspSpecificationModel.Enumerations();
        AssertCoverage<DiagnosticSeverity>(model, "DiagnosticSeverity");
        AssertCoverage<LspMessageType>(model, "MessageType");
        AssertCoverage<SymbolKind>(model, "SymbolKind");
        // Completion display kinds remain a separate follow-up to #359. Pin the existing omissions so
        // the guard still catches any newly specified value without widening the display model here.
        AssertCoverage<CompletionItemKind>(model, "CompletionItemKind",
            [2, 4, 5, 7, 8, 9, 11, 12, 13, 15, 16, 17, 18, 19, 20, 22, 23, 24, 25]);
    }

    private static void AssertCoverage<T>(IReadOnlyDictionary<string, int[]> model, string name,
        int[]? deferred = null) where T : struct, Enum
    {
        model.Should().ContainKey(name);
        var declared = Enum.GetValues<T>().Select(v => Convert.ToInt32(v)).ToArray();
        model[name].Except(declared).Should().BeEquivalentTo(deferred ?? [],
            $"{typeof(T).Name} must cover the specification except for explicitly deferred display kinds");
        declared.Except(model[name]).Should().BeEmpty();
    }
}
