using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// inspect_element's selectionItems names a selected item that has no row by the name its row would carry, never by its
/// type (#2026). The Properties list, too short at the IDE's default size to show a row, answered
/// "HexIDE.VisualDesigner.PropertyViewModel".
/// </summary>
public class UnrealizedSelectionTests
{
    // As the IDE's view models are: a name for the row to bind, and no ToString of its own.
    public sealed class Row(string name)
    {
        public string AccessibleName { get; } = name;
    }

    public sealed class Plain(string name)
    {
        public string Name { get; } = name;
    }

    // An unshown list has no rows, including the selected one. Avalonia 12.1 realises selected rows even at zero height.
    private static (Window Window, ListBox List) WithNoRows<T>(Func<int, T> make)
    {
        var list = new ListBox
        {
            IsVisible = false,
            ItemsSource = new ObservableCollection<T>(Enumerable.Range(0, 30).Select(make)),
            ItemTemplate = new FuncDataTemplate<T>((_, _) => new Border { Height = 16 }),
        };
        var window = new Window { Content = list, Width = 300, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        list.SelectedIndex = 25;
        Dispatcher.UIThread.RunJobs();
        return (window, list);
    }

    [AvaloniaFact]
    public void A_selected_item_with_no_row_is_named_by_the_name_its_row_would_carry()
    {
        var (window, list) = WithNoRows(i => new Row($"Property{i}"));
        try
        {
            list.ContainerFromIndex(25).Should().BeNull("the selected item must have no row for this to test anything");
            UiAutomationDriver.Inspect(list, "List").SelectionItems.Should().Equal("Property25");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void One_with_no_name_to_give_says_so_rather_than_naming_its_type()
    {
        var (window, list) = WithNoRows(i => new Plain($"P{i}"));
        try
        {
            list.ContainerFromIndex(25).Should().BeNull("the selected item must have no row for this to test anything");
            UiAutomationDriver.Inspect(list, "List").SelectionItems.Should()
                .Equal("#25, which has no row to read a name from: a list makes rows only for what it shows");
        }
        finally { window.Close(); }
    }
}
