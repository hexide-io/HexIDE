using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HexIDE.Conversations;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Redaction;
using HexIDE.Tools.LanguageServers;

namespace HexIDE.Integration.Tests.Views;

public class LanguageServersScrollTests
{
    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void ScrollingToEndShowsTheWholeRestartNote(double scaling)
    {
        var registry = Substitute.For<ILanguageConnectionRegistry>();
        registry.Connections.Returns([new LanguageServerConnection(
            "rdcore", "RDCore", LanguageConnectionKind.LanguageServer, LanguageConnectionState.Running,
            [".bas"], "vb6", null, LanguageConnectionTransport.Stdio, null, 0, DateTimeOffset.UtcNow, null)]);
        registry.ConfigurationProblems.Returns([new LanguageServerConfigProblem(
            "hexide.vb6", "'hexide.vb6' is switched off (\"enabled\": false) and was not registered.", true)]);
        var localization = new LocalizationService();
        var view = new LanguageServersToolView
        {
            DataContext = new LanguageServersToolViewModel(registry, localization, new ConversationLog(),
                Substitute.For<IEventBus>(), new Pseudonymiser())
        };
        var window = new Window { Content = view, Width = 920, Height = 520 };
        // Window resources keep the test independent of the application's currently selected language.
        foreach (var key in new[]
                 {
                     "Advertised", "AdvertisedNothing", "ArmFuture", "ArmFutureExplained", "Capture", "CaptureExplained",
                     "Claims", "CopyReport", "Declined", "Documents", "HowFar", "Id",
                     "KeepBodies", "None", "Output", "Priority", "Problems", "ReportRedacted",
                     "ReportedBy", "RestartNote", "SendingNothing", "ShowAllMessages", "ShowMessages", "Status",
                     "Transport", "WasCancelled", "WorkspaceRoot"
                 })
            window.Resources["Str.Tool.LanguageServers." + key] = localization.GetString("Str.Tool.LanguageServers." + key);
        window.SetRenderScaling(scaling);
        window.Show();
        try
        {
            window.RenderScaling.Should().Be(scaling);
            window.UpdateLayout();
            var scroll = (ScrollViewer)view.Content!;
            var presenter = scroll.GetVisualDescendants().OfType<ScrollContentPresenter>()
                .First(p => ReferenceEquals(p.Content, scroll.Content));
            var note = view.GetVisualDescendants().OfType<TextBlock>()
                .Single(t => t.Text == localization.GetString("Str.Tool.LanguageServers.RestartNote"));
            note.Bounds.Height.Should().BeGreaterThan(0);
            scroll.Extent.Height.Should().BeGreaterThan(scroll.Viewport.Height);
            scroll.ScrollToEnd();
            window.UpdateLayout();
            var bottom = note.TranslatePoint(new Point(0, note.Bounds.Height), presenter)!.Value.Y;
            var measurement = $"scale={scaling}: extent={scroll.Extent.Height}, viewport={scroll.Viewport.Height}, "
                + $"offset={scroll.Offset.Y}, presenter={presenter.Bounds.Height}, noteBottom={bottom}";
            Console.WriteLine(measurement);
            scroll.Offset.Y.Should().BeApproximately(scroll.Extent.Height - scroll.Viewport.Height, 0.01);
            bottom.Should().BeLessThanOrEqualTo(presenter.Bounds.Height, measurement);
        }
        finally { window.Close(); }
    }
}
