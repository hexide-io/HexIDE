using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Labs.Input;
using HexIDE.Keymaps;
using HexIDE.Menus;
using HexIDE.Utils;

namespace HexIDE.Tests.Infrastructure;

public class MenuGestureTests
{
    [AvaloniaTheory]
    [InlineData("StepOverCommand")]
    [InlineData("StepOutCommand")]
    [InlineData("RunToCursorCommand")]
    [InlineData("GoToDeclarationCommand")]
    public void Existing_items_follow_keymap_switches_in_both_directions(string name)
    {
        RuntimeHelpers.RunClassConstructor(typeof(MenuUtils).TypeHandle);
        var service = new KeymapService();
        var command = CommandKeyMapping.Table[name];
        service.Apply("Default");
        var factoryGesture = command.Gestures.FirstOrDefault();
        var item = new MenuItem { Command = command };
        item.InputGesture.Should().Be(factoryGesture);
        try
        {
            service.Apply("VB6");
            item.InputGesture.Should().Be(command.Gestures[0]);
            service.Apply("Default");
            item.InputGesture.Should().Be(factoryGesture);
        }
        finally { service.Apply("Default"); }
    }

    [AvaloniaFact]
    public void Replacing_or_removing_a_command_clears_its_previous_gesture()
    {
        RuntimeHelpers.RunClassConstructor(typeof(MenuUtils).TypeHandle);
        var item = new MenuItem { Command = ApplicationCommands.ToggleBookmarkCommand };
        item.InputGesture.Should().Be(ApplicationCommands.ToggleBookmarkCommand.Gestures[0]);
        item.Command = new RoutedCommand("Unbound");
        item.InputGesture.Should().BeNull();
        item.Command = ApplicationCommands.ToggleBookmarkCommand;
        item.Command = null;
        item.InputGesture.Should().BeNull();
    }

    [Theory]
    [InlineData("MainView.axaml", "ToggleBookmarkCommand")]
    [InlineData("MainView.axaml", "PreviousBookmarkCommand")]
    [InlineData("MainView.axaml", "ClearAllBookmarksCommand")]
    [InlineData("Forms/Views/CodeEditorView.axaml", "ToggleBookmarkCommand")]
    [InlineData("Forms/Views/CodeEditorView.axaml", "PreviousBookmarkCommand")]
    [InlineData("Forms/Views/CodeEditorView.axaml", "ClearAllBookmarksCommand")]
    public void Bookmark_placements_take_their_gesture_from_the_command(string file, string command)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "IDE", "HexIDE")))
            root = root.Parent;
        root.Should().NotBeNull();
        if (file == "MainView.axaml")
        {
            IEnumerable<MenuDeclaration> Walk(IEnumerable<MenuDeclaration> items) =>
                items.SelectMany(i => new[] { i }.Concat(Walk(i.Children ?? [])));
            Walk(MainMenuDeclaration.Load()).Should().ContainSingle(i => i.Command == "application." + command);
            return;
        }
        var document = XDocument.Load(Path.Combine(root!.FullName, "IDE", "HexIDE", file));
        var items = document.Descendants().Where(e => e.Name.LocalName == "MenuItem"
            && ((string?)e.Attribute("Command"))?.Contains("." + command + "}") == true).ToList();
        items.Should().ContainSingle();
        items[0].Attribute("InputGesture").Should().BeNull();
    }
}
