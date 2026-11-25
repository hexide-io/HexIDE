using HexIDE.Tests.Infrastructure;
using System.IO;
using Avalonia.Headless.XUnit;
using HexIDE.Localization;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.Projects;

public sealed class NamedFormFormatTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("hexide-named-forms-").FullName;
    private readonly ContributedProjects types = new();
    private static readonly AddinOrigin Origin = new("probe", "Probe");

    private AddinProjectType Type(string id)
    {
        return new AddinProjectType("Probe")
        {
            FileNames = ["*.probe"], FormFormat = id,
            OpenAsync = (_, _, _) => throw new NotSupportedException(),
        };
    }

    private AddinProjectMember Member(string name, AddinMemberKind kind, string text)
    {
        var path = Path.Join(directory, name);
        File.WriteAllText(path, text);
        return new(path, kind);
    }

    private Task<(ProjectDefinition Project, IReadOnlyList<string> Problems)> Build(AddinProjectContent content) =>
        types.BuildAsync(Path.Join(directory, "Project.probe"), content, types.Find("Project.probe")!,
            new Vb6TextEncoding(1252, false), null, new LocalizationService());

    [AvaloniaTheory]
    [InlineData(AddinMemberKind.Form, "Form", "frm")]
    [InlineData(AddinMemberKind.UserControl, "UserControl", "ctl")]
    [InlineData(AddinMemberKind.PropertyPage, "PropertyPage", "pag")]
    public async Task A_named_format_resolves_after_registration_and_writes_each_designer_kind(
        AddinMemberKind kind, string root, string extension)
    {
        using var registration = types.Register(Type("vb6"), Origin);
        var type = types.Find("Project.probe")!;
        type.Offers.WritesForms.Should().BeFalse();
        using var vb6 = Vb6Types.Register(types, new Vb6ProjectFileFormat());
        type.Offers.WritesForms.Should().BeTrue();
        var text = $"VERSION 5.00\r\nBegin VB.{root} Test\r\n   Caption = \"Test\"\r\nEnd\r\nAttribute VB_Name = \"Test\"\r\n";
        var (project, problems) = await Build(new("Test") { Members = [Member("Test." + extension, kind, text)] });
        problems.Should().BeEmpty();
        var form = kind == AddinMemberKind.Form ? project.Forms.Single() : project.Modules.Single().FormPart!;
        form.CanSaveFaithfully.Should().BeTrue();
        project.Offers.WritesForms.Should().BeTrue();
        project.FormFormat!.RenderDesignerHeader(form, "Test." + extension).Should().Contain("Begin VB." + root);
        project.FormFormat.CompanionPath(Path.Join(directory, "Test." + extension)).Should().NotBeNull();
        vb6.Dispose();
        type.Offers.WritesForms.Should().BeFalse();
        var render = () => project.FormFormat.RenderDesignerHeader(form, "Test." + extension);
        render.Should().Throw<RenderRefusal>();
    }

    [AvaloniaFact]
    public async Task An_unknown_id_names_every_omitted_form_and_keeps_modules()
    {
        using var registration = types.Register(Type("absent"), Origin);
        using var vb6 = Vb6Types.Register(types, new Vb6ProjectFileFormat());
        var (project, problems) = await Build(new("Test")
        {
            Members = [Member("First.frm", AddinMemberKind.Form, "unread"),
                Member("Second.ctl", AddinMemberKind.UserControl, "unread"),
                Member("Code.bas", AddinMemberKind.StandardModule, "Attribute VB_Name = \"Code\"\r\n")],
        });
        project.Forms.Should().BeEmpty();
        project.Modules.Should().ContainSingle().Which.Name.Should().Be("Code");
        problems.Should().HaveCount(2);
        problems.Should().Contain(p => p.Contains("First.frm") && p.Contains("no installed format reads 'absent' forms"));
        problems.Should().Contain(p => p.Contains("Second.ctl") && p.Contains("no installed format reads 'absent' forms"));
    }

    [Fact]
    public void A_type_cannot_name_a_format_and_supply_its_own_reader()
    {
        var type = Type("vb6") with { Forms = new Vb6ProjectFileFormat() };
        var register = () => types.Register(type, Origin);
        register.Should().Throw<ArgumentException>();
    }

    [AvaloniaFact]
    public async Task A_form_loads_without_project_components_but_its_library_is_absent_from_the_toolbox()
    {
        using var vb6 = Vb6Types.Register(types, new Vb6ProjectFileFormat());
        using var registration = types.Register(new AddinProjectType("Probe")
        {
            FileNames = ["*.probe"], Forms = new Vb6ProjectFileFormat(),
            OpenAsync = (_, _, _) => throw new NotSupportedException(),
        }, Origin);
        var text = "VERSION 5.00\r\nObject = \"{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0\"; \"MSCOMCTL.OCX\"\r\nBegin VB.Form Test\r\n   Begin MSComctlLib.ListView ListView1\r\n   End\r\nEnd\r\nAttribute VB_Name = \"Test\"\r\n";
        var (project, problems) = await Build(new("Test") { Members = [Member("Test.frm", AddinMemberKind.Form, text)] });
        problems.Should().BeEmpty();
        project.Forms.Single().Components.Should().Contain(c => c.BaseClass is ListViewComponentClass);
        var toolbox = new ToolBoxToolViewModel(Substitute.For<IDocumentDockService>(), InBoxRegistry.Create(), new LocalizationService());
        toolbox.ShowProject(project);
        toolbox.Components.Should().NotContain(c => c.VBTypeName == "MSComctlLib.ListView");
    }

    [AvaloniaFact]
    public async Task Listed_components_reach_the_project_and_its_toolbox()
    {
        using var vb6 = Vb6Types.Register(types, new Vb6ProjectFileFormat());
        using var registration = types.Register(new AddinProjectType("Probe")
        {
            FileNames = ["*.probe"], OpenAsync = (_, _, _) => throw new NotSupportedException(),
        }, Origin);
        var content = new AddinProjectContent("Test")
        {
            Components = [new AddinProjectComponent
            {
                Guid = new Guid("831FDD16-0C5C-11D2-A9FC-0000F8754DA1"), Major = 2, Minor = 0, FileName = "MSCOMCTL.OCX",
            }, new AddinProjectComponent
            {
                Guid = Guid.Empty, Major = 10, Minor = 11, FileName = "Probe.ocx",
            }],
        };
        var (project, problems) = await Build(content);
        problems.Should().BeEmpty();
        project.ObjectReferences.Should().Equal(
            new ObjectReference("{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}", "2.0", "MSCOMCTL.OCX"),
            new ObjectReference("{00000000-0000-0000-0000-000000000000}", "a.b", "Probe.ocx"));
        var toolbox = new ToolBoxToolViewModel(Substitute.For<IDocumentDockService>(), InBoxRegistry.Create(), new LocalizationService());
        toolbox.ShowProject(project);
        toolbox.Components.Should().Contain(c => c.VBTypeName == "MSComctlLib.ListView");
    }

    public void Dispose() => Directory.Delete(directory, true);
}
