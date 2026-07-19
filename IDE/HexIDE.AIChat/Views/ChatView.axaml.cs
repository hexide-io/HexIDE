using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HexIDE.Addins;

namespace HexIDE.AIChat.Views;

public partial class ChatView : UserControl
{
    private ChatViewModel? _vm;

    public ChatView(IHexIdeHost host, string settingsDir)
    {
        InitializeComponent();
        _vm = new ChatViewModel(host, settingsDir);
        DataContext = _vm;
        _vm.Messages.CollectionChanged += (_, _) => ScrollToBottom();
    }

    private void ScrollToBottom()
    {
        Scroller.ScrollToEnd();
    }

    private void OnSendClicked(object? sender, RoutedEventArgs e) =>
        _vm?.SendAsync().ConfigureAwait(false);

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            _vm?.SendAsync().ConfigureAwait(false);
        }
    }

    private void OnNewChatClicked(object? sender, RoutedEventArgs e) =>
        _vm?.NewChat();

    private void OnApplyClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string code)
            _vm?.ApplyToActiveFileAsync(code).ConfigureAwait(false);
    }

    private void OnApproveClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ChatMessageViewModel vm }) vm.Approve();
    }

    private void OnRejectClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ChatMessageViewModel vm }) vm.Reject();
    }
}
