using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Tools;

namespace HexIDE.Integration.Tests.Views;

public class FormLayoutToolViewTests
{
    private readonly EventBus bus = new();
    private readonly ProjectDefinition project = new(VBProjectType.EXE, "P");

    private (FormLayoutToolViewModel Vm, Border Thumb, TextBlock Label) CreateView()
    {
        var vm = new FormLayoutToolViewModel(Substitute.For<IDocumentDockService>(), bus,
            Substitute.For<ILocalizationService>());
        var view = new FormLayoutToolView { DataContext = vm };
        var canvas = view.FindControl<Canvas>("ScreenCanvas")!;
        return (vm, (Border)canvas.Children.Single(), canvas.GetVisualDescendants().OfType<TextBlock>().Single());
    }

    [AvaloniaFact]
    public void Initially_ThumbnailIsHidden()
    {
        var (_, thumb, _) = CreateView();
        thumb.IsVisible.Should().BeFalse();
    }

    [AvaloniaFact]
    public void ClearingForm_HidesThumbnail_AndSelectingAnotherShowsIt()
    {
        var (vm, thumb, _) = CreateView();
        vm.UpdateFromForm(new FormDefinition(project, FormComponentClass.Instance, "frmMain"));
        thumb.IsVisible.Should().BeTrue();
        vm.UpdateFromForm(null);
        vm.SetCanvasSize(200, 150);
        vm.SetScreenBounds(1920, 1080);
        thumb.IsVisible.Should().BeFalse();
        vm.UpdateFromForm(new FormDefinition(project, FormComponentClass.Instance, "frmOther"));
        thumb.IsVisible.Should().BeTrue();
    }

    [AvaloniaFact]
    public void ProjectUnload_HidesThumbnail()
    {
        var (vm, thumb, _) = CreateView();
        vm.UpdateFromForm(new FormDefinition(project, FormComponentClass.Instance, "frmMain"));
        bus.Publish(new ProjectUnloadedEvent(project));
        thumb.IsVisible.Should().BeFalse();
    }

    [AvaloniaFact]
    public void RemovingPreviewedForm_HidesThumbnail()
    {
        var (vm, thumb, _) = CreateView();
        var form = new FormDefinition(project, FormComponentClass.Instance, "frmMain");
        vm.UpdateFromForm(form);
        bus.Publish(new FormUnloadedEvent(new FormDefinition(project, FormComponentClass.Instance, "frmOther")));
        thumb.IsVisible.Should().BeTrue();
        bus.Publish(new FormUnloadedEvent(form));
        thumb.IsVisible.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Label_FollowsSelectedFormName_AndRenames()
    {
        var (vm, _, label) = CreateView();
        var first = new FormDefinition(project, FormComponentClass.Instance, "frmMain");
        first.Components[0].SetProperty(VBProperties.CaptionProperty, "A different caption");
        vm.UpdateFromForm(first);
        label.Text.Should().Be("frmMain");
        first.Components[0].SetProperty(VBProperties.NameProperty, "frmRenamed");
        label.Text.Should().Be("frmRenamed");
        vm.UpdateFromForm(new FormDefinition(project, FormComponentClass.Instance, "frmOther"));
        label.Text.Should().Be("frmOther");
        first.Components[0].SetProperty(VBProperties.NameProperty, "frmDetached");
        label.Text.Should().Be("frmOther");
    }
}
