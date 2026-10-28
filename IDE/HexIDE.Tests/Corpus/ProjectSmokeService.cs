using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Corpus;

internal static class ProjectSmokeService
{
    public static async Task<Func<Task<string?>>> Open(string path)
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
        await service.OpenProject(path);
        if (loaded.Count != 1) throw new InvalidOperationException("Open did not load one project: " + string.Join("; ", messages));
        var project = loaded.Single();
        messages.Clear();
        return async () =>
        {
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
