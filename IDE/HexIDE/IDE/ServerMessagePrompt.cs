using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using HexIDE.Lsp.Messages;

namespace HexIDE.IDE;

// Shared by all connections: server questions never stack over one another or over an IDE modal.
internal sealed class ServerMessagePrompt
{
    private readonly SemaphoreSlim _queue = new(1);
    private readonly Func<string, ShowMessageRequestParams, CancellationToken, int, Task<Task<MessageActionItem?>?>> _tryShow;

    internal ServerMessagePrompt(Func<string, ShowMessageRequestParams, CancellationToken, int, Task<Task<MessageActionItem?>?>> tryShow) => _tryShow = tryShow;

    public ServerMessagePrompt(Func<bool> isRunning) : this(async (title, request, token, defaultAction) =>
        await Dispatcher.UIThread.InvokeAsync<Task<MessageActionItem?>?>(() =>
        {
            token.ThrowIfCancellationRequested();
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
                || desktop.MainWindow is not { } owner)
                return Task.FromResult<MessageActionItem?>(null);
            if (IsBlocked(isRunning(), desktop.Windows, Static.MainView.PART_ModalHost.Children.Count > 0))
                return null;
            return Show(owner, title, request, token, defaultAction);
        })) { }

    internal static bool IsBlocked(bool running, System.Collections.Generic.IReadOnlyList<Window> windows, bool managedModal) =>
        running || managedModal || windows.Any(w => w.IsVisible && w.IsDialog);

    public Task<MessageActionItem?> ShowAsync(string title, ShowMessageRequestParams request, CancellationToken token) =>
        ShowAsync(title, request, token, 0);

    internal async Task<MessageActionItem?> ShowAsync(string title, ShowMessageRequestParams request, CancellationToken token, int defaultAction)
    {
        await _queue.WaitAsync(token);
        try
        {
            while (true)
            {
                var pending = await _tryShow(title, request, token, defaultAction);
                if (pending is not null) return await pending;
                await Task.Delay(50, token);
            }
        }
        finally { _queue.Release(); }
    }

    internal static async Task<MessageActionItem?> Show(Window owner, string title, ShowMessageRequestParams request, CancellationToken token, int defaultAction = 0)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var content = new StackPanel { Margin = new Thickness(12), Spacing = 12 };
        var message = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var icon = request.Type switch
        {
            1 => MessageBoxIcon.Error,
            2 => MessageBoxIcon.Warning,
            _ => MessageBoxIcon.Information
        };
        message.Children.Add(HexIDE.Dialogs.MessageBox.BuildIconElement(icon));
        message.Children.Add(new TextBlock { Text = request.Message, TextWrapping = TextWrapping.Wrap, MaxWidth = 480 });
        content.Children.Add(message);
        content.Children.Add(buttons);
        var window = new Window
        {
            Classes = { "ideDialog" },
            Title = title, Content = content, FlowDirection = owner.FlowDirection, SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false, CanMinimize = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        window.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            window.Close();
        };
        MessageActionItem? answer = null;
        foreach (var action in request.Actions ?? [])
        {
            var button = new Button { Content = action.Title, MinWidth = 75 };
            button.Click += (_, _) => { answer = action; window.Close(); };
            buttons.Children.Add(button);
        }
        window.Opened += (_, _) => buttons.Children.OfType<Button>().ElementAtOrDefault(defaultAction)?.Focus(NavigationMethod.Tab);
        using var cancellation = token.Register(() => Dispatcher.UIThread.Post(window.Close));
        token.ThrowIfCancellationRequested();
        await window.ShowDialog(owner);
        token.ThrowIfCancellationRequested();
        return answer;
    }
}
