using HexIDE.Runtime.Serialization;
using HexIDE.Projects;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The code window's symbol refresh, read off the wire (#4162). Every diagnostics publish refreshes the symbols, and with
/// two servers publishing for one edit the highest-priority server was asked the same question two or three times, each
/// costing RDCore a second or more in a large module, with only the last answer used.
/// </summary>
public class SymbolRefreshWireTests
{
    [AvaloniaFact]
    public async Task TwoPublishesForOneTextAskForSymbolsOnce()
    {
        await using var f = await Fixture.Open();
        await f.Publish();
        await f.UntilAsync(() => f.Server.Requests.Count == 1);
        await f.Publish();
        await f.Quiet();
        f.Server.Requests.Should().HaveCount(1, "the request still waiting is for the same text");
        f.Server.Cancelled.Should().Be(0);
    }

    [AvaloniaFact]
    public async Task AnEditWhileARequestWaitsCancelsItAndAsksForTheNewText()
    {
        await using var f = await Fixture.Open();
        await f.Publish();
        await f.UntilAsync(() => f.Server.Requests.Count == 1);
        f.Vm.Document.Insert(f.Vm.Document.TextLength, "\n' edited");
        await f.Publish();
        await f.UntilAsync(() => f.Server.Requests.Count == 2 && f.Server.Cancelled == 1);
        await f.Quiet();
        f.Server.Requests.Should().HaveCount(2);
        f.Server.Cancelled.Should().Be(1, "$/cancelRequest reached the server for the older text's request");
        f.Server.Requests.Last().Text.Should().EndWith("' edited", "the edit was sent before the new request");
    }

    [AvaloniaFact]
    public async Task APublishAfterTheAnswerAsksAgain()
    {
        await using var f = await Fixture.Open();
        await f.Publish();
        await f.UntilAsync(() => f.Server.Requests.Count == 1);
        f.Server.Requests.First().Answer.TrySetResult(Symbols("Test"));
        await f.UntilAsync(() => f.Vm.OutlineSymbols.Count == 1);
        await f.Publish();
        await f.UntilAsync(() => f.Server.Requests.Count == 2);
        f.Server.Cancelled.Should().Be(0);
    }

    private static JsonElement Symbols(string name) => JsonSerializer.SerializeToElement(new[]
    {
        new
        {
            name, kind = 12,
            range = new { start = new { line = 1, character = 0 }, end = new { line = 2, character = 7 } },
            selectionRange = new { start = new { line = 1, character = 11 }, end = new { line = 1, character = 15 } }
        }
    });

    private sealed record SymbolRequest(string Text, TaskCompletionSource<JsonElement> Answer);

    private sealed class Server
    {
        public readonly ConcurrentQueue<SymbolRequest> Requests = new();
        private int cancelled;
        public int Cancelled => Volatile.Read(ref cancelled);
        public readonly TaskCompletionSource<string> Opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Text = "";

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement p) => JsonSerializer.SerializeToElement(new
        {
            capabilities = new { textDocumentSync = 1, documentSymbolProvider = true }
        });
        [JsonRpcMethod("initialized", UseSingleObjectParameterDeserialization = true)]
        public void Initialized(JsonElement p) { }
        [JsonRpcMethod("textDocument/didOpen", UseSingleObjectParameterDeserialization = true)]
        public void Open(JsonElement p)
        {
            Text = p.GetProperty("textDocument").GetProperty("text").GetString()!;
            Opened.TrySetResult(p.GetProperty("textDocument").GetProperty("uri").GetString()!);
        }
        [JsonRpcMethod("textDocument/didChange", UseSingleObjectParameterDeserialization = true)]
        public void Change(JsonElement p) => Text = p.GetProperty("contentChanges")[0].GetProperty("text").GetString()!;
        [JsonRpcMethod("textDocument/documentSymbol", UseSingleObjectParameterDeserialization = true)]
        public async Task<JsonElement> DocumentSymbol(JsonElement p, CancellationToken ct)
        {
            var request = new SymbolRequest(Text, new(TaskCreationOptions.RunContinuationsAsynchronously));
            Requests.Enqueue(request);
            using var registration = ct.Register(() =>
            {
                Interlocked.Increment(ref cancelled);
                request.Answer.TrySetCanceled();
            });
            return await request.Answer.Task;
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private static readonly TimeSpan HangTimeout = TimeSpan.FromSeconds(30);
        public required Server Server;
        public required VBLspClient Client;
        public required JsonRpc Rpc;
        public required CodeEditorViewModel Vm;
        public required string Uri;

        public static async Task<Fixture> Open()
        {
            var server = new Server();
            var (local, remote) = FullDuplexStream.CreatePair();
            var rpc = new JsonRpc(new LspMessageHandler(remote, remote, new SystemTextJsonFormatter()), server);
            rpc.StartListening();
            var transport = Substitute.For<ILspTransport>();
            transport.IsAlive.Returns(true);
            transport.LeaveServerRunning.Returns(true);
            transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
                .Returns(c => Task.FromResult<IJsonRpcMessageHandler?>(new LspMessageHandler(local, local, c.Arg<IJsonRpcMessageFormatter>())));
            var client = new VBLspClient(transport, Substitute.For<ILogger<VBLspClient>>(), "vb6");
            await client.StartAsync(TestContext.Current.CancellationToken);
            var localization = new LocalizationService();
            localization.Apply("en");
            var settings = Substitute.For<ISettingsService>();
            settings.TabWidth.Returns(4);
            var vm = new CodeEditorViewModel(Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(),
                Substitute.For<IProjectService>(), Substitute.For<IEventBus>(), client, settings,
                Substitute.For<IStatusBarService>(), new BookmarkService(), new BreakpointService(),
                Substitute.For<IDebugController>(), new RunScope(), localization);
            var module = new FormDefinition(new ProjectDefinition(VBProjectType.EXE, "P"),
                HexIDE.Runtime.Components.FormComponentClass.Instance, "Module1");
            module.AbsolutePath = Path.Combine(Path.GetTempPath(), "HexIDE-symbol-refresh", "Module1.frm");
            module.UpdateCode(string.Join("\n", "Attribute VB_Name = \"Module1\"", "Public Sub Test()", "End Sub"));
            vm.Initialize(module);
            var uri = await server.Opened.Task.WaitAsync(HangTimeout, TestContext.Current.CancellationToken);
            return new Fixture { Server = server, Client = client, Rpc = rpc, Vm = vm, Uri = uri };
        }

        /// <summary>The server publishes an empty set for the document, as either of two servers might after an edit.</summary>
        public async Task Publish()
        {
            await Rpc.NotifyWithParameterObjectAsync("textDocument/publishDiagnostics",
                new { uri = Uri, diagnostics = Array.Empty<object>() });
            Dispatcher.UIThread.RunJobs();
        }

        public async Task UntilAsync(Func<bool> condition)
        {
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (!condition() && deadline.Elapsed < HangTimeout)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            condition().Should().BeTrue();
        }

        /// <summary>Time for a request that shouldn't come to arrive if it were going to.</summary>
        public async Task Quiet()
        {
            for (var i = 0; i < 40; i++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var request in Server.Requests) request.Answer.TrySetResult(JsonSerializer.SerializeToElement(Array.Empty<object>()));
            Vm.Dispose();
            await Client.DisposeAsync();
            Rpc.Dispose();
        }
    }
}
