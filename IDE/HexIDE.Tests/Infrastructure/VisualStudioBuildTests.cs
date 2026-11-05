using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace HexIDE.Tests.Infrastructure;

public class VisualStudioBuildTests
{
    [Fact]
    public void RoslynReferencesDoNotDependOnTheBuildHostsCompilerLayout()
    {
        var project = XDocument.Load(Path.Combine(RepoTree.Root(), "IDE", "HexIDE.Tests", "HexIDE.Tests.csproj"));

        project.Descendants("PackageReference").Select(item => (string?)item.Attribute("Include"))
            .Should().Contain("Microsoft.CodeAnalysis.CSharp");
        project.Descendants("Reference").Where(item =>
                ((string?)item.Attribute("Include"))?.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal) == true)
            .Should().BeEmpty("Visual Studio and dotnet MSBuild have different compiler folders (#3124)");
    }

    [Fact]
    public void WindowsCiBuildsTheSolutionWithVisualStudioMsBuild()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoTree.Root(), ".github", "workflows", "build.yml"))
            .ReplaceLineEndings("\n");
        var desktop = Regex.Match(workflow, @"(?ms)^  build-ide-desktop:\n(.*?)(?=^  [\w-]+:|\z)").Value;
        var step = Regex.Matches(desktop, @"(?ms)^      - name: .*?\n.*?(?=^      - name:|\z)")
            .Select(match => match.Value).SingleOrDefault(value => value.Contains("vswhere.exe", StringComparison.Ordinal));

        step.Should().NotBeNull("dotnet build cannot catch Visual Studio's different Roslyn layout (#3124)");
        step!.Should().Contain("if: runner.os == 'Windows'");
        step.Should().Contain("working-directory: .");
        step.Should().Contain("shell: pwsh");
        step.Should().Contain("-requires Microsoft.Component.MSBuild");
        step.Should().Contain("-find MSBuild\\**\\Bin\\MSBuild.exe");
        step.Should().Contain("& $msbuild HexIDE.slnx -restore -m");
        step.Should().Contain("exit $LASTEXITCODE");
    }
}
