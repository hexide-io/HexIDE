using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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

    // ── press_key target resolution ─────────────────────────────────────────────────────────────
    // Two recorded MCP gaps, one root cause: the key was raised on a control the caller did not mean,
    // and the reply said "success" either way. A routed event reaches the element it is raised on and
    // its ancestors -- never anything below -- so resolving too SHALLOW makes a handler unreachable,
    // and resolving to a non-focusable container makes the press a no-op. Both looked identical to a
    // feature that was simply broken, and one of them cost a wrongly-filed issue.

    [AvaloniaFact]
    public void PressKey_ResolvesToTheDeepestInputSurface_SoAHandlerBelowTheEditorStillFires()
    {
        // THE case. AvaloniaEdit nests TextEditor -> TextArea, and the code editor attaches its key
        // handling to TextArea. Raising on the TextEditor put TextArea on neither leg of the route.
        var editor = new TextEditor();
        var host = new StackPanel();
        host.Children.Add(editor);
        var window = Show(host);
        try
        {
            var seen = 0;
            editor.TextArea.AddHandler(
                InputElement.KeyDownEvent, (object? _, KeyEventArgs e) => { if (e.Key == Key.F12) seen++; },
                RoutingStrategies.Tunnel);

            var outcome = UiAutomationDriver.PressKey(host, "F12", null);

            outcome.Success.Should().BeTrue(outcome.Error);
            seen.Should().Be(1, "a handler on TextArea must receive a key pressed at the editor's host");
            outcome.Detail.Should().Contain("TextArea",
                "the reply has to name the receiver, or a no-op is unattributable");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PressKey_OnAContainerWithNothingFocusable_FailsRatherThanReportingSuccess()
    {
        // A dock container has no focus and no text surface. Reporting success there is what made
        // "input was blocked" and "the key went nowhere" indistinguishable from the result.
        var label = new TextBlock { Text = "not focusable" };
        var host = new StackPanel { Focusable = false };
        host.Children.Add(label);
        var window = Show(host);
        try
        {
            var outcome = UiAutomationDriver.PressKey(host, "A", null);

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("keyboard focus");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PressKey_OnAFocusableControlItself_ReachesItAndSaysNothingExtra()
    {
        // The ordinary case, pinned so the resolution above cannot quietly redirect a direct address.
        var box = new TextBox();
        var window = Show(box);
        try
        {
            var seen = false;
            box.AddHandler(InputElement.KeyDownEvent, (object? _, KeyEventArgs e) => seen = true,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

            var outcome = UiAutomationDriver.PressKey(box, "Enter", null);

            outcome.Success.Should().BeTrue(outcome.Error);
            seen.Should().BeTrue();

            // Asserted as an absence rather than an exact string: Avalonia's `Key.Enter` is an alias for
            // `Key.Return`, so the reply echoes the CANONICAL name rather than the one the caller typed.
            // That is useful — it says what the key resolved to — and it is not what this test is about.
            outcome.Detail.Should().NotContain(" on ",
                "naming the receiver is noise when it is the control that was addressed");
        }
        finally { window.Close(); }
    }

    // ── hover ──────────────────────────────────────────────────────────────────────────────────────
    // A hover is a POSITION, not just an event: the code editor reads e.GetPosition(TextView) and turns it
    // into a text location, so an event carrying no usable point produces no tip however well it is routed.

    [AvaloniaFact]
    public void Hover_RaisesEnteredAndMoved_CarryingAPosition()
    {
        var button = new Button { Content = "Hover me", Width = 120, Height = 40 };
        var window = Show(button);
        try
        {
            var events = new List<string>();
            Point? seen = null;
            button.AddHandler(InputElement.PointerEnteredEvent,
                // Direct, not Bubble: Avalonia registers PointerEntered/Exited as DIRECT events, so a
                // bubbling handler never sees one however it is raised.
                (object? _, PointerEventArgs e) => events.Add("entered"), RoutingStrategies.Direct);
            button.AddHandler(InputElement.PointerMovedEvent,
                (object? _, PointerEventArgs e) => { events.Add("moved"); seen = e.GetPosition(button); },
                RoutingStrategies.Bubble);

            var outcome = UiAutomationDriver.Hover(button, null, null);

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Mechanism.Should().Be("pointer");
            events.Should().Equal(["entered", "moved"],
                "a tooltip service waits for Entered; the editor listens to Moved — both are needed");
            seen.Should().NotBeNull("an event with no usable position produces no tip");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Hover_DefaultsToTheCentreOfAnOrdinaryControl()
    {
        var button = new Button { Content = "Hover me", Width = 120, Height = 40 };
        var window = Show(button);
        try
        {
            Point? seen = null;
            button.AddHandler(InputElement.PointerMovedEvent,
                (object? _, PointerEventArgs e) => seen = e.GetPosition(button), RoutingStrategies.Bubble);

            UiAutomationDriver.Hover(button, null, null).Success.Should().BeTrue();

            seen!.Value.X.Should().BeApproximately(button.Bounds.Width / 2, 1.0);
            seen!.Value.Y.Should().BeApproximately(button.Bounds.Height / 2, 1.0);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Hover_HonoursAnExplicitPoint()
    {
        // The escape hatch for anything the defaults get wrong — and the only way to hover a specific
        // pixel of a surface that is neither a text editor nor uniform.
        var button = new Button { Content = "Hover me", Width = 120, Height = 40 };
        var window = Show(button);
        try
        {
            Point? seen = null;
            button.AddHandler(InputElement.PointerMovedEvent,
                (object? _, PointerEventArgs e) => seen = e.GetPosition(button), RoutingStrategies.Bubble);

            UiAutomationDriver.Hover(button, 12, 7).Success.Should().BeTrue();

            seen!.Value.X.Should().BeApproximately(12, 1.0);
            seen!.Value.Y.Should().BeApproximately(7, 1.0);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Hover_OnADetachedControl_FailsRatherThanRaisingIntoNothing()
    {
        // No window means no coordinate space. Raising anyway would report success for a hover that could
        // not have had a position — the failure shape this whole pass keeps finding.
        var orphan = new Button { Content = "nowhere" };

        var outcome = UiAutomationDriver.Hover(orphan, null, null);

        outcome.Success.Should().BeFalse();
        outcome.Error.Should().Contain("not attached");
    }

    // ── Saying when a node is not on screen (gap 15) ─────────────────────────────────

    [AvaloniaFact]
    public void A_collapsed_control_is_reported_as_hidden()
    {
        // The regression: a read-only banner bound to IsVisible showed in the tree of a form that was NOT
        // read-only, with nothing to distinguish it from a rendered one. Read as "the banner is showing",
        // that says a fresh, perfectly reproducible file is being held unsaveable — a serious bug, and one
        // entirely consistent with the change under test at the time.
        var banner = new TextBlock { Text = "Read-only", IsVisible = false };
        var window = Show(new StackPanel { Children = { banner, new TextBlock { Text = "Code" } } });
        try
        {
            var tree = UiAutomationDriver.Dump(window, "Window", maxDepth: 20, interactiveOnly: false);

            var node = Find(tree, n => n.Name == "Read-only");
            node.Should().NotBeNull("the node stays in the tree — it exists in the template");
            node!.IsHidden.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_showing_control_says_nothing_at_all()
    {
        // Null, not false, so the field is absent from the overwhelmingly common case: a dump runs to
        // hundreds of nodes and the hidden one is the exception worth spelling out.
        var window = Show(new TextBlock { Text = "Code" });
        try
        {
            var tree = UiAutomationDriver.Dump(window, "Window", maxDepth: 20, interactiveOnly: false);

            Find(tree, n => n.Name == "Code")!.IsHidden.Should().BeNull();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_visible_control_inside_a_collapsed_parent_is_hidden_too()
    {
        // Effective visibility, not the local flag. The child's own IsVisible is true, and reporting that
        // would move the same trap up one level instead of closing it.
        var child = new TextBlock { Text = "Inner" };
        var parent = new StackPanel { IsVisible = false, Children = { child } };
        var window = Show(new StackPanel { Children = { parent, new TextBlock { Text = "Code" } } });
        try
        {
            child.IsVisible.Should().BeTrue("the child's own flag is untouched — that is the point");

            var tree = UiAutomationDriver.Dump(window, "Window", maxDepth: 20, interactiveOnly: false);

            Find(tree, n => n.Name == "Inner")!.IsHidden.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Inspect_reports_hidden_for_a_single_control_too()
    {
        // inspect_element is where a caller checks one specific banner, so it must answer the same question.
        var banner = new TextBlock { Text = "Read-only", IsVisible = false };
        var window = Show(new StackPanel { Children = { banner } });
        try
        {
            var detail = UiAutomationDriver.Inspect(banner, "Window/Text[Read-only]");

            detail.IsHidden.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    private static UiNode? Find(UiNode node, Func<UiNode, bool> predicate)
    {
        if (predicate(node)) return node;
        foreach (var child in node.Children)
            if (Find(child, predicate) is { } hit) return hit;
        return null;
    }


    // ── Reading what the IDE said (dataContextMembers values) ────────────────────────

    private sealed class ValueVm
    {
        public string Text { get; set; } = "a runtime error message";
        public int Count { get; } = 42;
        public bool Flag { get; } = true;
        public string? Missing { get; }
        public string Empty { get; } = "";
        public AvaloniaEdit.Document.TextDocument Document { get; } = new("x = 1" + (char)10 + "y = 2");
        public object Complicated { get; } = new();
        public string Explodes => throw new InvalidOperationException("not ready");
        public System.Windows.Input.ICommand? DoIt => null;
    }

    [AvaloniaFact]
    public void Scalar_properties_report_their_value()
    {
        var members = UiAutomationDriver.ReflectDataContextMembers(new ValueVm());

        Member(members, "Text").Value.Should().Be("a runtime error message");
        Member(members, "Count").Value.Should().Be("42");
        Member(members, "Flag").Value.Should().Be("True");
    }

    [AvaloniaFact]
    public void A_TextDocument_reports_its_text()
    {
        // The Immediate window's contents are a TextDocument, not a string — the single most useful piece
        // of text in the IDE, and unreachable to a scalars-only reader.
        UiAutomationDriver.ReflectDataContextMembers(new ValueVm())
            .Single(m => m.Name == "Document").Value.Should().Be("x = 1" + (char)10 + "y = 2");
    }

    [AvaloniaFact]
    public void Null_means_not_read_and_empty_means_empty()
    {
        // The distinction is the point: an empty string is a fact about the value, null is a fact about
        // the reader. Collapsing them would make "no output yet" and "cannot see the output" identical.
        var members = UiAutomationDriver.ReflectDataContextMembers(new ValueVm());

        Member(members, "Empty").Value.Should().Be("");
        Member(members, "Missing").Value.Should().BeNull();
        Member(members, "Complicated").Value.Should().BeNull("an unsupported type is listed, not read");
    }

    [AvaloniaFact]
    public void A_throwing_getter_does_not_fail_the_inspection()
    {
        // A computed getter can easily depend on state that is not there yet. Inspecting a control must
        // not fail because one view-model property is unhappy.
        var members = UiAutomationDriver.ReflectDataContextMembers(new ValueVm());

        Member(members, "Explodes").Value.Should().BeNull();
        members.Should().Contain(m => m.Name == "Text", "the other members still come back");
    }

    [AvaloniaFact]
    public void A_command_is_still_listed_without_a_value()
    {
        var doIt = Member(UiAutomationDriver.ReflectDataContextMembers(new ValueVm()), "DoIt");

        doIt.Kind.Should().Be("command");
        doIt.Value.Should().BeNull();
    }

    [AvaloniaFact]
    public void A_long_value_is_truncated_and_says_so()
    {
        // Debug.Print output has no natural limit, and a tool result that grows without one is a different
        // failure from the one being fixed.
        var vm = new ValueVm { Text = new string('x', 9000) };

        var value = Member(UiAutomationDriver.ReflectDataContextMembers(vm), "Text").Value;

        value!.Length.Should().BeLessThan(9000);
        value.Should().EndWith("chars]").And.Contain("truncated");
    }

    private static VmMember Member(VmMember[] members, string name) => members.Single(m => m.Name == name);
}
