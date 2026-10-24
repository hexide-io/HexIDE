using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using HexIDE.Addins;
using HexIDE.AIChat;
using HexIDE.AIChat.Views;

namespace HexIDE.Integration.Tests;

public class ChatPartialCancellationTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopFinalisesPartialTextOrPendingApprovals(bool approval)
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
            var chat = new ChatViewModel(Substitute.For<IHexIdeHost>(), directory) { InputText = "hello" };
            var send = chat.SendAsync();
            using var connection = await listener.AcceptTcpClientAsync(timeout.Token);
            await using var stream = connection.GetStream();
            using var reader = new StreamReader(stream, leaveOpen: true);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(timeout.Token))) { }
            await stream.WriteAsync(Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nConnection: close\r\n\r\n"), timeout.Token);
            var data = approval
                ? "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"c1\",\"function\":{\"name\":\"apply_edit\",\"arguments\":\"{}\"}},{\"index\":1,\"id\":\"c2\",\"function\":{\"name\":\"get_active_file\",\"arguments\":\"{}\"}}]}}]}\n\ndata: [DONE]\n\n"
                : "data: {\"choices\":[{\"delta\":{\"content\":\"partial reply\"}}]}\n\n";
            await stream.WriteAsync(Encoding.UTF8.GetBytes(data), timeout.Token);
            while (!chat.Messages.Any(m => approval ? m.IsAwaitingDecision : m.RawContent == "partial reply"))
                await Task.Delay(10, timeout.Token);
            chat.IsResponding.Should().BeTrue();

            // Reflection lets this regression run against the original panel, which has no Stop method.
            typeof(ChatViewModel).GetMethod("Stop")?.Invoke(chat, null);
            await send.WaitAsync(TimeSpan.FromSeconds(3), timeout.Token);

            chat.IsResponding.Should().BeFalse();
            chat.Messages.Should().NotContain(m => m.IsStreaming || m.IsError);
            if (approval)
            {
                chat.Messages.Should().NotContain(m => m.IsAwaitingDecision);
                var log = ChatSession.LoadFrom(Path.Combine(directory, "chat-history.json")).Log;
                log.Where(m => m.Role == "tool").Should().HaveCount(2);
                log.Last().Content.Should().Contain("Cancelled");
            }
            else
            {
                var bubble = chat.Messages.Single(m => m.RawContent == "partial reply");
                bubble.Parts.Should().NotBeEmpty();
                ChatSession.LoadFrom(Path.Combine(directory, "chat-history.json")).Log.Last().Content.Should().Be("partial reply");
            }
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
