using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.Projects;

public class CodeTextFormatTests
{
    [Fact]
    public void FormatWithoutVbHeadersAnswersNeutralDefaults()
    {
        IProjectFileFormat format = new ProjectFileFormatSeamTests.StandInFormat();
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

    [Theory]
    [InlineData("Attribute VB_Name = \"Module1\"\r\nOption Explicit\r\n")]
    [InlineData("VERSION 1.0 CLASS\r\nBEGIN\r\nEND\r\nAttribute VB_Name = \"Class1\"\r\nAttribute VB_Exposed = False\r\nOption Explicit\r\n")]
    [InlineData("VERSION 5.00\r\nBegin VB.Form Form1\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n")]
    public void VbFormatAnswersTheExistingTextRules(string text)
    {
        IProjectFileFormat format = new Vb6ProjectFileFormat();
        format.AttributeBlock(text).Should().Be(FormCodeText.AttributeBlock(text));
        var code = text[text.IndexOf("Attribute ", StringComparison.Ordinal)..];
        format.AttributeBlock(code).Should().Be(FormCodeText.AttributeBlock(code));
        format.ModuleNameFor("Fallback", text).Should().Be(ModuleFileFormat.ModuleNameFor("Fallback", text));
        format.IsDesignerFile(text).Should().Be(ModuleFileFormat.IsDesignerFile(text));
    }
}
