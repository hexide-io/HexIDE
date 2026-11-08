using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using HexIDE.Addins;
using HexIDE.AIChat;
using HexIDE.AIChat.Views;

namespace HexIDE.Tests.AIChat;

public class ChatCancellationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopCancelsAWaitingStreamAndAllowsAnotherMessage(bool compact)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "chat-settings.json"),
                $$"""{"baseUrl":"http://127.0.0.1:{{((IPEndPoint)listener.LocalEndpoint).Port}}","maxTurnsBeforeCompaction":2}""");
            if (compact)
            {
                var session = new ChatSession();
                for (var i = 0; i < 6; i++) session.Add(new ConversationMessage("user", "earlier"));
                session.SaveTo(Path.Combine(directory, "chat-history.json"));
            }
            var chat = new ChatViewModel(Substitute.For<IHexIdeHost>(), directory) { InputText = "hello" };
            var send = chat.SendAsync();
            using var connection = await listener.AcceptTcpClientAsync(timeout.Token);
            await using var stream = connection.GetStream();
            using var reader = new StreamReader(stream, leaveOpen: true);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(timeout.Token))) { }
            await stream.WriteAsync(Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nConnection: close\r\n\r\n"), timeout.Token);
            chat.IsResponding.Should().BeTrue();

            // Reflection lets this regression run against the original panel, which has no Stop method.
            typeof(ChatViewModel).GetMethod("Stop")?.Invoke(chat, null);
            await send.WaitAsync(TimeSpan.FromSeconds(3), timeout.Token);

            chat.IsResponding.Should().BeFalse();
            chat.Messages.Should().NotContain(m => m.IsStreaming || m.IsError);
            chat.InputText = "again";
            var next = chat.SendAsync();
            using var nextConnection = await listener.AcceptTcpClientAsync(timeout.Token);
            typeof(ChatViewModel).GetMethod("Stop")?.Invoke(chat, null);
            await next.WaitAsync(TimeSpan.FromSeconds(3), timeout.Token);
            chat.IsResponding.Should().BeFalse();
        }
        finally { Directory.Delete(directory, true); }
    }
}
