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
        AssertCoverage<CompletionItemKind>(model, "CompletionItemKind");
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
