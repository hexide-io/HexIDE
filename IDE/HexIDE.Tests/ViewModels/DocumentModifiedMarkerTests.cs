using System.Linq;
using HexIDE.Events;
using HexIDE.Runtime.ProjectElements;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.ViewModels;

public class DocumentModifiedMarkerTests
{
    [AvaloniaTheory]
    [InlineData(ModuleKind.UserControl)]
    [InlineData(ModuleKind.PropertyPage)]
    public void DesignerModifiedMarker_UsesOwningModule(ModuleKind kind)
    {
        var module = TestHelpers.CreateModule(kind: kind);
        var form = TestHelpers.CreateForm(name: "Designer1");
        module.UpdateFormPart(form);
        var service = Substitute.For<IProjectService>();
        service.HasUnsavedChanges(module).Returns(true);
        var bus = new EventBus();
        using var vm = new FormEditViewModel(null!, bus, service, null!, null!,
            Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>()).Initialize(form);
        vm.IsModified.Should().BeTrue();
        service.DidNotReceive().HasUnsavedChanges(form);
        service.HasUnsavedChanges(module).Returns(false);
        bus.Publish(new DocumentSavedEvent(null, module));
        vm.IsModified.Should().BeFalse();
    }

    [AvaloniaFact]
    public void DesignerModifiedMarker_FollowsDeletionAndMatchingSave()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        form.UpdateComponents([..form.Components, new ComponentInstance(CommandButtonComponentClass.Instance, "Command1")]);
        var service = Substitute.For<IProjectService>();
        var bus = new EventBus();
        using var vm = new FormEditViewModel(null!, bus, service, null!, null!,
            Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>()).Initialize(form);
        var control = vm.AllComponents.Last();
        var savedCount = form.Components.Count;
        service.HasUnsavedChanges(form).Returns(_ => form.Components.Count != savedCount);
        bus.Publish(new DocumentSavedEvent(form, null));
        vm.IsModified.Should().BeFalse();
        vm.SetSelectedComponents([control]);
        vm.DeleteSelected();
        vm.IsModified.Should().BeTrue();
        bus.Publish(new DocumentSavedEvent(TestHelpers.CreateForm(name: "Other"), null));
        vm.IsModified.Should().BeTrue();
        savedCount = form.Components.Count;
        bus.Publish(new DocumentSavedEvent(form, null));
        vm.IsModified.Should().BeFalse();
    }

    [AvaloniaFact]
    public void DesignerModifiedMarker_FollowsPropertyCommitAndReload()
    {
        var form = TestHelpers.CreateForm(name: "Form1");
        var service = Substitute.For<IProjectService>();
        using var vm = new FormEditViewModel(null!, new EventBus(), service, null!, null!,
            Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>()).Initialize(form);
        vm.Form!.Instance.SetProperty(VBProperties.CaptionProperty, "Edited");
        service.HasUnsavedChanges(form).Returns(true);
        vm.CommitLayout();
        vm.IsModified.Should().BeTrue();
        service.HasUnsavedChanges(form).Returns(false);
        vm.ReloadFromModel();
        vm.IsModified.Should().BeFalse();
    }
}
