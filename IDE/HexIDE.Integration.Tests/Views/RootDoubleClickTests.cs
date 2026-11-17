using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Bookmarks;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Projects;
using HexIDE.Debugging;
using HexIDE.Runtime.Debugging;
using HexIDE.Lsp;
using HexIDE.Runtime.Components;
using HexIDE.Localization;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A double click on a designer's background writes the root's own event: a form's Form_Load (#1532), a UserControl's
/// UserControl_Initialize and a PropertyPage's PropertyPage_SelectionChanged (#1632). The designer wrote Form_Load for all
/// three. Measured in VB6's IDE on the oracle guest's console, on the release build and on SP6 alike (the oracle's "A
/// UserControl's and a PropertyPage's own events").
/// </summary>
public class RootDoubleClickTests
{
    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private static FormEditViewModel Open(string kind, string name, string control = "", IEventBus? bus = null, IEditorService? editors = null)
    {
        var file = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"),
            $"VERSION 5.00\r\nBegin VB.{kind} {name} \r\n   ClientHeight    =   3600\r\n   ClientLeft      =   0\r\n"
          + $"   ClientTop       =   0\r\n   ClientWidth     =   4800\r\n{control}End\r\nAttribute VB_Name = \"{name}\"\r\n", new NullSink())!;
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        return new FormEditViewModel(null!, bus ?? Substitute.For<IEventBus>(), Substitute.For<HexIDE.Projects.IProjectService>(),
            editors!, null!, Substitute.For<ISettingsService>(), localization).Initialize(file);
    }

    [AvaloniaTheory]
    [InlineData("Form", "Form1", "Form_Load")]
    [InlineData("UserControl", "UserControl1", "UserControl_Initialize")]
    [InlineData("PropertyPage", "PropertyPage1", "PropertyPage_SelectionChanged")]
    public void A_double_click_on_the_background_writes_the_roots_own_event(string kind, string name, string procedure) =>
        Open(kind, name).RootDoubleClickProcedure.Should().Be(procedure);

    // Measured with the real pointer on VB6 SP6, oracle PR #4460 (#4411).
    [AvaloniaTheory]
    [InlineData("TextBox", "Text1", "Text1_Change")]
    [InlineData("Frame", "Frame1", "Frame1_DragDrop")]
    [InlineData("Shape", "Shape1", null)]
    public void A_control_double_click_uses_its_declared_event(string kind, string name, string? procedure)
    {
        var designer = Open("Form", "Form1", $"Begin VB.{kind} {name}\r\nEnd\r\n");
        designer.ControlDoubleClickProcedure(designer.AllComponents.Single(c => c.Name == name)).Should().Be(procedure);
    }

    [AvaloniaFact]
    public void A_Frame_DragDrop_stub_has_the_measured_arguments()
    {
        var designer = Open("Form", "Form1", "Begin VB.Frame Frame1\r\nEnd\r\n");
        var bus = new EventBus();
        using var editor = new CodeEditorViewModel(Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(), bus, Substitute.For<ILspClient>(), Substitute.For<ISettingsService>(),
            Substitute.For<IStatusBarService>(), Substitute.For<IBookmarkService>(), Substitute.For<IBreakpointService>(),
            Substitute.For<IDebugController>(), new RunScope(), Substitute.For<ILocalizationService>())
            .Initialize(designer.FormDefinition!);
        bus.Publish(new CreateOrNavigateToSubEvent(designer.FormDefinition!, "Frame1_DragDrop", "Frame1", "DragDrop"));
        editor.Document.Text.ReplaceLineEndings("\n").Should().Contain("Private Sub Frame1_DragDrop(Source As Control, X As Single, Y As Single)\n\nEnd Sub");
    }

    [AvaloniaFact]
    public async Task A_TextBox_double_tap_routes_to_Change()
    {
        var bus = new EventBus();
        var editors = Substitute.For<IEditorService>();
        using var designer = Open("Form", "Form1", "Begin VB.TextBox Text1\r\nEnd\r\n", bus, editors);
        CreateOrNavigateToSubEvent? request = null;
        using var subscription = bus.Subscribe<CreateOrNavigateToSubEvent>(e => request = e);
        var view = new FormEditView { DataContext = designer };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var item = view.GetVisualDescendants().OfType<ControlItem>().Single();
            item.RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent,
                new PointerEventArgs(InputElement.PointerPressedEvent, item, new Pointer(1, PointerType.Mouse, true),
                    item, default, 0, default, KeyModifiers.None)));
            for (var i = 0; i < 100 && request is null; i++)
                await Task.Delay(10, TestContext.Current.CancellationToken);
            editors.Received().EditCode(designer.FormDefinition);
            request.Should().NotBeNull();
            request!.Sub.Should().Be("Text1_Change");
        }
        finally { window.Close(); }
    }
}
