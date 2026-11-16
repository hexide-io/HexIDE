using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Automation;
using HexIDE.Conversations;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Redaction;
using HexIDE.Tools.LanguageServers;

namespace HexIDE.Integration.Tests.Views;

public class LanguageServersAccessKeyTests
{
    [AvaloniaTheory]
    [InlineData("en", "S", true)]
    [InlineData("de", "N", true)]
    [InlineData("en", "R", false)]
    public async Task Access_key_routes_to_restart_or_the_main_menu(string language, string key, bool restart)
    {
        await using var owned = new RealComposition();
        var composition = owned.Composition;
        var root = composition.Root;
        var localization = composition.LocalizationService;
        localization.Apply(language);
        root.ResetWindowLayout();
        var registry = Substitute.For<ILanguageConnectionRegistry>();
        registry.Connections.Returns([new LanguageServerConnection(
            "test", "Test server", LanguageConnectionKind.LanguageServer, LanguageConnectionState.Running,
            [".bas"], "vb6", null, LanguageConnectionTransport.Stdio, null, 0, DateTimeOffset.UtcNow, null)]);
        registry.ConfigurationProblems.Returns([]);
        var rows = new LanguageServersToolViewModel(registry, localization, new ConversationLog(),
            Substitute.For<IEventBus>(), new Pseudonymiser());
        root.LanguageServers.Groups.Clear();
        root.LanguageServers.Groups.Add(rows.Groups.Single());
        var view = new MainView { DataContext = root };
        view.DataTemplates.Add(new ViewLocator());
        var window = new Window { Content = view, Width = 1200, Height = 900 };
        try
        {
            window.Show();
            root.OpenLanguageServersCommand.Execute(null);
            for (var i = 0; i < 3; i++)
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var pane = view.GetVisualDescendants().OfType<LanguageServersToolView>().Single();
            var source = pane.GetVisualDescendants().OfType<TextBox>().Single();
            source.Focus().Should().BeTrue();
            UiAutomationDriver.PressKey(source, key, "Alt").Success.Should().BeTrue();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var menu = view.GetLogicalDescendants().OfType<Menu>().Single();
            var run = menu.Items.OfType<MenuItem>().Single(m => Equals(m.Header, localization.GetString("Str.Menu.Run")));
            if (restart)
            {
                await registry.Received(1).RestartAsync("test");
                run.IsSubMenuOpen.Should().BeFalse();
            }
            else
            {
                await registry.DidNotReceive().RestartAsync(Arg.Any<string>());
                run.IsSubMenuOpen.Should().BeTrue();
            }
        }
        finally { window.Close(); view.DataContext = null; localization.Apply("en"); }
    }
}
