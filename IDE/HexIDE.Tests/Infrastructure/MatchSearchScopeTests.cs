using Avalonia.Headless;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using System.Text.RegularExpressions;
using HexIDE.Tests.ViewModels;
using HexIDE.Desktop.Server;
using HexIDE.Forms.ViewModels.Options;
using HexIDE.IDE;

namespace HexIDE.Tests.Infrastructure;

public class MatchSearchScopeTests
{
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void Split_documents_only_name_tabs_not_shown_in_any_area(int matches)
    {
        var (factory, _) = DockFactoryTests.CreateFactory();
        factory.InitLayout(factory.CreateLayout());
        using var service = new DocumentDockService(new DockFactoryReference { Value = factory },
            Substitute.For<IEventBus>());
        var original = factory.DocumentDock!;
        var first = new Document { Title = "Module1 (Code)" };
        var hidden = new Document { Title = "Language & Debug Servers" };
        var second = new Document { Title = "Module2 (Code)" };
        factory.AddDockable(original, first);
        factory.AddDockable(original, hidden);
        factory.AddDockable(original, second);
        var split = factory.CreateDocumentDock();
        split.VisibleDockables = factory.CreateList<IDockable>();
        factory.MoveDockable(original, split, second, null);
        factory.SplitToDock(original, split, DockOperation.Bottom);
        original.ActiveDockable = first;
        split.ActiveDockable = second;
        factory.SetFocusedDockable(split, second);

        var note = HexIdeTools.MatchNote("TextEditor", matches, 0, service);

        note.Should().Contain("1 open tab(s) are not selected")
            .And.Contain("'Language & Debug Servers'")
            .And.NotContain("'Module1 (Code)'").And.NotContain("'Module2 (Code)'");
    }

    [Fact]
    public void Nothing_matches_lists_the_fields_Find_searches_in_order()
    {
        var source = File.ReadAllText(Path.Combine(RepoTree.Root(), "IDE", "HexIDE", "Automation", "UiAutomationDriver.cs"));
        var answers = source.Split("private static bool Answers(UiNode node, string match) =>")[1]
            .Split(".Any(")[0];
        var fields = Regex.Matches(answers, @"node\.(\w+)").Select(m => m.Groups[1].Value)
            .Where(f => f != "InheritsDataContext").ToArray();
        fields.Should().HaveCount(7);
        var names = fields.Select(f => f switch
        {
            "Label" => "label",
            "TypeNameAsName" => "typeNameAsName",
            "DataContextType" => "the DataContext type",
            _ => f,
        }).ToArray();

        HexIdeTools.MatchNote("absent", 0, 0).Should().Be(
            "Nothing matches 'absent'. " + string.Join(", ", names[..^1]) + " and " + names[^1]
            + " are searched, as text that contains it, ignoring case.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Unselected_documents_are_named_even_when_something_matches(int matches)
    {
        var dock = Substitute.For<IDocumentDockService>();
        var code = new Document { Title = "Project1 - Form1 (Code)" };
        var designer = new Document { Title = "Project1 - Form1 (Form)" };
        dock.AllTabs.Returns([code, designer]);
        dock.ShownTabs.Returns([designer]);

        var note = HexIdeTools.MatchNote("TextEditor", matches, 0, dock);

        note.Should().Contain("1 open tab(s) are not selected")
            .And.Contain("'Project1 - Form1 (Code)'")
            .And.NotContain("'Project1 - Form1 (Form)'")
            .And.Contain("not in the tree").And.Contain("activate_document_tab");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Unselected_pages_are_named_with_their_category_and_how_to_select_them(int matches)
    {
        var general = new OptionsNodeViewModel("General", new object());
        var editor = new OptionsNodeViewModel("Editor").Add(new OptionsNodeViewModel("General", new object()));
        var environment = new OptionsNodeViewModel("Environment").Add(general);

        var note = HexIdeTools.MatchNote("Variable", matches, 2, pages: [environment, editor], selectedPage: general);

        note.Should().Contain("1 page(s) are not selected").And.Contain("'Editor > General'")
            .And.NotContain("'Environment > General'")
            .And.Contain("not in the tree").And.Contain("interact").And.Contain("select")
            .And.Contain("2 branches stopped at maxDepth");
    }

    [Fact]
    public void With_every_page_and_tab_selected_no_omission_is_claimed()
    {
        var dock = Substitute.For<IDocumentDockService>();
        var tab = new Document { Title = "Code" };
        dock.AllTabs.Returns([tab]);
        dock.ShownTabs.Returns([tab]);
        var page = new OptionsNodeViewModel("General", new object());

        HexIdeTools.MatchNote("TextEditor", 1, 0, dock, [page], page)
            .Should().Be("1 node matches 'TextEditor'.");
    }
}
