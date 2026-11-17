using System.Linq;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;
using HexIDE.VisualDesigner.Views;

namespace HexIDE.Integration.Tests.Views;

public class DesignerKeyboardNavigationTests
{
    private const string Frm = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Form1"
           ClientHeight    =   7200
           ClientLeft      =   60
           ClientTop       =   450
           ClientWidth     =   9600
           Begin VB.CommandButton Command1
              TabIndex = 2
           End
           Begin VB.TextBox Text1
              TabIndex = 0
           End
           Begin VB.CheckBox Check1
              TabIndex = 1
           End
        End
        Attribute VB_Name = "Form1"
        """;

    // vb6.exe SP6's second form: https://github.com/hexide-io/HexIDE/issues/4211
    private const string MixedFrm = """
        VERSION 5.00
        Begin VB.Form Form1
           ClientHeight = 7200
           ClientWidth = 9600
           Begin VB.CommandButton Command1
              TabIndex = 0
           End
           Begin VB.TextBox Text1
              TabIndex = 2
              TabStop = 0
           End
           Begin VB.Frame Frame1
              TabIndex = 3
              Begin VB.TextBox Text2
                 TabIndex = 1
              End
              Begin VB.CommandButton Command2
                 TabIndex = 5
              End
           End
           Begin VB.CommandButton Command3
              TabIndex = 4
           End
           Begin VB.Label Label1
              TabIndex = 6
           End
           Begin VB.CheckBox Check1
              TabIndex = 7
              Visible = 0
           End
           Begin VB.OptionButton Option1
              TabIndex = 8
              Enabled = 0
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private static (FormEditViewModel vm, FormEditView view, Window window) Designed(string frm = Frm)
    {
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), frm, new NullSink())!;
        var vm = new FormEditViewModel(null!, Substitute.For<IEventBus>(), Substitute.For<HexIDE.Projects.IProjectService>(),
            null!, null!, Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>()).Initialize(form);
        var view = new FormEditView { DataContext = vm };
        var window = new Window { Width = 1000, Height = 800, Content = view };
        var app = new App();
        app.LoadMarkup();
        var resources = app.Resources;
        app.Resources = new ResourceDictionary();
        window.Resources.MergedDictionaries.Add(resources);
        window.Show();
        var caption = view.GetVisualDescendants().OfType<HexIDE.Controls.MDICaptionButtons>().Single();
        window.TryFindResource(typeof(HexIDE.Controls.MDICaptionButtons), out var captionTheme).Should().BeTrue();
        caption.Theme = (Avalonia.Styling.ControlTheme)captionTheme!;
        caption.Theme.Should().NotBeNull();
        caption.ApplyStyling();
        caption.ApplyTemplate();
        caption.Template.Should().NotBeNull();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return (vm, view, window);
    }

    // vb6.exe SP6 measurements: https://github.com/hexide-io/HexIDE/issues/3399
    [AvaloniaTheory]
    [InlineData(null, Key.Tab, KeyModifiers.None, "Text1")]
    [InlineData("Text1", Key.Tab, KeyModifiers.None, "Check1")]
    [InlineData("Check1", Key.Tab, KeyModifiers.None, "Command1")]
    [InlineData("Command1", Key.Tab, KeyModifiers.None, "Text1")]
    [InlineData("Text1", Key.Tab, KeyModifiers.Shift, "Command1")]
    [InlineData("Command1", Key.Tab, KeyModifiers.Shift, "Check1")]
    [InlineData("Check1", Key.Tab, KeyModifiers.Shift, "Text1")]
    [InlineData("Text1", Key.Escape, KeyModifiers.None, null)]
    [InlineData(null, Key.Tab, KeyModifiers.Shift, "Command1")]
    public void Designer_keys_follow_the_measured_selection_table(string? from, Key key, KeyModifiers modifiers, string? expected)
    {
        var (vm, view, window) = Designed();
        try
        {
            vm.SelectedComponent = from is null ? vm.Form : vm.Components.Single(c => c.Name == from);
            var form = view.FindControl<Control>("FormContainer")!;
            if (from is null)
                form.Focus();
            else
                view.FindControl<HexIDE.Controls.ControlsContainer>("ControlsContainer")!
                    .ContainerFromIndex(vm.Components.IndexOf(vm.SelectedComponent!))!.Focus();
            var physicalKey = key == Key.Tab ? PhysicalKey.Tab : PhysicalKey.Escape;
            var rawModifiers = modifiers == KeyModifiers.Shift ? RawInputModifiers.Shift : RawInputModifiers.None;
            window.KeyPressQwerty(physicalKey, rawModifiers);
            window.KeyReleaseQwerty(physicalKey, rawModifiers);
            Dispatcher.UIThread.RunJobs();
            vm.SelectedComponent.Should().Be(expected is null ? vm.Form : vm.Components.Single(c => c.Name == expected));
            window.FocusManager!.GetFocusedElement().Should().BeSameAs(form);
            vm.IsModified.Should().BeFalse();
        }
        finally { window.Close(); }
    }

    // Each restriction also stands alone, so a combined fixture cannot hide one left behind.
    [AvaloniaTheory]
    [InlineData("TabStop")]
    [InlineData("Visible")]
    [InlineData("Enabled")]
    public void Tab_ignores_runtime_properties(string property)
    {
        var (vm, view, window) = Designed(Frm.Replace("TabIndex = 1", $"TabIndex = 1\n      {property} = 0"));
        try
        {
            vm.SelectedComponent = vm.Components.Single(c => c.Name == "Text1");
            view.FindControl<Control>("FormContainer")!.Focus();
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            vm.SelectedComponent!.Name.Should().Be("Check1");
            vm.IsModified.Should().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Tab_visits_every_control_in_global_index_order(bool backwards)
    {
        var (vm, view, window) = Designed(MixedFrm);
        try
        {
            vm.Components.Single(c => c.Name == "Text2").Instance.Container.Should()
                .BeSameAs(vm.Components.Single(c => c.Name == "Frame1").Instance);
            vm.Components.Single(c => c.Name == "Text1").Instance.GetPropertyOrDefault(VBProperties.TabStopProperty).Should().BeFalse();
            vm.Components.Single(c => c.Name == "Check1").IsRuntimeVisible.Should().BeFalse();
            vm.Components.Single(c => c.Name == "Option1").Instance.GetPropertyOrDefault(VBProperties.EnabledProperty).Should().BeFalse();
            var names = new[] { "Command1", "Text2", "Text1", "Frame1", "Command3", "Command2", "Label1", "Check1", "Option1" };
            if (backwards)
                names = names.Reverse().ToArray();
            vm.SelectedComponent = vm.Form;
            var form = view.FindControl<Control>("FormContainer")!;
            form.Focus();
            foreach (var name in names.Append(names[0]))
            {
                var modifiers = backwards ? RawInputModifiers.Shift : RawInputModifiers.None;
                window.KeyPressQwerty(PhysicalKey.Tab, modifiers);
                window.KeyReleaseQwerty(PhysicalKey.Tab, modifiers);
                Dispatcher.UIThread.RunJobs();
                vm.SelectedComponent.Should().BeSameAs(vm.Components.Single(c => c.Name == name));
                vm.SelectedComponents.Should().ContainSingle().Which.Should().BeSameAs(vm.SelectedComponent);
                window.FocusManager!.GetFocusedElement().Should().BeSameAs(form);
            }
            vm.IsModified.Should().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Command3", "Command1")]
    [InlineData("Text1", "Check1")]
    public void Tab_replaces_multiple_selection_with_the_primarys_successor(string primary, string expected)
    {
        // The first measured form has Command3 at 8, Text1 at 1 and Check1 at 2.
        int[] firstFormIndexes = [0, 5, 1, 4, 8, 6, 7, 2, 3];
        var frm = System.Text.RegularExpressions.Regex.Replace(MixedFrm, @"TabIndex = (\d)",
            m => $"TabIndex = {firstFormIndexes[int.Parse(m.Groups[1].Value)]}");
        var (vm, view, window) = Designed(frm);
        try
        {
            var controls = view.FindControl<HexIDE.Controls.ControlsContainer>("ControlsContainer")!;
            var first = primary == "Command3" ? "Command1" : "Command3";
            controls.Selection.Select(vm.Components.IndexOf(vm.Components.Single(c => c.Name == first)));
            controls.Selection.Select(vm.Components.IndexOf(vm.Components.Single(c => c.Name == primary)));
            vm.SelectedComponents.Should().HaveCount(2);
            vm.SelectedComponent!.Name.Should().Be(primary);
            view.FindControl<Control>("FormContainer")!.Focus();
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            vm.SelectedComponent!.Name.Should().Be(expected);
            vm.SelectedComponents.Should().ContainSingle().Which.Name.Should().Be(expected);
            vm.IsModified.Should().BeFalse();
        }
        finally { window.Close(); }
    }

    // Escape selects the form regardless of TabStop.
    [AvaloniaFact]
    public void Escape_selects_the_form_with_TabStop_false()
    {
        var (vm, view, window) = Designed(Frm.Replace("TabIndex = 1", "TabIndex = 1\n      TabStop = 0   'False"));
        try
        {
            vm.Components.Single(c => c.Name == "Check1").Instance.GetPropertyOrDefault(VBProperties.TabStopProperty)
                .Should().BeFalse("the fixture must have TabStop False");
            vm.SelectedComponent = vm.Components.Single(c => c.Name == "Text1");
            view.FindControl<HexIDE.Controls.ControlsContainer>("ControlsContainer")!
                .ContainerFromIndex(vm.Components.IndexOf(vm.SelectedComponent!))!.Focus();
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            vm.SelectedComponent.Should().Be(vm.Form);
        }
        finally { window.Close(); }
    }

    // A control drawn in the designer has no container, where one loaded from a file has the form. Both are top-level.
    [AvaloniaFact]
    public void Tab_reaches_a_control_drawn_in_the_designer()
    {
        var (vm, view, window) = Designed();
        try
        {
            vm.Components.Single(c => c.Name == "Text1").Instance.SetContainer(null);
            vm.SelectedComponent = vm.Form;
            view.FindControl<Control>("FormContainer")!.Focus();
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            vm.SelectedComponent.Should().Be(vm.Components.Single(c => c.Name == "Text1"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Designer_caption_buttons_are_not_tab_stops()
    {
        var (_, view, window) = Designed();
        try
        {
            var buttons = view.GetVisualDescendants().OfType<HexIDE.Controls.MDICaptionButtons>().Single().GetVisualDescendants()
                .OfType<Button>().ToArray();
            buttons.Should().HaveCount(3);
            buttons.Should().OnlyContain(b => !b.Focusable);
        }
        finally { window.Close(); }
    }
}
