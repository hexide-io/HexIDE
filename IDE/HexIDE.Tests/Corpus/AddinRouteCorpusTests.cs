using System.IO;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;
using HexIDE.Tests.Infrastructure;

namespace HexIDE.Tests.Corpus;

/// <summary>
/// VB6's own format, handed in as an add-in's project type's format, writes every form, UserControl and PropertyPage of
/// the corpus byte for byte as the same project opened from its <c>.vbp</c> writes them, unedited and edited (#3666). It
/// is one implementation among others there, not a special case, so the add-in route mustn't change what it writes.
/// </summary>
public sealed class AddinRouteCorpusTests
{
    private static readonly AddinOrigin Route = new("route-probe", "A route probe");
    private const string ProjectFileName = "Contributed.routeproj";

    [AvaloniaFact]
    public async Task Every_corpus_designer_file_saves_through_an_add_in_s_type_as_through_its_vbp()
    {
        var roots = ProjectCorpusSmokeTests.Roots();
        var evidence = Path.Join(AppContext.BaseDirectory, "TestResults", "addin-route-evidence");
        if (Directory.Exists(evidence)) Directory.Delete(evidence, true);

        var outcomes = new List<RouteOutcome>();
        foreach (var root in roots.Distinct(StringComparer.Ordinal))
        {
            Directory.Exists(root).Should().BeTrue("a configured corpus must exist: " + root);
            foreach (var vbp in ProjectSmokeHarness.Discover([root]))
                outcomes.Add(await Compare(vbp, Path.Join(evidence, outcomes.Count.ToString())));
        }

        var report = Path.Join(AppContext.BaseDirectory, "TestResults", "hexide-addin-route-report.json");
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        File.WriteAllText(report, JsonSerializer.Serialize(outcomes, new JsonSerializerOptions { WriteIndented = true }));
        outcomes.Should().NotBeEmpty("an empty corpus proves nothing");
        outcomes.SelectMany(outcome => outcome.Kinds).Should().Contain(["Form", "UserControl", "PropertyPage"],
            "each kind of designer file has to have been through both routes");
        outcomes.Where(outcome => outcome.Differences.Count > 0)
            .Select(outcome => outcome.Project + ": " + string.Join("; ", outcome.Differences))
            .Should().BeEmpty("see " + report);
    }

    private sealed record RouteOutcome(string Project, IReadOnlyList<string> Designers, IReadOnlyList<string> Kinds,
        IReadOnlyList<string> Differences);

    private static async Task<RouteOutcome> Compare(string vbp, string evidence)
    {
        var source = Path.GetDirectoryName(vbp)!;
        var name = Path.GetRelativePath(RepoTree.Root(), vbp);
        var builtIn = CopyOf(source);
        var addin = CopyOf(source);
        var differences = new List<string>();
        try
        {
            var (vbpService, vbpProject) = await OpenThroughVbp(Path.Join(builtIn, Path.GetFileName(vbp)));
            var types = new ContributedProjects { ReadFile = path => File.ReadAllBytesAsync(path) };
            using var type = types.Register(TypeListing(vbpProject, builtIn, addin), Route);
            var (addinService, addinProject) = await Open(Path.Join(addin, ProjectFileName), types);

            var pairs = Designers(vbpProject).Select(designer => (designer,
                    Designers(addinProject).SingleOrDefault(other => other.Name == designer.Name)))
                .ToList();
            foreach (var (designer, other) in pairs)
            {
                if (other is null)
                {
                    differences.Add($"{designer.Name}: not read through the add-in's type");
                    continue;
                }
                if (designer.Form.CanSaveFaithfully != other.Form.CanSaveFaithfully)
                    differences.Add($"{designer.Name}: saves faithfully by one route only ({designer.Form.UnfaithfulSaveReason} | "
                                    + $"{other.Form.UnfaithfulSaveReason})");
                if (addinService.HasUnsavedChanges(other.Form))
                    differences.Add($"{designer.Name}: reads as edited through the add-in's type before any edit");
            }

            async Task<List<Designer>> SaveBoth(string when)
            {
                var savedByBoth = new List<Designer>();
                foreach (var (designer, other) in pairs.Where(pair => pair.Item2 is not null))
                {
                    var saved = await designer.Save(vbpService);
                    var savedToo = await other!.Save(addinService);
                    if (saved != savedToo)
                        differences.Add($"{designer.Name} ({when}): saved by one route only");
                    else if (saved)
                        savedByBoth.Add(designer);
                }
                differences.AddRange(Differing(builtIn, addin, Path.Join(evidence, when)).Select(file => $"{file} ({when})"));
                return savedByBoth;
            }

            await SaveBoth("unedited");
            foreach (var (designer, other) in pairs.Where(pair => pair.Item2 is not null))
            {
                designer.Edit();
                other!.Edit();
            }
            var saved = await SaveBoth("edited");
            // Two copies left as they were would match too, so each edit saved has to have reached its file.
            foreach (var designer in saved)
                if (File.ReadAllBytes(designer.FilePath).AsSpan()
                    .SequenceEqual(File.ReadAllBytes(Path.Join(source, Path.GetRelativePath(builtIn, designer.FilePath)))))
                    differences.Add($"{designer.Name}: the edit didn't reach its file");

            return new(name, pairs.Select(pair => pair.designer.Name).ToList(),
                saved.Select(designer => designer.Kind).Distinct().ToList(), differences);
        }
        catch (Exception exception)
        {
            return new(name, [], [], [.. differences, "not completed: " + exception]);
        }
        finally
        {
            Directory.Delete(builtIn, true);
            Directory.Delete(addin, true);
        }
    }

    // A form, or a module with a designer part, as one: whichever it is, it saves as its own kind does.
    private sealed record Designer(string Name, string Kind, FormDefinition Form, ModuleDefinition? Module)
    {
        public string FilePath => (Module?.AbsolutePath ?? Form.AbsolutePath)!;

        public Task<bool> Save(ProjectService service) => Module is { } module
            ? service.SaveModule(module, saveAs: false, interactive: false)
            : service.SaveForm(Form, saveAs: false, interactive: false);

        // The same edit by both routes: a procedure added to the code, and the first control moved, or the root's Tag set
        // where the designer has no control.
        public void Edit()
        {
            const string probe = "Private Sub HexIDE_RouteProbe()\r\nEnd Sub\r\n";
            if (Module is { } module) module.UpdateCode(module.Code + probe);
            else Form.UpdateCode(Form.Code + probe);
            var root = Form.Components.First(component => component.BaseClass.VBTypeName is "VB.Form" or "VB.UserControl" or "VB.PropertyPage");
            if (Form.Components.FirstOrDefault(component => component != root) is { } control)
                control.SetProperty(VBProperties.LeftProperty, control.GetPropertyOrDefault(VBProperties.LeftProperty) + 8);
            else
                root.SetProperty(VBProperties.TagProperty, "route probe");
        }
    }

    private static IEnumerable<Designer> Designers(ProjectDefinition project) =>
        project.Forms.Select(form => new Designer(form.Name, "Form", form, null))
            .Concat(project.Modules.Where(module => module.FormPart is not null)
                .Select(module => new Designer(module.Name, module.Kind.ToString(), module.FormPart!, module)));

    // The add-in's listing of the project the .vbp opened: the same members, at the same places in the other copy.
    private static AddinProjectType TypeListing(ProjectDefinition project, string from, string to)
    {
        string There(string path) => Path.Join(to, Path.GetRelativePath(from, path));
        var members = project.Forms.Select(form => new AddinProjectMember(There(form.AbsolutePath!), AddinMemberKind.Form))
            .Concat(project.Modules.Select(module => new AddinProjectMember(There(module.AbsolutePath!), module.Kind switch
            {
                ModuleKind.StandardModule => AddinMemberKind.StandardModule,
                ModuleKind.ClassModule => AddinMemberKind.ClassModule,
                ModuleKind.UserControl => AddinMemberKind.UserControl,
                _ => AddinMemberKind.PropertyPage,
            })))
            .Concat(project.RelatedDocuments.Select(document => new AddinProjectMember(There(document.AbsolutePath!), AddinMemberKind.Other)))
            .ToList();
        var content = new AddinProjectContent(project.Name) { Members = members };
        return new AddinProjectType("Route probe projects")
        {
            FileNames = ["*.routeproj"],
            Forms = new Vb6ProjectFileFormat(),
            OpenAsync = (_, _, _) => Task.FromResult(content),
        };
    }

    private static Task<(ProjectService, ProjectDefinition)> OpenThroughVbp(string vbp) => Open(vbp, null);

    private static async Task<(ProjectService, ProjectDefinition)> Open(string path, ContributedProjects? types)
    {
        if (types is not null)
            File.WriteAllText(path, "listed by the add-in");
        var loaded = new List<ProjectDefinition>();
        var manager = Substitute.For<IProjectManager>();
        manager.LoadedProjects.Returns(_ => loaded);
        manager.When(m => m.AddProject(Arg.Any<ProjectDefinition>())).Do(call => loaded.Add(call.Arg<ProjectDefinition>()));
        var windows = Substitute.For<IWindowManager>();
        var messages = new List<string>();
        windows.MessageBox(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>())
            .Returns(call => { messages.Add(call.ArgAt<string>(0)); return Task.FromResult(MessageBoxResult.Ok); });
        var strings = Substitute.For<ILocalizationService>();
        strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>() + " {0}");
        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        var service = new ProjectService(new Vb6ReadOnlyRegionWriter(), () => throw new InvalidOperationException("no new-project dialog"), windows,
            Substitute.For<IEventBus>(), manager, Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), sidecar, new FileBaselineStore(), strings,
            Substitute.For<IHeaderRefresher>(), new Vb6ProjectFileFormat(), new ComponentRegistry(), contributed: types);
        await service.OpenProject(path);
        if (loaded.Count != 1)
            throw new InvalidOperationException($"{Path.GetFileName(path)} didn't open: " + string.Join("; ", messages));
        return (service, loaded[0]);
    }

    private static string CopyOf(string source)
    {
        var copy = Directory.CreateTempSubdirectory("hexide-addin-route-").FullName;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Join(copy, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
        return copy;
    }

    // Every file but each route's own project file, which only the .vbp route writes.
    private static IEnumerable<string> Differing(string builtIn, string addin, string evidence)
    {
        static Dictionary<string, byte[]> Files(string root) => Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(file => Path.GetExtension(file).ToLowerInvariant() is not (".vbp" or ".vbw" or ".routeproj"))
            .ToDictionary(file => Path.GetRelativePath(root, file), File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);
        var (left, right) = (Files(builtIn), Files(addin));
        foreach (var file in left.Keys.Union(right.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
        {
            if (left.TryGetValue(file, out var a) && right.TryGetValue(file, out var b) && a.AsSpan().SequenceEqual(b))
                continue;
            foreach (var (route, files) in new[] { ("vbp", left), ("addin", right) })
                if (files.TryGetValue(file, out var bytes))
                {
                    var at = Path.Join(evidence, route, file);
                    Directory.CreateDirectory(Path.GetDirectoryName(at)!);
                    File.WriteAllBytes(at, bytes);
                }
            yield return file;
        }
    }
}
