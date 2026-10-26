using Dock.Model.Mvvm.Controls;
using HexIDE.Desktop.Server;
using HexIDE.Forms.ViewModels.Options;
using HexIDE.IDE;

namespace HexIDE.Tests.Infrastructure;

public class MatchSearchScopeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Unselected_documents_are_named_even_when_something_matches(int matches)
    {
        var dock = Substitute.For<IDocumentDockService>();
        var code = new Document { Title = "Project1 - Form1 (Code)" };
        var designer = new Document { Title = "Project1 - Form1 (Form)" };
        dock.AllTabs.Returns([code, designer]);
        dock.ActiveTab.Returns(designer);

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
        dock.ActiveTab.Returns(tab);
        var page = new OptionsNodeViewModel("General", new object());

        HexIdeTools.MatchNote("TextEditor", 1, 0, dock, [page], page)
            .Should().Be("1 node matches 'TextEditor'.");
    }
}
