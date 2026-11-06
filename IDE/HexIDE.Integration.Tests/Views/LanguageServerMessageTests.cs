using System;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;

namespace HexIDE.Integration.Tests.Views;

public class LanguageServerMessageTests
{
    [AvaloniaTheory]
    [InlineData(LspMessageType.Error, "Str.Lsp.ServerError")]
    [InlineData(LspMessageType.Warning, "Str.Lsp.ServerMessage")]
    [InlineData(LspMessageType.Info, "Str.Lsp.ServerMessage")]
    public void ServerMessageUsesTheSelectedLanguagesPrefix(LspMessageType type, string key)
    {
        var localization = new LocalizationService();
        localization.Apply("pseudo");
        var client = Substitute.For<ILspClient>();
        var status = new StatusBarService(localization);
        using var reporter = new LanguageServerMessageReporter(client, status, localization);
        client.MessageShown += Raise.Event<EventHandler<ShowMessageParams>>(client,
            new ShowMessageParams(type, "Server text"));
        var format = localization.GetString(key);
        format.Should().StartWith("⟦");
        status.Message.Should().Be(string.Format(format, "Server text"));
    }
}
