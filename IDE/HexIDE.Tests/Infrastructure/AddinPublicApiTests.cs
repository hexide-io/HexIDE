using System.Diagnostics;

namespace HexIDE.Tests.Infrastructure;

public class AddinPublicApiTests
{
    [Theory]
    [InlineData("positional")]
    [InlineData("reordered")]
    [InlineData("removed")]
    [InlineData("addition")]
    [InlineData("outside")]
    [InlineData("generated")]
    public async Task BuildEnforcesOnlyTheAddinContract(string mutation)
    {
        var repository = RepoTree.Root();
        var root = Path.Combine(repository, "artifacts", "addin-api-tests", Guid.NewGuid().ToString("N"));
        var core = Path.Combine(root, "Core");
        Directory.CreateDirectory(core);
        try
        {
            var source = Path.Combine(repository, "IDE", "HexIDE.Core");
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file);
                if (relative.Split(Path.DirectorySeparatorChar)[0] is "bin" or "obj")
                    continue;
                var destination = Path.Combine(core, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
            File.Copy(Path.Combine(repository, "IDE", "Directory.Build.props"), Path.Combine(root, "Directory.Build.props"));
            File.Copy(Path.Combine(repository, "Directory.Packages.props"), Path.Combine(root, "Directory.Packages.props"));

            var editorTypes = Path.Combine(core, "Addins", "AddinEditorTypes.cs");
            // Keep the fixture's own call valid when parameters move: the SDK guard must reject the change.
            var original = File.ReadAllText(editorTypes).Replace("new(false, \"Refused.", "new(false, Reason: \"Refused.");
            File.WriteAllText(editorTypes, original);
            var initial = await Build(core);
            initial.ExitCode.Should().Be(0, initial.Output);
            switch (mutation)
            {
                case "positional":
                    File.WriteAllText(editorTypes, original.Replace("bool HeaderKept = false)", "bool HeaderKept = false, int ApiProbe = 0)"));
                    break;
                case "reordered":
                    File.WriteAllText(editorTypes, original.Replace("string? Reason = null, bool HeaderKept = false)", "bool HeaderKept = false, string? Reason = null)"));
                    break;
                case "removed":
                    File.WriteAllText(editorTypes, original.Replace("public bool Refused => Reason is not null;", "internal bool Refused => Reason is not null;"));
                    break;
                case "generated":
                    var consent = Path.Combine(core, "Addins", "AddinConsent.cs");
                    File.WriteAllText(consent, File.ReadAllText(consent).Replace(
                        "public partial class AddinConsentJsonContext", "[JsonSerializable(typeof(Guid))]\npublic partial class AddinConsentJsonContext"));
                    break;
                case "addition":
                case "outside":
                    var folder = mutation == "addition" ? Path.Combine(core, "Addins") : core;
                    var ns = mutation == "addition" ? "HexIDE.Addins" : "HexIDE.ApiProbe";
                    File.WriteAllText(Path.Combine(folder, "ApiProbe.cs"), $"namespace {ns}; public static class ApiProbe {{ public static int Value => 1; }}");
                    break;
            }

            var changed = await Build(core);
            if (mutation == "outside")
            {
                changed.ExitCode.Should().Be(0, changed.Output);
                changed.Output.Should().Contain("0 Warning(s)");
            }
            else
            {
                changed.ExitCode.Should().NotBe(0, changed.Output);
                changed.Output.Should().NotContain(": error CS", "only the API guard should reject these valid sources");
                if (mutation == "addition")
                {
                    changed.Output.Should().Contain("RS0016").And.Contain("ApiProbe.Value.get");
                    File.AppendAllText(Path.Combine(core, "PublicAPI.Unshipped.txt"),
                        "HexIDE.Addins.ApiProbe\nstatic HexIDE.Addins.ApiProbe.Value.get -> int\n");
                    var recorded = await Build(core);
                    recorded.ExitCode.Should().Be(0, recorded.Output);
                }
                else if (mutation == "generated")
                    changed.Output.Should().Contain("RS0016").And.Contain("AddinConsentJsonContext.Guid.get");
                else if (mutation == "removed")
                    changed.Output.Should().Contain("RS0017").And.Contain("AddinDiagnosticsResult.Refused.get");
                else
                    changed.Output.Should().Contain("RS0016").And.Contain("RS0017")
                        .And.Contain("AddinEditResult.AddinEditResult(").And.Contain("AddinEditResult.Deconstruct(");
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static async Task<(int ExitCode, string Output)> Build(string directory)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { "build", "HexIDE.Core.csproj", "--no-incremental", "-m:1", "-p:NuGetAudit=false" }
        };
        start.Environment["UseSharedCompilation"] = "false";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return (process.ExitCode, await output + await error);
    }
}
