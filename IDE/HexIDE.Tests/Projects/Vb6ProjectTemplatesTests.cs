using System.IO;
using Avalonia.Platform.Storage;
using HexIDE.Addins;
using HexIDE.Forms.ViewModels;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Projects;

/// <summary>
/// That VB6's templates are offered as its type registers them, that <c>--newproject</c> takes the first Standard EXE
/// registered, and that both Open lists are the registered types' files (#4053, #3411 slice 10b).
/// </summary>
public sealed class Vb6ProjectTemplatesTests
{
    private static readonly AddinOrigin Rdcore = new("rdcore", "RDCore for HexIDE");
    private static readonly string[] OfficePatterns = ["*.xlsm", "*.xltm", "*.xlam", "*.xlsb", "*.docm", "*.dotm",
        "*.pptm", "*.potm", "*.ppsm", "*.ppam", "*.xls", "*.xlt", "*.xla", "*.doc", "*.dot"];

    private static AddinProjectTemplateService WithVb6()
    {
        var templates = new AddinProjectTemplateService();
        Vb6ProjectType.RegisterTemplates(templates);
        return templates;
    }

    // ── Templates ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_profile_offers_the_first_party_templates_that_are_registered()
    {
        var registered = WithVb6();

        new ProfileService((HexIDE.Profiles.ProfileSnapshot?)null, registered).AvailableProjectTypes
            .Should().Equal(Vb6ProjectType.Templates);
        new ProfileService((HexIDE.Profiles.ProfileSnapshot?)null, new AddinProjectTemplateService()).AvailableProjectTypes
            .Should().BeEmpty("with no project type registered, no template is offered");
    }

    [Fact]
    public void An_add_in_s_template_is_not_a_first_party_one()
    {
        var registered = WithVb6();
        var mine = Substitute.For<IAddinProjectTemplate>();
        registered.Register(mine);

        registered.FirstParty.Select(t => t.Template).Should().Equal(Vb6ProjectType.Templates);
        registered.Templates.Should().Equal(mine);
        registered.FirstPartyTemplate("standard-exe").Should().BeSameAs(Vb6ProjectType.StandardExe);
        registered.FirstPartyTemplate("no-such-template").Should().BeNull();
    }

    private static ProjectService Service(IProjectManager projects, AddinProjectTemplateService? templates)
    {
        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        return new ProjectService(            () => new NewProjectViewModel(Substitute.For<IWindowManager>(), Substitute.For<IRecentProjectsService>(),
                Substitute.For<ISettingsService>(), Substitute.For<IProfileService>(), Substitute.For<ILocalizationService>()),
            Substitute.For<IWindowManager>(), Substitute.For<IEventBus>(), projects, Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), sidecar, new FileBaselineStore(), Substitute.For<ILocalizationService>(),
            Substitute.For<IHeaderRefresher>(), InBoxRegistry.Create(), templates: templates);
    }

    [Fact]
    public async Task The_default_project_is_made_from_the_first_Standard_EXE_registered()
    {
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([]);
        var registered = new AddinProjectTemplateService();
        var first = Substitute.For<IProjectTemplate>();
        first.Supported.Returns(true);
        registered.RegisterFirstParty("standard-exe", first);
        Vb6ProjectType.RegisterTemplates(registered);

        await Service(projects, registered).CreateDefaultProject();

        projects.Received(1).NewProject(first, Arg.Any<string>(), Arg.Any<Action<ProjectDefinition>>());
    }

    [Fact]
    public async Task No_project_is_made_without_Standard_EXE()
    {
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([]);

        var make = () => Service(projects, new AddinProjectTemplateService()).CreateDefaultProject();

        await make.Should().NotThrowAsync();
        projects.DidNotReceiveWithAnyArgs().NewProject(default!, default!, default);
    }

    // ── The Open lists ─────────────────────────────────────────────────────────────────────────────────────────

    private static ContributedProjects Types(bool withAddin)
    {
        var types = new ContributedProjects();
        Vb6ProjectType.Register(types, new Vb6ProjectFileFormat());
        if (withAddin)
            types.Register(new AddinProjectType("RDCore projects")
            {
                FileNames = [".rdproj"],
                OpenAsync = (_, _, _) => throw new NotImplementedException(),
            }, Rdcore);
        return types;
    }

    [Theory]
    [InlineData(false, new[] { "*.vbp", "*.vbg", "*.twinproj" })]
    [InlineData(true, new[] { "*.vbp", "*.vbg", "*.twinproj", ".rdproj" })]
    public void Open_offers_the_registered_types_files_VB6_s_first_then_the_archives_then_add_ins(bool withAddin,
        string[] expected) =>
        Types(withAddin).OpenablePatterns.Should().Equal(expected.Take(3).Concat(OfficePatterns).Concat(expected.Skip(3)));

    [Fact]
    public void The_existing_project_tab_offers_what_it_is_given()
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.Office.ProjectFiles").Returns("Project and Office files");
        localization.GetString("Str.Office.AllFiles").Returns("All files");
        var browser = new FileBrowserViewModel(localization);
        var expected = new[] { "*.vbp", "*.vbg", "*.twinproj" }.Concat(OfficePatterns).ToArray();
        browser.FileTypeFilters[0].Should().Be($"Project and Office files ({string.Join(", ", expected)})");

        browser.OfferProjects(Types(withAddin: true).OpenablePatterns);

        browser.FileTypeFilters.Should().Equal($"Project and Office files ({string.Join(", ", expected.Append(".rdproj"))})", "All files (*.*)");
    }

    [Fact]
    public async Task The_Open_Project_picker_lists_VB6_s_groups_and_projects_apart_and_each_add_in_type_after()
    {
        var windows = Substitute.For<IWindowManager>();
        FilePickerOpenOptions? asked = null;
        windows.OpenFilePickerAsync(Arg.Do<FilePickerOpenOptions>(o => asked = o))
            .Returns(Task.FromResult<IReadOnlyList<string>?>(null));
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([]);
        var sidecar = Substitute.For<IUserSidecarService>();
        var service = new ProjectService(            () => throw new InvalidOperationException("no new-project dialog here"), windows, Substitute.For<IEventBus>(), projects,
            Substitute.For<IRecentProjectsService>(), Substitute.For<IReferenceLibraryService>(), sidecar, new FileBaselineStore(),
            new LocalizationService(), Substitute.For<IHeaderRefresher>(),             InBoxRegistry.Create(), contributed: Types(withAddin: true));

        var open = () => service.OpenProject();

        await open.Should().ThrowAsync<OperationCanceledException>();
        asked!.FileTypeFilter!.Select(f => $"{f.Name}: {string.Join(" ", f.Patterns!)}").Should().Equal(
            $"Project Files: *.vbp *.vbg *.twinproj {string.Join(" ", OfficePatterns)} .rdproj",
            "Project Group: *.vbg",
            "Project: *.vbp",
            "RDCore projects: .rdproj",
            "All Files: *.*");
    }
}
