using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Views;

public class MakeProjectGroupLayoutTests
{
    [AvaloniaFact]
    public void Locator_renders_the_projects_selected_and_default_options_checked()
    {
        var vm = new MakeProjectGroupViewModel([
            new ProjectDefinition(VBProjectType.EXE, "First") { AbsolutePath = "/projects/First.vbp" },
            new ProjectDefinition(VBProjectType.EXE, "Unsaved")]);
        var view = new ViewLocator().Build(vm);
        view.Should().BeOfType<MakeProjectGroupView>();
        var window = new DialogWindow { Content = view, DataContext = vm, SizeToContent = SizeToContent.WidthAndHeight };
        window.Show();
        try
        {
            window.UpdateLayout();
            var list = view!.GetVisualDescendants().OfType<ListBox>().Single();
            list.Items.Cast<MakeProjectGroupItem>().Select(i => i.Name).Should().Equal("First.vbp", "Unsaved");
            list.SelectedItems!.Count.Should().Be(2);
            list.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text)
                .Should().Contain(["First.vbp", "Unsaved"]);
            view.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked.Should().BeTrue();
            vm.Title.Should().Be(Distribution.DisplayName);
            vm.SelectedProjects.Clear();
            vm.BuildCommand.CanExecute(null).Should().BeFalse();
            vm.SelectedProjects.Add(vm.Projects[1]);
            vm.BuildCommand.CanExecute(null).Should().BeTrue();
        }
        finally { window.Close(); }
    }
}
