using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using HexIDE.IDE;
using HexIDE.Runtime;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Running;

namespace HexIDE.Tests.DebugAdapter;

// Host policy (#4187), not a new VB6 measurement. The default stays big only through FormIcon:
// docs/vb6-fidelity-oracle/a-forms-window-icon-no-icon-line.md.
public class RunFormIconTests
{
    [AvaloniaFact]
    public void Every_run_prepares_the_shared_neutral_icon()
    {
        var previous = VBLoader.DefaultFormIcon;
        try
        {
            VBLoader.DefaultFormIcon = null;
            var engine = new InterpreterRunEngine(TestRunners.Services(Substitute.For<IProgramHost>()), new DebugController());
            var project = new ProjectDefinition(VBProjectType.EXE, "P") { StartsAtSubMain = true };
            engine.Prepare(project).Failure.Should().NotBeNull();

            VBLoader.DefaultFormIcon.Should().NotBeNull();
            using var actual = new MemoryStream();
            var form = new FormDefinition(project, FormComponentClass.Instance, "Form1");
            var root = form.Components.Single(c => c.BaseClass is FormComponentClass);
            var window = FormComponentClass.Instance.InstantiateWindow(root);
            window.Icon.Should().BeSameAs(VBLoader.DefaultFormIcon);
            window.Icon!.Save(actual);
            using var expected = AssetLoader.Open(new Uri("avares://HexIDE.Runtime/form.ico"));
            using var bytes = new MemoryStream();
            new WindowIcon(expected).Save(bytes);
            actual.ToArray().Should().Equal(bytes.ToArray());
        }
        finally
        {
            VBLoader.DefaultFormIcon = previous;
        }
    }

    [Fact]
    public void The_program_icon_is_not_the_IDEs_mark()
    {
        var root = RepoRoot();
        File.ReadAllBytes(Path.Combine(root, "IDE", "HexIDE.Runtime", "form.ico"))
            .Should().NotEqual(File.ReadAllBytes(Path.Combine(root, "IDE", "HexIDE", "Icons", "AppIcon", "hexide.ico")));
    }

    [AvaloniaFact]
    public void The_runner_loads_and_embeds_the_same_shared_icon()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "LspServer")))
            root = root.Parent;
        root.Should().NotBeNull();
        var runner = Path.Combine(root!.FullName, "IDE", "HexIDE.Standalone");
        var source = File.ReadAllText(Path.Combine(runner, "App.axaml.cs"));
        source.Should().Contain("avares://HexIDE.Runtime/form.ico");
        source.Should().NotContain("avares://HexIDE.Standalone/form.ico");
        var engine = File.ReadAllText(Path.Combine(root.FullName, "IDE", "HexIDE.Runtime", "Running", "InterpreterRunEngine.cs"));
        engine.Should().Contain("avares://HexIDE.Runtime/form.ico");
        // Provider boundary policy, not a VB6 measurement (#5646).
        foreach (var directory in new[] { "HexIDE.Runtime", "HexIDE.Standalone" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root.FullName, "IDE", directory), "*.cs", SearchOption.AllDirectories))
        {
            if (Path.GetRelativePath(root.FullName, file).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
                continue;
            File.ReadAllText(file).Should().NotContain("Distribution", $"{file} belongs to the provider, independent of the IDE's brand");
        }
        using var stream = AssetLoader.Open(new Uri("avares://HexIDE.Runtime/form.ico"));
        new WindowIcon(stream).Should().NotBeNull();
        using var bytes = AssetLoader.Open(new Uri("avares://HexIDE.Runtime/form.ico"));
        using var actual = new MemoryStream();
        bytes.CopyTo(actual);
        var project = System.Xml.Linq.XDocument.Load(Path.Combine(runner, "HexIDE.Standalone.csproj"));
        var path = project.Descendants("ApplicationIcon").Single().Value;
        var embedded = Path.GetFullPath(Path.Combine(runner, path.Replace('\\', Path.DirectorySeparatorChar)));
        embedded.Should().Be(Path.Combine(root.FullName, "IDE", "HexIDE.Runtime", "form.ico"));
        actual.ToArray().Should().Equal(File.ReadAllBytes(embedded));
    }

    private static string RepoRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "LspServer")))
            root = root.Parent;
        return root!.FullName;
    }
}
