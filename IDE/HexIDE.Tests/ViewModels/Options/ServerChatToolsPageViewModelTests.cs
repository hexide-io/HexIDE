using HexIDE.Forms.ViewModels.Options;
using HexIDE.IDE;
using HexIDE.Lsp;

namespace HexIDE.Tests.ViewModels.Options;

/// <summary>
/// Tools → Options → AI Chat Tools (#4257): the person's choice, per language server, over what its entry says, an
/// add-in's entry included.
/// </summary>
public sealed class ServerChatToolsPageViewModelTests
{
    private static readonly ServerChatToolsCandidate RdCore = new("rdcore", "RDCore", "RDCore Add-in", ServerChatToolsPolicy.Hints);
    private static readonly ServerChatToolsCandidate Probe = new("probe", "Probe", null, ServerChatToolsPolicy.AskEveryCall);

    private static ISettingsService Settings(Dictionary<string, string> chosen)
    {
        var settings = Substitute.For<ISettingsService>();
        settings.ServerChatTools.Returns(_ => new Dictionary<string, string>(chosen));
        return settings;
    }

    [Fact]
    public void EachServer_IsListedByName_WithItsAddin_AndThePersonsChoice()
    {
        var page = new ServerChatToolsPageViewModel(Settings(new() { ["rdcore"] = "ask" }), () => [RdCore, Probe]);

        page.HasNoServers.Should().BeFalse();
        page.Servers.Select(s => s.Name).Should().Equal("Probe", "RDCore");
        page.Servers[1].FromAddin.Should().Be("From RDCore Add-in");
        page.Servers[0].FromAddin.Should().BeNull("the configuration file's server came from no add-in");
        page.Servers[1].Selected.Policy.Should().Be(ServerChatToolsPolicy.AskEveryCall, "the person chose ask for RDCore");
        page.Servers[0].Selected.Policy.Should().BeNull("Probe has no choice of the person's, so it follows its entry");
        page.Servers[0].Selected.Label.Should().Be("As configured (Ask before every call)");
        page.Servers[1].Choices.Select(c => c.Label).Should().Equal(
            "As configured (On: ask unless the tool only reads)", "On: ask unless the tool only reads", "Ask before every call", "Off");
        page.Servers[1].ChoiceName.Should().Be("AI Chat tools from RDCore");
    }

    [Fact]
    public void Saving_WritesEachChoice_ForgetsAsConfigured_AndKeepsServersNotListed()
    {
        var settings = Settings(new() { ["rdcore"] = "ask", ["gone"] = "off" });
        var page = new ServerChatToolsPageViewModel(settings, () => [RdCore, Probe]);
        page.Servers.Single(s => s.Id == "rdcore").Selected = page.Servers.Single(s => s.Id == "rdcore").Choices[0];
        page.Servers.Single(s => s.Id == "probe").Selected = page.Servers.Single(s => s.Id == "probe").Choices[3];

        page.SaveToSettings();

        settings.Received().ServerChatTools = Arg.Is<Dictionary<string, string>>(d =>
            d.Count == 2 && d["probe"] == "off" && d["gone"] == "off" && !d.ContainsKey("rdcore"));
    }

    [Fact]
    public void RestoringDefaults_PutsEveryServerBackToItsEntry()
    {
        var page = new ServerChatToolsPageViewModel(Settings(new() { ["rdcore"] = "off", ["probe"] = "on" }), () => [RdCore, Probe]);

        page.RestoreDefaults();

        page.Servers.Should().OnlyContain(s => s.Selected.Policy == null);
    }

    [Fact]
    public void WithNoServerThatCanOfferTools_ThePageSaysSo()
    {
        new ServerChatToolsPageViewModel(Settings([]), () => []).HasNoServers.Should().BeTrue();
    }
}
