using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using HexIDE.Tests.Infrastructure;

namespace HexIDE.Tests.Corpus;

public class ProjectCorpusSmokeTests
{
    private static string RepositoryRoot() => RepoTree.Root();

    internal static IEnumerable<string> AdditionalRoots(string? corpus, string? templates)
    {
        if (!string.IsNullOrWhiteSpace(corpus))
            foreach (var root in corpus.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                yield return root;
        if (!string.IsNullOrWhiteSpace(templates)) yield return templates;
    }

    [Fact]
    public void Optional_corpus_and_template_roots_are_both_included()
    {
        AdditionalRoots("first; second;; ", "templates").Should().Equal("first", "second", "templates");
        AdditionalRoots(null, null).Should().BeEmpty();
    }

    [Fact]
    public void Repository_root_requires_both_project_trees_despite_a_nearer_corpus_directory()
    {
        var decoy = Path.Join(AppContext.BaseDirectory, "corpus");
        var existed = Directory.Exists(decoy);
        Directory.CreateDirectory(decoy);
        try
        {
            var repo = RepositoryRoot();
            Directory.Exists(Path.Join(repo, "IDE")).Should().BeTrue();
            Directory.Exists(Path.Join(repo, "LspServer")).Should().BeTrue();
        }
        finally
        {
            if (!existed) Directory.Delete(decoy);
        }
    }

    [Fact]
    public async Task Every_corpus_project_opens_saves_runs_for_three_seconds_and_makes_or_refuses()
    {
        var repo = RepositoryRoot();
        var roots = new List<string>
        {
            Path.Join(repo, "demo"),
            Path.Join(repo, "corpus", "designer"),
            Path.Join(repo, "corpus", "member-attributes", "round-trip"),
            Path.Join(repo, "corpus", "inbox-controls", "saved")
        };
        roots.AddRange(Directory.EnumerateDirectories(Path.Join(repo, "corpus", "member-attributes", "evidence"),
            "saved*", SearchOption.AllDirectories));
        roots.AddRange(AdditionalRoots(Environment.GetEnvironmentVariable("HEXIDE_ROUNDTRIP_CORPUS"),
            Environment.GetEnvironmentVariable("VB6_TEMPLATES")));
        var evidence = Path.Join(AppContext.BaseDirectory, "TestResults", "project-smoke-evidence");
        if (Directory.Exists(evidence)) Directory.Delete(evidence, true);
        var results = new List<ProjectSmokeResult>();
        foreach (var root in roots.Distinct(StringComparer.Ordinal))
        {
            Directory.Exists(root).Should().BeTrue("a configured corpus must exist: " + root);
            foreach (var project in ProjectSmokeHarness.Discover([root]))
                results.Add(await ProjectSmokeHarness.Check(root, project, ProjectSmokeService.Open,
                    Path.Join(evidence, results.Count.ToString()), run: path => ProjectSmokeRunner.Run(path),
                    make: path => ProjectSmokeService.Make(path)));
        }
        var report = Path.Join(AppContext.BaseDirectory, "TestResults", "hexide-project-smoke-report.json");
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        File.WriteAllText(report, JsonSerializer.Serialize(results.Select((result, index) => new
        {
            result.Project, result.Opened, result.Save, result.Run, result.Make, result.Differences, result.Normalisations,
            Evidence = Path.Join("project-smoke-evidence", index.ToString()),
            Crash = result.Crash?.ToString(), result.Failed
        }), new JsonSerializerOptions { WriteIndented = true }));
        results.Should().NotBeEmpty("an empty corpus proves nothing");
        results.Where(result => result.Failed).Select(result => result.Project + ": " +
            (result.Crash?.ToString() ?? string.Join(", ", result.Differences))
            + (result.Run.Failed ? "\nRun: " + result.Run.Detail : "")
            + (result.Make.Failed ? "\nMake: " + result.Make.Detail : ""))
            .Should().BeEmpty("see " + report);
    }
}
