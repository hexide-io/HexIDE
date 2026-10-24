using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HexIDE.Addins;
using HexIDE.AIChat.Views;

namespace HexIDE.Integration.Tests;

public class ChatStopTests
{
    [AvaloniaFact]
    public void StopIsAvailableOnlyWhileResponding()
    {
        var directory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        var window = new Window();
        window.Resources["Str.AIChat.Stop"] = "Stop";
        try
        {
            var view = new ChatView(Substitute.For<IHexIdeHost>(), directory);
            window.Content = view;
            window.Show();
            var chat = (ChatViewModel)view.DataContext!;
            var stop = view.GetVisualDescendants().OfType<Button>()
                .SingleOrDefault(b => b.Content?.ToString() == "Stop");
            stop.Should().NotBeNull();
            stop!.IsVisible.Should().BeFalse();
            // IsResponding is internal; the existing automation surface also exposes writable VM properties.
            typeof(ChatViewModel).GetProperty(nameof(ChatViewModel.IsResponding))!.SetValue(chat, true);
            stop.IsVisible.Should().BeTrue();
            stop.IsEnabled.Should().BeTrue();
            stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            typeof(ChatViewModel).GetProperty(nameof(ChatViewModel.IsResponding))!.SetValue(chat, false);
            stop.IsVisible.Should().BeFalse();
        }
        finally
        {
            window.Close();
            Directory.Delete(directory, true);
        }
    }
}
