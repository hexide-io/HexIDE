using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// invoke_command reaching a method a Command is bound to, as the Project Explorer's Remove is to DeleteForm (#836).
/// It looked only for an ICommand property, and refused a method by saying there was no such property.
/// </summary>
public class InvokeCommandMethodTests
{
    private sealed class ProjectVm
    {
        public int Deleted { get; private set; }
        public bool Allowed { get; set; } = true;

        public void DeleteForm() => Deleted++;

        public bool CanGuarded(object? parameter) => Allowed;
        public void Guarded() => Deleted++;

        public void Rename(string name) => Deleted--;
        public int Count() => Deleted;
        public void Throws() => throw new InvalidOperationException("nothing selected");
        public Task Pending() => new TaskCompletionSource().Task;
        public Task Faulted() => Task.FromException(new InvalidOperationException("save failed"));
        public void Dispose() => Deleted = -1;
        public Task Loading => Task.CompletedTask;
    }

    private static (Window Window, Button Button) Show(ProjectVm vm)
    {
        var button = new Button { Content = "Go", DataContext = vm };
        var window = new Window { Content = button, Width = 300, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, button);
    }

    [AvaloniaFact]
    public void A_parameterless_method_is_called_as_a_Command_bound_to_it_would_be()
    {
        var vm = new ProjectVm();
        var (window, button) = Show(vm);
        try
        {
            var outcome = UiAutomationDriver.Interact(button, "invoke_command", "DeleteForm");

            outcome.Success.Should().BeTrue();
            outcome.Detail.Should().Be("called method 'DeleteForm' on ProjectVm");
            vm.Deleted.Should().Be(1);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_method_whose_Can_method_says_no_is_refused_and_not_called()
    {
        var vm = new ProjectVm { Allowed = false };
        var (window, button) = Show(vm);
        try
        {
            var outcome = UiAutomationDriver.Interact(button, "invoke_command", "Guarded");

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Be("'CanGuarded'(null) on ProjectVm returned false");
            vm.Deleted.Should().Be(0);

            vm.Allowed = true;
            UiAutomationDriver.Interact(button, "invoke_command", "Guarded").Success.Should().BeTrue();
            vm.Deleted.Should().Be(1);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Rename")]
    [InlineData("Count")]
    [InlineData("Dispose")]
    public void A_method_a_Command_binding_would_not_call_is_refused_by_saying_why(string name)
    {
        var vm = new ProjectVm();
        var (window, button) = Show(vm);
        try
        {
            var outcome = UiAutomationDriver.Interact(button, "invoke_command", name);

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().StartWith($"'{name}' on ProjectVm is a method invoke_command cannot call");
            vm.Deleted.Should().Be(0);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_name_that_is_neither_says_what_was_looked_for()
    {
        var (window, button) = Show(new ProjectVm());
        try
        {
            UiAutomationDriver.Interact(button, "invoke_command", "DeleteFrom").Error.Should().Be(
                "no ICommand property or parameterless method 'DeleteFrom' on ProjectVm; inspect_element on the control "
                + "that owns the DataContext lists both");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Throws", "invoke_command 'Throws' threw: nothing selected")]
    [InlineData("Faulted", "invoke_command 'Faulted' failed: save failed")]
    public void A_method_that_fails_says_how(string name, string error)
    {
        var (window, button) = Show(new ProjectVm());
        try
        {
            UiAutomationDriver.Interact(button, "invoke_command", name).Error.Should().Be(error);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_method_still_running_says_so()
    {
        var (window, button) = Show(new ProjectVm());
        try
        {
            UiAutomationDriver.Interact(button, "invoke_command", "Pending").Detail.Should().Be(
                "called method 'Pending' on ProjectVm; it returned a task that is still running");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Inspection_lists_the_methods_invoke_command_can_call_and_no_others()
    {
        var (members, _) = UiAutomationDriver.DescribeDataContext(new ProjectVm());

        members.Where(m => m.Kind == "method").Select(m => m.Name).Should().Equal(
            "DeleteForm", "Faulted", "Guarded", "Pending", "Throws");
    }

    [AvaloniaFact]
    public void The_Project_Explorer_s_Remove_is_listed()
    {
        UiAutomationDriver.CommandMethods(typeof(HexIDE.Tools.ProjectToolViewModel))
            .Select(m => m.Name).Should().Contain("DeleteForm");
    }

    // List<T> declares Clear, Reverse, Sort and TrimExcess: a framework base's methods are not the view model's.
    private sealed class ListVm : List<int>
    {
        public void Own() { }
    }

    [AvaloniaFact]
    public void A_framework_base_s_methods_are_not_listed()
    {
        UiAutomationDriver.CommandMethods(typeof(ListVm)).Select(m => m.Name).Should().Equal("Own");
    }
}
