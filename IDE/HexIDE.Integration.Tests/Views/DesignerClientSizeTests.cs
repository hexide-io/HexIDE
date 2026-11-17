using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;
using HexIDE.VisualDesigner.Views;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The area the designer draws a form's controls in is the form's client area, the size the form is saved with and
/// runs at (#924). The designer sized the whole drawn window, frame and caption row included, to <c>Form.Width</c> and
/// <c>Form.Height</c>, which hold <c>ClientWidth</c> and <c>ClientHeight</c>: a form saved and run at 640 x 480 was
/// designed in an area 8 pixels narrower and about 25 shorter.
/// </summary>
public class DesignerClientSizeTests
{
    private const string Frm = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Form1"
           ClientHeight    =   7200
           ClientLeft      =   60
           ClientTop       =   450
           ClientWidth     =   9600
        End
        Attribute VB_Name = "Form1"
        """;

    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private static (FormEditViewModel vm, FormEditView view, Window window) Designed()
    {
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), Frm, new NullSink())!;
        var vm = new FormEditViewModel(null!, Substitute.For<IEventBus>(), Substitute.For<HexIDE.Projects.IProjectService>(),
            null!, null!, Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>()).Initialize(form);
        var view = new FormEditView { DataContext = vm };
        var window = new Window { Width = 1000, Height = 800, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (vm, view, window);
    }

    private static Size ClientOf(FormEditView view) => view.FindControl<DockPanel>("FormClient")!.Bounds.Size;

    [AvaloniaFact]
    public void The_designed_area_is_the_forms_client_size()
    {
        var (vm, view, window) = Designed();
        try
        {
            (vm.Form.Width, vm.Form.Height).Should().Be((640.0, 480.0), "the premise: ClientWidth 9600 and ClientHeight 7200 twips");
            ClientOf(view).Should().Be(new Size(640, 480), "the controls are designed in the area the form runs with");
            view.FindControl<Control>("ControlsContainer")!.Bounds.Size.Should().Be(new Size(640, 480),
                "with no menu, all of it is where controls are placed; main drew 632 x 455 here");

            var frame = view.FindControl<Control>("FormContainer")!.Bounds.Size;
            frame.Width.Should().BeGreaterThan(640, "the frame is drawn outside the client, as the running window's is");
            frame.Height.Should().BeGreaterThan(480 + 8, "and so is the caption row");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Dragging_the_frame_saves_the_client_size_it_leaves()
    {
        // The handles are on the frame, as VB6's are, so a drag gives the frame's new size. What is saved is the client
        // inside it. Driven through the adorner's last step, not a pointer: the handles' template is in a dictionary the
        // harness cannot load.
        var (vm, view, window) = Designed();
        try
        {
            vm.SelectedComponent = vm.Form;
            Dispatcher.UIThread.RunJobs();
            var frameControl = view.FindControl<Control>("FormContainer")!;
            var adorner = (HexIDE.Controls.ResizeAdorner)Avalonia.Controls.Primitives.AdornerLayer.GetAdorner(frameControl)!;
            var frame = frameControl.Bounds.Size;

            adorner.OnDragStarted!();
            adorner.Apply(frameControl, 0, 0, frame.Width + 40, frame.Height + 20);
            adorner.OnDragCompleted!();
            Dispatcher.UIThread.RunJobs();

            (vm.Form.Width, vm.Form.Height).Should().Be((680.0, 500.0), "the frame grew by 40 x 20, and so did the client");
            ClientOf(view).Should().Be(new Size(680, 500));
            var (frm, _) = new FormSerializer().Serialize(vm.FormDefinition!, "Form1.frm");
            frm.Should().Contain("ClientWidth     =   10200").And.Contain("ClientHeight    =   7500",
                "what is saved is the client the developer sees: 680 x 500 pixels in twips");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_new_client_size_redraws_the_designed_area()
    {
        var (vm, view, window) = Designed();
        try
        {
            vm.Form.Width = 500;
            vm.Form.Height = 300;
            Dispatcher.UIThread.RunJobs();

            ClientOf(view).Should().Be(new Size(500, 300));
        }
        finally { window.Close(); }
    }
}
