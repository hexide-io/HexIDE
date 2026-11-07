using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.Conversations;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Redaction;
using HexIDE.Tools.ProtocolInspector;

namespace HexIDE.Integration.Tests.Views;

public class ProtocolInspectorTrafficTests
{
    [AvaloniaFact]
    public async Task VisibleInspectorReportsArrivalsWithoutMovingTheGrid()
    {
        await using var capture = new ConversationLog();
        var loc = Substitute.For<ILocalizationService>();
        loc.GetString(Arg.Any<string>()).Returns("{0}");
        var vm = new ProtocolInspectorToolViewModel(capture, loc, new Pseudonymiser(),
            Substitute.For<IWindowManager>());
        var view = new ProtocolInspectorToolView { DataContext = vm };
        var parent = new StackPanel { Children = { view } };
        var window = new Window { Content = parent };
        window.Show();
        try
        {
            capture.Record("vb6", ConversationDirection.Sent, ConversationEntryKind.Notification, "first", null, 1);
            await capture.DrainAsync();
            await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            vm.PendingCount.Should().Be(1);
            vm.HasPending.Should().BeTrue();
            vm.Rows.Should().BeEmpty();
            vm.Refresh();
            vm.PendingCount.Should().Be(0);
            vm.HasPending.Should().BeFalse();
            vm.Rows.Should().ContainSingle();

            parent.IsVisible = false;
            capture.Record("vb6", ConversationDirection.Sent, ConversationEntryKind.Notification, "hidden", null, 1);
            await capture.DrainAsync();
            await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            vm.PendingCount.Should().Be(0, "hidden views stop polling");
            parent.IsVisible = true;
            await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            vm.PendingCount.Should().Be(1, "showing the view resumes polling");

            window.Content = null;
            vm.Refresh();
            capture.Record("vb6", ConversationDirection.Sent, ConversationEntryKind.Notification, "detached", null, 1);
            await capture.DrainAsync();
            await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            vm.PendingCount.Should().Be(0, "detached views stop polling");
        }
        finally { window.Close(); }
    }
}
