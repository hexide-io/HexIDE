using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Automation;
using HexIDE.Controls;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Automation;

public class BoundControlCommandTests
{
    private sealed class Sink : IDeserializeErrorSink { public void LogError(string message) { } }

    private static (Window Window, FormEditViewModel Vm, MenuItem Delete) Open()
    {
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"),
            "VERSION 5.00\r\nBegin VB.Form Form1\r\n ClientHeight = 3600\r\n ClientWidth = 4800\r\n"
            + " Begin VB.CommandButton Button1\r\n Left = 120\r\n Top = 120\r\n Width = 1200\r\n Height = 400\r\n End\r\nEnd\r\n", new Sink())!;
        var vm = new FormEditViewModel(null!, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(),
            null!, null!, Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>()).Initialize(form);
        var selected = vm.Components.Single(c => c.Name == "Button1");
        vm.SetSelectedComponents([selected]);
        vm.SelectedComponent = selected;
        var view = new FormEditView { DataContext = vm };
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var control = view.GetVisualDescendants().OfType<ControlItem>().Single(c => ReferenceEquals(c.DataContext, selected));
        UiAutomationDriver.Interact(control, "expand", null).Success.Should().BeTrue();
        Dispatcher.UIThread.RunJobs();
        var flyout = (MenuFlyout)control.ContextFlyout!;
        return (window, vm, flyout.Items.OfType<MenuItem>().ElementAt(3));
    }

    [AvaloniaFact]
    public void Invoke_deletes_the_selected_designer_control()
    {
        var (window, vm, delete) = Open();
        try
        {
            UiAutomationDriver.Interact(delete, "invoke_command", "Command").Success.Should().BeTrue();
            vm.Components.Should().NotContain(c => c.Name == "Button1");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Inspect_lists_the_resolved_command_and_its_binding()
    {
        var (window, _, delete) = Open();
        try
        {
            var detail = UiAutomationDriver.Inspect(delete, "Delete", window);
            detail.DataContextMembers.Should().Contain(m => m.Name == "Command" && m.Kind == "command");
            detail.DataContextNote.Should().Contain("DeleteSelected").And.Contain("invoke_command")
                .And.Contain("bound through another element's DataContext");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_wrong_name_explains_the_binding_and_how_to_run_it()
    {
        var (window, _, delete) = Open();
        try
        {
            var result = UiAutomationDriver.Interact(delete, "invoke_command", "WrongName");
            result.Success.Should().BeFalse();
            result.Error.Should().Contain("DeleteSelected").And.Contain("invoke").And.Contain("Command");
        }
        finally { window.Close(); }
    }
    private sealed class ParameterCommand : System.Windows.Input.ICommand
    {
        public object Parameter { get; } = new();
        public bool Allowed { get; set; } = true;
        public object? ExecutedWith { get; private set; }
        public bool CanExecute(object? parameter) => Allowed && ReferenceEquals(parameter, Parameter);
        public void Execute(object? parameter) => ExecutedWith = parameter;
        public event System.EventHandler? CanExecuteChanged { add { } remove { } }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_control_parameter_is_used_for_both_guard_and_execution(bool menu)
    {
        var command = new ParameterCommand();
        Control control = menu
            ? new MenuItem { Command = command, CommandParameter = command.Parameter }
            : new Button { Command = command, CommandParameter = command.Parameter };
        command.Allowed = false;
        UiAutomationDriver.Interact(control, "invoke_command", "Command").Success.Should().BeFalse();
        command.ExecutedWith.Should().BeNull();
        command.Allowed = true;
        UiAutomationDriver.Interact(control, "invoke_command", "Command").Success.Should().BeTrue();
        command.ExecutedWith.Should().BeSameAs(command.Parameter);
    }

}
