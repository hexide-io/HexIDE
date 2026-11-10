using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace HexIDE.Integration.Tests.Views;

public class OnTypeFormattingTests
{
    [AvaloniaTheory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task EnterSendsSyncedTextAndMapsCaretWithoutLocalCorrection(string newline)
    {
        await using var f = await Fixture.Open(newline);
        f.Enter();
        var request = await f.Request();
        request.GetProperty("ch").GetString().Should().Be("\n");
        request.GetProperty("textDocument").GetProperty("uri").GetString().Should().Be(f.Vm.GetDocumentUriPublic());
        var expectedPosition = f.Vm.Document.GetLocation(f.Start + newline.Length + 4);
        request.GetProperty("position").GetProperty("line").GetInt32().Should().Be(expectedPosition.Line - 1);
        request.GetProperty("position").GetProperty("character").GetInt32().Should().Be(4);
        request.GetProperty("options").GetProperty("insertSpaces").GetBoolean().Should().BeTrue();
        request.GetProperty("options").GetProperty("tabSize").GetInt32().Should().Be(4);
        f.Server.TextAtRequest.Should().Be(f.Before.Insert(f.Start, newline + "    "));
        f.Answer(request);
        await f.Formatted();
        f.Vm.Document.Text.Should().Be(f.Before.Insert(f.Start, newline + "        " + newline + "    End If"));
        f.Editor.CaretOffset.Should().Be(f.Start + newline.Length + 8);
    }

    [AvaloniaFact]
    public async Task LineBreakAndServerEditsUndoAndRedoTogether()
    {
        await using var f = await Fixture.Open();
        f.Enter();
        f.Answer(await f.Request());
        await f.Formatted();
        var formatted = f.Vm.Document.Text;
        f.Vm.Document.UndoStack.Undo();
        f.Vm.Document.Text.Should().Be(f.Before);
        f.Vm.Document.UndoStack.Redo();
        f.Vm.Document.Text.Should().Be(formatted);
    }

    [AvaloniaFact]
    public async Task TimeoutCancelsAndKeepsOnlyTheTypedLineBreak()
    {
        await using var f = await Fixture.Open();
        f.Enter();
        await f.Request();
        await f.Server.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        f.Vm.Document.Text.Should().Be(f.Before.Insert(f.Start, "\n    "));
        f.Answer(f.Server.Request.Task.Result);
        await f.Settled();
        f.Vm.Document.Text.Should().Be(f.Before.Insert(f.Start, "\n    "));
    }

    [AvaloniaFact]
    public async Task ReplyAfterAnotherEditIsDiscardedEvenWhenTextIsRestored()
    {
        await using var f = await Fixture.Open();
        f.Enter();
        var request = await f.Request();
        var offset = f.Editor.CaretOffset;
        f.Vm.Document.Insert(offset, "Z");
        f.Vm.Document.Remove(offset, 1);
        var typed = f.Vm.Document.Text;
        f.Answer(request);
        await f.Settled();
        f.Vm.Document.Text.Should().Be(typed);
        f.Vm.Document.UndoStack.Undo();
        f.Vm.Document.Text.Should().Contain("Z");
    }

    [AvaloniaFact]
    public async Task UndoBeforeReplyDiscardsIt()
    {
        await using var f = await Fixture.Open();
        f.Enter();
        var request = await f.Request();
        f.Vm.Document.UndoStack.Undo();
        f.Answer(request);
        await f.Settled();
        f.Vm.Document.Text.Should().Be(f.Before);
    }

    [AvaloniaTheory]
    [InlineData("\"")]
    [InlineData("(")]
    public async Task OtherClaimedCharactersSendPostTypingPosition(string trigger)
    {
        await using var f = await Fixture.Open(trigger: trigger);
        f.Window.KeyTextInput(trigger);
        var request = await f.Request();
        request.GetProperty("ch").GetString().Should().Be(trigger);
        f.Server.TextAtRequest.Should().Be(f.Before.Insert(f.Start, trigger));
        var p = request.GetProperty("position");
        f.Server.Answer.TrySetResult(JsonSerializer.SerializeToElement(new[]
        {
            new TextEdit(new(new(p.GetProperty("line").GetInt32(), p.GetProperty("character").GetInt32()),
                new(p.GetProperty("line").GetInt32(), p.GetProperty("character").GetInt32())), "server")
        }));
        await f.WaitUntil(() => f.Vm.Document.Text.Contains("server", StringComparison.Ordinal));
        f.Editor.CaretOffset.Should().Be(f.Start + trigger.Length + 6);
        f.Vm.Document.UndoStack.Undo();
        f.Vm.Document.Text.Should().Be(f.Before);
    }

    [AvaloniaFact]
    public async Task EnterReplacesSelectionAndUndoesItWithTheReply()
    {
        await using var f = await Fixture.Open();
        f.Vm.Document.Insert(f.Start, "remove");
        var selected = f.Vm.Document.Text;
        f.Editor.Select(f.Start, 6);
        f.Vm.Document.UndoStack.ClearAll();
        f.Enter();
        var request = await f.Request();
        f.Server.TextAtRequest.Should().Be(f.Before.Insert(f.Start, "\n    "));
        f.Answer(request);
        await f.Formatted();
        f.Vm.Document.UndoStack.Undo();
        f.Vm.Document.Text.Should().Be(selected);
    }

    [AvaloniaFact]
    public async Task ReplacementBeforeCaretMapsItsNewLength()
    {
        await using var f = await Fixture.Open();
        f.Enter();
        var request = await f.Request();
        var line = request.GetProperty("position").GetProperty("line").GetInt32();
        f.Server.Answer.TrySetResult(JsonSerializer.SerializeToElement(new[]
        {
            new TextEdit(new(new(line, 0), new(line, 4)), "  ")
        }));
        await f.Settled();
        f.Editor.CaretOffset.Should().Be(f.Start + 3);
        f.Vm.Document.Text.Should().Be(f.Before.Insert(f.Start, "\n  "));
    }

    [AvaloniaFact]
    public async Task ProtectedServerEditsAreSkippedWhileCodeIsFormatted()
    {
        await using var f = await Fixture.Open();
        f.Enter();
        var request = await f.Request();
        var line = request.GetProperty("position").GetProperty("line").GetInt32();
        f.Server.Answer.TrySetResult(JsonSerializer.SerializeToElement(new[]
        {
            new TextEdit(new(new(0, 0), new(0, 1)), "X"),
            new TextEdit(new(new(line, 4), new(line, 4)), "    ")
        }));
        await f.Settled();
        f.Vm.Document.Text.Should().Be(f.Before.Insert(f.Start, "\n        "));
        f.Editor.CaretOffset.Should().Be(f.Start + 9);
    }

    [AvaloniaFact]
    public async Task DetachedViewDiscardsItsReply()
    {
        await using var f = await Fixture.Open();
        f.Enter();
        var request = await f.Request();
        var typed = f.Vm.Document.Text;
        f.Window.Close();
        f.Answer(request);
        await f.Settled();
        f.Vm.Document.Text.Should().Be(typed);
    }

    [AvaloniaFact]
    public async Task ServerWithoutNewlineTriggerKeepsLocalEnter()
    {
        await using var f = await Fixture.Open(trigger: ";");
        f.Enter();
        f.Vm.Document.Text.Should().Contain("    If True Then\n        \n    End If\nEnd Sub");
        await Task.Delay(80, TestContext.Current.CancellationToken);
        f.Server.Request.Task.IsCompleted.Should().BeFalse();
    }

    [AvaloniaTheory]
    [InlineData(0, false, false)]
    [InlineData(0, true, false)]
    [InlineData(1, false, false)]
    [InlineData(1, true, false)]
    [InlineData(2, false, false)]
    [InlineData(2, true, false)]
    [InlineData(3, false, false)]
    [InlineData(3, true, false)]
    [InlineData(0, false, true)]
    [InlineData(0, true, true)]
    [InlineData(1, false, true)]
    [InlineData(1, true, true)]
    [InlineData(2, false, true)]
    [InlineData(2, true, true)]
    [InlineData(3, false, true)]
    [InlineData(3, true, true)]
    public async Task BlockReplyKeepsCaretInBody(int shape, bool lastLine, bool protectedEdit)
    {
        await using var f = await Fixture.Open(lastLine: lastLine);
        f.Enter();
        var request = await f.Request();
        var line = request.GetProperty("position").GetProperty("line").GetInt32();
        TextEdit[] edits = shape switch
        {
            0 => [new(new(new(line, 4), new(line, 4)), "    \n    End If")],
            1 => [new(new(new(line, 0), new(line, 4)), "        "),
                new(new(new(line, 4), new(line, 4)), "\n    End If")],
            2 => [new(new(new(line, 0), new(line, 4)), "        \n    End If")],
            _ => [new(new(new(line - 1, 0), new(line, 4)), "    If True Then\n        \n    End If")]
        };
        if (protectedEdit)
            edits = [new(new(new(0, 0), new(0, 1)), "X"), .. edits];
        f.Server.Answer.TrySetResult(JsonSerializer.SerializeToElement(edits));
        await f.Settled();
        f.Vm.Document.GetText(f.Editor.CaretOffset - 8, 8).Should().Be("        ");
        f.Vm.Document.GetCharAt(f.Editor.CaretOffset).Should().Be('\n');
        f.Vm.Document.GetLocation(f.Editor.CaretOffset).Line.Should().Be(line + 1);
        var expected = f.Before.Insert(f.Start, "\n        \n    End If");
        if (shape == 3) expected = expected.Replace("    if True then", "    If True Then", StringComparison.Ordinal);
        f.Vm.Document.Text.Should().Be(expected);
    }

    [AvaloniaFact]
    public async Task SamePositionInsertionsKeepServerArrayOrder()
    {
        await using var f = await Fixture.Open(lastLine: true);
        f.Enter();
        var request = await f.Request();
        var line = request.GetProperty("position").GetProperty("line").GetInt32();
        f.Server.Answer.TrySetResult(JsonSerializer.SerializeToElement(new TextEdit[]
        {
            new(new(new(line, 4), new(line, 4)), "    "),
            new(new(new(line, 4), new(line, 4)), "\n    End If")
        }));
        await f.Settled();
        f.Vm.Document.Text.Should().Be(f.Before + "\n        \n    End If");
        f.Editor.CaretOffset.Should().Be(f.Start + 9);
    }

    private sealed class Server(string trigger)
    {
        public readonly TaskCompletionSource<JsonElement> Request = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<JsonElement> Answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Text = "";
        public string? TextAtRequest;

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement p) => JsonSerializer.SerializeToElement(new
        {
            capabilities = new { textDocumentSync = 1, documentOnTypeFormattingProvider = new { firstTriggerCharacter = trigger } }
        });
        [JsonRpcMethod("initialized", UseSingleObjectParameterDeserialization = true)]
        public void Initialized(JsonElement p) { }
        [JsonRpcMethod("textDocument/didOpen", UseSingleObjectParameterDeserialization = true)]
        public void Open(JsonElement p) => Text = p.GetProperty("textDocument").GetProperty("text").GetString()!;
        [JsonRpcMethod("textDocument/didChange", UseSingleObjectParameterDeserialization = true)]
        public void Change(JsonElement p) => Text = p.GetProperty("contentChanges")[0].GetProperty("text").GetString()!;
        [JsonRpcMethod("textDocument/onTypeFormatting", UseSingleObjectParameterDeserialization = true)]
        public async Task<JsonElement> Format(JsonElement p, CancellationToken ct)
        {
            using var registration = ct.Register(() => Cancelled.TrySetResult());
            TextAtRequest = Text;
            Request.TrySetResult(p.Clone());
            return await Answer.Task;
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required Server Server;
        public required VBLspClient Client;
        public required JsonRpc Rpc;
        public required CodeEditorViewModel Vm;
        public required Window Window;
        public required TextEditor Editor;
        public required CodeEditorView View;
        public required string Before;
        public required string Newline;
        public int Start;

        public static async Task<Fixture> Open(string newline = "\n", string trigger = "\n", bool lastLine = false)
        {
            var server = new Server(trigger);
            var (local, remote) = FullDuplexStream.CreatePair();
            var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(remote, remote, new SystemTextJsonFormatter()), server);
            rpc.StartListening();
            var transport = Substitute.For<ILspTransport>();
            transport.IsAlive.Returns(true);
            transport.LeaveServerRunning.Returns(true);
            transport.ConnectAsync(Arg.Any<IJsonRpcMessageFormatter>(), Arg.Any<CancellationToken>())
                .Returns(c => Task.FromResult<IJsonRpcMessageHandler?>(new HeaderDelimitedMessageHandler(local, local, c.Arg<IJsonRpcMessageFormatter>())));
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
            var module = new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "Module1", ModuleKind.StandardModule);
            module.AbsolutePath = Path.Combine(Path.GetTempPath(), "HexIDE-on-type", "Module1.bas");
            module.RecordOriginalHeader("");
            module.UpdateCode(string.Join(newline, "Attribute VB_Name = \"Module1\"", "Public Sub Test()", "    if True then", "End Sub"));
            if (lastLine)
                module.UpdateCode(string.Join(newline, "Attribute VB_Name = \"Module1\"", "Public Sub Test()", "    if True then"));
            vm.Initialize(module);
            var view = new CodeEditorView { DataContext = vm };
            var window = new Window { Content = view, Width = 900, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var editor = view.FindControl<TextEditor>("TextEditor")!;
            var start = vm.Document.Text.IndexOf("then", StringComparison.Ordinal) + 4;
            editor.CaretOffset = start;
            editor.TextArea.Focus();
            vm.Document.UndoStack.ClearAll();
            return new Fixture { Server = server, Client = client, Rpc = rpc, Vm = vm, Window = window,
                Editor = editor, View = view, Before = vm.Document.Text, Newline = newline, Start = start };
        }

        public void Enter()
        {
            Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        }
        public Task<JsonElement> Request() => Server.Request.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        public void Answer(JsonElement request)
        {
            var line = request.GetProperty("position").GetProperty("line").GetInt32();
            Server.Answer.TrySetResult(JsonSerializer.SerializeToElement(new[]
            {
                new TextEdit(new(new(line, 4), new(line, 4)), "    "),
                new TextEdit(new(new(line + 1, 0), new(line + 1, 0)), "    End If" + Newline)
            }));
        }
        public async Task Settled()
        {
            // Reflection keeps this regression runnable against main, before the editor seam exists.
            if (typeof(CodeEditorView).GetProperty("PendingOnTypeFormatting", System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic)?.GetValue(View) is Task pending)
                await pending.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        }
        public async Task Formatted()
        {
            await Settled();
            Vm.Document.Text.Should().Contain("End If");
        }
        public async Task WaitUntil(Func<bool> predicate)
        {
            for (var i = 0; i < 150 && !predicate(); i++)
                await Task.Delay(10, TestContext.Current.CancellationToken);
            predicate().Should().BeTrue();
        }
        public async ValueTask DisposeAsync()
        {
            Server.Answer.TrySetResult(JsonSerializer.SerializeToElement(Array.Empty<TextEdit>()));
            Window.Close();
            Vm.Dispose();
            await Client.DisposeAsync();
            Rpc.Dispose();
        }
    }
}
