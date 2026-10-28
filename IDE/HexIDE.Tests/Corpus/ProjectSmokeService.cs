using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using HexIDE.IDE;
using HexIDE.Tests.Projects;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Corpus;

internal static class ProjectSmokeService
{
    public static Task<Func<Task<string?>>> Open(string path) => Open(path, false);

    public static async Task<SmokeStage> Make(string path, string? runtimeDirectory = null)
    {
        try
        {
            var make = await Open(path, true, runtimeDirectory);
            var refusal = await make();
            return refusal is null ? new("made") : new("refused", refusal);
        }
        catch (Exception exception)
        {
            return SmokeStage.FromException(exception);
        }
    }

    private static async Task<Func<Task<string?>>> Open(string path, bool making, string? runtimeDirectory = null)
    {
        var loaded = new List<ProjectDefinition>();
        var manager = Substitute.For<IProjectManager>();
        manager.LoadedProjects.Returns(_ => loaded);
        manager.When(m => m.AddProject(Arg.Any<ProjectDefinition>())).Do(ci => loaded.Add(ci.Arg<ProjectDefinition>()));
        var windows = Substitute.For<IWindowManager>();
        var messages = new List<string>();
        windows.MessageBox(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>())
            .Returns(ci => { messages.Add(ci.ArgAt<string>(0)); return Task.FromResult(MessageBoxResult.Ok); });
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(ci => ci.Arg<string>() + " {0}");
        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        var service = new ProjectService(() => throw new InvalidOperationException("Unexpected project dialog"),
            windows, Substitute.For<IEventBus>(), manager, Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), sidecar, new FileBaselineStore(), localization,
            Substitute.For<IHeaderRefresher>());
        service.RuntimeDirectory = runtimeDirectory ?? MadeProgramFactAttribute.MakeRuntime;
        var output = Path.Join(Path.GetDirectoryName(path)!, "smoke-output", OperatingSystem.IsWindows() ? "Program.exe" : "Program");
        windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns(output);
        await service.OpenProject(path);
        if (loaded.Count != 1) throw new InvalidOperationException("Open did not load one project: " + string.Join("; ", messages));
        var project = loaded.Single();
        messages.Clear();
        return async () =>
        {
            if (making)
            {
                messages.Clear();
                try { await service.MakeProject(project); }
                catch (OperationCanceledException exception)
                {
                    return "refused: " + string.Join("; ", messages.Append(exception.Message));
                }
                if (!File.Exists(output) || !File.Exists(Path.ChangeExtension(output, "dll")))
                    throw new IOException("Make returned without its runner and project archive.");
                return null;
            }
            // An absolute member reference can still name the original after copying a corpus root.
            var directory = Path.GetDirectoryName(path)!;
            foreach (var member in project.Forms.Select(form => form.AbsolutePath).Concat(project.Modules.Select(module => module.AbsolutePath)))
            {
                if (member is null) return "refused: member has no disk path";
                var relative = Path.GetRelativePath(directory, member);
                if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    return "refused: member is outside the project directory: " + member;
            }
            var reasons = project.Forms.Concat(project.Modules.Where(module => module.FormPart is not null)
                .Select(module => module.FormPart!)).Where(form => !form.CanSaveFaithfully)
                .Select(form => form.Name + ": " + form.UnfaithfulSaveReason).ToArray();
            await service.SaveProject(project, saveAs: false);
            return messages.Count == 0 ? null : "refused: " + string.Join("; ", reasons.Concat(messages));
        };
    }
}
