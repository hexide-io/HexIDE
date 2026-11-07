using System;
using System.Linq;
using System.Text.Json;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Integration.Tests.Views;

public class ExternalServerDocumentTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullUriIsShownAndOnlyYesLaunches(bool yes) => await CheckConsent(yes, false);

    [AvaloniaFact]
    public async Task EnterOnLinkQuestionDeclines() => await CheckConsent(false, true);

    private async Task CheckConsent(bool yes, bool enter)
    {
        const string uri = "https://example.com/a/long/path?full=query#fragment";
        var owner = new Window();
        owner.Show();
        try
        {
            var localization = new LocalizationService();
            localization.Apply("en");
            var launcher = Substitute.For<Func<Uri, CancellationToken, Task<bool>>>();
            launcher(Arg.Any<Uri>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));
            var queue = new ServerMessagePrompt(async (title, request, token, defaultAction) =>
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync<Task<HexIDE.Lsp.Messages.MessageActionItem?>?>(
                    () => ServerMessagePrompt.Show(owner, title, request, token, defaultAction)));
            var navigation = new ServerDocumentNavigation(queue, localization, launcher);
            var (local, remote) = FullDuplexStream.CreatePair();
            using var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(remote, remote, new SystemTextJsonFormatter()), new Server());
            rpc.StartListening();
            var transport = Substitute.For<ILspTransport>();
            transport.IsAlive.Returns(true);
            transport.LeaveServerRunning.Returns(true);
            transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
                .Returns(c => Task.FromResult<IJsonRpcMessageHandler?>(
                    new HeaderDelimitedMessageHandler(local, local, c.Arg<IJsonRpcMessageFormatter>())));
            await using var client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), "vb6",
                showDocument: (request, token) => navigation.ShowExternalAsync("Test server", request.Uri, token));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await client.StartAsync(timeout.Token);
            var pending = rpc.InvokeWithParameterObjectAsync<JsonElement>("window/showDocument",
                new HexIDE.Lsp.Messages.ShowDocumentParams(uri, External: true), timeout.Token);
            // Await dispatcher work while the incoming request reaches the shared prompt queue.
            while (owner.OwnedWindows.Count == 0 && !pending.IsCompleted)
                await Task.Delay(10, timeout.Token);
            var prompt = owner.OwnedWindows.Single();
            try
            {
                prompt.Title.Should().Be("Test server");
                if (!enter)
                    prompt.GetVisualDescendants().OfType<TextBlock>().Should()
                        .Contain(t => t.Text != null && t.Text.Contains(uri));
                await launcher.DidNotReceive()(Arg.Any<Uri>(), Arg.Any<CancellationToken>());
                if (enter)
                {
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    prompt.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                    owner.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                }
                else
                    prompt.GetVisualDescendants().OfType<Button>().Single(b => (string?)b.Content == localization.GetString(
                        yes ? "Str.Dialog.MessageBox.Button.Yes" : "Str.Dialog.MessageBox.Button.No"))
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                (await pending).GetProperty("success").GetBoolean().Should().Be(yes);
                await launcher.Received(yes ? 1 : 0)(Arg.Is<Uri>(u => u.OriginalString == uri), Arg.Any<CancellationToken>());
            }
            finally { prompt.Close(); }
        }
        finally { owner.Close(); }
    }

    private sealed class Server
    {
        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement p) => JsonDocument.Parse("""{"capabilities":{}}""").RootElement.Clone();
        [JsonRpcMethod("initialized", UseSingleObjectParameterDeserialization = true)]
        public void Initialized(JsonElement p) { }
    }
}
