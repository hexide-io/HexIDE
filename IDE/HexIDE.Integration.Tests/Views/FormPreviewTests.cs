using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

public class FormPreviewTests
{
    private sealed class Sink : IDeserializeErrorSink { public void LogError(string message) { } }

    private static (FormEditViewModel Vm, FormDefinition Form) Designer(IWindowManager? windows = null)
    {
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), """
            VERSION 5.00
            Begin VB.Form Form1
               Caption = "Layout"
               ClientWidth = 4800
               ClientHeight = 3600
               Begin VB.CommandButton Command1
                  Caption = "Original"
                  Left = 300
                  Top = 450
                  Width = 1500
                  Height = 450
                  TabIndex = 1
               End
               Begin VB.TextBox Text1
                  Text = "Design text"
                  TabIndex = 0
                  TabStop = 0
               End
               Begin VB.Menu Menu1
                  Caption = "Menu"
               End
            End
            Private Sub Form_Load()
                Command1.Caption = "Load ran"
            End Sub
            Private Sub Command1_Click()
                Command1.Caption = "Click ran"
            End Sub
            Private Sub Form_Resize()
                Command1.Caption = "Resize ran"
            End Sub
            Private Sub Form_QueryUnload(Cancel As Integer, UnloadMode As Integer)
                Cancel = True
            End Sub
            """, new Sink())!;
        var vm = new FormEditViewModel(null!, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(),
            null!, windows ?? Substitute.For<IWindowManager>(), Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>()).Initialize(form);
        return (vm, form);
    }

    private static async Task<VBFormRuntime> Preview(FormEditViewModel vm)
    {
        VBFormRuntime? window = null;
        using var opened = Window.WindowOpenedEvent.AddClassHandler<VBFormRuntime>((w, _) => window = w);
        await vm.PreviewForm();
        window.Should().NotBeNull("the designer command must open a live preview window");
        return window!;
    }

    [AvaloniaFact]
    public async Task Unsupported_runtime_content_is_reported_without_opening_a_preview()
    {
        var windows = Substitute.For<IWindowManager>();
        var (vm, _) = Designer(windows);
        vm.Form.Instance.UnknownRawProperties.Add(new("DataSource", ["DataSource = \"Data1\""]));
        VBFormRuntime? preview = null;
        using var opened = Window.WindowOpenedEvent.AddClassHandler<VBFormRuntime>((w, _) => preview = w);
        await vm.PreviewForm();
        preview.Should().BeNull();
        await windows.Received().MessageBox(Arg.Is<string>(s => s.Contains("data binding")), Arg.Any<string>(),
            MessageBoxButtons.Ok, MessageBoxIcon.Error);
    }

    [AvaloniaTheory]
    [InlineData("VB.Form", true)]
    [InlineData("VB.UserControl", false)]
    [InlineData("VB.PropertyPage", false)]
    public void Preview_command_is_available_only_for_forms(string rootType, bool visible)
    {
        var (vm, form) = Designer();
        form.UpdateRootTypeName(rootType);
        var view = new FormEditView { DataContext = vm };
        var button = view.FindControl<Button>("PreviewFormButton");
        button.Should().NotBeNull();
        button!.IsVisible.Should().Be(visible);
        button.Command.Should().NotBeNull();
    }

    [AvaloniaFact]
    public async Task Preview_uses_live_controls_with_runtime_layout_menus_and_tab_settings()
    {
        var (vm, form) = Designer();
        vm.Form.Instance.SetProperty(VBProperties.CaptionProperty, "Unsaved title");
        var preview = await Preview(vm);
        try
        {
            var button = preview.GetVisualDescendants().OfType<Button>().Single(b => VBProps.GetName(b) == "Command1");
            button.Content.Should().Be("Original");
            Canvas.GetLeft(button).Should().Be(20);
            Canvas.GetTop(button).Should().Be(30);
            button.Width.Should().Be(100);
            button.Height.Should().Be(30);
            KeyboardNavigation.GetTabIndex(button).Should().Be(1);
            var text = preview.GetVisualDescendants().OfType<TextBox>().Single();
            KeyboardNavigation.GetTabIndex(text).Should().Be(0);
            KeyboardNavigation.GetIsTabStop(text).Should().BeFalse();
            preview.GetVisualDescendants().OfType<MenuItem>().Should().Contain(m => m.Header!.ToString() == "Menu");
            preview.Height.Should().Be(240 + VBLoader.MenuBarHeight);
            preview.Title.Should().Be("Unsaved title");
            preview.Context.Interpreter.Should().BeNull();
            preview.Context.Program.Should().BeNull();
        }
        finally { preview.Close(); }
    }

    [AvaloniaFact]
    public async Task Interaction_resize_and_close_run_no_code_and_leave_designer_unchanged()
    {
        var (vm, form) = Designer();
        vm.ToggleLockControls();
        var undo = vm.UndoStack.UndoDescription;
        var components = form.Components.ToArray();
        var selected = vm.SelectedComponent;
        var modified = vm.IsModified;
        var code = form.Code;
        var preview = await Preview(vm);
        try
        {
            var button = preview.GetVisualDescendants().OfType<Button>().Single(b => VBProps.GetName(b) == "Command1");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            button.Content.Should().Be("Original", "neither Load nor Click may run");
            var text = preview.GetVisualDescendants().OfType<TextBox>().Single();
            text.Text = "Preview edit";
            preview.Width += 80;
            preview.Height += 40;
            button.Content.Should().Be("Original", "Resize may not run");
        }
        finally { preview.Close(); }
        preview.IsVisible.Should().BeFalse("QueryUnload must not run and cancel closing");
        form.Components.Should().Equal(components);
        form.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "Text1")
            .GetPropertyOrDefault(VBProperties.TextProperty).Should().Be("Design text");
        vm.Form.Width.Should().Be(320);
        vm.Form.Height.Should().Be(240);
        vm.SelectedComponent.Should().BeSameAs(selected);
        vm.IsModified.Should().Be(modified);
        form.Code.Should().Be(code);
        vm.UndoStack.CanUndo.Should().BeTrue();
        vm.UndoStack.UndoDescription.Should().Be(undo);
        vm.UndoStack.CanRedo.Should().BeFalse();
        form.LockControls.Should().BeTrue();
    }
}
