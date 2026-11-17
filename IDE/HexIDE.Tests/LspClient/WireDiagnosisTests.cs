namespace HexIDE.Tests.LspClient;

public class WireDiagnosisTests
{
    [Fact]
    public void DiagnosisIncludesRunnerHistoryForTheTestElapsedTime()
    {
        var diagnosis = new WireDiagnosis(TestContext.Current.CancellationToken);
        diagnosis.Diagnosis("stand-in did not initialize").Should()
            .Contain("stand-in did not initialize")
            .And.Contain("runner load over the last ")
            .And.NotContain("thread pool threads=");
    }
}
