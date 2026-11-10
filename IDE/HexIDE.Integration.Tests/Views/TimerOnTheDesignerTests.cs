using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Controls;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

// #3508: a .frm holds only a Timer's Left and Top, so its size has to come from the class, or it loads 0 by 0 and can't be
// seen or clicked.
public class TimerOnTheDesignerTests
{
    private const string Source =
        "VERSION 5.00\r\nBegin VB.Form Form1\r\n   ClientHeight = 3000\r\n   ClientWidth = 4000\r\n" +
        "   Begin VB.Timer Timer1\r\n      Left = 600\r\n      Top = 300\r\n   End\r\nEnd\r\n";

    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private static FormDefinition Load()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, Source, new NullSink())!;
        project.AddForm(form);
        return form;
    }

    [AvaloniaFact]
    public void A_Timer_loaded_from_a_file_is_drawn_at_its_icon_s_size()
    {
        var vm = new FormEditViewModel(null!, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(), null!, null!,
            Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>(), new Vb6ProjectFileFormat()).Initialize(Load());
        var window = new Window { Width = 800, Height = 600, Content = new FormEditView { DataContext = vm } };
        window.Show();
        try
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var renderer = window.GetVisualDescendants().OfType<ControlRenderer>().Single(r => r.Component?.AddressedName == "Timer1");
            (renderer.Component!.Width, renderer.Component.Height).Should().Be((28d, 28d));
            renderer.Child.Should().BeOfType<Image>();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void A_Timer_is_still_written_with_no_size() =>
        new FormSerializer().Serialize(Load(), "Form1.frm").frmText.Should().Contain("Begin VB.Timer Timer1")
            .And.Subject.Split("Begin VB.Timer Timer1")[1].Split("End")[0].Should().NotContain("Width").And.NotContain("Height");
}
