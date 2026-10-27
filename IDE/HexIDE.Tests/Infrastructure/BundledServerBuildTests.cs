using System.Diagnostics;
using System.Xml.Linq;

namespace HexIDE.Tests.Infrastructure;

public class BundledServerBuildTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DependencyFollowsReferencedOutputForBuildAndPublish(bool artifactsLayout)
    {
        var root = Path.Combine(RepoTree.Root(), "artifacts", "bundled-server-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var desktop = XDocument.Load(Path.Combine(RepoTree.Root(), "IDE", "HexIDE.Desktop", "HexIDE.Desktop.csproj"));
            // Exercise the production content rules and target with a tiny server, without rebuilding the IDE.
            var content = desktop.Root!.Elements("ItemGroup").Elements("Content").Where(e => e.Attribute("Include")!.Value.Contains("LanguageServer.Framework.dll"));
            var target = desktop.Root!.Elements("Target").Where(e => (string?)e.Attribute("Name") == "IncludeBundledServerDependency");
            var server = Path.Combine(root, "LspServer", "HexIDE.VbLspServer");
            var client = Path.Combine(root, "IDE", "Desktop");
            Directory.CreateDirectory(server);
            Directory.CreateDirectory(client);
            File.WriteAllText(Path.Combine(server, "HexIDE.VbLspServer.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                  <Target Name="WriteDependency" AfterTargets="Build">
                    <WriteLinesToFile File="$(TargetDir)LanguageServer.Framework.dll" Lines="from referenced server" Overwrite="true" />
                  </Target>
                </Project>
                """);
            var project = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0")),
                new XElement("ItemGroup", new XElement("ProjectReference",
                    new XAttribute("Include", "../../LspServer/HexIDE.VbLspServer/HexIDE.VbLspServer.csproj"),
                    new XAttribute("ReferenceOutputAssembly", "false"),
                    new XAttribute("OutputItemType", "Content"),
                    new XAttribute("CopyToOutputDirectory", "PreserveNewest")), content.Select(e => new XElement(e))),
                target.Select(e => new XElement(e)));
            var path = Path.Combine(client, "Desktop.csproj");
            project.Save(path);
            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("publish");
            start.ArgumentList.Add("-m:1");
            start.ArgumentList.Add("-p:NuGetAudit=false");
            start.ArgumentList.Add(path);
            start.ArgumentList.Add("--output");
            start.ArgumentList.Add(Path.Combine(root, "published"));
            if (artifactsLayout)
            {
                start.ArgumentList.Add("--artifacts-path");
                start.ArgumentList.Add(Path.Combine(root, "outputs"));
            }
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            var finished = process.WaitForExit(120_000);
            if (!finished)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
            finished.Should().BeTrue("the small MSBuild fixture should finish");
            process.ExitCode.Should().Be(0, (await output) + (await error));
            var buildOutput = artifactsLayout
                ? Path.Combine(root, "outputs", "bin", "Desktop", "release")
                : Path.Combine(client, "bin", "Release", "net10.0");
            foreach (var folder in new[] { buildOutput, Path.Combine(root, "published") })
                File.ReadAllText(Path.Combine(folder, "LanguageServer.Framework.dll")).Trim()
                    .Should().Be("from referenced server");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
