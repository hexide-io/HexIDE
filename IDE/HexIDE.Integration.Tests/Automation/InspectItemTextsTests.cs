using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// inspect_element gives the text of every item of a list or open dropdown, rows scrolled out of view included (#2450).
/// A list makes rows only for what shows, so the dump and its match saw the first ten of Options' 31 languages, and nothing
/// said what the rest were called or how to spell one for select.
/// </summary>
public class InspectItemTextsTests
{
    private sealed record Language(string Code, string DisplayName);

    private static readonly Language[] Languages =
        [.. Enumerable.Range(0, 31).Select(i => new Language($"l{i}", $"Language {i:00}"))];

    // Each item shown through a template, as Options' Language dropdown shows LanguageInfo.DisplayName.
    private static readonly FuncDataTemplate<Language> Template =
        new((language, _) => new TextBlock { Text = language?.DisplayName });

    private static Window Show(Control content)
    {
        var window = new Window { Width = 300, Height = 300, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void A_long_list_gives_every_item_s_text_and_is_left_where_it_was()
    {
        var list = new ListBox { ItemsSource = Languages, ItemTemplate = Template, Height = 120 };
        var window = Show(list);
        try
        {
            var scroller = list.GetVisualDescendants().OfType<ScrollViewer>().First();
            list.GetRealizedContainers().Count().Should().BeLessThan(31, "the list makes rows only for what shows");
            var before = scroller.Offset;

            var detail = UiAutomationDriver.Inspect(list, "Window/List", window);

            detail.ItemCount.Should().Be(31);
            detail.Items.Should().Equal(Languages.Select(l => l.DisplayName));
            detail.ItemsNote.Should().BeNull();
            scroller.Offset.Should().Be(before, "a read puts the list back where it was");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void An_open_dropdown_gives_every_item_s_text_and_select_takes_one_of_them()
    {
        var combo = new ComboBox { ItemsSource = Languages, ItemTemplate = Template, MaxDropDownHeight = 120 };
        var window = Show(combo);
        try
        {
            combo.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();

            var detail = UiAutomationDriver.Inspect(combo, "Window/ComboBox", window);

            detail.Items.Should().HaveCount(31).And.EndWith("Language 30");
            combo.IsDropDownOpen.Should().BeTrue();
            // What the caller reads is what select takes.
            var picked = UiAutomationDriver.Interact(combo, "select", detail.Items![27]);
            picked.Success.Should().BeTrue(picked.Error);
            combo.SelectedItem.Should().Be(Languages[27]);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_closed_dropdown_says_to_open_it_and_is_not_opened()
    {
        var combo = new ComboBox { ItemsSource = Languages, ItemTemplate = Template };
        var window = Show(combo);
        try
        {
            var detail = UiAutomationDriver.Inspect(combo, "Window/ComboBox", window);

            detail.ItemCount.Should().Be(31);
            detail.Items.Should().BeNull();
            detail.ItemsNote.Should().Be("Its items are made only while it is open, so their text can't be read now: 'expand' "
                + "it, then inspect it again. Opening it is left to the caller, as it is a person's action, not a read.");
            combo.IsDropDownOpen.Should().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_hidden_item_is_left_out_and_counted_as_select_passes_over_it()
    {
        var list = new ListBox { ItemsSource = new[] { "one", "two", "three" } };
        // As the Properties window hides a collapsed category's rows, and as HeldRowTests hides one.
        list.ContainerPrepared += (_, e) => e.Container.IsVisible = e.Index != 1;
        var window = Show(list);
        try
        {
            var detail = UiAutomationDriver.Inspect(list, "Window/List", window);

            detail.Items.Should().Equal("one", "three");
            detail.ItemsNote.Should().Be("1 item(s) that do not show are left out, as select passes over them.");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_list_longer_than_the_limit_says_how_many_it_left_out()
    {
        var list = new ListBox { ItemsSource = Enumerable.Range(0, UiAutomationDriver.ItemTextLimit + 5).Select(i => $"item {i}").ToArray(), Height = 120 };
        var window = Show(list);
        try
        {
            var detail = UiAutomationDriver.Inspect(list, "Window/List", window);

            detail.Items.Should().HaveCount(UiAutomationDriver.ItemTextLimit).And.StartWith("item 0");
            detail.ItemsNote.Should().Be($"5 item(s) after the first {UiAutomationDriver.ItemTextLimit} are not listed.");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void An_empty_list_gives_an_empty_list_and_a_button_none()
    {
        var list = new ListBox();
        var button = new Button { Content = "OK" };
        var window = Show(new StackPanel { Children = { list, button } });
        try
        {
            UiAutomationDriver.Inspect(list, "Window/List", window).Items.Should().BeEmpty();
            UiAutomationDriver.Inspect(button, "Window/Button", window).Items.Should().BeNull();
        }
        finally { window.Close(); }
    }
}
