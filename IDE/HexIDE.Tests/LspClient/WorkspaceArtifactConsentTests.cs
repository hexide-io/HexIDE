using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Lsp;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.ViewModels.Options;
using HexIDE.Localization;

namespace HexIDE.Tests.LspClient;

public sealed class WorkspaceArtifactConsentTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hexide-consent-" + Guid.NewGuid().ToString("N"));
    public WorkspaceArtifactConsentTests() => Directory.CreateDirectory(_dir);

    [Theory]
    [InlineData(WorkspaceArtifactDecision.Allow)]
    [InlineData(WorkspaceArtifactDecision.Deny)]
    [InlineData(WorkspaceArtifactDecision.AllowAlways)]
    public async Task SessionAndPersistentConsentHaveServerAndWorkspaceScope(WorkspaceArtifactDecision decision)
    {
        var path = Path.Combine(_dir, "consent.json");
        var calls = 0;
        Task<WorkspaceArtifactDecision> Prompt(string name, string target)
        {
            name.Should().Be("Server X");
            target.Should().Be("local.json");
            calls++;
            return Task.FromResult(decision);
        }
        var sut = new WorkspaceArtifactConsent(new ConsentStore(path), Prompt);
        var first = Path.Combine(_dir, "one");
        var second = Path.Combine(_dir, "two");
        (await sut.AllowAsync("x", "Server X", first, "local.json")).Should().Be(decision != WorkspaceArtifactDecision.Deny);
        await sut.AllowAsync("x", "Server X", first, "local.json");
        await sut.AllowAsync("x", "Server X", first + Path.DirectorySeparatorChar, "local.json");
        calls.Should().Be(1);
        await sut.AllowAsync("x", "Server X", second, "local.json");
        await sut.AllowAsync("y", "Server X", first, "local.json");
        calls.Should().Be(3);
        var next = new WorkspaceArtifactConsent(new ConsentStore(path), Prompt);
        await next.AllowAsync("x", "Server X", first, "local.json");
        calls.Should().Be(decision == WorkspaceArtifactDecision.AllowAlways ? 3 : 4);
        if (decision == WorkspaceArtifactDecision.AllowAlways)
        {
            var store = new ConsentStore(path);
            store.Records.Should().HaveCount(3);
            store.Revoke(WorkspaceArtifactConsent.Key("x", first));
            var reset = new WorkspaceArtifactConsent(new ConsentStore(path), Prompt);
            await reset.AllowAsync("x", "Server X", first, "local.json");
            calls.Should().Be(4);
        }
    }

    [Theory]
    [InlineData(WorkspaceArtifactDecision.Allow)]
    [InlineData(WorkspaceArtifactDecision.Deny)]
    [InlineData(WorkspaceArtifactDecision.AllowAlways)]
    public void PromptNamesBothPartiesAndCommandsSetTheDecision(WorkspaceArtifactDecision decision)
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.LspArtifact.Prompt").Returns("Server {0} has requested to write local file {1}.");
        var sut = new WorkspaceArtifactConsentViewModel("Server X", "local.json", localization);
        sut.Decision.Should().Be(WorkspaceArtifactDecision.Deny);
        sut.Message.Should().Be("Server Server X has requested to write local file local.json.");
        bool? closed = null;
        sut.CloseRequested += allowed => closed = allowed;
        switch (decision)
        {
            case WorkspaceArtifactDecision.Allow: sut.AllowCommand.Execute(null); break;
            case WorkspaceArtifactDecision.AllowAlways: sut.AlwaysCommand.Execute(null); break;
            default: sut.DenyCommand.Execute(null); break;
        }
        sut.Decision.Should().Be(decision);
        closed.Should().Be(decision != WorkspaceArtifactDecision.Deny);
    }

    [Fact]
    public void OptionsListsAndResetsPersistentWorkspaceGrants()
    {
        var file = Path.Combine(_dir, "consent.json");
        var store = new ConsentStore(file);
        store.RecordWorkspace("x", _dir, "key");
        var sut = new WorkspaceArtifactPageViewModel(store);
        var grant = sut.Grants.Should().ContainSingle().Subject;
        grant.Server.Should().Be("x");
        grant.Workspace.Should().Be(_dir);
        sut.ResetCommand.Execute(grant);
        sut.Grants.Should().BeEmpty();
        new ConsentStore(file).Records.Should().BeEmpty();
    }

    public void Dispose() => Directory.Delete(_dir, true);
}
