using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The Components dialog's view (#1103). A refused uncheck puts the box back in the view model, and the check box has to
/// follow it: its binding ignores a change raised while it is itself writing the value, so the box stayed clear while OK
/// still kept the reference (found by the live check).
/// </summary>
public class ComponentsViewTests
{
    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    [AvaloniaFact]
    public void Unchecking_a_library_a_form_uses_leaves_its_box_checked()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.UnknownPreSectionLines.Add((1, ControlLibraries.MSComctlLib.ProjectLine));
        project.AddForm(new FormDeserializer().Deserialize(project,
            "VERSION 5.00\r\nObject = \"{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0\"; \"MSCOMCTL.OCX\"\r\n"
          + "Begin VB.Form Form1\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n", new NullSink())!);
        var vm = new ComponentsViewModel(project);
        var window = new Window { Content = new ComponentsView { DataContext = vm }, Width = 480, Height = 400 };
        window.Show();
        try
        {
        Dispatcher.UIThread.RunJobs();

        var box = window.GetVisualDescendants().OfType<CheckBox>()
            .Single(c => c.Content as string == ControlLibraries.MSComctlLib.Description);
        box.IsChecked.Should().BeTrue("the premise: the project references 6.0");

        // As a click does: ToggleButton.Toggle sets the current value, which leaves the binding in place.
        box.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
        Dispatcher.UIThread.RunJobs();

        vm.Controls.Single(c => c.Library == ControlLibraries.MSComctlLib).IsSelected.Should().BeTrue();
        box.IsChecked.Should().BeTrue("the box shows what OK will keep");
        }
        finally
        {
            window.Close();
        }
    }
}
