using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HexIDE.Tests.Infrastructure;

public class AvaloniaTestGuardTests
{
    private static readonly Lazy<MetadataReference[]> References = new(() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat(Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
            .Distinct().Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToArray());

    private static readonly string Folder = Path.Combine(RepoTree.Root(), "IDE", "HexIDE.Tests");

    // This project's own source, bound against the existing build so aliases, target-typed new and derived controls
    // are covered. Shared by the guards below.
    private static readonly Lazy<(SyntaxTree[] Trees, CSharpCompilation Compilation)> Source = new(() =>
    {
        var trees = Directory.GetFiles(Folder, "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(Folder, path).Split(Path.DirectorySeparatorChar)
                .Any(part => part is "bin" or "obj"))
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path)).ToArray();
        var globals = CSharpSyntaxTree.ParseText(
            "global using System; global using System.Linq; global using System.Collections.Generic; global using Xunit;");
        return (trees, CSharpCompilation.Create("AffinityGuard", trees.Append(globals), References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)));
    });

    /// <summary>
    /// A test that calls <c>HeadlessUnitTestSession.Dispatch</c> itself resumes on the session's thread, and takes
    /// xunit's runner there with it, which hangs the next <c>[AvaloniaFact]</c> and so the whole run (#4130).
    /// <c>HeadlessDispatch.RunAsync</c> is the one place that calls it.
    /// </summary>
    [Fact]
    public void OnlyHeadlessDispatchCallsTheSessionsDispatch()
    {
        var (trees, compilation) = Source.Value;
        var failures = trees.Where(tree => Path.GetFileName(tree.FilePath) != "HeadlessDispatch.cs")
            .SelectMany(tree => DirectDispatches(tree, compilation.GetSemanticModel(tree))
                .Select(line => $"{Path.GetRelativePath(Folder, tree.FilePath)}:{line}"))
            .ToArray();
        failures.Should().BeEmpty("a test reaches the headless session's thread through HeadlessDispatch.RunAsync, "
            + "or the run can hang with no failure: {0}", string.Join(Environment.NewLine, failures));
    }

    [Theory]
    [InlineData("Task T() => Avalonia.Headless.HeadlessUnitTestSession.GetOrStartForAssembly(typeof(Fixture).Assembly).Dispatch<bool>(() => Task.FromResult(true), default);", true)]
    [InlineData("Task T() { var s = Avalonia.Headless.HeadlessUnitTestSession.GetOrStartForAssembly(typeof(Fixture).Assembly); return s.Dispatch(() => { }, default); }", true)]
    [InlineData("Task T() => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }).GetTask();", false)]
    [InlineData("Task T() => Dispatch(); Task Dispatch() => Task.CompletedTask;", false)]
    public void DispatchGuardFindsTheSessionsDispatchOnly(string members, bool expected)
    {
        var tree = CSharpSyntaxTree.ParseText("using System.Threading.Tasks; class Fixture { " + members + " }",
            cancellationToken: TestContext.Current.CancellationToken);
        var compilation = CSharpCompilation.Create("Fixture", [tree], References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        DirectDispatches(tree, compilation.GetSemanticModel(tree)).Any().Should().Be(expected);
    }

    private static IEnumerable<int> DirectDispatches(SyntaxTree tree, SemanticModel model) =>
        tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(invocation => invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "Dispatch" }
                && model.GetSymbolInfo(invocation) is var info
                && (info.Symbol ?? info.CandidateSymbols.FirstOrDefault())?.ContainingType.ToDisplayString()
                    == "Avalonia.Headless.HeadlessUnitTestSession")
            .Select(invocation => invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1);

    [Fact]
    public void PlainTestsDoNotTouchAvalonia()
    {
        var folder = Folder;
        var (trees, compilation) = Source.Value;
        var failures = new List<string>();
        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var method in tree.GetRoot(TestContext.Current.CancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (!method.AttributeLists.SelectMany(list => list.Attributes)
                    .Any(attribute => model.GetSymbolInfo(attribute, TestContext.Current.CancellationToken)
                        .Symbol?.ContainingType.ToDisplayString() is "Xunit.FactAttribute" or "Xunit.TheoryAttribute"))
                    continue;
                if (TestTouchesAvalonia(method, model))
                    failures.Add($"{Path.GetRelativePath(folder, tree.FilePath)}: {method.Identifier.Text}");
            }
        }
        failures.Should().BeEmpty("tests touching Avalonia must use AvaloniaFact/AvaloniaTheory: {0}",
            string.Join(Environment.NewLine, failures));
    }

    [Theory]
    [InlineData("AnInnerFoldKeepsItsStateWhenTheOuterVanishes")]
    [InlineData("AReturningFoldTakesItsDefaultRatherThanTheSurvivorsState")]
    [InlineData("ASameStartPairKeepsStatesAcrossUpdates")]
    public void GuardRecognizesTheReportedFoldingTheories(string name)
    {
        var path = Path.Combine(RepoTree.Root(), "IDE", "HexIDE.Tests", "Controls", "Vb6FoldingAdapterTests.cs");
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path),
            cancellationToken: TestContext.Current.CancellationToken);
        var compilation = CSharpCompilation.Create("FoldingGuard", [tree], References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var method = tree.GetRoot(TestContext.Current.CancellationToken).DescendantNodes()
            .OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.Text == name);

        TestTouchesAvalonia(method, compilation.GetSemanticModel(tree)).Should().BeTrue();
        method.AttributeLists.SelectMany(list => list.Attributes).Select(attribute => attribute.Name.ToString())
            .Should().Contain("AvaloniaTheory");
    }

    [Theory]
    [InlineData("void Test() { var c = new Avalonia.Controls.Button(); }", true)]
    [InlineData("void Test() { Make(); } void Make() { Again(); } void Again() { var c = new Avalonia.Controls.Button(); }", true)]
    [InlineData("void Test() { Avalonia.Controls.Button c = new(); }", true)]
    [InlineData("void Test() {} Avalonia.Controls.Button c = new();", true)]
    [InlineData("void Test() {} Fixture() { var c = new Avalonia.Controls.Button(); }", true)]
    [InlineData("void Test() { c.Content = null; } Avalonia.Controls.Button c;", true)]
    [InlineData("void Test() { var c = new MyControl(); } class MyControl : Avalonia.Controls.Button {}", true)]
    [InlineData("void Test() { var a = new AvaloniaEdit.Editing.TextArea(); }", true)]
    [InlineData("void Test() { Make<int>(); } void Make<T>() { var c = new Avalonia.Controls.Button(); }", true)]
    [InlineData("void Test() { var d = new AvaloniaEdit.Document.TextDocument(); }", true)]
    [InlineData("void Test() { var f = new AvaloniaEdit.Folding.FoldingManager(null); }", true)]
    [InlineData("void Test() { var b = new Avalonia.Media.SolidColorBrush(); }", true)]
    [InlineData("void Test() { var g = new Avalonia.Media.StreamGeometry(); }", true)]
    [InlineData("void Test() { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }", true)]
    [InlineData("void Test() { Again(); } void Again() { Test(); }", false)]
    [InlineData("void Test() { var text = \"new Button() Dispatcher.UIThread\"; /* new TextDocument() */ }", false)]
    public void GuardFollowsTypesAndHelpers(string members, bool expected)
    {
        var tree = CSharpSyntaxTree.ParseText("class Fixture { " + members + " }",
            cancellationToken: TestContext.Current.CancellationToken);
        var compilation = CSharpCompilation.Create("Fixture", [tree], References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var method = tree.GetRoot(TestContext.Current.CancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>().First();
        TestTouchesAvalonia(method, compilation.GetSemanticModel(tree)).Should().Be(expected);
    }

    private static bool TestTouchesAvalonia(MethodDeclarationSyntax method, SemanticModel model)
    {
        var owner = method.Parent!;
        var setup = owner.ChildNodes().Where(member => member is FieldDeclarationSyntax or ConstructorDeclarationSyntax);
        return setup.Prepend(method).Any(member => TouchesAvalonia(member, owner, model, []));
    }

    private static bool TouchesAvalonia(SyntaxNode node, SyntaxNode owner, SemanticModel model,
        HashSet<SyntaxNode> visited)
    {
        if (!visited.Add(node))
            return false;
        foreach (var expression in node.DescendantNodes().OfType<ExpressionSyntax>())
        {
            ITypeSymbol? type = null;
            if (expression is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax)
                type = model.GetTypeInfo(expression, TestContext.Current.CancellationToken).Type;
            else if (expression is MemberAccessExpressionSyntax)
                type = model.GetSymbolInfo(expression, TestContext.Current.CancellationToken).Symbol?.ContainingType;
            for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
            {
                if (current.ToDisplayString() is "Avalonia.AvaloniaObject" or
                    "AvaloniaEdit.Document.TextDocument" or "AvaloniaEdit.Folding.FoldingManager" or
                    "Avalonia.Threading.Dispatcher")
                    return true;
            }
            if (expression is not InvocationExpressionSyntax invocation)
                continue;
            var name = invocation.Expression switch
            {
                IdentifierNameSyntax identifier => identifier.Identifier.Text,
                GenericNameSyntax generic => generic.Identifier.Text,
                MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
                _ => ""
            };
            foreach (var helper in owner.ChildNodes().OfType<MethodDeclarationSyntax>()
                         .Where(method => method.Identifier.Text == name))
            {
                if (TouchesAvalonia(helper, owner, model, visited))
                    return true;
            }
        }
        return false;
    }
}
