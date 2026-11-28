using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Tools.ObjectBrowser;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.ViewModels;

public class ComponentClassNameTests
{
    private static ObjectBrowserToolViewModel Browser(ILocalizationService? localization = null)
    {
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns(new List<ProjectDefinition>());
        var client = Substitute.For<ILspClient>();
        client.RequestBuiltinSymbolsAsync().Returns(Array.Empty<HexIDE.Lsp.Messages.VbaBuiltinSymbol>());
        localization ??= Substitute.For<ILocalizationService>();
        localization.GetString("Str.Tool.ObjectBrowser.MembersOf").Returns("Members of '{0}'");
        localization.GetString("Str.Tool.ObjectBrowser.MemberOf").Returns("Member of {0}.{1}");
        return new ObjectBrowserToolViewModel(projects, client, Substitute.For<IEditorService>(),
            InBoxRegistry.Create(), Substitute.For<ITypeLibraryService>(), Substitute.For<IFocusedProjectUtil>(),
            localization, Substitute.For<ILanguageConnectionRegistry>());
    }

    [Fact]
    public void Vb_library_uses_class_names_in_list_header_and_membership()
    {
        var localization = Substitute.For<ILocalizationService>();
        var browser = Browser(localization);
        var library = browser.Libraries.Single(l => l.Name == "VB");
        library.Classes.Select(c => c.Name).Should().Equal("CheckBox", "ComboBox", "CommandButton", "Form",
            "Frame", "HScrollBar", "Image", "Label", "ListBox", "OptionButton", "PictureBox", "Shape",
            "TextBox", "Timer", "VScrollBar");
        browser.SelectedClass = library.Classes.Single(c => c.Name == "TextBox");
        browser.SelectedMember = browser.SelectedClass.Members[0];
        browser.MembersHeader.Should().Be("Members of 'TextBox'");
        browser.DescriptionMembership.Should().Be(string.Format(
            localization.GetString("Str.Tool.ObjectBrowser.MemberOf"), "VB", "TextBox"));
    }

    [Fact]
    public void Description_membership_formats_the_localized_library_and_class()
    {
        var localization = Substitute.For<ILocalizationService>();
        var browser = Browser(localization);
        localization.GetString("Str.Tool.ObjectBrowser.MemberOf").Returns("Mitglied von {0}.{1}");
        browser.SelectedClass = browser.Libraries.Single(l => l.Name == "VB")
            .Classes.Single(c => c.Name == "TextBox");
        browser.SelectedMember = browser.SelectedClass.Members[0];

        browser.DescriptionMembership.Should().Be("Mitglied von VB.TextBox");
    }

    [Fact]
    public void Searching_for_CommandButton_finds_the_class()
    {
        var browser = Browser();
        browser.SearchText = "CommandButton";
        browser.SearchCommand.Execute(null);
        browser.FilteredClasses.Select(c => c.Name).Should().Equal("CommandButton");
    }

    [Fact]
    public void Properties_object_box_uses_TextBox_class_name()
    {
        var instance = new ComponentInstance(TextBoxComponentClass.Instance, "Text0");
        var viewModel = new ComponentInstanceViewModel(null!, instance);
        viewModel.BaseClassName.Should().Be("TextBox");
        instance.BaseClass.Name.Should().Be("Text");
    }
}
