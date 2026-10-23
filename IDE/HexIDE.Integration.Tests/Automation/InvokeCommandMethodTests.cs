using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// invoke_command reaching a method a Command is bound to, as the Project Explorer's Remove is to DeleteForm (#836).
/// It looked only for an ICommand property, and refused a method by saying there was no such property. No XAML here
/// binds these view models, so the bindings compiled XAML would have are registered.
/// </summary>
public class InvokeCommandMethodTests
{
    private class ProjectVm
    {
        public int Deleted { get; protected set; }
        public bool Allowed { get; set; } = true;

        public void DeleteForm() => Deleted++;

        private bool CanGuarded(object? parameter) => Allowed;
        public void Guarded() => Deleted++;

        public bool CanBroken(object? parameter) => throw new InvalidOperationException("no selection model");
        public void Broken() => Deleted++;

        public void Rename(string name) => Deleted--;
        public void Plumbing() => Deleted = -1;
        public void Throws() => throw new InvalidOperationException("nothing selected");
        public Task Pending() => new TaskCompletionSource().Task;
        public Task Faulted() => Task.FromException(new InvalidOperationException("save failed"));
        public Task Cancelled() => Task.FromCanceled(new CancellationToken(true));
    }

    private sealed class DerivedVm : ProjectVm
    {
        public new void DeleteForm() => Deleted += 10;
    }

    private class BaseVm
    {
        public int Saved { get; private set; }
        public void Save() => Saved++;
        private bool CanPublish(object? _) => false;
        public void Publish() => Saved++;
    }

    private sealed class DerivedSaveVm : BaseVm
    {
        public bool CanSave(object? _) => false;
    }

    private sealed class HiddenSaveVm : BaseVm
    {
        public new void Publish() => Save();
        public bool CanPublish(object? _) => true;
    }

    static InvokeCommandMethodTests()
    {
        foreach (var name in new[] { "DeleteForm", "Guarded", "Broken", "Throws", "Pending", "Faulted", "Cancelled" })
            XamlCommandBindings.Register(typeof(ProjectVm), name, 0);
        XamlCommandBindings.Register(typeof(ProjectVm), "Rename", 1);
        XamlCommandBindings.Register(typeof(BaseVm), "Save", 0);
        XamlCommandBindings.Register(typeof(BaseVm), "Publish", 0);
        XamlCommandBindings.Register(typeof(DerivedVm), "DeleteForm", 0);
    }

    private static InteractOutcome Invoke(object vm, string name)
    {
        var button = new Button { Content = "Go", DataContext = vm };
        var window = new Window { Content = button, Width = 300, Height = 200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try { return UiAutomationDriver.Interact(button, "invoke_command", name); }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_bound_method_is_called_and_the_reply_says_nothing_guarded_it()
    {
        var vm = new ProjectVm();

        var outcome = Invoke(vm, "DeleteForm");

        outcome.Success.Should().BeTrue();
        outcome.Detail.Should().Be(
            "called method 'DeleteForm' on ProjectVm; no Can method guards it, so check that it did what was meant");
        vm.Deleted.Should().Be(1);
    }

    [AvaloniaFact]
    public void A_method_whose_Can_method_says_no_is_refused_even_when_that_is_private()
    {
        var vm = new ProjectVm { Allowed = false };

        Invoke(vm, "Guarded").Error.Should().Be("'CanGuarded'(null) on ProjectVm returned false");
        vm.Deleted.Should().Be(0);

        vm.Allowed = true;
        Invoke(vm, "Guarded").Detail.Should().Be("called method 'Guarded' on ProjectVm", "a Can method guarded it");
        vm.Deleted.Should().Be(1);
    }

    [AvaloniaFact]
    public void A_private_guard_on_the_bound_methods_base_type_is_honoured()
    {
        var vm = new DerivedVm { Allowed = false };

        var outcome = Invoke(vm, "Guarded");

        outcome.Success.Should().BeFalse();
        outcome.Error.Should().Be("'CanGuarded'(null) on DerivedVm returned false");
        vm.Deleted.Should().Be(0);
    }

    [AvaloniaFact]
    public void A_guard_below_the_bound_methods_declaring_type_is_ignored()
    {
        var vm = new DerivedSaveVm();

        var outcome = Invoke(vm, "Save");

        outcome.Success.Should().BeTrue();
        outcome.Detail.Should().Be(
            "called method 'Save' on DerivedSaveVm; no Can method guards it, so check that it did what was meant");
        vm.Saved.Should().Be(1);
    }

    [AvaloniaFact]
    public void A_hidden_method_uses_the_guard_from_the_type_the_binding_matched()
    {
        var vm = new HiddenSaveVm();

        var outcome = Invoke(vm, "Publish");

        outcome.Success.Should().BeFalse();
        outcome.Error.Should().Be("'CanPublish'(null) on HiddenSaveVm returned false");
        vm.Saved.Should().Be(0);
    }

    [AvaloniaFact]
    public void A_Can_method_that_throws_says_what_it_threw()
    {
        var vm = new ProjectVm();

        Invoke(vm, "Broken").Error.Should().Be("invoke_command 'Broken' threw: no selection model");
        vm.Deleted.Should().Be(0);
    }

    [AvaloniaTheory]
    [InlineData("Plumbing", "'Plumbing' on ProjectVm is a method no Command is bound to, so invoke_command does not call it")]
    [InlineData("Rename", "'Rename' on ProjectVm is bound as a Command with a CommandParameter, which invoke_command cannot "
                          + "supply: drive the control bound to it")]
    [InlineData("DeleteFrom", "no ICommand property or Command-bound method 'DeleteFrom' on ProjectVm; inspect_element on "
                              + "the control that owns the DataContext lists both")]
    public void A_method_a_person_could_not_click_is_refused_by_saying_why(string name, string error)
    {
        var vm = new ProjectVm();

        Invoke(vm, name).Error.Should().Be(error);
        vm.Deleted.Should().Be(0);
    }

    [AvaloniaTheory]
    [InlineData("Throws", "invoke_command 'Throws' threw: nothing selected")]
    [InlineData("Faulted", "invoke_command 'Faulted' failed: save failed")]
    [InlineData("Cancelled", "invoke_command 'Cancelled' was cancelled")]
    public void A_method_that_fails_says_how(string name, string error)
    {
        Invoke(new ProjectVm(), name).Error.Should().Be(error);
    }

    [AvaloniaFact]
    public void A_method_still_running_says_so()
    {
        Invoke(new ProjectVm(), "Pending").Detail.Should().StartWith(
            "called method 'Pending' on ProjectVm; it returned a task that is still running");
    }

    [AvaloniaFact]
    public void A_method_a_derived_view_model_hides_is_the_one_called()
    {
        var vm = new DerivedVm();

        Invoke(vm, "DeleteForm").Success.Should().BeTrue();
        vm.Deleted.Should().Be(10);

        // Bound on the base type only, as XAML written against the base view model would bind it.
        Invoke(vm, "Throws").Error.Should().Be("invoke_command 'Throws' threw: nothing selected");
    }

    [AvaloniaFact]
    public void Inspection_lists_the_bound_methods_and_no_others()
    {
        var (members, _) = UiAutomationDriver.DescribeDataContext(new ProjectVm());

        members.Where(m => m.Kind == "method").Select(m => m.Name).Should().Equal(
            "Broken", "Cancelled", "DeleteForm", "Faulted", "Guarded", "Pending", "Throws");
    }
}
