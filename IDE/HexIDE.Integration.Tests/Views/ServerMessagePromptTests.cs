using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.IDE;
using HexIDE.Lsp.Messages;

namespace HexIDE.Integration.Tests.Views;

public class ServerMessagePromptTests
{
    [AvaloniaFact]
    public async Task EnterOnANewPromptAnswersTheFirstAction()
    {
        var owner = new Window();
        owner.Show();
        try
        {
            var first = new MessageActionItem("First");
            var pending = ServerMessagePrompt.Show(owner, "Server", new(3, "Choose", [first, new("Second")]), CancellationToken.None);
            var prompt = owner.OwnedWindows.Single();
            try
            {
                Dispatcher.UIThread.RunJobs();
                prompt.GetVisualDescendants().OfType<Button>().First().IsFocused.Should().BeTrue();
                prompt.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                owner.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                (await pending.WaitAsync(System.TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken))
                    .Should().BeSameAs(first);
            }
            finally { prompt.Close(); }
        }
        finally { owner.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1, "✕")]
    [InlineData(2, "!")]
    [InlineData(3, "i")]
    [InlineData(4, "i")]
    public async Task MessageTypeShowsItsIcon(int type, string symbol)
    {
        var owner = new Window();
        owner.Show();
        try
        {
            var pending = ServerMessagePrompt.Show(owner, "Server", new(type, "Message", []), CancellationToken.None);
            var prompt = owner.OwnedWindows.Single();
            try
            {
                prompt.GetVisualDescendants().OfType<TextBlock>().Should().Contain(t => t.Text == symbol);
            }
            finally { prompt.Close(); }
            await pending;
        }
        finally { owner.Close(); }
    }

    [AvaloniaFact]
    public async Task EscapeDismissesWithoutChoosingAnAction()
    {
        var owner = new Window();
        owner.Show();
        try
        {
            var pending = ServerMessagePrompt.Show(owner, "Server", new(3, "Message", [new("Choose")]), CancellationToken.None);
            var prompt = owner.OwnedWindows.Single();
            try
            {
                prompt.RaiseEvent(new Avalonia.Input.KeyEventArgs
                {
                    RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent,
                    Key = Avalonia.Input.Key.Escape
                });
                prompt.IsVisible.Should().BeFalse();
                (await pending).Should().BeNull();
            }
            finally { prompt.Close(); }
        }
        finally { owner.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServerTitleAndActionsAnswerTheOriginalItemOrDismiss(bool dismiss)
    {
        var owner = new Window();
        owner.Show();
        try
        {
            var first = new MessageActionItem("First");
            var pending = ServerMessagePrompt.Show(owner, "Test server", new(3, "Choose", [first, new("Second")]), CancellationToken.None);
            var prompt = owner.OwnedWindows.Single();
            prompt.Title.Should().Be("Test server");
            prompt.IsDialog.Should().BeTrue();
            ServerMessagePrompt.IsBlocked(false, [owner, prompt], false).Should().BeTrue();
            var buttons = prompt.GetVisualDescendants().OfType<Button>().ToArray();
            buttons.Select(b => b.Content).Should().Equal("First", "Second");
            if (dismiss) prompt.Close();
            else buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            (await pending).Should().BeSameAs(dismiss ? null : first);
        }
        finally { owner.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativePickerIsTrackedUntilItEnds(bool fails)
    {
        var manager = new WindowManager(Substitute.For<HexIDE.Localization.ILocalizationService>());
        var completion = new TaskCompletionSource<int>();
        var pending = manager.TrackNativeDialog(() => completion.Task);
        manager.IsNativeDialogOpen.Should().BeTrue();
        if (fails)
        {
            completion.SetException(new System.InvalidOperationException("Picker failed"));
            await ((System.Func<Task>)(async () => await pending)).Should().ThrowAsync<System.InvalidOperationException>();
        }
        else
        {
            completion.SetResult(42);
            (await pending).Should().Be(42);
        }
        manager.IsNativeDialogOpen.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task CancellationClosesTheOwnedPrompt()
    {
        var owner = new Window();
        owner.Show();
        using var cancellation = new CancellationTokenSource();
        try
        {
            var pending = ServerMessagePrompt.Show(owner, "Test server", new(3, "Choose", [new("First")]), cancellation.Token);
            var prompt = owner.OwnedWindows.Single();
            cancellation.Cancel();
            Dispatcher.UIThread.RunJobs();
            await ((System.Func<Task>)(async () => await pending)).Should().ThrowAsync<System.OperationCanceledException>();
            prompt.IsVisible.Should().BeFalse();
        }
        finally { owner.Close(); }
    }
}
