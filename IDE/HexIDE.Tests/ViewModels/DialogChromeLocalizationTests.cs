using System.IO;
using System.Text.Json;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.ViewModels;

/// <summary>
/// Dialog chrome set from C# rather than AXAML (#3384), which <see cref="LocalizationCoverageTests"/> cannot see:
/// that test reads AXAML keys only, so a <c>GetString</c> key missing from the canonical pack would render raw.
/// </summary>
public class DialogChromeLocalizationTests
{
    private readonly ILocalizationService _localization = Substitute.For<ILocalizationService>();

    private static ProjectDefinition Project(VBProjectType type = VBProjectType.EXE) => new(type, "Project1");

    private static string? NoObjection(string _) => null;

    [Fact]
    public void ProjectProperties_TakesItsTitleAndTypeNameFromTheLanguagePack()
    {
        _localization.GetString("Str.Dialog.ProjectProperties.TitleFormat").Returns("{0} / Eigenschaften");
        _localization.GetString("Str.ProjectType.StandardEXE").Returns("Standard-EXE");

        var vm = new ProjectPropertiesViewModel(Project(), NoObjection, _localization);

        vm.Title.Should().Be("Project1 / Eigenschaften");
        vm.ProjectTypes.Should().ContainSingle().Which.ToString().Should().Be("Standard-EXE",
            "Project Type used to show the raw enum name, EXE");
    }

    [Fact]
    public void ProjectType_StaysTheStoredValueWhateverTheRowReads()
    {
        _localization.GetString("Str.ProjectType.StandardEXE").Returns("Standard-EXE");
        var project = Project();
        var vm = new ProjectPropertiesViewModel(project, NoObjection, _localization);

        vm.SelectedProjectTypeChoice.Should().Be(vm.ProjectTypes[0]);
        vm.SelectedProjectTypeChoice = vm.ProjectTypes[0];
        vm.Apply(project);

        project.ProjectType.Should().Be(VBProjectType.EXE);
    }

    [Fact]
    public void AProjectTypeTheListDoesNotOffer_IsLeftAloneOnOk()
    {
        var project = Project(VBProjectType.OleDll);
        var vm = new ProjectPropertiesViewModel(project, NoObjection, _localization);

        vm.SelectedProjectTypeChoice.Should().BeNull();
        vm.Apply(project);

        project.ProjectType.Should().Be(VBProjectType.OleDll);
    }

    [Fact]
    public void Components_TakesItsTitleFromTheLanguagePack()
    {
        _localization.GetString("Str.Dialog.Components.Title").Returns("Komponenten");

        new ComponentsViewModel(Project(), localization: _localization).Title.Should().Be("Komponenten");
    }

    [Fact]
    public void AddProcedure_TakesItsTitleFromTheLanguagePack()
    {
        _localization.GetString("Str.Dialog.AddProcedure.Title").Returns("Prozedur hinzufügen");

        new AddProcedureViewModel(_localization).Title.Should().Be("Prozedur hinzufügen");
    }

    [Fact]
    public void References_TakesItsTitleFromTheLanguagePack()
    {
        _localization.GetString("Str.Dialog.References.TitleFormat").Returns("Verweise - {0}");

        var vm = new ReferencesViewModel(Project(), Substitute.For<IReferenceLibraryService>(),
            Substitute.For<IWindowManager>(), _localization);

        vm.Title.Should().Be("Verweise - Project1");
    }

    [AvaloniaFact]
    public void ATemplate_ReadsInTheLanguageAndKeepsItsEnglishName()
    {
        _localization.GetString("Str.ProjectType.DataProject").Returns("Datenprojekt");
        var template = IProjectTemplate.Templates.Single(t => t.NameKey == "Str.ProjectType.DataProject");

        var row = new ProjectTemplateViewModel(template, _localization);

        row.DisplayName.Should().Be("Datenprojekt");
        row.Name.Should().Be("Data Project", "automation and the profile catalogue identify a template by this");
    }

    [AvaloniaFact]
    public void EveryBuiltInTemplateNamesAKeyTheCanonicalPackHas()
    {
        var strings = CanonicalStrings();

        foreach (var template in IProjectTemplate.Templates)
            strings.Should().ContainKey(template.NameKey!, template.Name);
    }

    [Theory]
    [InlineData("Str.Dialog.Components.Title")]
    [InlineData("Str.Dialog.ProjectProperties.TitleFormat")]
    [InlineData("Str.Dialog.References.TitleFormat")]
    [InlineData("Str.Dialog.References.SelectTypeLibrary")]
    [InlineData("Str.Dialog.AddProcedure.Title")]
    [InlineData("Str.Dialog.MenuEditor.Title")]
    [InlineData("Str.Dialog.About.Tagline")]
    [InlineData("Str.Shell.NotImplemented")]
    public void KeyReadFromCode_IsInTheCanonicalPack(string key)
    {
        CanonicalStrings().Should().ContainKey(key);
    }

    private static Dictionary<string, string> CanonicalStrings()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var en = Path.Combine(dir.FullName, "IDE", "HexIDE", "Localization", "Packs", "en.json");
            if (File.Exists(en))
                return JsonSerializer.Deserialize(File.ReadAllText(en), LocalizationJsonContext.Default.LanguagePackRecord)!.Strings!;
        }
        throw new DirectoryNotFoundException("Could not locate the canonical language pack");
    }
}
