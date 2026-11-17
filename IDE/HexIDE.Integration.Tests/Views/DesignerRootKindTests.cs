using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Controls;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;
using HexIDE.VisualDesigner.Views;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The designer draws and names a UserControl or PropertyPage as what it is, not as a form (#1544). VB6's designers for
/// the two are borderless surfaces with no title bar, since neither has a window of its own at run time; their tabs read
/// (UserControl) and (PropertyPage); and the Properties window's object box gives the class as UserControl and
/// PropertyPage.
/// </summary>
public class DesignerRootKindTests
{
    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private static string File(string kind, string name) =>
        $"VERSION 5.00\r\nBegin VB.{kind} {name} \r\n   ClientHeight    =   3600\r\n   ClientLeft      =   0\r\n"
      + $"   ClientTop       =   0\r\n   ClientWidth     =   4800\r\nEnd\r\nAttribute VB_Name = \"{name}\"\r\n";

    private static FormEditViewModel Designer(string kind, string name)
    {
        var root = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), File(kind, name), new NullSink())!;
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        return new FormEditViewModel(null!, Substitute.For<IEventBus>(), Substitute.For<HexIDE.Projects.IProjectService>(),
            null!, null!, Substitute.For<ISettingsService>(), localization).Initialize(root);
    }

    [AvaloniaTheory]
    [InlineData("Form", "Form1", "Form")]
    [InlineData("UserControl", "UserControl1", "UserControl")]
    [InlineData("PropertyPage", "PropertyPage1", "PropertyPage")]
    public void The_tab_and_the_object_box_name_the_root_by_its_kind(string kind, string name, string suffix)
    {
        var vm = Designer(kind, name);

        vm.Title.Should().Be($"P - {name} (Str.Document.{suffix}Suffix)");
        vm.Form.BaseClassName.Should().Be(kind);
    }

    [AvaloniaTheory]
    [InlineData("UserControl", "UserControl1")]
    [InlineData("PropertyPage", "PropertyPage1")]
    public void A_UserControl_or_PropertyPage_is_drawn_with_no_frame_and_no_title_bar(string kind, string name)
    {
        var vm = Designer(kind, name);
        var view = new FormEditView { DataContext = vm };
        var window = new Window { Width = 1000, Height = 800, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var client = view.FindControl<DockPanel>("FormClient")!.Bounds.Size;
            client.Should().Be(new Size(320, 240), "the premise: 4800 x 3600 twips");
            view.FindControl<Control>("FormContainer")!.Bounds.Size.Should().Be(client, "the surface is the client itself");
            view.GetVisualDescendants().OfType<MDICaptionButtons>().Should().NotContain(b => b.IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_form_keeps_its_frame_and_title_bar()
    {
        var vm = Designer("Form", "Form1");
        var view = new FormEditView { DataContext = vm };
        var window = new Window { Width = 1000, Height = 800, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var client = view.FindControl<DockPanel>("FormClient")!.Bounds.Size;
            var frame = view.FindControl<Control>("FormContainer")!.Bounds.Size;
            frame.Height.Should().BeGreaterThan(client.Height + 8, "the caption row is drawn above the client");
            view.GetVisualDescendants().OfType<MDICaptionButtons>().Should().Contain(b => b.IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }
}
