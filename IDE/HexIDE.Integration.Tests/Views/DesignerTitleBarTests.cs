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

public class DesignerTitleBarTests
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

    [AvaloniaFact]
    public void A_designer_without_an_MDI_ancestor_uses_the_active_title_bar_resource()
    {
        var (_, view, window) = Designed();
        try
        {
            var titleBar = view.FindControl<Border>("PART_FormTitleBar")!;
            titleBar.TryFindResource("ActiveTitleBarBrush", out var activeBrush).Should().BeTrue();
            titleBar.Background.Should().BeSameAs(activeBrush);
        }
        finally { window.Close(); }
    }
}
