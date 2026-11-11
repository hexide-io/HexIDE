using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

// Linked into HexIDE.Integration.Tests too, so each test project checks its own listing against its own build (#3663).
namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// Sorts a test project's files by which side of #3411's provider line the names they bind come from, and checks the
/// sort against the listing kept beside the project, <c>ProviderTestSort.md</c>.
/// </summary>
/// <remarks>
/// The compiler decides, not a text scan: <c>HexIDE.Core</c> declares <c>HexIDE.Runtime.*</c> namespaces of its own,
/// such as <c>FormDefinition</c>'s, so a namespace says nothing about which assembly a type is in. A name counts for the
/// side of the assembly its symbol comes from, and so does every type in its signature. A test reached through a helper
/// in the test project counts what the helper binds, followed to a fixed point, and constructing one of the project's
/// own classes counts everything that class declares, since whatever is handed the object can call any of it.
/// </remarks>
internal static class ProviderTestSort
{
    /// <summary>The assemblies that leave with the provider.</summary>
    public static readonly IReadOnlySet<string> ProviderAssemblies = new HashSet<string> { "HexIDE.Runtime", "HexIDE.Standalone" };

    /// <summary>The assembly both sides can use. Every other assembly named <c>HexIDE*</c> is the IDE's.</summary>
    public const string SharedAssembly = "HexIDE.Core";

    /// <summary>The name of the listing in each test project's folder.</summary>
    public const string ListingName = "ProviderTestSort.md";

    public const string Provider = "provider";
    public const string Ide = "IDE";
    public const string Split = "split";

    /// <summary>One file's place in the sort, and what decided it.</summary>
    /// <param name="Path">The file, relative to its test project's folder, with forward slashes.</param>
    /// <param name="Label"><see cref="Provider"/>, <see cref="Ide"/> or <see cref="Split"/>.</param>
    /// <param name="Tests">How many test methods the file declares.</param>
    /// <param name="ProviderOnly">Tests that bind a provider symbol and no IDE symbol.</param>
    /// <param name="Both">Tests that bind both.</param>
    /// <param name="IdeOnly">Tests that bind an IDE symbol and no provider symbol.</param>
    /// <param name="CoreAndProvider">Tests that bind a provider symbol and a <c>HexIDE.Core</c> one. In a provider file
    /// these may be testing Core with a provider type for a fixture, and then they stay with the IDE once it has a
    /// stand-in.</param>
    /// <param name="ProviderTypes">The provider types the file binds, by name.</param>
    /// <param name="Flags">Text indicators that don't decide the label: <c>composition</c> where the file names the
    /// application's composition, which supplies the provider through its bindings, and <c>VB6 text</c> where it holds
    /// VB6 file text or a VB6 file name.</param>
    public sealed record FileSort(string Path, string Label, int Tests, int ProviderOnly, int Both, int IdeOnly,
        int CoreAndProvider, IReadOnlyList<string> ProviderTypes, IReadOnlyList<string> Flags);

    private static readonly Regex Vb6Text = new(
        @"""[^""\r\n]*\.(?:vbp|vbg|frm|frx|bas|cls|ctl|ctx|dsr)\b|VERSION 5\.00|Begin VB\.", RegexOptions.IgnoreCase);

    private static readonly Regex Row = new(@"^\|\s*`(?<path>[^`]+)`\s*\|\s*(?<label>[A-Za-z]+)\s*\|");

    /// <summary>The project's folder, from the repository root and the test assembly's name.</summary>
    public static string ProjectFolder(string project) => Path.Join(Root(), "IDE", project);

    /// <summary>
    /// Compiles <paramref name="project"/>'s sources against the build the test runs from, as
    /// <c>AvaloniaTestGuardTests</c> does, and sorts every file under the project's folder that binds a provider symbol.
    /// </summary>
    public static (IReadOnlyList<FileSort> Sort, IReadOnlyList<string> Errors) Of(string project, CancellationToken cancel)
    {
        var folder = ProjectFolder(project);
        var csproj = XDocument.Load(Path.Join(folder, project + ".csproj"));
        var own = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(folder, path).Split(Path.DirectorySeparatorChar)
                .Any(part => part is "bin" or "obj"))
            .ToList();
        var linked = csproj.Descendants("Compile")
            .Select(item => (string?)item.Attribute("Include"))
            .OfType<string>()
            .Select(include => Path.GetFullPath(Path.Join(folder, include.Replace('\\', Path.DirectorySeparatorChar))))
            .Where(File.Exists)
            .ToList();
        var usings = ImplicitUsings
            .Concat(csproj.Descendants("Using").Select(item => (string?)item.Attribute("Include")).OfType<string>())
            .Append("System.ComponentModel")
            .Distinct()
            .Select(name => $"global using {name};");
        var paths = own.Concat(linked).Distinct().ToList();
        var parsed = new SyntaxTree[paths.Count];
        Parallel.For(0, paths.Count, new ParallelOptions { CancellationToken = cancel },
            i => parsed[i] = CSharpSyntaxTree.ParseText(File.ReadAllText(paths[i]), Parsing, paths[i], cancellationToken: cancel));
        var trees = parsed.Append(CSharpSyntaxTree.ParseText(string.Join("\n", usings), cancellationToken: cancel)).ToList();
        var compilation = CSharpCompilation.Create(project, trees, References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var listed = trees.Where(tree => tree.FilePath.Length > 0 && !Path.GetRelativePath(folder, tree.FilePath).StartsWith(".."));
        return Sort(compilation, listed, folder, cancel);
    }

    /// <summary>
    /// Sorts <paramref name="files"/> within <paramref name="compilation"/>, with paths relative to <paramref name="folder"/>,
    /// and lists the compilation's errors.
    /// </summary>
    /// <remarks>
    /// The sort trusts a name only in a compilation that builds as the project does. A name that doesn't bind, or binds to
    /// two types at once, can't be sorted, and a sort that quietly skipped it would pass with a shorter listing. So every
    /// error is reported, apart from CS8795, a partial method whose other half a source generator writes in the real build.
    /// </remarks>
    public static (IReadOnlyList<FileSort> Sort, IReadOnlyList<string> Errors) Sort(CSharpCompilation compilation,
        IEnumerable<SyntaxTree> files, string folder, CancellationToken cancel)
    {
        var errors = compilation.GetDiagnostics(cancel)
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Id != "CS8795")
            .Select(d => $"{Relative(folder, d.Location.SourceTree?.FilePath ?? "")}: {d.Id} {d.GetMessage()}")
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
        var graph = new Graph(compilation);
        graph.ReadAll(cancel);
        var sorts = new List<FileSort>();
        foreach (var tree in files)
        {
            var model = compilation.GetSemanticModel(tree);
            var declared = tree.GetRoot(cancel).DescendantNodes()
                .Select(node => Graph.Declared(node, model, cancel))
                .OfType<Key>()
                .Distinct()
                .ToList();
            var whole = graph.Reach(declared);
            if (whole.ProviderTypes.Count == 0)
                continue;
            var tests = declared.Where(key => key.Kind == Kind.Member && key.Symbol is IMethodSymbol method && IsTest(method))
                .ToList();
            // A file with no tests, such as a helper or a stand-in, is judged on everything it declares, and has no counts.
            var units = tests.Count > 0
                ? tests.Select(test => graph.Reach(graph.Setup(test.Symbol.ContainingType).Prepend(test))).ToList()
                : [whole];
            var providerOnly = units.Count(u => u.ProviderTypes.Count > 0 && !u.Ide);
            var both = units.Count(u => u.ProviderTypes.Count > 0 && u.Ide);
            var ideOnly = units.Count(u => u.ProviderTypes.Count == 0 && u.Ide);
            var coreAndProvider = units.Count(u => u.ProviderTypes.Count > 0 && u.Core);
            var label = providerOnly + both == 0 ? Ide : both + ideOnly == 0 ? Provider : Split;
            if (tests.Count == 0)
                providerOnly = both = ideOnly = coreAndProvider = 0;
            var flags = new List<string>();
            if (graph.NamesComposition(tree))
                flags.Add("composition");
            if (Vb6Text.IsMatch(tree.GetText(cancel).ToString()))
                flags.Add("VB6 text");
            sorts.Add(new FileSort(Relative(folder, tree.FilePath), label, tests.Count, providerOnly, both, ideOnly,
                coreAndProvider, whole.ProviderTypes.Order(StringComparer.Ordinal).ToList(), flags));
        }

        return (sorts.OrderBy(sort => sort.Path, StringComparer.Ordinal).ToList(), errors);
    }

    /// <summary>The rows of a listing, by path.</summary>
    public static IReadOnlyDictionary<string, string> ReadListing(string text) =>
        text.Split('\n')
            .Select(line => Row.Match(line.TrimEnd('\r')))
            .Where(match => match.Success)
            .ToDictionary(match => match.Groups["path"].Value, match => match.Groups["label"].Value, StringComparer.Ordinal);

    /// <summary>The listing for <paramref name="project"/>, as the guard writes it.</summary>
    public static string Listing(string project, IReadOnlyList<FileSort> sort)
    {
        var text = new StringBuilder();
        text.Append($"# Test files that bind provider types: {project}\n\n");
        text.Append("The sort of this project's tests for #3411, the move of the interpreter and forms engine to an optional provider. ");
        text.Append("`ProviderTestSortTests` writes it and fails when it and the build disagree. Don't edit the rows by hand: run the test, ");
        text.Append($"and copy `artifacts/provider-test-sort/{project}.md` over this file. ");
        text.Append($"The same run writes `{project}-detail.md` beside it, with each file's counts and provider types.\n\n");
        text.Append("A file is listed when something in it binds a symbol from a provider assembly, `HexIDE.Runtime` or `HexIDE.Standalone`. ");
        text.Append("A symbol counts for the assembly it comes from, and so does every type in its signature. `HexIDE.Core` counts for neither side, ");
        text.Append("so a namespace decides nothing: Core declares `HexIDE.Runtime.*` namespaces of its own. Every other `HexIDE*` assembly is the IDE's. ");
        text.Append("A test counts what its class's setup and the helpers it calls bind, followed through the test project to a fixed point.\n\n");
        text.Append("- **provider:** its tests bind provider symbols and no IDE symbols. A file with no tests is judged on what it declares.\n");
        text.Append("- **split:** some of its tests bind a provider symbol, and some bind an IDE symbol.\n");
        text.Append("- **IDE:** none of its tests binds a provider symbol, though something else in the file does.\n\n");
        text.Append("A provider file whose tests also bind Core may be testing Core with a provider type for its fixture, and then it ");
        text.Append("stays with the IDE once that fixture has a stand-in. The detail's Core column says which files to read first.\n\n");
        text.Append("A test that reaches the provider only at run time, through the application's composition or by opening VB6 files, ");
        text.Append("isn't seen here. The detail flags both, as a text indicator that doesn't decide the label.\n\n");
        text.Append("| File | Label |\n|---|---|\n");
        foreach (var file in sort)
            text.Append($"| `{file.Path}` | {file.Label} |\n");
        return text.ToString();
    }

    /// <summary>Each file's counts, provider types and flags, written beside the listing.</summary>
    public static string Detail(string project, IReadOnlyList<FileSort> sort, TimeSpan took)
    {
        var text = new StringBuilder();
        text.Append($"# Provider test sort, detail: {project}\n\n");
        text.Append($"Bound and sorted in {took.TotalSeconds:0.0} s on {Environment.ProcessorCount} logical processors.\n\n");
        text.Append($"{sort.Count} files: {sort.Count(f => f.Label == Provider)} provider, {sort.Count(f => f.Label == Split)} split, ");
        text.Append($"{sort.Count(f => f.Label == Ide)} IDE. Of their {sort.Sum(f => f.Tests)} tests, {sort.Sum(f => f.ProviderOnly)} bind provider ");
        text.Append($"symbols only, {sort.Sum(f => f.Both)} both sides, and {sort.Sum(f => f.IdeOnly)} IDE symbols only.\n\n");
        text.Append("Counts are of test methods. A file with no tests is judged on what it declares, and its counts are left at 0.\n\n");
        text.Append("| File | Label | Tests | Provider only | Both | IDE only | Core | Flags | Provider types |\n|---|---|---|---|---|---|---|---|---|\n");
        foreach (var file in sort)
        {
            var types = file.ProviderTypes.Count <= 6
                ? string.Join(", ", file.ProviderTypes)
                : string.Join(", ", file.ProviderTypes.Take(6)) + $" and {file.ProviderTypes.Count - 6} more";
            text.Append($"| `{file.Path}` | {file.Label} | {file.Tests} | {file.ProviderOnly} | {file.Both} | {file.IdeOnly} | {file.CoreAndProvider} | ");
            text.Append($"{string.Join(", ", file.Flags)} | {types} |\n");
        }

        return text.ToString();
    }

    // Read as the build running this test compiled it, so a block under #if DEBUG is sorted in a Debug run.
    private static readonly CSharpParseOptions Parsing = new(preprocessorSymbols:
#if DEBUG
        ["DEBUG", "TRACE"]);
#else
        ["TRACE"]);
#endif

    private static readonly string[] ImplicitUsings =
        ["System", "System.Collections.Generic", "System.IO", "System.Linq", "System.Net.Http", "System.Threading", "System.Threading.Tasks"];

    /// <summary>
    /// What the test run itself resolved, less the test assembly, whose sources are what gets compiled. Not every assembly in
    /// the output folder: some are copied there and never referenced, such as the bundled language server, and its own
    /// <c>LineRange</c> made Core's ambiguous.
    /// </summary>
    public static readonly Lazy<MetadataReference[]> References = new(() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(path => !string.Equals(Path.GetFileName(path), Path.GetFileName(typeof(ProviderTestSortTests).Assembly.Location),
                StringComparison.OrdinalIgnoreCase))
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(group => (MetadataReference)MetadataReference.CreateFromFile(group.First()))
            .ToArray());

    private static bool IsTest(IMethodSymbol method) =>
        method.GetAttributes().Any(attribute =>
        {
            for (var type = attribute.AttributeClass; type is not null; type = type.BaseType)
            {
                if (type.ToDisplayString() == "Xunit.FactAttribute")
                    return true;
            }

            return false;
        });

    private static string Relative(string folder, string path) =>
        Path.GetRelativePath(folder, path).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>The directory holding both <c>IDE/</c> and <c>LspServer/</c>.</summary>
    public static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Join(dir.FullName, "IDE")) && Directory.Exists(Path.Join(dir.FullName, "LspServer")))
                return dir.FullName;
        }

        throw new InvalidOperationException("no ancestor holds both IDE/ and LspServer/");
    }

    private enum Kind { Member, TypeHeader, TypeWhole }

    /// <summary>A member of the test project, the syntax a type declares outside its members, or everything a type declares.</summary>
    private readonly record struct Key(ISymbol Symbol, Kind Kind)
    {
        public bool Equals(Key other) => Kind == other.Kind && SymbolEqualityComparer.Default.Equals(Symbol, other.Symbol);

        public override int GetHashCode() => HashCode.Combine(SymbolEqualityComparer.Default.GetHashCode(Symbol), Kind);
    }

    private sealed class Node
    {
        public readonly HashSet<string> ProviderTypes = new(StringComparer.Ordinal);
        public readonly HashSet<Key> Edges = [];
        public bool Ide;
        public bool Core;
    }

    private sealed record Reached(IReadOnlySet<string> ProviderTypes, bool Ide, bool Core);

    /// <summary>What one file binds, read on its own so that files can be read in parallel and merged afterwards.</summary>
    private sealed class TreeRead(SyntaxTree tree)
    {
        public readonly SyntaxTree Tree = tree;
        public readonly Dictionary<Key, Node> Nodes = [];
        public readonly Dictionary<ISymbol, string> Names = new(SymbolEqualityComparer.Default);
        public bool Composition;

        public Node NodeOf(Key key)
        {
            if (!Nodes.TryGetValue(key, out var node))
                Nodes[key] = node = new Node();
            return node;
        }
    }

    private sealed class Graph(CSharpCompilation compilation)
    {
        private readonly Dictionary<Key, Node> _nodes = [];
        private readonly HashSet<SyntaxTree> _composition = [];

        /// <summary>Reads every file. Each one binds against the same immutable compilation, so they can go in parallel.</summary>
        public void ReadAll(CancellationToken cancel)
        {
            var trees = compilation.SyntaxTrees;
            var reads = new TreeRead[trees.Length];
            Parallel.For(0, trees.Length, new ParallelOptions { CancellationToken = cancel },
                i => reads[i] = Read(trees[i], cancel));
            foreach (var read in reads)
            {
                foreach (var (key, node) in read.Nodes)
                {
                    if (!_nodes.TryGetValue(key, out var into))
                    {
                        _nodes[key] = node;
                        continue;
                    }

                    into.ProviderTypes.UnionWith(node.ProviderTypes);
                    into.Edges.UnionWith(node.Edges);
                    into.Ide |= node.Ide;
                    into.Core |= node.Core;
                }

                if (read.Composition)
                    _composition.Add(read.Tree);
            }
        }

        private bool IsSource(ISymbol symbol) =>
            SymbolEqualityComparer.Default.Equals(symbol.ContainingAssembly, compilation.Assembly);

        public bool NamesComposition(SyntaxTree tree) => _composition.Contains(tree);

        /// <summary>The member or type a piece of syntax declares, if it declares one.</summary>
        public static Key? Declared(SyntaxNode node, SemanticModel model, CancellationToken cancel) => node switch
        {
            BaseMethodDeclarationSyntax or BasePropertyDeclarationSyntax or VariableDeclaratorSyntax { Parent.Parent: BaseFieldDeclarationSyntax }
                or EnumMemberDeclarationSyntax or DelegateDeclarationSyntax
                => model.GetDeclaredSymbol(node, cancel) is { } member ? new Key(member.OriginalDefinition, Kind.Member) : null,
            BaseTypeDeclarationSyntax type
                => model.GetDeclaredSymbol(type, cancel) is { } declared ? new Key(declared.OriginalDefinition, Kind.TypeHeader) : null,
            _ => null,
        };

        /// <summary>
        /// Reads one file: each declaration's signature from its symbol, and its bodies, initialisers and attributes as the
        /// operations they bind to. Each member is bound once, where asking for each name's symbol rebinds what's around it.
        /// </summary>
        private TreeRead Read(SyntaxTree tree, CancellationToken cancel)
        {
            var model = compilation.GetSemanticModel(tree);
            var read = new TreeRead(tree);
            var declarations = tree.GetRoot(cancel)
                .DescendantNodes(node => node is not (BlockSyntax or ArrowExpressionClauseSyntax or EqualsValueClauseSyntax));
            foreach (var declaration in declarations)
            {
                if (Declared(declaration, model, cancel) is not { } key)
                    continue;
                var into = read.NodeOf(key);
                Signature(read, into, key.Symbol);
                foreach (var part in Parts(declaration))
                {
                    if (model.GetOperation(part, cancel) is { } operation)
                        Walk(read, into, operation, model, cancel);
                    else
                        Names(read, into, part, model, cancel);
                }
            }

            return read;
        }

        /// <summary>The parts of a declaration that bind: attributes, parameter defaults, bodies and initialisers.</summary>
        private static IEnumerable<SyntaxNode> Parts(SyntaxNode declaration)
        {
            var attributes = declaration switch
            {
                VariableDeclaratorSyntax { Parent.Parent: BaseFieldDeclarationSyntax field } => field.AttributeLists,
                MemberDeclarationSyntax member => member.AttributeLists,
                _ => default,
            };
            var parameters = declaration switch
            {
                BaseMethodDeclarationSyntax method => method.ParameterList.Parameters,
                IndexerDeclarationSyntax indexer => indexer.ParameterList.Parameters,
                DelegateDeclarationSyntax @delegate => @delegate.ParameterList.Parameters,
                TypeDeclarationSyntax { ParameterList: { } primary } => primary.Parameters,
                _ => default,
            };
            var accessors = declaration is BasePropertyDeclarationSyntax { AccessorList: { } list } ? list.Accessors : default;
            var parts = attributes.SelectMany(each => each.Attributes).Cast<SyntaxNode>()
                .Concat(parameters.SelectMany(parameter => parameter.AttributeLists.SelectMany(each => each.Attributes)))
                .Concat(parameters.Select(parameter => parameter.Default).OfType<SyntaxNode>())
                .Concat(accessors.SelectMany(accessor => accessor.AttributeLists.SelectMany(each => each.Attributes)))
                .Concat(accessors);
            return parts.Concat(declaration switch
            {
                BaseMethodDeclarationSyntax => [declaration],
                PropertyDeclarationSyntax property => new SyntaxNode?[] { property.ExpressionBody, property.Initializer }.OfType<SyntaxNode>(),
                IndexerDeclarationSyntax { ExpressionBody: { } body } => [body],
                VariableDeclaratorSyntax { Initializer: { } initializer } => [initializer],
                EnumMemberDeclarationSyntax { EqualsValue: { } value } => [value],
                TypeDeclarationSyntax { BaseList: { } bases } => bases.Types.OfType<PrimaryConstructorBaseTypeSyntax>(),
                _ => [],
            });
        }

        /// <summary>The types a declaration's own symbol names: what it returns, takes, holds, extends and constrains.</summary>
        private void Signature(TreeRead read, Node into, ISymbol symbol)
        {
            switch (symbol)
            {
                case IMethodSymbol method:
                    BindType(read, into, method.ReturnType);
                    foreach (var parameter in method.Parameters)
                        BindType(read, into, parameter.Type);
                    foreach (var constraint in method.TypeParameters.SelectMany(parameter => parameter.ConstraintTypes))
                        BindType(read, into, constraint);
                    break;
                case IPropertySymbol property:
                    BindType(read, into, property.Type);
                    foreach (var parameter in property.Parameters)
                        BindType(read, into, parameter.Type);
                    break;
                case IFieldSymbol field:
                    BindType(read, into, field.Type);
                    break;
                case IEventSymbol @event:
                    BindType(read, into, @event.Type);
                    break;
                case INamedTypeSymbol type:
                    BindType(read, into, type.BaseType);
                    foreach (var implemented in type.Interfaces)
                        BindType(read, into, implemented);
                    foreach (var constraint in type.TypeParameters.SelectMany(parameter => parameter.ConstraintTypes))
                        BindType(read, into, constraint);
                    if (type.DelegateInvokeMethod is { } invoke)
                        Signature(read, into, invoke);
                    // A primary constructor's parameters are declared on the type, not on a member of their own.
                    foreach (var primary in type.InstanceConstructors.Where(constructor =>
                                 constructor.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is TypeDeclarationSyntax)))
                        Signature(read, into, primary);
                    break;
            }
        }

        /// <summary>Every symbol and type an operation and the operations under it use, implicit ones included.</summary>
        private void Walk(TreeRead read, Node into, IOperation operation, SemanticModel model, CancellationToken cancel)
        {
            foreach (var each in operation.DescendantsAndSelf())
            {
                BindType(read, into, each.Type);
                switch (each)
                {
                    case IObjectCreationOperation { Constructor: { } constructor }:
                        Bind(read, into, constructor);
                        // Whatever is handed the object can call any of it, so constructing one of the project's own
                        // classes counts everything the class declares.
                        if (IsSource(constructor.ContainingType))
                            into.Edges.Add(new Key(constructor.ContainingType.OriginalDefinition, Kind.TypeWhole));
                        break;
                    case IInvocationOperation invocation:
                        Bind(read, into, invocation.TargetMethod);
                        break;
                    case IMemberReferenceOperation reference:
                        Bind(read, into, reference.Member);
                        break;
                    case IAnonymousFunctionOperation function:
                        Signature(read, into, function.Symbol);
                        break;
                    case ILocalFunctionOperation function:
                        Signature(read, into, function.Symbol);
                        break;
                    // The operation for a nameof is its constant, and a method it names isn't a reference under it. A test that
                    // runs another file's test by its name, as WindowLifetimeTests does, depends on that test all the same.
                    case INameOfOperation nameOf:
                        Names(read, into, nameOf.Argument.Syntax, model, cancel);
                        break;
                }

                if (each switch
                    {
                        IConversionOperation conversion => conversion.OperatorMethod,
                        IBinaryOperation binary => binary.OperatorMethod,
                        IUnaryOperation unary => unary.OperatorMethod,
                        ICompoundAssignmentOperation compound => compound.OperatorMethod,
                        IIncrementOrDecrementOperation step => step.OperatorMethod,
                        _ => null,
                    } is { } userOperator)
                    Bind(read, into, userOperator);
                BindType(read, into, each switch
                {
                    ITypeOfOperation typeOf => typeOf.TypeOperand,
                    IIsTypeOperation isType => isType.TypeOperand,
                    ISizeOfOperation sizeOf => sizeOf.TypeOperand,
                    IDeclarationPatternOperation pattern => pattern.MatchedType,
                    ITypePatternOperation pattern => pattern.MatchedType,
                    IRecursivePatternOperation pattern => pattern.MatchedType,
                    IVariableDeclaratorOperation variable => variable.Symbol.Type,
                    ICatchClauseOperation @catch => @catch.ExceptionType,
                    _ => null,
                });
            }
        }

        /// <summary>
        /// For a part Roslyn gives no operation for: the symbol of every name in it, and of every construction, indexer and
        /// attribute, which bind a symbol with no name of their own.
        /// </summary>
        private void Names(TreeRead read, Node into, SyntaxNode part, SemanticModel model, CancellationToken cancel)
        {
            foreach (var node in part.DescendantNodesAndSelf())
            {
                if (node is not (SimpleNameSyntax or ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax
                    or ConstructorInitializerSyntax or ElementAccessExpressionSyntax or AttributeSyntax
                    or PrimaryConstructorBaseTypeSyntax))
                    continue;
                var info = model.GetSymbolInfo(node, cancel);
                foreach (var symbol in info.Symbol is { } bound ? [bound] : info.CandidateSymbols)
                {
                    Bind(read, into, symbol);
                    if (node is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax
                        && symbol is IMethodSymbol { ContainingType: { } created } && IsSource(created))
                        into.Edges.Add(new Key(created.OriginalDefinition, Kind.TypeWhole));
                }
            }
        }

        private void Bind(TreeRead read, Node into, ISymbol symbol)
        {
            switch (symbol)
            {
                case INamespaceSymbol or IAliasSymbol or ILabelSymbol or IDiscardSymbol:
                    return;
                case ILocalSymbol local:
                    BindType(read, into, local.Type);
                    return;
                case IParameterSymbol parameter:
                    BindType(read, into, parameter.Type);
                    return;
                case IRangeVariableSymbol:
                    return;
                case ITypeSymbol type:
                    BindType(read, into, type);
                    return;
            }

            if (IsSource(symbol))
            {
                if (symbol is IMethodSymbol { MethodKind: MethodKind.LocalFunction or MethodKind.AnonymousFunction })
                    return;
                into.Edges.Add(new Key(symbol.OriginalDefinition, Kind.Member));
                // A generic helper's type arguments are named where it's used, not in the helper.
                if (symbol is IMethodSymbol { IsGenericMethod: true } generic)
                {
                    foreach (var argument in generic.TypeArguments)
                        BindType(read, into, argument);
                }

                BindType(read, into, symbol.ContainingType);
                return;
            }

            Mark(read, into, symbol.ContainingAssembly, (ISymbol?)symbol.ContainingType ?? symbol);
            BindType(read, into, symbol.ContainingType);
            switch (symbol)
            {
                case IMethodSymbol method:
                    BindType(read, into, method.ReturnType);
                    foreach (var parameter in method.Parameters)
                        BindType(read, into, parameter.Type);
                    foreach (var argument in method.TypeArguments)
                        BindType(read, into, argument);
                    break;
                case IPropertySymbol property:
                    BindType(read, into, property.Type);
                    foreach (var parameter in property.Parameters)
                        BindType(read, into, parameter.Type);
                    break;
                case IFieldSymbol field:
                    BindType(read, into, field.Type);
                    break;
                case IEventSymbol @event:
                    BindType(read, into, @event.Type);
                    break;
            }
        }

        private void BindType(TreeRead read, Node into, ITypeSymbol? type)
        {
            switch (type)
            {
                case null or ITypeParameterSymbol or IErrorTypeSymbol:
                    return;
                case IArrayTypeSymbol array:
                    BindType(read, into, array.ElementType);
                    return;
                case IPointerTypeSymbol pointer:
                    BindType(read, into, pointer.PointedAtType);
                    return;
                case INamedTypeSymbol named:
                    // The application's composition supplies the provider through DISetup's bindings, which no name shows.
                    if (named is { Name: "RealComposition" } or { Name: "DISetup", ContainingNamespace.Name: "HexIDE" })
                        read.Composition = true;
                    if (!IsSource(named))
                        Mark(read, into, named.ContainingAssembly, named.OriginalDefinition);
                    foreach (var argument in named.TypeArguments)
                        BindType(read, into, argument);
                    return;
            }
        }

        private void Mark(TreeRead read, Node into, IAssemblySymbol? assembly, ISymbol named)
        {
            if (assembly is null || SymbolEqualityComparer.Default.Equals(assembly, compilation.Assembly))
                return;
            var assemblyName = assembly.Identity.Name;
            if (ProviderAssemblies.Contains(assemblyName))
            {
                // Formatting a name is the walk's dearest step, so only the provider's are formatted, and once per file.
                if (!read.Names.TryGetValue(named, out var name))
                    read.Names[named] = name = named.ToDisplayString();
                into.ProviderTypes.Add(name);
            }
            else if (assemblyName == SharedAssembly)
                into.Core = true;
            else if (assemblyName.StartsWith("HexIDE", StringComparison.Ordinal))
                into.Ide = true;
        }

        /// <summary>What runs for every test in <paramref name="type"/>: its construction, setup and teardown, and its bases'.</summary>
        public IEnumerable<Key> Setup(INamedTypeSymbol? type)
        {
            for (var current = type; current is not null && IsSource(current); current = current.BaseType)
            {
                yield return new Key(current.OriginalDefinition, Kind.TypeHeader);
                foreach (var member in current.GetMembers())
                {
                    if (member is IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor }
                        or IMethodSymbol { Name: "Dispose" or "DisposeAsync" or "InitializeAsync" }
                        || member is IFieldSymbol or IPropertySymbol && HasInitializer(member))
                        yield return new Key(member.OriginalDefinition, Kind.Member);
                }

                foreach (var fixture in current.Interfaces.Where(i => i.Name is "IClassFixture" or "ICollectionFixture")
                             .SelectMany(i => i.TypeArguments).OfType<INamedTypeSymbol>().Where(IsSource))
                    yield return new Key(fixture.OriginalDefinition, Kind.TypeWhole);
            }
        }

        private static bool HasInitializer(ISymbol member) =>
            member.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() switch
            {
                VariableDeclaratorSyntax variable => variable.Initializer is not null,
                PropertyDeclarationSyntax property => property.Initializer is not null,
                _ => false,
            });

        /// <summary>Everything reachable from <paramref name="starts"/> through the test project's own declarations.</summary>
        public Reached Reach(IEnumerable<Key> starts)
        {
            var seen = new HashSet<Key>();
            var pending = new Stack<Key>(starts);
            var types = new HashSet<string>(StringComparer.Ordinal);
            var ide = false;
            var core = false;
            while (pending.Count > 0)
            {
                var key = pending.Pop();
                if (!seen.Add(key))
                    continue;
                if (key.Kind == Kind.TypeWhole && key.Symbol is INamedTypeSymbol whole)
                {
                    pending.Push(new Key(whole, Kind.TypeHeader));
                    foreach (var member in whole.GetMembers().Where(m => m is not INamedTypeSymbol))
                        pending.Push(new Key(member.OriginalDefinition, Kind.Member));
                    foreach (var basis in whole.Interfaces.Append(whole.BaseType).OfType<INamedTypeSymbol>().Where(IsSource))
                        pending.Push(new Key(basis.OriginalDefinition, Kind.TypeWhole));
                    continue;
                }

                if (!_nodes.TryGetValue(key, out var node))
                    continue;
                types.UnionWith(node.ProviderTypes);
                ide |= node.Ide;
                core |= node.Core;
                foreach (var edge in node.Edges)
                    pending.Push(edge);
            }

            return new Reached(types, ide, core);
        }
    }
}

/// <summary>
/// Holds the listing beside this project to the build (#3663). Its rows say which test files go with the provider when
/// the interpreter and forms engine move out (#3411), which stay, and which have to be split first.
/// </summary>
public class ProviderTestSortTests
{
    [Fact]
    public void The_listing_names_every_file_that_binds_a_provider_type_with_its_label()
    {
        var project = typeof(ProviderTestSortTests).Assembly.GetName().Name!;
        var cancel = TestContext.Current.CancellationToken;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var (sort, errors) = ProviderTestSort.Of(project, cancel);
        clock.Stop();
        errors.Should().BeEmpty("the sort trusts a name only in a compilation that builds, as the project does");

        var output = Path.Join(ProviderTestSort.Root(), "artifacts", "provider-test-sort");
        Directory.CreateDirectory(output);
        var written = Path.Join(output, project + ".md");
        File.WriteAllText(written, ProviderTestSort.Listing(project, sort));
        File.WriteAllText(Path.Join(output, project + "-detail.md"), ProviderTestSort.Detail(project, sort, clock.Elapsed));

        var listingPath = Path.Join(ProviderTestSort.ProjectFolder(project), ProviderTestSort.ListingName);
        var listed = File.Exists(listingPath)
            ? ProviderTestSort.ReadListing(File.ReadAllText(listingPath))
            : new Dictionary<string, string>();
        var sorted = sort.ToDictionary(file => file.Path, file => file.Label, StringComparer.Ordinal);
        var problems = sorted.Where(row => !listed.ContainsKey(row.Key)).Select(row => $"not listed: `{row.Key}` | {row.Value}")
            .Concat(listed.Where(row => !sorted.ContainsKey(row.Key)).Select(row => $"listed, binds no provider type now: `{row.Key}`"))
            .Concat(sorted.Where(row => listed.TryGetValue(row.Key, out var label) && label != row.Value)
                .Select(row => $"listed as {listed[row.Key]}, sorts as {row.Value}: `{row.Key}`"))
            .ToList();
        problems.Should().BeEmpty("{0} should match the build. Copy {1} over it. The differences:\n{2}",
            listingPath, written, string.Join("\n", problems.Take(40)));
    }

    // Each case is a test file in miniature, sorted against the real assemblies: InterpreterBrackets is the provider's,
    // MainViewViewModel the IDE's, and FormDefinition Core's, though its namespace is HexIDE.Runtime.ProjectElements.
    [Theory]
    [InlineData("class T { [Fact] public void A() { HexIDE.Runtime.ProjectElements.FormDefinition? f = null; } }", null)]
    [InlineData("class T { [Fact] public void A() { var b = new HexIDE.Runtime.Interpreter.InterpreterBrackets(); } }", "provider")]
    [InlineData("class T { [Fact] public void A() { var b = new HexIDE.Runtime.Interpreter.InterpreterBrackets(); } " +
                "[Fact] public void B() { HexIDE.MainViewViewModel? m = null; } }", "split")]
    [InlineData("class T { [Fact] public void A() => Make(); static object Make() => new HexIDE.Runtime.Interpreter.InterpreterBrackets(); }",
        "provider")]
    [InlineData("class T { [Fact] public void A() { var s = new StandIn(); } } " +
                "class StandIn { public object Reader() => new HexIDE.Runtime.Interpreter.InterpreterBrackets(); }", "provider")]
    [InlineData("class Helper { public static object Reader() => new HexIDE.Runtime.Interpreter.InterpreterBrackets(); }", "provider")]
    [InlineData("class T { [Fact] public void A() { HexIDE.MainViewViewModel? m = null; } " +
                "static object Unused() => new HexIDE.Runtime.Interpreter.InterpreterBrackets(); }", "IDE")]
    [InlineData("class T { private readonly object _b = new HexIDE.Runtime.Interpreter.InterpreterBrackets(); " +
                "[Fact] public void A() { HexIDE.MainViewViewModel? m = null; } }", "split")]
    [InlineData("class T { [Fact] public void A() { var r = Reader(); } " +
                "static HexIDE.Language.IBracketReader Reader() => new HexIDE.Runtime.Interpreter.InterpreterBrackets(); }", "provider")]
    [InlineData("class T { [Theory] [InlineData(nameof(Other.Run))] public void A(string name) { } } " +
                "class Other { public void Run() { var b = new HexIDE.Runtime.Interpreter.InterpreterBrackets(); } }", "provider")]
    public void A_file_is_sorted_by_the_assembly_each_name_it_binds_comes_from(string source, string? expected)
    {
        var cancel = TestContext.Current.CancellationToken;
        var folder = Path.GetTempPath();
        var tree = CSharpSyntaxTree.ParseText(source, path: Path.Join(folder, "Case.cs"), cancellationToken: cancel);
        var usings = CSharpSyntaxTree.ParseText("global using System; global using Xunit;", cancellationToken: cancel);
        var compilation = CSharpCompilation.Create("Case", [tree, usings], ProviderTestSort.References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var (sort, errors) = ProviderTestSort.Sort(compilation, [tree], folder, cancel);

        errors.Should().BeEmpty();
        var labels = sort.Select(file => file.Label).ToList();
        labels.Should().Equal(expected is null ? [] : [expected]);
    }
}
