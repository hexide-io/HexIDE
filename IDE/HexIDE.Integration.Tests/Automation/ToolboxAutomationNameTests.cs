using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Automation;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.ProjectElements;
using HexIDE.VisualDesigner;
using HexIDE.VisualDesigner.Views;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// A toolbox button is named by its control's class, the name code uses (#2751). It was named by the prefix a new
/// control's name takes, Command, Check, Text, so a caller looking for the CommandButton found nothing.
/// </summary>
public class ToolboxAutomationNameTests
{
    [AvaloniaFact]
    public void A_caller_finds_the_CommandButton_and_an_in_box_control_by_their_class_names()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var comctl = ControlLibraries.MSComctlLib;
        project.UnknownPreSectionLines.Add((1, comctl.ProjectLine));
        var toolbox = new ToolBoxToolViewModel(Substitute.For<IDocumentDockService>(),
            InBoxRegistry.Create(), Substitute.For<ILocalizationService>());
        toolbox.ShowProject(project);
        var window = new Window { Content = new ToolBoxToolView { DataContext = toolbox }, Width = 300, Height = 600 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var tree = UiAutomationDriver.Dump(window, "Window", 20, interactiveOnly: true);

            var command = UiAutomationDriver.Find(tree, "CommandButton", 10).Matches.Single();
            command.Path.Should().EndWith("[CommandButton]");
            var button = (Control)UiAutomationDriver.Resolve(window, command.Path).control!;
            ToolTip.GetTip(button).Should().Be("CommandButton", "VB6 shows the control class (#2751, oracle PR #4729)");

            UiAutomationDriver.Find(tree, "ImageList", 10).Matches.Should().ContainSingle(m => m.Path.EndsWith("[ImageList]"),
                "an in-box control is named by the class its library declares");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("CheckBox")]
    [InlineData("ComboBox")]
    [InlineData("HScrollBar")]
    public void The_tooltip_shows_the_measured_control_class(string className)
    {
        var toolbox = new ToolBoxToolViewModel(Substitute.For<IDocumentDockService>(),
            InBoxRegistry.Create(), Substitute.For<ILocalizationService>());
        toolbox.ShowProject(new ProjectDefinition(VBProjectType.EXE, "P"));
        var window = new Window { Content = new ToolBoxToolView { DataContext = toolbox }, Width = 300, Height = 600 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var tree = UiAutomationDriver.Dump(window, "Window", 20, interactiveOnly: true);
            var item = UiAutomationDriver.Find(tree, className, 10).Matches.Single();
            var button = (Control)UiAutomationDriver.Resolve(window, item.Path).control!;
            ToolTip.GetTip(button).Should().Be(className, "VB6 shows the control class (#2751, oracle PR #4729)");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_name_is_the_class_part_of_the_type_name_and_the_pointer_keeps_its_own()
    {
        var toolbox = new ToolBoxToolViewModel(Substitute.For<IDocumentDockService>(),
            InBoxRegistry.Create(), Substitute.For<ILocalizationService>());
        toolbox.ShowProject(new ProjectDefinition(VBProjectType.EXE, "P"));

        var command = toolbox.Components.Single(c => c.BaseClass?.VBTypeName == "VB.CommandButton");
        (command.Name, command.DisplayName).Should().Be(("Command", "CommandButton"));
        var pointer = toolbox.Components[0];
        pointer.VBTypeName.Should().BeNull("the pointer places nothing");
        pointer.DisplayName.Should().Be(pointer.Name);
    }
}
