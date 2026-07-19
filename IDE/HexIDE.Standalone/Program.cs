using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Avalonia;
using Avalonia.Media.Fonts;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using Classic.CommonControls;

namespace HexIDE.Standalone;

public class Program
{
    public static FormDefinition? StartupForm;
    public static string? Error;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--check")
        {
            Environment.Exit(RunCheck(args[1]));
        }

        try
        {
            LoadProject(args);
        }
        catch (Exception e)
        {
            Error = e.Message;
        }

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    private static int RunCheck(string projectPath)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (!File.Exists(projectPath))
        {
            Console.WriteLine($"Error: file not found: {projectPath}");
            return 1;
        }

        Console.WriteLine($"Checking: {projectPath}");
        Console.WriteLine();

        var projectDeserializer = new ProjectDeserializer();
        var formDeserializer = new FormDeserializer();
        var serializedProject = projectDeserializer.Deserialize(File.ReadAllText(projectPath), new NullErrorSink());
        var projectDir = Path.GetDirectoryName(projectPath)!;

        Console.WriteLine($"Project: {serializedProject.Name ?? "(unnamed)"} ({serializedProject.ProjectType})");
        Console.WriteLine($"  Startup: {serializedProject.StartupFormName ?? "(none)"}");

        var projectDefinition = new ProjectDefinition(serializedProject.ProjectType, serializedProject.Name ?? "Unnamed");

        // Forms
        int formsOk = 0, formsFailed = 0, formsMissing = 0;
        var unknownTypes = new Dictionary<string, int>();
        Console.WriteLine();
        Console.WriteLine($"Forms ({serializedProject.RelativeFormPaths.Count}):");

        foreach (var relativePath in serializedProject.RelativeFormPaths)
        {
            var absolutePath = Path.Join(projectDir, relativePath);
            var fileName = Path.GetFileName(relativePath);

            if (!File.Exists(absolutePath))
            {
                Console.WriteLine($"  [MISS] {fileName} (file not found)");
                formsMissing++;
                continue;
            }

            var sink = new CollectingErrorSink();
            var form = formDeserializer.Deserialize(projectDefinition, File.ReadAllText(absolutePath), sink);

            if (sink.Errors.Count == 0 && form != null)
            {
                Console.WriteLine($"  [OK]   {fileName} ({form.Components.Count} controls)");
                formsOk++;
            }
            else
            {
                Console.WriteLine($"  [FAIL] {fileName}");
                formsFailed++;
                foreach (var error in sink.Errors)
                {
                    Console.WriteLine($"           {error}");
                    if (error.StartsWith("Class ") && error.Contains(" of control "))
                    {
                        var typeName = error["Class ".Length..error.IndexOf(" of control ")];
                        unknownTypes[typeName] = unknownTypes.GetValueOrDefault(typeName) + 1;
                    }
                }
            }
        }

        // Modules
        int modulesOk = 0, modulesMissing = 0;
        Console.WriteLine();
        Console.WriteLine($"Modules ({serializedProject.RelativeModulePaths.Count}):");

        foreach (var (_, relativePath, _) in serializedProject.RelativeModulePaths)
        {
            var absolutePath = Path.Join(projectDir, relativePath);
            var fileName = Path.GetFileName(relativePath);

            if (!File.Exists(absolutePath))
            {
                Console.WriteLine($"  [MISS] {fileName} (file not found)");
                modulesMissing++;
            }
            else
            {
                Console.WriteLine($"  [OK]   {fileName}");
                modulesOk++;
            }
        }

        // Summary
        Console.WriteLine();
        Console.WriteLine("Summary:");
        Console.WriteLine($"  Forms:   {formsOk} OK, {formsFailed} failed, {formsMissing} missing");
        Console.WriteLine($"  Modules: {modulesOk} OK, {modulesMissing} missing");

        if (unknownTypes.Count > 0)
        {
            var typeList = string.Join(", ", unknownTypes.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} (×{kv.Value})"));
            Console.WriteLine($"  Unknown control types: {typeList}");
        }

        var exitCode = (formsFailed > 0 || formsMissing > 0 || modulesMissing > 0) ? 1 : 0;
        Console.WriteLine($"Exit code: {exitCode}");
        return exitCode;
    }

    private static void LoadProject(string[] args)
    {
        string? projectPath = null;

        if (args.Length == 0)
        {
            if (Environment.ProcessPath == null)
            {
                Error = $"Couldn't extract current process path.";
            }
            else
            {
                var dir = Path.GetDirectoryName(Environment.ProcessPath);
                var fileName = Path.GetFileNameWithoutExtension(Environment.ProcessPath);
                projectPath = Path.Join(dir, Path.ChangeExtension(fileName, "dll"));
            }
        }
        else
        {
            projectPath = args[0];
        }

        if (projectPath == null || !File.Exists(projectPath))
        {
            Error = $"Couldn't find any project in {projectPath}";
        }
        else
        {
            var projectDeserializer = new ProjectDeserializer();
            var formDeserializer = new FormDeserializer();

            // This is not a .dll file, but it will look better :wink: :wink:
            if (string.Equals(Path.GetExtension(projectPath), ".dll", StringComparison.OrdinalIgnoreCase))
            {
                var tempPath = Path.GetTempFileName();
                File.Delete(tempPath);
                ZipFile.ExtractToDirectory(projectPath, tempPath);

                var actualProjectPath = Path.Join(tempPath,
                    Path.ChangeExtension(Path.GetFileNameWithoutExtension(projectPath), "vbp"));

                if (!File.Exists(actualProjectPath))
                {
                    Error = $"{projectPath} is not a valid project";
                    return;
                }

                projectPath = actualProjectPath;
            }

            var serializedProject = projectDeserializer.Deserialize(File.ReadAllText(projectPath), new NullErrorSink());
            var forms = new List<FormDefinition>();

            var projectDefinition = new ProjectDefinition(serializedProject.ProjectType, serializedProject.Name ?? "Unnamed project");

            foreach (var relativeFormPath in serializedProject.RelativeFormPaths)
            {
                var absolutePath = Path.Join(Path.GetDirectoryName(projectPath)!, relativeFormPath);
                var form = formDeserializer.Deserialize(projectDefinition, File.ReadAllText(absolutePath), new NullErrorSink());
                if (form != null)
                    forms.Add(form);
            }

            var startupForm = forms.FirstOrDefault(x => x.Name == serializedProject.StartupFormName);

            if (startupForm == null)
            {
                Error = "No startup form found";
                return;
            }

            StartupForm = startupForm;
        }
    }

    private class NullErrorSink : IDeserializeErrorSink
    {
        public void LogError(string error)
        {
        }
    }

    private class CollectingErrorSink : IDeserializeErrorSink
    {
        public List<string> Errors { get; } = new();
        public void LogError(string error) => Errors.Add(error);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseMessageBoxSounds()
            .LogToTrace();
            // No bundled font: the non-redistributable MS Sans Serif was removed. VB6 form text falls
            // back via VBFont.ToFontFamily (Liberation Sans, then the platform sans-serif).
}