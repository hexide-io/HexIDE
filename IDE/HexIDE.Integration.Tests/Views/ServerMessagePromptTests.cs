using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.IDE;
using HexIDE.Lsp.Messages;

namespace HexIDE.Integration.Tests.Views;

public class ServerMessagePromptTests
{
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
