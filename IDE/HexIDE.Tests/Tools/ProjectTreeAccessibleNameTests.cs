using System.ComponentModel;
using System.IO;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Tools;

namespace HexIDE.Tests.Tools;

/// <summary>
/// A Project Explorer node is named by the text the tree shows for it (#542). Nodes had no automation name, so
/// a project's forms could be told apart only by position, and a screen reader had nothing to announce.
/// </summary>
public class ProjectTreeAccessibleNameTests
{
    [Fact]
    public void A_form_node_is_named_as_the_tree_shows_it_and_follows_a_first_save()
    {
        var project = TestHelpers.CreateProjectWithForm("P", "Form1");
        var form = project.Forms[0];
        var node = new FormViewModel(null!, form);
        var raised = new List<string?>();
        ((INotifyPropertyChanged)node).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        node.AccessibleName.Should().Be("Form1 (Form1)", "a form with no file shows its name in both places");

        form.AbsolutePath = Path.Combine(Path.GetTempPath(), "Form1.frm");

        node.AccessibleName.Should().Be("Form1 (Form1.frm)");
        raised.Should().Contain(nameof(FormViewModel.AccessibleName), "the tree item's name is bound to it");
    }

    [Fact]
    public void A_project_node_follows_a_rename()
    {
        var definition = TestHelpers.CreateProjectWithForm("Project1");
        // Saved, so the rename leaves the file name alone: an unsaved project shows its name in both places.
        definition.AbsolutePath = Path.Combine(Path.GetTempPath(), "Project1.vbp");
        var tool = new ProjectToolViewModel(Substitute.For<IProjectManager>(), Substitute.For<IEventBus>(),
            Substitute.For<IProjectService>(), Substitute.For<IEditorService>(), Substitute.For<ILocalizationService>());
        var node = new ProjectViewModel(tool, definition);
        var raised = new List<string?>();
        ((INotifyPropertyChanged)node).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        definition.Name = "Renamed";

        node.AccessibleName.Should().Be("Renamed (Project1.vbp)");
        raised.Should().Contain(nameof(ProjectViewModel.AccessibleName),
            "a project's Name passes through from its definition, so nothing generated raises the name built on it");
    }

    [Fact]
    public void A_directory_node_is_named_by_its_directory()
    {
        new DirectoryViewModel(null, "Forms", "Forms").AccessibleName.Should().Be("Forms");
    }
}
