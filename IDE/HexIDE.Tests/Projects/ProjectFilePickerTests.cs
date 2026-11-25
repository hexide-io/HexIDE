using HexIDE.Runtime.Serialization;
using HexIDE.Tests.Infrastructure;
using System.Reflection;
using Avalonia.Platform.Storage;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Projects;

public sealed class ProjectFilePickerTests
{
    [Theory]
    [InlineData(false, "ChooseProjectPath", "vbp", false)]
    [InlineData(true, "ChooseProjectPath", "xproj", false)]
    [InlineData(false, "ChooseGroupPath", "vbg", false)]
    [InlineData(true, "ChooseGroupPath", "xgrp", false)]
    [InlineData(false, "ChooseModulePath", "cls", false)]
    [InlineData(true, "ChooseModulePath", "xcls", false)]
    [InlineData(false, "ChooseFormPath", "frm", false)]
    [InlineData(true, "ChooseFormPath", "xfrm", false)]
    [InlineData(true, "ChooseProjectPath", null, true)]
    [InlineData(true, "ExistingGroupPath", "vbg", false)]
    public async Task Picker_uses_the_owning_type_and_format(bool custom, string method, string? extension, bool wholeName)
    {
        var format = custom ? (IProjectFileFormat)new ProjectFileFormatSeamTests.StandInFormat { FormExtension = "xfrm", ClassExtension = "xcls" } : new Vb6ProjectFileFormat();
        var types = new ContributedProjects();
        Vb6Types.Register(types, custom ? new Vb6ProjectFileFormat() : format);
        types.Register(new AddinProjectType("Other custom") { FileNames = ["*.wrong"], Format = new Vb6ProjectFileFormat() },
            new AddinOrigin("custom", "Custom", IsFirstParty: true));
        types.Register(new AddinProjectType("Custom")
        {
            FileNames = [wholeName ? ".rdproj" : "*.xproj"], GroupFileNames = ["*.xgrp"], Format = format,
        }, new AddinOrigin("custom", "Custom", IsFirstParty: true));
        var project = new ProjectDefinition(VBProjectType.EXE, "Ledger")
        {
            Format = format, ContributedBy = custom ? "custom" : Vb6ProjectType.Id,
        };
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns(new List<ProjectDefinition>
            { new(VBProjectType.EXE, "Provider") { ContributedBy = "provider" }, project });
        var windows = Substitute.For<IWindowManager>();
        FilePickerSaveOptions? picked = null;
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>()).Returns(call =>
        {
            picked = call.Arg<FilePickerSaveOptions>();
            return Task.FromResult<string?>("chosen");
        });
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => "localized:" + call.Arg<string>());
        var service = new ProjectService(() => throw new InvalidOperationException(), windows,
            Substitute.For<IEventBus>(), projects, Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), Substitute.For<IUserSidecarService>(), new FileBaselineStore(),
            localization, Substitute.For<IHeaderRefresher>(), InBoxRegistry.Create(), contributed: types);
        var existingGroup = method == "ExistingGroupPath";
        if (existingGroup) method = "ChooseGroupPath";
        object document = method switch
        {
            "ChooseModulePath" => new ModuleDefinition(project, "Ledger", ModuleKind.ClassModule),
            "ChooseFormPath" => new FormDefinition(project, FormComponentClass.Instance, "Ledger"),
            "ChooseGroupPath" => new ProjectGroupDefinition("Ledger") { AbsolutePath = existingGroup ? "existing.vbg" : null },
            _ => project,
        };
        var arguments = method == "ChooseGroupPath" ? new[] { document } : new[] { document, null };
        await (Task<string>)typeof(ProjectService).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(service, arguments)!;

        picked.Should().NotBeNull();
        var label = method switch
        {
            "ChooseGroupPath" => "ProjectGroup",
            "ChooseFormPath" => "FormFiles",
            "ChooseModulePath" => "ModuleFiles",
            _ => "ProjectFiles",
        };
        picked!.FileTypeChoices![0].Name.Should().Be("localized:Str.FilePicker." + label);
        picked.DefaultExtension.Should().Be(extension);
        picked.FileTypeChoices![0].Patterns.Should().Equal(wholeName ? ".rdproj" : "*." + extension);
        picked.SuggestedFileName.Should().Be(wholeName ? ".rdproj" : "Ledger." + extension);
    }
    [Fact]
    public async Task A_group_without_a_writer_refuses_before_any_picker_or_write()
    {
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns(new List<ProjectDefinition>());
        projects.CurrentGroup.Returns(new ProjectGroupDefinition("Ledger"));
        var windows = Substitute.For<IWindowManager>();
        var service = new ProjectService(() => throw new InvalidOperationException(), windows,
            Substitute.For<IEventBus>(), projects, Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), Substitute.For<IUserSidecarService>(), new FileBaselineStore(),
            new LocalizationService(), Substitute.For<IHeaderRefresher>(), InBoxRegistry.Create(), contributed: new ContributedProjects());

        var save = () => service.SaveAllProjects(saveAs: true);
        await save.Should().ThrowAsync<OperationCanceledException>();

        await windows.DidNotReceive().SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>());
        await windows.Received().MessageBox("No registered project type can save this project group. Nothing was written.",
            Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), MessageBoxIcon.Warning);
    }

}
