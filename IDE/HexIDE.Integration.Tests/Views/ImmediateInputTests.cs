using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using AvaloniaEdit;
using HexIDE.Localization;
using HexIDE.Runtime.Debugging;
using HexIDE.Tools;

namespace HexIDE.Integration.Tests.Views;

public class ImmediateInputTests
{
    [AvaloniaFact]
    public async Task Pending_enter_is_not_resubmitted_and_reply_survives_replaced_document_lines()
    {
        var interpreter = Substitute.For<IDebugController>();
        interpreter.State.Returns(DebugState.Paused);
        var reply = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        interpreter.EvaluateAsync("? answer").Returns(reply.Task);
        var vm = new ImmediateToolViewModel(Substitute.For<ILocalizationService>(), interpreter);
        var view = new ImmediateToolView { DataContext = vm };
        var window = new Window { Content = view, Width = 500, Height = 250 };
        window.Show();
        try
        {
            var editor = view.FindControl<TextEditor>("Editor")!;
            vm.Document.Text = "? answer";
            editor.CaretOffset = vm.Document.TextLength;
            void Enter() => editor.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter,
                KeyModifiers = KeyModifiers.None,
            });
            Enter();
            await interpreter.Received(1).EvaluateAsync("? answer");
            // Simulate clearing/editing the console while adapter output arrives. The original
            // DocumentLine no longer exists, so completion must not use its EndOffset.
            vm.Document.Text = "adapter output\nsecond command";
            editor.CaretOffset = vm.Document.TextLength;
            Enter();
            await interpreter.DidNotReceive().EvaluateAsync("second command");
            var printed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            vm.Document.Changed += (_, _) =>
            {
                if (vm.Document.Text.EndsWith("42\n", StringComparison.Ordinal)) printed.TrySetResult();
            };
            reply.SetResult("42");
            await printed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            vm.Document.Text.Should().Be("adapter output\nsecond command\n42\n");
        }
        finally { window.Close(); }
    }
}
