using Avalonia.Controls;
using Avalonia.Labs.Input;
using HexIDE.Automation;
using HexIDE.Desktop.Server;

namespace HexIDE.Tests.Infrastructure;

public class MenuCommandRefusalTests
{
    private const string FocusReply = "'File/Make Project Group...' cannot execute where keyboard focus is now (nothing has focus). It is a routed "
        + "command, which acts on the focused control as a real click does, so an item that belongs to a "
        + "document needs focus in that document: open_file, view_designer or activate_document_tab give it, "
        + "and so does press_key on it. ";

    [AvaloniaFact]
    public void A_disabled_materialised_routed_item_names_the_disabled_state_without_focus_advice()
    {
        var item = new MenuItem { Header = "Make Project Group...", IsEnabled = false };
        var found = new MenuPath.Result(item, null, Command: new RoutedCommand("MakeGroup"));

        MenuCommandRefusal.Describe(found, "File/Make Project Group...", () => throw new InvalidOperationException("Focus must not be read"))
            .Should().Be("'File/Make Project Group...' cannot execute now: the item is disabled.");
    }

    [AvaloniaFact]
    public void An_enabled_materialised_routed_item_keeps_the_focus_explanation()
    {
        var item = new MenuItem { Header = "Make Project Group..." };
        item.IsEffectivelyEnabled.Should().BeTrue();
        var found = new MenuPath.Result(item, null, Command: new RoutedCommand("MakeGroup"));

        MenuCommandRefusal.Describe(found, "File/Make Project Group...", () => "nothing has focus")
            .Should().Be(FocusReply + "An item that is disabled in the menu is refused the same way.");
    }

    [AvaloniaFact]
    public void An_unmaterialised_routed_item_says_the_menu_was_not_read_and_how_to_check()
    {
        var entry = new Entry("Make Project Group...", new RoutedCommand("MakeGroup"));
        var found = MenuPath.Resolve(new[] { new MenuItem { Header = "File", ItemsSource = new[] { entry } } },
            "File/Make Project Group...");
        found.Item.Should().BeNull();
        found.Command.Should().BeSameAs(entry.Command);

        MenuCommandRefusal.Describe(found, "File/Make Project Group...", () => "nothing has focus")
            .Should().Be(FocusReply + "The menu wasn't read, so whether the item is disabled is unknown. Opening the menu (or dump_visual_tree on it) would tell.");
    }

    public sealed record Entry(string Header, RoutedCommand Command);

    [Fact]
    public void Invoke_menu_item_uses_the_tested_refusal_when_CanExecute_is_false()
    {
        ToolSource.Tools.Single(t => t.Name == "invoke_menu_item").Body.Should().Contain(
            "if (!command.CanExecute(found.CommandParameter))\n                return new MutateResult(false,\n                    MenuCommandRefusal.Describe(found, path, () => FocusedElementName(window)))");
    }
}
