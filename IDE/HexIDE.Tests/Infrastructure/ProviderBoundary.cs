using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace HexIDE.Tests.Infrastructure;

internal static class ProviderBoundary
{
    internal enum ProjectKind { Product, Provider, IdeTest, ProviderTest, Tool }

    internal static readonly IReadOnlyDictionary<string, ProjectKind> Projects = new Dictionary<string, ProjectKind>(StringComparer.Ordinal)
    {
        ["HexIDE"] = ProjectKind.Product,
        ["HexIDE.Core"] = ProjectKind.Product,
        ["HexIDE.Desktop"] = ProjectKind.Product,
        ["HexIDE.Browser"] = ProjectKind.Product,
        ["HexIDE.Lsp"] = ProjectKind.Product,
        ["HexIDE.Dap"] = ProjectKind.Product,
        ["HexIDE.AIChat"] = ProjectKind.Product,
        ["HexIDE.AddinPacker"] = ProjectKind.Product,
        // hexide-pack (#5555): a command-line tool for publishers, built to an assembly of that name.
        ["HexIDE.AddinPacker.Interactive"] = ProjectKind.Tool,
        ["HexIDE.LspGuard"] = ProjectKind.Product,
        ["HexIDE.Office"] = ProjectKind.Product,
        ["HexIDE.Repack"] = ProjectKind.Product,
        ["HexIDE.TwinBasic"] = ProjectKind.Product,
        ["HexIDE.TestAddin"] = ProjectKind.Product,
        ["HexIDE.Runtime"] = ProjectKind.Provider,
        ["HexIDE.Standalone"] = ProjectKind.Provider,
        ["HexIDE.VbLspServer"] = ProjectKind.Provider,
        ["HexIDE.Tests"] = ProjectKind.IdeTest,
        ["HexIDE.Integration.Tests"] = ProjectKind.IdeTest,
        ["HexIDE.TwinBasic.Tests"] = ProjectKind.IdeTest,
        ["HexIDE.Runtime.Tests"] = ProjectKind.ProviderTest,
        ["HexIDE.Runtime.Integration.Tests"] = ProjectKind.ProviderTest,
        ["HexIDE.VbLspServer.Tests"] = ProjectKind.ProviderTest,
        ["HexIDE.Lsp.TestHost"] = ProjectKind.Tool,
        ["HexIDE.Lsp.TestServer"] = ProjectKind.Tool,
        ["HexIDE.Dap.TestAdapter"] = ProjectKind.Tool,
        ["HexIDE.Dap.Smoke"] = ProjectKind.Tool,
        ["HexIDE.Dap.Parity"] = ProjectKind.Tool,
    };

    internal sealed record Row(string Kind, string From, string To)
    {
        public override string ToString() => $"| {Kind} | {From} | {To} |";
    }

    private static bool IsProvider(string name) => Projects.TryGetValue(name, out var kind) && kind == ProjectKind.Provider;

    internal static IReadOnlySet<Row> ReadAssembly(Stream stream)
    {
        using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
        var metadata = pe.GetMetadataReader();
        var from = metadata.GetString(metadata.GetAssemblyDefinition().Name);
        var rows = new HashSet<Row>();
        foreach (var handle in metadata.AssemblyReferences)
        {
            var name = metadata.GetString(metadata.GetAssemblyReference(handle).Name);
            if (IsProvider(name))
                rows.Add(new Row("assembly", from, name));
        }
        foreach (var handle in metadata.TypeReferences)
        {
            var (assembly, name) = TypeName(handle);
            if (IsProvider(assembly))
                rows.Add(new Row("type", from, name));
        }
        return rows;

        (string Assembly, string Name) TypeName(TypeReferenceHandle handle)
        {
            var type = metadata.GetTypeReference(handle);
            var name = metadata.GetString(type.Name);
            if (type.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                var parent = TypeName((TypeReferenceHandle)type.ResolutionScope);
                return (parent.Assembly, parent.Name + "+" + name);
            }
            var ns = metadata.GetString(type.Namespace);
            return (type.ResolutionScope.Kind == HandleKind.AssemblyReference
                    ? metadata.GetString(metadata.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope).Name) : "",
                ns.Length == 0 ? name : ns + "." + name);
        }
    }

    internal static IReadOnlySet<Row> ReadProjects(string root)
    {
        var rows = new HashSet<Row>();
        foreach (var path in new[] { "IDE", "LspServer" }.SelectMany(folder =>
                     Directory.EnumerateFiles(Path.Join(root, folder), "*.csproj", SearchOption.AllDirectories))
                     .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")))
        {
            var from = Path.GetFileNameWithoutExtension(path);
            var kind = Classify(from);
            var xml = XDocument.Load(path);
            foreach (var reference in xml.Descendants().Where(element => element.Name.LocalName == "ProjectReference"))
            {
                var target = Path.GetFileNameWithoutExtension(((string?)reference.Attribute("Include") ?? "")
                    .Replace('\\', '/'));
                var targetKind = Classify(target);
                if (kind is ProjectKind.Product or ProjectKind.IdeTest && targetKind == ProjectKind.Provider)
                    rows.Add(new Row("project", from, target));
            }
            foreach (var grant in xml.Descendants().Where(element => element.Name.LocalName == "InternalsVisibleTo"))
            {
                var target = ((string?)grant.Attribute("Include") ?? "").Split(',')[0].Trim();
                var targetKind = Classify(target);
                if (kind == ProjectKind.Product && targetKind is ProjectKind.Provider or ProjectKind.ProviderTest
                    || kind is ProjectKind.Provider or ProjectKind.ProviderTest && targetKind == ProjectKind.Product
                    || kind == ProjectKind.Provider && targetKind == ProjectKind.IdeTest)
                    rows.Add(new Row("internals", from, target));
            }
        }
        return rows;
    }

    internal static ProjectKind Classify(string name) => Projects.TryGetValue(name, out var kind)
        ? kind : throw new InvalidOperationException($"Unclassified project: {name}. Classify it in ProviderBoundary.Projects.");

    internal static string Listing(IEnumerable<Row> rows) =>
        "| kind | from | to |\n|---|---|---|\n" + string.Join("\n", rows.Select(row => row.ToString()).Order(StringComparer.Ordinal)) + "\n";

    internal static IReadOnlySet<Row> ReadListing(string text) => text.ReplaceLineEndings("\n").Split('\n')
        .Where(line => line.StartsWith("| ", StringComparison.Ordinal) && !line.StartsWith("| kind |", StringComparison.Ordinal))
        .Select(line => line.Split('|').Select(cell => cell.Trim()).ToArray())
        .Select(cells => new Row(cells[1], cells[2], cells[3])).ToHashSet();

    internal static string Compare(IReadOnlySet<Row> current, IReadOnlySet<Row> listed, bool checkRemoved)
    {
        var added = current.Except(listed).Select(row => row.ToString()).Order(StringComparer.Ordinal).ToArray();
        var removed = checkRemoved ? listed.Except(current).Select(row => row.ToString()).Order(StringComparer.Ordinal).ToArray() : [];
        if (added.Length == 0 && removed.Length == 0)
            return "";
        return (added.Length == 0 ? "" : "Unlisted references:\n" + string.Join("\n", added) +
                "\nMove the code into the provider or behind a HexIDE.Core contract (#3411). Do not add rows without the move's lead agreeing.\n") +
            (removed.Length == 0 ? "" : "Listed references that have gone; delete these rows:\n" + string.Join("\n", removed) + "\n") +
            "Full current list:\n" + Listing(current);
    }
}

/// <summary>
/// Pins the product's remaining dependencies on the provider before #3411's move. HexIDE.Browser is classified,
/// but its DLL is omitted: HexIDE.Tests does not build it. Metadata includes generated code and compiled AXAML.
/// </summary>
public class ProviderBoundaryTests
{
    [Fact]
    public void Product_references_match_the_committed_boundary()
    {
        var rows = ProviderBoundary.ReadProjects(ProviderTestSort.Root()).ToHashSet();
        foreach (var project in ProviderBoundary.Projects.Where(project =>
                     project.Value == ProviderBoundary.ProjectKind.Product && project.Key != "HexIDE.Browser"))
        {
            using var stream = File.OpenRead(Path.Join(AppContext.BaseDirectory, project.Key + ".dll"));
            rows.UnionWith(ProviderBoundary.ReadAssembly(stream));
        }
        var path = Path.Join(ProviderTestSort.Root(), "IDE", "HexIDE.Tests", "ProviderBoundary.md");
        var listed = ProviderBoundary.ReadListing(File.Exists(path) ? File.ReadAllText(path) : "");
#if DEBUG
        const bool checkRemoved = true;
#else
        // Desktop's Server/ code is Debug-only; Release can have fewer references without the move removing them.
        const bool checkRemoved = false;
#endif
        var message = ProviderBoundary.Compare(rows, listed, checkRemoved);
        (message.Length == 0).Should().BeTrue("{0}", message);
    }

    [Fact]
    public void Metadata_follows_nested_types_to_the_provider_assembly()
    {
        using var provider = Emit("HexIDE.Runtime", "namespace Example { public class Outer { public class Inner { } } }");
        using var product = Emit("Product", "public class Use { public Example.Outer A = new(); public Example.Outer.Inner B = new(); }",
            MetadataReference.CreateFromImage(provider.ToArray()));
        ProviderBoundary.ReadAssembly(product).Should().BeEquivalentTo(new[]
        {
            new ProviderBoundary.Row("assembly", "Product", "HexIDE.Runtime"),
            new ProviderBoundary.Row("type", "Product", "Example.Outer"),
            new ProviderBoundary.Row("type", "Product", "Example.Outer+Inner"),
        });
    }

    private static MemoryStream Emit(string name, string source, params MetadataReference[] additional)
    {
        var compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source)],
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) }.Concat(additional),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var stream = new MemoryStream();
        var result = compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        result.Success.Should().BeTrue("{0}", string.Join("\n", result.Diagnostics));
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void An_unlisted_reference_fails_and_names_the_row()
    {
        var row = new ProviderBoundary.Row("type", "Product", "Example.New");
        var message = ProviderBoundary.Compare(new HashSet<ProviderBoundary.Row> { row }, new HashSet<ProviderBoundary.Row>(), true);
        message.Should().Contain("Unlisted references:").And.Contain(row.ToString())
            .And.Contain("HexIDE.Core contract (#3411)").And.Contain("lead agreeing").And.Contain("Full current list:");
    }

    [Fact]
    public void A_removed_reference_fails_and_names_the_row()
    {
        var row = new ProviderBoundary.Row("type", "Product", "Example.Gone");
        ProviderBoundary.Compare(new HashSet<ProviderBoundary.Row>(), new HashSet<ProviderBoundary.Row> { row }, true)
            .Should().Contain("delete these rows:").And.Contain(row.ToString());
    }

    [Fact]
    public void Equal_sets_pass()
    {
        var rows = new HashSet<ProviderBoundary.Row> { new("assembly", "Product", "HexIDE.Runtime") };
        ProviderBoundary.Compare(rows, rows, true).Should().BeEmpty();
        ProviderBoundary.ReadListing(ProviderBoundary.Listing(rows)).Should().BeEquivalentTo(rows);
    }

    [Fact]
    public void Release_allows_removed_references_but_refuses_new_ones()
    {
        var rows = new HashSet<ProviderBoundary.Row> { new("assembly", "Product", "HexIDE.Runtime") };
        ProviderBoundary.Compare(new HashSet<ProviderBoundary.Row>(), rows, false).Should().BeEmpty();
        ProviderBoundary.Compare(rows, new HashSet<ProviderBoundary.Row>(), false).Should().Contain("Unlisted references:");
    }

    [Fact]
    public void Project_files_include_conditional_references_and_cross_boundary_grants()
    {
        var root = Path.Join(ProviderTestSort.Root(), "artifacts", "boundary-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Join(root, "IDE"));
            Directory.CreateDirectory(Path.Join(root, "LspServer"));
            File.WriteAllText(Path.Join(root, "IDE", "HexIDE.Core.csproj"), """
                <Project><ItemGroup>
                  <ProjectReference Include="..\HexIDE.Runtime\HexIDE.Runtime.csproj" Condition="false" />
                  <InternalsVisibleTo Include="HexIDE.Runtime.Tests" />
                  <InternalsVisibleTo Include="HexIDE.Tests" />
                </ItemGroup></Project>
                """);
            File.WriteAllText(Path.Join(root, "IDE", "HexIDE.Tests.csproj"), """
                <Project><ItemGroup><ProjectReference Include="../HexIDE.Standalone/HexIDE.Standalone.csproj" /></ItemGroup></Project>
                """);
            File.WriteAllText(Path.Join(root, "LspServer", "HexIDE.VbLspServer.csproj"), """
                <Project><ItemGroup>
                  <InternalsVisibleTo Include="HexIDE.Tests" />
                  <InternalsVisibleTo Include="HexIDE.Core" />
                  <InternalsVisibleTo Include="HexIDE.VbLspServer.Tests" />
                </ItemGroup></Project>
                """);
            ProviderBoundary.ReadProjects(root).Should().BeEquivalentTo(new[]
            {
                new ProviderBoundary.Row("project", "HexIDE.Core", "HexIDE.Runtime"),
                new ProviderBoundary.Row("project", "HexIDE.Tests", "HexIDE.Standalone"),
                new ProviderBoundary.Row("internals", "HexIDE.Core", "HexIDE.Runtime.Tests"),
                new ProviderBoundary.Row("internals", "HexIDE.VbLspServer", "HexIDE.Tests"),
                new ProviderBoundary.Row("internals", "HexIDE.VbLspServer", "HexIDE.Core"),
            });
            File.WriteAllText(Path.Join(root, "IDE", "HexIDE.NewProject.csproj"), "<Project />");
            var read = () => ProviderBoundary.ReadProjects(root);
            read.Should().Throw<InvalidOperationException>().WithMessage("*Unclassified project: HexIDE.NewProject*");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_new_project_must_be_classified()
    {
        var act = () => ProviderBoundary.Classify("HexIDE.NewProject");
        act.Should().Throw<InvalidOperationException>().WithMessage("*Unclassified project: HexIDE.NewProject*");
    }
}
