using System.IO;
using HexIDE.Addins;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Sidecar;
using StandInFormat = HexIDE.Tests.Projects.ProjectFileFormatSeamTests.StandInFormat;

namespace HexIDE.Tests.Projects;

/// <summary>
/// That VB6 registers as a first-party project type, that its files and the archives' are reserved against every other
/// type whatever order they register in, and that a project file opens through the type that claims it (#4053, #3411
/// slice 10b).
/// </summary>
public sealed class Vb6ProjectTypeTests : IDisposable
{
    private static readonly AddinOrigin Other = new("other", "Other add-in");
    private static readonly AddinOrigin OtherFirstParty = new("hexide.other", "Another first-party type", IsFirstParty: true);

    private readonly string _dir = Path.Join(Path.GetTempPath(), "hexide-vb6-type-" + Guid.NewGuid().ToString("N"));
    private readonly List<ProjectDefinition> _loaded = [];
    private readonly IWindowManager _windows = Substitute.For<IWindowManager>();
    private readonly StandInFormat _format = new();

    public Vb6ProjectTypeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best effort */ }
    }

    private static AddinProjectType Reading(params string[] names) =>
        new("Other") { FileNames = names, OpenAsync = (_, _, _) => throw new NotImplementedException() };

    private static ContributedProjects WithVb6(IProjectFileFormat format)
    {
        var types = new ContributedProjects();
        Vb6ProjectType.Register(types, format);
        return types;
    }

    // ── What is reserved, and for whom ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("*.vbp", "Visual Basic 6.0")]
    [InlineData("*.VBP", "Visual Basic 6.0")]
    [InlineData("Project1.vbp", "Visual Basic 6.0")]
    [InlineData("*.vbg", "Visual Basic 6.0")]
    [InlineData("*.twinproj", "twinBASIC")]
    public void A_type_claiming_a_reserved_file_is_refused_naming_its_reader_after_VB6_registers(string name, string reader)
    {
        var types = WithVb6(_format);

        var register = () => types.Register(Reading(name), Other);

        register.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain(reader).And.Contain("Other add-in");
    }

    [Theory]
    [InlineData("*.vbp", "Visual Basic 6.0")]
    [InlineData("*.vbg", "Visual Basic 6.0")]
    [InlineData("*.twinproj", "twinBASIC")]
    public void A_type_claiming_a_reserved_file_is_refused_before_VB6_registers_too(string name, string reader)
    {
        var types = new ContributedProjects();

        var register = () => types.Register(Reading(name), Other);

        register.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain(reader);
        Vb6ProjectType.Register(types, _format);
        types.Find(Path.Join(_dir, "x.vbp"))!.AddinId.Should().Be(Vb6ProjectType.Id, "VB6's files stayed VB6's");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_file_nobody_reserves_is_accepted_before_and_after_VB6_registers(bool vb6First)
    {
        var types = vb6First ? WithVb6(_format) : new ContributedProjects();

        using var mine = types.Register(Reading("*.mypro"), Other);
        if (!vb6First)
            Vb6ProjectType.Register(types, _format);

        types.Find(Path.Join(_dir, "x.mypro"))!.AddinId.Should().Be("other");
        types.Find(Path.Join(_dir, "x.vbp"))!.AddinId.Should().Be(Vb6ProjectType.Id);
    }

    [Fact]
    public void A_first_party_type_of_another_id_cannot_take_VB6_s_files_either()
    {
        var types = new ContributedProjects();

        var register = () => types.Register(Vb6ProjectType.For(_format), OtherFirstParty);

        register.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("Visual Basic 6.0");
    }

    // ── A format of its own is first-party only ──────────────────────────────────────────────────────────────────

    [Fact]
    public void A_type_with_a_format_of_its_own_is_refused_from_an_add_in_that_is_not_first_party()
    {
        var types = new ContributedProjects();

        var register = () => types.Register(new AddinProjectType("Other") { FileNames = ["*.mypro"], Format = _format }, Other);

        register.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("Other add-in").And.Contain("format");
        types.Find(Path.Join(_dir, "x.mypro")).Should().BeNull();
    }

    [Fact]
    public void A_first_party_type_with_a_format_of_its_own_is_accepted_and_offers_everything()
    {
        var types = new ContributedProjects();

        using var mine = types.Register(new AddinProjectType("Other") { FileNames = ["*.mypro"], Format = _format }, OtherFirstParty);

        types.Find(Path.Join(_dir, "x.mypro"))!.Offers.Should().Be(ProjectOffers.Everything);
    }

    [Fact]
    public void A_type_needs_a_format_to_read_group_files()
    {
        var types = new ContributedProjects();

        var register = () => types.Register(Reading("*.mypro") with { GroupFileNames = ["*.mygrp"] }, OtherFirstParty);

        register.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void VB6_s_type_reads_its_projects_and_groups_and_offers_everything()
    {
        var types = WithVb6(_format);

        var project = types.Find(Path.Join(_dir, "x.vbp"))!;
        project.Type.Format.Should().BeSameAs(_format);
        project.Offers.Should().Be(ProjectOffers.Everything);
        types.FindGroup(Path.Join(_dir, "x.vbg"))!.AddinId.Should().Be(Vb6ProjectType.Id);
        types.Find(Path.Join(_dir, "x.vbg")).Should().BeNull("a group file isn't a project file");
    }

    // ── Opening through the type ──────────────────────────────────────────────────────────────────────────────

    private ProjectService Service(ContributedProjects types, bool withArchives = false)
    {
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns(_ => _loaded);
        projects.When(m => m.AddProject(Arg.Any<ProjectDefinition>())).Do(call => _loaded.Add(call.Arg<ProjectDefinition>()));
        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>() switch
        {
            "Str.ContributedProject.ProviderNotLoaded" => "HexIDE can't open {0} as a project: it's a {1} file, and {1} isn't loaded.",
            "Str.ContributedProject.NoLoadedReader" => "HexIDE can't open {0} as a project: no loaded project type reads it. The loaded types read {1}.",
            "Str.ContributedProject.NoTypeLoaded" => "HexIDE can't open {0} as a project: no project type is loaded.",
            var key => key + " {0}",
        });
        var archives = withArchives ? new ArchiveProjectService(Substitute.For<IDocumentDockService>(),
            Substitute.For<IRecentProjectsService>(), localization, _windows,
            Substitute.For<HexIDE.Language.IConditionalCodeReader>()) : null;
        return new ProjectService(
            () => throw new InvalidOperationException("no new-project dialog here"), _windows, Substitute.For<IEventBus>(),
            projects, Substitute.For<IRecentProjectsService>(), Substitute.For<IReferenceLibraryService>(), sidecar,
            new FileBaselineStore(), localization, Substitute.For<IHeaderRefresher>(),
            InBoxRegistry.Create(), archives: archives, contributed: types);
    }

    private string Vbp()
    {
        var path = Path.Join(_dir, "Test.vbp");
        File.WriteAllText(path, "not a project file");
        return path;
    }

    [Fact]
    public async Task A_vbp_opens_through_VB6_s_registered_type_and_belongs_to_it()
    {
        await Service(WithVb6(_format)).OpenProject(Vbp());

        var project = _loaded.Should().ContainSingle().Subject;
        project.Format.Should().BeSameAs(_format, "the type's format read it, not the one the service was given");
        project.ContributedBy.Should().Be(Vb6ProjectType.Id);
        project.ContributorTitle.Should().Be("Visual Basic 6.0");
        project.Offers.Should().Be(ProjectOffers.Everything);
    }

    [Theory]
    [InlineData("Test.vbp")]
    [InlineData("Test.vbg")]
    [InlineData("Test.frm")]
    [InlineData("Test.bas")]
    [InlineData("Test.ctl")]
    [InlineData("Test.pag")]
    [InlineData("Test.dsr")]
    [InlineData("Test.dob")]
    public async Task An_absent_VB6_reader_is_named_for_its_projects_and_members(string file)
    {
        await Service(new ContributedProjects()).OpenProject(Path.Join(_dir, file));

        await Refused($"HexIDE can't open {file} as a project: it's a Visual Basic 6.0 file, and Visual Basic 6.0 isn't loaded.");
    }

    // A .cls is equally a LaTeX class file (#279), so with VB6's type absent it isn't called VB6's: it gets what any
    // other unread file gets.
    [Fact]
    public async Task A_cls_is_not_called_a_VB6_file_since_LaTeX_uses_the_extension_too()
    {
        await Service(new ContributedProjects()).OpenProject(Path.Join(_dir, "Test.cls"));

        await Refused("HexIDE can't open Test.cls as a project: no project type is loaded.");
    }

    [Fact]
    public async Task An_unclaimed_file_lists_registered_projects_groups_and_archives()
    {
        await Service(WithVb6(_format), withArchives: true).OpenProject(Path.Join(_dir, "notes.txt"));

        await Refused("HexIDE can't open notes.txt as a project: no loaded project type reads it. The loaded types read .vbp, .vbg, .twinproj.");
    }

    [Fact]
    public async Task An_add_in_adds_its_patterns_in_registration_order_without_naming_the_add_in()
    {
        var types = WithVb6(_format);
        using var added = types.Register(Reading("*.mypro", "Projectfile"), Other);

        await Service(types, withArchives: true).OpenProject(Path.Join(_dir, "notes.txt"));

        await Refused("HexIDE can't open notes.txt as a project: no loaded project type reads it. The loaded types read .vbp, .vbg, .mypro, Projectfile, .twinproj.");
    }

    [Fact]
    public async Task An_absent_archive_reader_is_named()
    {
        await Service(new ContributedProjects()).OpenProject(Path.Join(_dir, "Test.twinproj"));

        await Refused("HexIDE can't open Test.twinproj as a project: it's a twinBASIC file, and twinBASIC isn't loaded.");
    }

    [Fact]
    public async Task With_no_reader_loaded_an_unknown_file_says_so()
    {
        await Service(new ContributedProjects()).OpenProject(Path.Join(_dir, "notes.txt"));

        await Refused("HexIDE can't open notes.txt as a project: no project type is loaded.");
    }

    [Fact]
    public async Task An_absent_archive_service_is_not_listed()
    {
        await Service(WithVb6(_format)).OpenProject(Path.Join(_dir, "notes.txt"));

        await Refused("HexIDE can't open notes.txt as a project: no loaded project type reads it. The loaded types read .vbp, .vbg.");
    }

    private async Task Refused(string message)
    {
        _loaded.Should().BeEmpty();
        message.Should().NotContain("add-in");
        await _windows.Received(1).MessageBox(message, Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(),
            MessageBoxIcon.Information, Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task A_vbg_opens_through_the_format_of_the_type_that_reads_group_files()
    {
        var vbg = Path.Join(_dir, "Group.vbg");
        File.WriteAllText(vbg, "not a group");
        Vbp();
        _format.Group = new("Test.vbp", ["Test.vbp"], []);

        await Service(WithVb6(_format)).OpenProject(vbg);

        _loaded.Should().ContainSingle().Which.ContributedBy.Should().Be(Vb6ProjectType.Id);
    }
}
