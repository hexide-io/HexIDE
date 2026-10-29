using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

// The ListView oracle's keyboard measurements (#1085), native and p-code identical.
public class ListViewKeyboardTests
{
    private sealed class Recorder : Decorator, IModuleExecutionRoot
    {
        public List<string> Calls { get; } = [];
        public List<object?> Items { get; } = [];
        public List<object?> Checked { get; } = [];
        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null)
        {
            Calls.Add(name);
            if (args?[0].Value is ListViewItem)
                Items.Add(args[0].Value);
            if (args?[0].Value is ICSharpPropertyBag item)
                Checked.Add(Read(item, "Checked"));
        }
    }

    private static async Task<(VBListView List, Recorder Recorder, ListViewItem First, ListViewItem Second)> Loaded()
    {
        var list = new VBListView();
        VBProps.SetName(list, "ListView1");
        var recorder = new Recorder { Child = list };
        list.TryGetProperty("ListItems", out var collection);
        var items = (ICSharpAsyncProxy)collection.Value!;
        list.TrySetProperty("Checkboxes", new Vb6Value(true));
        var first = (ListViewItem)(await items.CallAsync("Add", []))!.Value.Value!;
        var second = (ListViewItem)(await items.CallAsync("Add", []))!.Value.Value!;
        return (list, recorder, first, second);
    }

    private static void Press(VBListView list, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });

    private static object? Read(ICSharpPropertyBag bag, string name)
    {
        bag.TryGetProperty(name, out var value);
        return value.Value;
    }

    [AvaloniaFact]
    public async Task KeyUp_follows_the_item_events()
    {
        var (list, recorder, _, second) = await Loaded();
        Press(list, Key.Down);
        list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Down });
        recorder.Calls.Should().Equal("ListView1_KeyDown", "ListView1_ItemClick", "ListView1_KeyUp");
        recorder.Items.Should().Equal(second);
    }

    [AvaloniaFact]
    public async Task Down_selects_and_raises_ItemClick_with_the_item()
    {
        var (list, recorder, first, second) = await Loaded();
        Press(list, Key.Down);
        Read(list, "SelectedItem").Should().BeSameAs(second);
        Read((ICSharpPropertyBag)first, "Selected").Should().Be(false);
        Read((ICSharpPropertyBag)second, "Selected").Should().Be(true);
        recorder.Calls.Should().Equal("ListView1_KeyDown", "ListView1_ItemClick");
        recorder.Items.Should().Equal(second);
    }

    [AvaloniaFact]
    public async Task Control_Down_moves_focus_without_selection_or_ItemClick()
    {
        var (list, recorder, first, second) = await Loaded();
        list.TrySetProperty("MultiSelect", new Vb6Value(true));
        Press(list, Key.Down, Application.Current!.PlatformSettings!.HotkeyConfiguration.CommandModifiers);
        Read(list, "SelectedItem").Should().BeSameAs(second);
        Read((ICSharpPropertyBag)first, "Selected").Should().Be(true);
        Read((ICSharpPropertyBag)second, "Selected").Should().Be(false);
        recorder.Calls.Should().Equal("ListView1_KeyDown");
    }

    [AvaloniaFact]
    public async Task Up_selects_the_previous_item_and_raises_ItemClick()
    {
        var (list, recorder, first, second) = await Loaded();
        list.TrySetProperty("SelectedItem", new Vb6Value(second));
        Press(list, Key.Up);
        Read(list, "SelectedItem").Should().BeSameAs(first);
        Read((ICSharpPropertyBag)first, "Selected").Should().Be(true);
        Read((ICSharpPropertyBag)second, "Selected").Should().Be(false);
        recorder.Calls.Should().Equal("ListView1_KeyDown", "ListView1_ItemClick");
        recorder.Items.Should().Equal(first);
    }

    [AvaloniaFact]
    public async Task Control_Up_moves_focus_without_selection_or_ItemClick()
    {
        var (list, recorder, first, second) = await Loaded();
        list.TrySetProperty("MultiSelect", new Vb6Value(true));
        ((ICSharpPropertyBag)first).TrySetProperty("Selected", new Vb6Value(false));
        list.TrySetProperty("SelectedItem", new Vb6Value(second));
        Press(list, Key.Up, Application.Current!.PlatformSettings!.HotkeyConfiguration.CommandModifiers);
        Read(list, "SelectedItem").Should().BeSameAs(first);
        Read((ICSharpPropertyBag)first, "Selected").Should().Be(false);
        Read((ICSharpPropertyBag)second, "Selected").Should().Be(true);
        recorder.Calls.Should().Equal("ListView1_KeyDown");
    }

    [AvaloniaFact]
    public async Task Home_selects_the_first_item()
    {
        var (list, recorder, first, second) = await Loaded();
        list.TrySetProperty("SelectedItem", new Vb6Value(second));
        Press(list, Key.Home);
        Read(list, "SelectedItem").Should().BeSameAs(first);
        recorder.Calls.Should().Equal("ListView1_KeyDown", "ListView1_ItemClick");
        recorder.Items.Should().Equal(first);
    }

    [AvaloniaFact]
    public async Task Space_checks_the_focused_item_before_ItemClick_without_selecting_it()
    {
        var (list, recorder, first, second) = await Loaded();
        list.TrySetProperty("SelectedItem", new Vb6Value(second));
        ((ICSharpPropertyBag)second).TrySetProperty("Selected", new Vb6Value(false));
        Press(list, Key.Space);
        Read((ICSharpPropertyBag)second, "Checked").Should().Be(true);
        Read((ICSharpPropertyBag)second, "Selected").Should().Be(false);
        recorder.Calls.Should().Equal("ListView1_KeyDown", "ListView1_ItemCheck", "ListView1_ItemClick");
        recorder.Items.Should().Equal(second, second);
        recorder.Checked.Should().Equal(true, true);
    }
}
