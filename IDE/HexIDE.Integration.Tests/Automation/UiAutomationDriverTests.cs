using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaEdit;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

// Headless coverage for the Phase-5 read surface of the MCP automation tools (dump_visual_tree /
// inspect_element). The driver takes a root Control (not the live window) precisely so it can be
// exercised here without a running IDE or the HTTP loop. The thin active-window/dialog glue lives in
// HexIdeTools and is covered by the live MCP loop.
public class UiAutomationDriverTests
{
    private sealed class SampleVm
    {
        public ICommand DoThing { get; } = new NoopCommand();
        public string Caption { get; set; } = "hello";
        public int ReadOnlyNumber { get; } = 42;

        private sealed class NoopCommand : ICommand
        {
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) { }
            public event EventHandler? CanExecuteChanged { add { } remove { } }
        }
    }

    private static (Window window, Button button, TextBox textbox, CheckBox check, SampleVm vm) BuildShownWindow()
    {
        var button = new Button { Name = "TheButton", Content = "Click" };
        AutomationProperties.SetAutomationId(button, "btnId");
        var textbox = new TextBox { Name = "TheText", Text = "hi" };
        var check = new CheckBox { Name = "TheCheck" };

        var panel = new StackPanel();
        panel.Children.Add(button);
        panel.Children.Add(textbox);
        panel.Children.Add(check);

        var vm = new SampleVm();
        var window = new Window { Content = panel, Width = 320, Height = 240, DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, button, textbox, check, vm);
    }

    private static IEnumerable<UiNode> Flatten(UiNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var d in Flatten(child))
                yield return d;
    }

    [AvaloniaFact]
    public void Dump_FindsControls_WithExpectedProviders()
    {
        var (window, _, _, _, _) = BuildShownWindow();
        try
        {
            var tree = UiAutomationDriver.Dump(window, "Window", maxDepth: 20, interactiveOnly: false);
            var all = Flatten(tree).ToList();

            all.First(n => n.Name == "TheButton").Providers.Should().Contain("invoke");
            all.First(n => n.Name == "TheText").Providers.Should().Contain("value");
            all.First(n => n.Name == "TheCheck").Providers.Should().Contain("toggle");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Dump_CollapsesStructuralWrappers_IntoShortPaths()
    {
        // A button buried under Border -> StackPanel -> Border (all structural 'None' wrappers) must
        // surface as a direct control-view child of the window, with no structural segments in its path.
        var button = new Button { Name = "DeepButton", Content = "x" };
        var nested = new Border { Child = new StackPanel { Children = { button } } };
        var window = new Window { Content = new Border { Child = nested }, Width = 200, Height = 150 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var tree = UiAutomationDriver.Dump(window, "Window", maxDepth: 20, interactiveOnly: true);
            var node = Flatten(tree).First(n => n.Name == "DeepButton");

            node.Path.Should().NotContain("None[");                 // structural wrappers collapsed away
            node.Path.Should().Be("Window/Button[DeepButton]");     // re-parented directly under the window

            var (resolved, error) = UiAutomationDriver.Resolve(window, node.Path);
            error.Should().BeNull();
            resolved.Should().BeSameAs(button);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Dump_UniqueTypeWithoutName_EmitsBareSegment()
    {
        // A control that is the sole instance of its type and has no AutomationId/Name/label should get a
        // bare segment (no positional index) — e.g. the IDE's single, unnamed <Menu> becomes "Menu".
        var slider = new Slider();   // unique type, no x:Name, no content -> no discriminator
        var window = new Window { Content = new StackPanel { Children = { slider } }, Width = 200, Height = 120 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var tree = UiAutomationDriver.Dump(window, "Window", maxDepth: 20, interactiveOnly: true);
            var node = Flatten(tree).First(n => n.ClassName == "Slider");

            node.Path.Should().NotContain("[#");                  // no positional index
            node.Path.Should().Be($"Window/{node.ControlType}");  // bare, because unique-by-type and nameless

            var (resolved, error) = UiAutomationDriver.Resolve(window, node.Path);
            error.Should().BeNull();
            resolved.Should().BeSameAs(slider);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Dump_NameCollidesWithSiblingAutomationId_PathsStillRoundTripDistinctly()
    {
        // Regression for the emitter/resolver precedence bug: X's x:Name equals Y's AutomationId. The
        // emitter must not hand X a path that the resolver (AutomationId-first) would route to Y.
        var x = new Button { Name = "Foo", Content = "X" };
        var y = new Button { Content = "contentY" };
        AutomationProperties.SetAutomationId(y, "Foo");
        var window = new Window { Content = new StackPanel { Children = { x, y } }, Width = 240, Height = 160 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var tree = UiAutomationDriver.Dump(window, "Window", maxDepth: 20, interactiveOnly: true);
            var buttonNodes = Flatten(tree).Where(n => n.ClassName == "Button").ToList();
            buttonNodes.Should().HaveCount(2);

            var resolved = buttonNodes.Select(n => UiAutomationDriver.Resolve(window, n.Path).control).ToList();
            resolved.Should().NotContainNulls();
            resolved.Should().OnlyHaveUniqueItems();          // no two emitted paths collide on one control
            resolved.Should().Contain(x).And.Contain(y);      // both controls remain individually addressable
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Dump_StructuralWrapperWithAutomationId_IsKeptAndAddressable()
    {
        // A bare Border is structural (collapsed) — but tagging it with an AutomationId (as the toolbar
        // containers now are) must keep it visible in the dump and addressable via #id.
        var tagged = new Border();
        AutomationProperties.SetAutomationId(tagged, "MyAnchor");
        var window = Show(new Border { Child = tagged });
        try
        {
            var tree = UiAutomationDriver.Dump(window, "Window", maxDepth: 20, interactiveOnly: false);
            Flatten(tree).Any(n => n.AutomationId == "MyAnchor")
                .Should().BeTrue("an AutomationId-tagged structural wrapper must not be collapsed away");

            var (resolved, error) = UiAutomationDriver.Resolve(window, "Window/#MyAnchor");
            error.Should().BeNull();
            resolved.Should().BeSameAs(tagged);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Dump_EmittedPath_RoundTripsThroughResolve()
    {
        var (window, button, _, _, _) = BuildShownWindow();
        try
        {
            var tree = UiAutomationDriver.Dump(window, "Window", maxDepth: 20, interactiveOnly: false);
            var buttonNode = Flatten(tree).First(n => n.Name == "TheButton");

            var (resolved, error) = UiAutomationDriver.Resolve(window, buttonNode.Path);

            error.Should().BeNull();
            resolved.Should().BeSameAs(button);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Resolve_AutomationIdShortcut_FindsDescendant()
    {
        var (window, button, _, _, _) = BuildShownWindow();
        try
        {
            var (resolved, error) = UiAutomationDriver.Resolve(window, "Window/#btnId");

            error.Should().BeNull();
            resolved.Should().BeSameAs(button);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Resolve_RejectsPathNotRootedAtWindow()
    {
        var (window, _, _, _, _) = BuildShownWindow();
        try
        {
            var (resolved, error) = UiAutomationDriver.Resolve(window, "Foo/Bar");

            resolved.Should().BeNull();
            error.Should().Contain("Window");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Resolve_MissingChild_ReturnsError()
    {
        var (window, _, _, _, _) = BuildShownWindow();
        try
        {
            var (resolved, error) = UiAutomationDriver.Resolve(window, "Window/Button[NoSuchName]");

            resolved.Should().BeNull();
            error.Should().NotBeNull();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Inspect_ReportsProvidersValueAndDataContext()
    {
        var (window, _, textbox, check, _) = BuildShownWindow();
        try
        {
            var text = UiAutomationDriver.Inspect(textbox, "Window/Edit[TheText]");
            text.Providers.Should().Contain("value");
            text.Value.Should().Be("hi");
            text.DataContextType.Should().Be(nameof(SampleVm));

            var checkbox = UiAutomationDriver.Inspect(check, "Window/CheckBox[TheCheck]");
            checkbox.Providers.Should().Contain("toggle");
            checkbox.ToggleState.Should().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ReflectDataContextMembers_ListsCommandsAndProperties()
    {
        var members = UiAutomationDriver.ReflectDataContextMembers(new SampleVm());

        members.Should().Contain(m => m.Name == "DoThing" && m.Kind == "command");
        members.Should().Contain(m => m.Name == "Caption" && m.Kind == "property" && m.CanWrite);
        members.Should().Contain(m => m.Name == "ReadOnlyNumber" && m.Kind == "property" && !m.CanWrite);
    }

    [AvaloniaFact]
    public void ReflectDataContextMembers_NullDataContext_ReturnsEmpty()
    {
        UiAutomationDriver.ReflectDataContextMembers(null).Should().BeEmpty();
    }

    // ── Phase 6: interaction ───────────────────────────────────────────────────────────────────────

    private static Window Show(Control content)
    {
        var window = new Window { Content = content, Width = 400, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    [AvaloniaFact]
    public void Interact_Invoke_FiresButton()
    {
        var clicked = false;
        var button = new Button { Content = "Go" };
        button.Click += (_, _) => clicked = true;
        var window = Show(button);
        try
        {
            var outcome = UiAutomationDriver.Interact(button, "invoke", null);
            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Mechanism.Should().Be("peer");
            clicked.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_Toggle_FlipsCheckBox()
    {
        var check = new CheckBox { IsChecked = false };
        var window = Show(check);
        try
        {
            UiAutomationDriver.Interact(check, "toggle", null).Success.Should().BeTrue();
            check.IsChecked.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_SetValue_SetsTextBoxText()
    {
        var textbox = new TextBox { Text = "old" };
        var window = Show(textbox);
        try
        {
            UiAutomationDriver.Interact(textbox, "set_value", "hello").Success.Should().BeTrue();
            textbox.Text.Should().Be("hello");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_Select_ByValue_SelectsListBoxItem()
    {
        var list = new ListBox { ItemsSource = new[] { "Alpha", "Beta", "Gamma" } };
        var window = Show(list);
        try
        {
            var outcome = UiAutomationDriver.Interact(list, "select", "Beta");
            outcome.Success.Should().BeTrue(outcome.Error);
            list.SelectedItem.Should().Be("Beta");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_Select_AmbiguousValue_ErrorsInsteadOfSilentlyPickingFirst()
    {
        // Two items render the same text. select-by-value must error (like path resolution) rather than
        // silently select whichever realized first and report success — a false-positive in verification.
        var list = new ListBox { ItemsSource = new[] { "Dup", "Dup", "Other" } };
        var window = Show(list);
        try
        {
            var outcome = UiAutomationDriver.Interact(list, "select", "Dup");
            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("ambiguous");
            list.SelectedItem.Should().BeNull();   // nothing was selected
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_ExpandCollapse_TogglesComboBoxDropDown()
    {
        var combo = new ComboBox { ItemsSource = new[] { "A", "B" } };
        var window = Show(combo);
        try
        {
            UiAutomationDriver.Interact(combo, "expand", null).Success.Should().BeTrue();
            combo.IsDropDownOpen.Should().BeTrue();
            UiAutomationDriver.Interact(combo, "collapse", null).Success.Should().BeTrue();
            combo.IsDropDownOpen.Should().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_UnsupportedAction_ReturnsCleanError()
    {
        var textbox = new TextBox();
        var window = Show(textbox);
        try
        {
            var outcome = UiAutomationDriver.Interact(textbox, "invoke", null);
            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("does not support 'invoke'");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_ResolveThenInteract_EndToEnd()
    {
        var check = new CheckBox { Name = "MyCheck", IsChecked = false };
        var window = Show(new StackPanel { Children = { check } });
        try
        {
            var (control, error) = UiAutomationDriver.Resolve(window, "Window/CheckBox[MyCheck]");
            error.Should().BeNull();
            UiAutomationDriver.Interact(control!, "toggle", null).Success.Should().BeTrue();
            check.IsChecked.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    // ── Phase 7: reflection fallback ───────────────────────────────────────────────────────────────

    private sealed class RelayCommand(Action exec, Func<bool>? can = null) : ICommand
    {
        public bool CanExecute(object? p) => can?.Invoke() ?? true;
        public void Execute(object? p) => exec();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }

    private sealed class Phase7Vm
    {
        public bool Fired { get; private set; }
        public bool CanFire { get; set; } = true;
        public ICommand Fire { get; }
        public string Caption { get; set; } = "";
        public int Count { get; set; }
        public bool Flag { get; set; }
        public DayOfWeek Day { get; set; } = DayOfWeek.Monday;
        public string ReadOnlyName => "ro";
        public Phase7Vm() => Fire = new RelayCommand(() => Fired = true, () => CanFire);
    }

    // A Border has a NoneAutomationPeer (no interaction provider) — exactly the gap the reflection
    // fallback fills. DataContext carries the VM the reflection actions target.
    private static (Border control, Phase7Vm vm, Window window) BorderBoundTo()
    {
        var vm = new Phase7Vm();
        var control = new Border { DataContext = vm };
        return (control, vm, Show(control));
    }

    [AvaloniaFact]
    public void Interact_InvokeCommand_ExecutesVmCommand_ViaReflection()
    {
        var (control, vm, window) = BorderBoundTo();
        try
        {
            var outcome = UiAutomationDriver.Interact(control, "invoke_command", "Fire");
            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Mechanism.Should().Be("reflection");
            vm.Fired.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_InvokeCommand_CanExecuteFalse_Errors()
    {
        var (control, vm, window) = BorderBoundTo();
        vm.CanFire = false;
        try
        {
            var outcome = UiAutomationDriver.Interact(control, "invoke_command", "Fire");
            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("CanExecute");
            vm.Fired.Should().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_InvokeCommand_NotACommand_Errors()
    {
        var (control, _, window) = BorderBoundTo();
        try
        {
            UiAutomationDriver.Interact(control, "invoke_command", "Caption").Error.Should().Contain("not an ICommand");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_SetProperty_CoercesAndSetsVmProperty()
    {
        var (control, vm, window) = BorderBoundTo();
        try
        {
            UiAutomationDriver.Interact(control, "set_property", "Caption=hello").Success.Should().BeTrue();
            vm.Caption.Should().Be("hello");

            UiAutomationDriver.Interact(control, "set_property", "Count=42").Success.Should().BeTrue();
            vm.Count.Should().Be(42);

            UiAutomationDriver.Interact(control, "set_property", "Flag=true").Success.Should().BeTrue();
            vm.Flag.Should().BeTrue();

            var enumOutcome = UiAutomationDriver.Interact(control, "set_property", "Day=Friday");
            enumOutcome.Success.Should().BeTrue(enumOutcome.Error);
            enumOutcome.Mechanism.Should().Be("reflection");
            vm.Day.Should().Be(DayOfWeek.Friday);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_SetProperty_ReadOnlyOrBadFormat_Errors()
    {
        var (control, _, window) = BorderBoundTo();
        try
        {
            UiAutomationDriver.Interact(control, "set_property", "ReadOnlyName=x").Error.Should().Contain("writable");
            UiAutomationDriver.Interact(control, "set_property", "noequals").Error.Should().NotBeNull();
            UiAutomationDriver.Interact(control, "set_property", "Count=notanumber").Error.Should().Contain("convert");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Interact_NoImplicitFallback_InvokeOnProviderlessControlDoesNotReflect()
    {
        // A Border has no IInvokeProvider; 'invoke' must report unsupported, NOT silently reflect a command.
        var (control, vm, window) = BorderBoundTo();
        try
        {
            var outcome = UiAutomationDriver.Interact(control, "invoke", null);
            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("does not support 'invoke'");
            vm.Fired.Should().BeFalse();
        }
        finally { window.Close(); }
    }

    // ── Phase 10: keyboard input ───────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void TypeText_IntoTextBox_InsertsAtCaret()
    {
        var box = new TextBox { Text = "" };
        var window = Show(box);
        try
        {
            UiAutomationDriver.TypeText(box, "hello").Success.Should().BeTrue();
            UiAutomationDriver.TypeText(box, " world").Success.Should().BeTrue();
            box.Text.Should().Be("hello world");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TypeText_IntoCodeEditor_InsertsMultiLineIntoDocument()
    {
        var editor = new TextEditor();
        var window = Show(editor);
        try
        {
            UiAutomationDriver.TypeText(editor, "Sub Main()\n").Success.Should().BeTrue();
            UiAutomationDriver.TypeText(editor, "    MsgBox \"hi\"\n").Success.Should().BeTrue();
            UiAutomationDriver.TypeText(editor, "End Sub").Success.Should().BeTrue();
            editor.Document.Text.Should().Be("Sub Main()\n    MsgBox \"hi\"\nEnd Sub");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TypeText_FindsNestedTextSurface()
    {
        var box = new TextBox();
        var panel = new StackPanel { Children = { box } };
        var window = Show(panel);
        try
        {
            UiAutomationDriver.TypeText(panel, "nested").Success.Should().BeTrue();
            box.Text.Should().Be("nested");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TypeText_NoTextSurface_Errors()
    {
        var button = new Button { Content = "x" };
        var window = Show(button);
        try
        {
            var outcome = UiAutomationDriver.TypeText(button, "x");
            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("no text surface");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PressKey_RaisesRealKeyEvents()
    {
        Avalonia.Input.Key? got = null;
        var mods = Avalonia.Input.KeyModifiers.None;
        var box = new TextBox();
        box.KeyDown += (_, e) => { got = e.Key; mods = e.KeyModifiers; };
        var window = Show(box);
        try
        {
            UiAutomationDriver.PressKey(box, "Enter", null).Success.Should().BeTrue();
            got.Should().Be(Avalonia.Input.Key.Enter);

            UiAutomationDriver.PressKey(box, "S", "Ctrl").Success.Should().BeTrue();
            got.Should().Be(Avalonia.Input.Key.S);
            mods.Should().Be(Avalonia.Input.KeyModifiers.Control);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PressKey_UnknownKeyOrModifier_Errors()
    {
        var box = new TextBox();
        var window = Show(box);
        try
        {
            UiAutomationDriver.PressKey(box, "NotAKey", null).Error.Should().Contain("unknown key");
            UiAutomationDriver.PressKey(box, "S", "Hyper").Error.Should().Contain("unknown modifier");
        }
        finally { window.Close(); }
    }
}
