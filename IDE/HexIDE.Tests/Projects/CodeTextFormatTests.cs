using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Tests.Projects;

public class CodeTextFormatTests
{
    [Fact]
    public void FormatWithoutVbHeadersAnswersNeutralDefaults()
    {
        IProjectFileFormat format = new StandInFormat();
        format.AttributeBlock("Attribute VB_Name = \"Other\"\r\n").Should().BeEmpty();
        format.ModuleNameFor("Fallback", "Attribute VB_Name = \"Other\"\r\n").Should().Be("Fallback");
        format.IsDesignerFile("VERSION 5.00\r\nBegin VB.Form Form1\r\nEnd\r\n").Should().BeFalse();
    }

    [Fact]
    public async Task WithoutMemberFormatContributedModuleKeepsItsWholeText()
    {
        const string text = "Attribute VB_Name = \"Module1\"\r\nOption Explicit\r\n";
        var types = new ContributedProjects { ReadFile = _ => Task.FromResult(System.Text.Encoding.UTF8.GetBytes(text)) };
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1");
        var path = Path.GetFullPath("Module1.bas");

        var member = await types.AddMemberAsync(project, new AddinProjectMember(path, AddinMemberKind.StandardModule),
            Vb6TextEncoding.Ansi, null, new LocalizationService());

        var module = member.Should().BeOfType<ModuleDefinition>().Subject;
        module.OriginalHeader.Should().BeEmpty();
        module.Code.Should().Be(text);
    }
}
