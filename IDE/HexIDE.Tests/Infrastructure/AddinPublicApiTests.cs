using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HexIDE.Tests.Infrastructure;

public class AddinPublicApiTests
{
    [Fact]
    public void EverySdkReachableTypeIsGuarded()
    {
        var core = typeof(HexIDE.Addins.IHexIdeHost).Assembly;
        var directory = Path.Combine(RepoTree.Root(), "IDE", "HexIDE.Core");
        var baseline = File.ReadAllLines(Path.Combine(directory, "PublicAPI.Shipped.txt"))
            .Concat(File.ReadAllLines(Path.Combine(directory, "PublicAPI.Unshipped.txt"))).ToHashSet();
        var declarations = SourceDeclarations(directory);
        var editorConfig = File.ReadAllLines(Path.Combine(directory, ".editorconfig"));
        var skippedNamespaces = File.ReadAllLines(Path.Combine(directory, "PublicAPI.globalconfig"))
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2 && parts[0].Trim() == "dotnet_public_api_analyzer.skip_namespaces")
            .SelectMany(parts => parts[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .ToArray();
        var missing = new List<string>();
        foreach (var (type, path) in SdkReachableTypes(core))
        {
            if (type.Assembly != core)
            {
                // Only the runtime's own assemblies are BCL. A third-party dependency, even one named
                // System.*, must not silently become part of the add-in contract.
                if (!Path.GetDirectoryName(type.Assembly.Location)!.Equals(
                        Path.GetDirectoryName(typeof(object).Assembly.Location), StringComparison.Ordinal))
                    missing.Add($"Non-BCL type {type.FullName} ({type.Assembly.GetName().Name}), reached via {path}");
                continue;
            }

            if (skippedNamespaces.Any(ns => type.Namespace == ns || type.Namespace?.StartsWith(ns + ".", StringComparison.Ordinal) == true))
                missing.Add($"Skipped namespace for {type.FullName}, reached via {path}");
            if (!baseline.Contains(ApiTypeName(type)))
                missing.Add($"Missing API type line: {ApiTypeName(type)}, reached via {path}");
            if (!declarations.TryGetValue(type.FullName!, out var files))
                missing.Add($"No source declaration for {type.FullName}, reached via {path}");
            else
                foreach (var file in files)
                    if (!HasApiGuard(file, editorConfig))
                        missing.Add($"Missing RS0016 error section: {file} ({type.FullName}), reached via {path}");
        }
        missing.Should().BeEmpty("every SDK-reachable Core type must have both a baseline and a guarded source file:{0}{1}",
            Environment.NewLine, string.Join(Environment.NewLine, missing));
    }

    private static Dictionary<Type, string> SdkReachableTypes(System.Reflection.Assembly core)
    {
        var reached = new Dictionary<Type, string>();
        var pending = new Queue<(Type Type, string Path)>();
        foreach (var type in core.GetExportedTypes().Where(t => t.Namespace == "HexIDE.Addins"))
            pending.Enqueue((type, type.FullName!));
        while (pending.TryDequeue(out var item))
        {
            var (type, path) = item;
            if (type.HasElementType)
            {
                pending.Enqueue((type.GetElementType()!, path));
                continue;
            }
            if (type.IsGenericParameter)
            {
                foreach (var constraint in type.GetGenericParameterConstraints())
                    pending.Enqueue((constraint, path));
                continue;
            }
            if (type.IsGenericType)
            {
                foreach (var argument in type.GetGenericArguments().Where(t => !t.IsGenericParameter))
                    pending.Enqueue((argument, path));
                type = type.GetGenericTypeDefinition();
            }
            if (!reached.TryAdd(type, path) || type.Assembly != core)
                continue;

            void Add(Type? next, string member)
            {
                if (next is not null)
                    pending.Enqueue((next, $"{path} -> {type.Name}.{member}"));
            }

            Add(type.BaseType, "base");
            foreach (var contract in type.GetInterfaces())
                Add(contract, "interface");
            foreach (var argument in type.GetGenericArguments())
                foreach (var constraint in argument.GetGenericParameterConstraints())
                    Add(constraint, "constraint");
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var field in type.GetFields(flags).Where(f => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly))
                Add(field.FieldType, field.Name);
            // Accessors cover properties (including indexers) and events; Invoke covers delegates.
            foreach (var method in type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags))
                         .Where(m => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly))
            {
                if (method is MethodInfo info)
                    Add(info.ReturnType, method.Name);
                foreach (var parameter in method.GetParameters())
                    Add(parameter.ParameterType, method.Name);
                if (method.IsGenericMethod)
                    foreach (var argument in method.GetGenericArguments())
                        foreach (var constraint in argument.GetGenericParameterConstraints())
                            Add(constraint, method.Name);
            }
            foreach (var nested in type.GetNestedTypes(BindingFlags.Public))
                Add(nested, nested.Name);
        }
        return reached;
    }

    private static string ApiTypeName(Type type)
    {
        var prefix = type.IsNested ? ApiTypeName(type.DeclaringType!) : type.Namespace;
        var name = type.Name.Split('`')[0];
        var arguments = type.GetGenericArguments().Skip(type.DeclaringType?.GetGenericArguments().Length ?? 0).ToArray();
        if (arguments.Length > 0)
            name += "<" + string.Join(", ", arguments.Select(t => t.Name)) + ">";
        return prefix + "." + name;
    }

    private static Dictionary<string, HashSet<string>> SourceDeclarations(string directory)
    {
        var declarations = new Dictionary<string, HashSet<string>>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
            if (relative.StartsWith("obj/", StringComparison.Ordinal) || relative.StartsWith("bin/", StringComparison.Ordinal))
                continue;
            var root = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot();
            foreach (var declaration in root.DescendantNodes().Where(n => n is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax))
            {
                static string Name(SyntaxNode node) => node switch
                {
                    TypeDeclarationSyntax t => t.Identifier.ValueText + (t.TypeParameterList is { } p ? "`" + p.Parameters.Count : ""),
                    BaseTypeDeclarationSyntax t => t.Identifier.ValueText,
                    DelegateDeclarationSyntax d => d.Identifier.ValueText + (d.TypeParameterList is { } p ? "`" + p.Parameters.Count : ""),
                    _ => throw new InvalidOperationException()
                };
                var ns = string.Join(".", declaration.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(n => n.Name.ToString()));
                var parents = declaration.Ancestors().OfType<TypeDeclarationSyntax>().Reverse().Select(Name);
                var fullName = ns + "." + string.Join("+", parents.Append(Name(declaration)));
                if (!declarations.TryGetValue(fullName, out var files))
                    declarations.Add(fullName, files = []);
                files.Add(relative);
            }
        }
        return declarations;
    }

    private static bool HasApiGuard(string file, string[] editorConfig)
    {
        var matches = false;
        var severity = "";
        foreach (var raw in editorConfig)
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var glob = line[1..^1];
                var expression = Regex.Escape(glob).Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]");
                matches = Regex.IsMatch(glob.Contains('/') ? file : Path.GetFileName(file), "^" + expression + "$");
            }
            else if (matches && line.Split('=', 2) is [var key, var value] && key.Trim() == "dotnet_diagnostic.RS0016.severity")
                severity = value.Trim();
        }
        return severity == "error";
    }

    [Theory]
    [InlineData("positional")]
    [InlineData("reordered")]
    [InlineData("removed")]
    [InlineData("addition")]
    [InlineData("outside")]
    [InlineData("unreachable-lsp")]
    [InlineData("generated")]
    [InlineData("language-server-positional")]
    [InlineData("form-addition")]
    [InlineData("release")]
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
                case "language-server-positional":
                    var config = Path.Combine(core, "Lsp", "LanguageServerConfig.cs");
                    File.WriteAllText(config, File.ReadAllText(config).Replace(
                        "public sealed class LanguageServerEntry", "public sealed class LanguageServerEntry(int apiProbe = 0)")
                        .Replace("public string? Id { get; set; }", "public string? Id { get; set; } = apiProbe.ToString();"));
                    break;
                case "form-addition":
                    var form = Path.Combine(core, "ProjectElements", "FormDefinition.cs");
                    File.WriteAllText(form, File.ReadAllText(form).Replace(
                        "private Vb6TextEncoding textEncoding", "public int ApiProbe => 1;\n    private Vb6TextEncoding textEncoding"));
                    break;
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
                case "unreachable-lsp":
                    File.AppendAllText(Path.Combine(core, "Lsp", "Messages", "LspMessages.cs"),
                        "\npublic record ApiProbe(int Value);\n");
                    break;
                case "addition":
                case "outside":
                    var folder = mutation == "addition" ? Path.Combine(core, "Addins") : core;
                    var ns = mutation == "addition" ? "HexIDE.Addins" : "HexIDE.ApiProbe";
                    File.WriteAllText(Path.Combine(folder, "ApiProbe.cs"), $"namespace {ns}; public static class ApiProbe {{ public static int Value => 1; }}");
                    break;
            }

            var changed = await Build(core, mutation == "release" ? "Release" : "Debug");
            if (mutation is "outside" or "unreachable-lsp" or "release")
            {
                changed.ExitCode.Should().Be(0, changed.Output);
                changed.Output.Should().Contain("0 Warning(s)");
            }
            else
            {
                changed.ExitCode.Should().NotBe(0, changed.Output);
                changed.Output.Should().NotContain(": error CS", "only the API guard should reject these valid sources");
                if (mutation == "form-addition")
                {
                    changed.Output.Should().Contain("RS0016").And.Contain("FormDefinition.ApiProbe.get");
                    File.AppendAllText(Path.Combine(core, "PublicAPI.Unshipped.txt"),
                        "HexIDE.Runtime.ProjectElements.FormDefinition.ApiProbe.get -> int\n");
                    var recorded = await Build(core);
                    recorded.ExitCode.Should().Be(0, recorded.Output);
                }
                else if (mutation == "language-server-positional")
                    changed.Output.Should().Contain("RS0016").And.Contain("RS0017")
                        .And.Contain("LanguageServerEntry.LanguageServerEntry(");
                else if (mutation == "addition")
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

    private static async Task<(int ExitCode, string Output)> Build(string directory, string configuration = "Debug")
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { "build", "HexIDE.Core.csproj", "--no-incremental", "-c", configuration, "-m:1", "-p:NuGetAudit=false" }
        };
        start.Environment["UseSharedCompilation"] = "false";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return (process.ExitCode, await output + await error);
    }
}
