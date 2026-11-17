using System.Text.Json;
using HexIDE.Addins;

namespace HexIDE.Tests.Addins;

/// <summary>
/// Only HexIDE's own add-ins reach language servers' AI Chat tools (#4257): the caller supplies the approval callback, so
/// any other add-in could pass one that always says yes and drive a server's tools with no question asked.
/// </summary>
public sealed class ServerChatToolsGateTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly ServerChatTool Write = new("probe", "Probe", "write", "Writes.",
        JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone(), Asks: true);

    private static (AddinOwnedHost Owned, IServerChatTools Real) Owned(bool firstParty, bool developerUnsigned)
    {
        var real = Substitute.For<IServerChatTools>();
        real.ListAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<ServerChatTool>>([Write]));
        var host = Substitute.For<IHexIdeHost>();
        host.ServerChatTools.Returns(real);
        var origin = new AddinOrigin("com.example.addin", "Example", firstParty, developerUnsigned);
        return (new AddinOwnedHost(host, new AddinToolWindowService().ForOwner(origin), origin), real);
    }

    [Fact]
    public async Task AThirdPartyAddin_GetsNoTools_NoContext_AndCannotCall_WhateverItsCallbackSays()
    {
        var (owned, real) = Owned(firstParty: false, developerUnsigned: false);
        var asked = 0;

        var tools = await owned.ServerChatTools.ListAsync(Token);
        var context = await owned.ServerChatTools.GetContextAsync(Token);
        var result = await owned.ServerChatTools.CallAsync(Write, null, (_, _) => { asked++; return Task.FromResult(true); }, Token);

        tools.Should().BeEmpty();
        context.Should().BeEmpty();
        result.IsError.Should().BeTrue();
        asked.Should().Be(0);
        real.ReceivedCalls().Should().BeEmpty("nothing an add-in that isn't HexIDE's own does reaches the host's service");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void HexIdesOwnAddin_OrOneLoadedUnsignedInDeveloperMode_ReachesTheHostsService(bool firstParty, bool developerUnsigned)
    {
        var (owned, real) = Owned(firstParty, developerUnsigned);

        owned.ServerChatTools.Should().BeSameAs(real);
    }
}
