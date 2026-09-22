using Avalonia.Controls;
using System.Windows.Input;
using Avalonia.Headless.XUnit;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// Guards the resolver behind <c>invoke_menu_item</c> (hexide-io/HexIDE#501).
///
/// <c>Project/Add Module</c> answered "not found" while the item was plainly there. The recorded cause was
/// that a closed menu has not realised its children, which is true of the <em>visual</em> tree and not of
/// the logical <c>Items</c> the resolver walks. The real cause was the header: <c>Add _Module</c> carries its
/// access key mid-word, and only a <em>leading</em> underscore was stripped. So every header with a
/// non-initial access key was unreachable — <c>F_ormat</c> among the top-level menus.
/// </summary>
public class MenuPathTests
{
    [Theory]
    [InlineData("_Project", "Project")]
    [InlineData("Add _Module", "Add Module")]
    [InlineData("F_ormat", "Format")]
    [InlineData("Remove", "Remove")]
    [InlineData("Snake__case", "Snake_case")]
    [InlineData("Trailing_", "Trailing_")]
    [InlineData("_First_Second", "First_Second")]
    [InlineData("__x_y", "_xy")]
    [InlineData("", "")]
    public void StripAccessKey_matches_what_Avalonia_displays(string header, string expected)
    {
        MenuPath.StripAccessKey(header).Should().Be(expected);
    }

    private static Menu BuildMenu() => new()
    {
        Items =
        {
            new MenuItem
            {
                Header = "_Project",
                Items =
                {
                    new MenuItem { Header = "Add _Form" },
                    new MenuItem { Header = "Add _Module" },
                },
            },
            new MenuItem
            {
                Header = "F_ormat",
                Items = { new MenuItem { Header = "_Align", Items = { new MenuItem { Header = "_Lefts" } } } },
            },
        },
    };

    [AvaloniaFact]
    public void Resolves_an_item_whose_access_key_is_not_its_first_letter()
    {
        var result = MenuPath.Resolve(BuildMenu().Items, "Project/Add Module");

        result.Error.Should().BeNull();
        result.Item!.Header.Should().Be("Add _Module");
    }

    [AvaloniaFact]
    public void Resolves_a_nested_path_under_a_top_level_menu_with_a_mid_word_access_key()
    {
        var result = MenuPath.Resolve(BuildMenu().Items, "Format/Align/Lefts");

        result.Error.Should().BeNull();
        result.Item!.Header.Should().Be("_Lefts");
    }

    [AvaloniaFact]
    public void Accepts_the_header_spelled_with_its_access_key()
    {
        var result = MenuPath.Resolve(BuildMenu().Items, "_Project/Add _Module");

        result.Item!.Header.Should().Be("Add _Module");
    }

    [AvaloniaFact]
    public void A_miss_names_the_menu_it_looked_in_and_what_that_menu_holds()
    {
        var result = MenuPath.Resolve(BuildMenu().Items, "Project/Add Widget");

        result.Item.Should().BeNull();
        result.Error.Should().Be("No item 'Add Widget' in menu 'Project'. It holds: Add Form, Add Module");
    }

    [AvaloniaFact]
    public void A_miss_at_the_top_level_names_the_menu_bar()
    {
        var result = MenuPath.Resolve(BuildMenu().Items, "Tools/Options");

        result.Error.Should().Be("No item 'Tools' in the menu bar. It holds: Project, Format");
    }

    [AvaloniaFact]
    public void An_empty_path_is_refused()
    {
        MenuPath.Resolve(BuildMenu().Items, "/").Error.Should().Be("Path is empty");
    }

    // #544: a caller types what the menu displays, and a displayed underscore is literal.
    [AvaloniaFact]
    public void Reaches_an_item_whose_displayed_text_contains_an_underscore()
    {
        // "_Remove {0}" formatted with a project called My_App: the menu shows "Remove My_App".
        var menu = new Menu { Items = { new MenuItem { Header = "_File", Items = { new MenuItem { Header = "_Remove My_App" } } } } };

        var result = MenuPath.Resolve(menu.Items, "File/Remove My_App");

        result.Error.Should().BeNull();
        result.Item!.Header.Should().Be("_Remove My_App");
    }

    public sealed record RecentEntry(string Header, ICommand Command);

    private sealed class Recorder : ICommand
    {
        public int Runs { get; private set; }
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => Runs++;
    }

    // #544: an ItemsSource-backed submenu holds view models until it opens, and used to answer "no items".
    [AvaloniaFact]
    public void Reaches_an_entry_of_a_submenu_bound_to_ItemsSource_before_it_opens()
    {
        var open = new Recorder();
        var menu = new Menu
        {
            Items =
            {
                new MenuItem
                {
                    Header = "_File",
                    Items =
                    {
                        new MenuItem
                        {
                            Header = "Recent _Projects",
                            ItemsSource = new[] { new RecentEntry("_1 C:\\my_dir\\P.vbp", open) },
                        },
                    },
                },
            },
        };

        var result = MenuPath.Resolve(menu.Items, "File/Recent Projects/1 C:\\my_dir\\P.vbp");

        result.Error.Should().BeNull();
        result.Command.Should().BeSameAs(open, "the entry's own command is what the menu would run");
    }

    // #544: a hidden item is not in the menu the user sees.
    [AvaloniaFact]
    public void A_hidden_item_is_neither_listed_nor_resolved()
    {
        var menu = new Menu
        {
            Items =
            {
                new MenuItem { Header = "_Help" },
                new MenuItem { Header = "Go to github repo", IsVisible = false },
            },
        };

        MenuPath.Resolve(menu.Items, "Tolls").Error.Should().Be("No item 'Tolls' in the menu bar. It holds: Help");
        var hidden = MenuPath.Resolve(menu.Items, "Go to github repo");
        hidden.Command.Should().BeNull();
        hidden.Error.Should().Be("'Go to github repo' is in the menu bar but hidden in this IDE, so it was not invoked.");
    }
}
