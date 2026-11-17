using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Forms.Views;
using HexIDE.Tools;

namespace HexIDE.Integration.Tests.Views;

public class IdeOptionGroupTests
{
    [AvaloniaTheory]
    [InlineData("ProcedureType", 0)]
    [InlineData("ProcedureScope", 4)]
    [InlineData("Watch", 0)]
    [InlineData("Find", 0)]
    [InlineData("Project", 0)]
    [InlineData("Hierarchy", 0)]
    public void Each_group_enters_at_selection_and_arrows_select(string kind, int firstIndex)
    {
        UserControl view = kind switch
        {
            "ProcedureType" or "ProcedureScope" => new AddProcedureView(),
            "Watch" => new AddWatchDialog(),
            "Find" => new FindReplaceView(),
            "Project" => new ProjectPropertiesView(),
            _ => new CallHierarchyToolView(),
        };
        var window = new Window { Content = new StackPanel { Children = { new TextBox(), view, new TextBox() } } };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var first = view.GetVisualDescendants().OfType<RadioButton>().ElementAt(firstIndex);
            // Threading is not implemented yet: exercise its navigation without enabling it in the IDE.
            foreach (var ancestor in first.GetVisualAncestors().OfType<Control>())
                ancestor.IsEnabled = true;
            var group = first.GetVisualParent()!.GetVisualChildren().OfType<RadioButton>().ToArray();
            foreach (var option in group)
                option.IsEnabled = true;
            group[^1].IsChecked = true;
            first.Focus();
            Press(window, Key.Right);
            window.FocusManager!.GetFocusedElement().Should().BeSameAs(group[1]);
            group[1].IsChecked.Should().BeTrue();
            group[0].IsChecked.Should().BeFalse();
            group[1].Focus();
            Press(window, Key.Tab);
            group.Should().NotContain(option => ReferenceEquals(option, window.FocusManager.GetFocusedElement()));
            Press(window, Key.Tab, RawInputModifiers.Shift);
            window.FocusManager.GetFocusedElement().Should().BeSameAs(group[1]);
        }
        finally { window.Close(); }
    }

    private static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, Physical(key), null);
        window.KeyRelease(key, modifiers, Physical(key), null);
        Dispatcher.UIThread.RunJobs();
    }
    private static PhysicalKey Physical(Key key) => key switch
    {
        Key.Tab => PhysicalKey.Tab,
        Key.Left => PhysicalKey.ArrowLeft,
        Key.Right => PhysicalKey.ArrowRight,
        Key.Up => PhysicalKey.ArrowUp,
        Key.Down => PhysicalKey.ArrowDown,
        _ => PhysicalKey.None,
    };
}
