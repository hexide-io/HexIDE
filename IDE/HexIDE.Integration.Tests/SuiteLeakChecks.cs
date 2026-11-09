namespace HexIDE.Integration.Tests;

// Ordinary test failures are reported once; assembly-fixture failures are repeated for every test (#3502).
[Collection(HexIDE.TestGuards.LspSessionLeakGuard.RunsLast)]
public class SuiteLeakChecks
{
    [Fact]
    public void Every_language_server_was_stopped() => LanguageServerLeakGuard.Check();

    [AvaloniaFact]
    public void Every_window_was_closed() => WindowLeakGuard.Check();
}
