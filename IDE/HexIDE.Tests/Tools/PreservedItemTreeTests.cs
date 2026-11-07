using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Tools;

namespace HexIDE.Tests.Tools;

public class PreservedItemTreeTests
{
    [Theory]
    [InlineData(@"Form=Forms\Lost.frm", "Lost.frm", false)]
    [InlineData(@"Module=Lost; Forms\Lost.bas", "Lost", false)]
    [InlineData(@"Class=Lost; Forms\Lost.cls", "Lost", false)]
    [InlineData(@"UserControl=Forms\Lost.ctl", "Lost.ctl", false)]
    [InlineData(@"PropertyPage=Forms\Lost.pag", "Lost.pag", false)]
    [InlineData(@"UserDocument=Lost; Forms\Lost.dob", "Lost", true)]
    public void Preserved_members_are_visible_with_their_path_and_status(string line, string name, bool unsupported)
    {
        var definition = TestHelpers.CreateProjectWithForm("P");
        var anchor = Path.Combine(Path.GetTempPath(), "hexide-missing-" + Guid.NewGuid());
        definition.AbsolutePath = Path.Combine(anchor, "P.vbp");
        definition.PreservedItemLines.Add(line);
        using var project = CreateNode(definition);

        var directory = project.Elements.OfType<DirectoryViewModel>().Should().ContainSingle().Subject;
        directory.Name.Should().Be("Forms");
        var node = directory.Children.Should().ContainSingle().Subject.Should().BeOfType<PreservedItemViewModel>().Subject;
        node.Name.Should().Be(name);
        node.IsMissing.Should().BeTrue();
        node.IsUnsupported.Should().Be(unsupported);
        node.AbsolutePath.Should().StartWith(Path.Combine(anchor, "Forms") + Path.DirectorySeparatorChar);
        node.AccessibleName.Should().Be(line);
        definition.PreservedItemLines.Should().Equal(line);
    }

    [Fact]
    public void Existing_unsupported_member_is_not_reported_missing_and_keeps_its_original_directory_after_save_as()
    {
        var anchor = Path.Combine(Path.GetTempPath(), "hexide-kept-" + Guid.NewGuid());
        Directory.CreateDirectory(anchor);
        try
        {
            File.WriteAllText(Path.Combine(anchor, "Doc.dob"), "");
            var definition = TestHelpers.CreateProjectWithForm("P");
            definition.AbsolutePath = Path.Combine(anchor, "Elsewhere", "P.vbp");
            definition.ItemLinesDirectory = anchor;
            definition.PreservedItemLines.Add("UserDocument=Doc; Doc.dob");
            var tool = CreateTool();
            using var project = new ProjectViewModel(tool, definition);

            var node = project.Elements.OfType<PreservedItemViewModel>().Should().ContainSingle().Subject;
            node.IsMissing.Should().BeFalse();
            node.IsUnsupported.Should().BeTrue();
            node.AbsolutePath.Should().Be(Path.Combine(anchor, "Doc.dob"));
            node.LocationCaption.Should().Be("..");
            tool.SelectedItem = node;
            File.WriteAllText(Path.Combine(anchor, "Broken.frm"), "not a loadable form");
            definition.PreservedItemLines.Add("Form=Broken.frm");
            definition.AbsolutePath = Path.Combine(anchor, "P.vbp");
            var selected = tool.SelectedItem.Should().BeOfType<PreservedItemViewModel>().Subject;
            project.Elements.Should().Contain(selected);
            selected.AbsolutePath.Should().Be(node.AbsolutePath);
            selected.LocationCaption.Should().BeNull();
            var unloaded = project.Elements.OfType<PreservedItemViewModel>().Single(n => n.Line == "Form=Broken.frm");
            unloaded.IsMissing.Should().BeFalse();
            unloaded.IsUnsupported.Should().BeFalse("a failed load does not make a supported file type unsupported");
        }
        finally { Directory.Delete(anchor, true); }
    }

    private static ProjectViewModel CreateNode(HexIDE.Runtime.ProjectElements.ProjectDefinition definition) =>
        new(CreateTool(), definition);

    private static ProjectToolViewModel CreateTool() =>
        new(Substitute.For<IProjectManager>(), Substitute.For<IEventBus>(),
            Substitute.For<IProjectService>(), Substitute.For<IEditorService>(), Substitute.For<ILocalizationService>());
}
