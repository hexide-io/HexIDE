using System;
using System.IO;
using System.Linq;

namespace HexIDE.Integration.Tests;

public class DialogDependencyTests
{
    [Fact]
    public void The_IDE_names_no_runtime_dialog()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "IDE", "HexIDE")))
            root = root.Parent;
        var project = Path.Combine(root!.FullName, "IDE", "HexIDE");
        var files = Directory.EnumerateFiles(project, "*", SearchOption.AllDirectories)
            .Where(file => Path.GetExtension(file) is ".cs" or ".axaml")
            .Where(file => !Path.GetRelativePath(project, file).Split('/', '\\').Any(part => part is "bin" or "obj"))
            .ToArray();
        files.Should().NotBeEmpty();
        files.Where(file => File.ReadAllText(file).Contains("HexIDE.Runtime.Dialogs", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(project, file)).Should().BeEmpty(
                "IDE prompts belong to the IDE, and a program's boxes are its engine's own (#3513, #5616)");
    }
}
