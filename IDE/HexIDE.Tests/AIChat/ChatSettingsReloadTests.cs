using System.IO;
using Avalonia.Controls;
using HexIDE.Addins;
using HexIDE.AIChat.Views;

namespace HexIDE.Tests.AIChat;

public class ChatSettingsReloadTests
{
    [AvaloniaTheory]
    [InlineData("edited", "updated.example", false)]
    [InlineData("edited", "updated.example", true)]
    [InlineData("deleted", "api.anthropic.com", false)]
    [InlineData("deleted", "api.anthropic.com", true)]
    [InlineData("invalid", "api.anthropic.com", false)]
    [InlineData("invalid", "api.anthropic.com", true)]
    public void OpeningTheExistingPanelReloadsSettings(string change, string expectedHost, bool responding)
    {
        var directory = Path.Join(Path.GetTempPath(), "hexide-chat-reload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Join(directory, "chat-settings.json");
            File.WriteAllText(path, """{"baseUrl":"https://original.example/v1"}""");
            var host = Substitute.For<IHexIdeHost>();
            var view = new ChatView(host, directory);
            var window = new Window { Content = view };
            window.Show();
            try
            {
                var chat = (ChatViewModel)view.DataContext!;
                chat.Say = "Keep this conversation";
                if (change == "deleted") File.Delete(path);
                else File.WriteAllText(path, change == "invalid" ? "{" : """{"baseUrl":"https://updated.example/v1"}""");

                chat.IsResponding = responding;
                window.Content = null;
                window.Content = view;
                chat.IsResponding = false;
                chat.MaybeShowEgressNotice();

                chat.Messages.Last().RawContent.Should().Contain(expectedHost);
                chat.Messages.First().RawContent.Should().Be("Keep this conversation");
                view.DataContext.Should().BeSameAs(chat);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
