using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Tools.ObjectBrowser;

namespace HexIDE.Tests.ViewModels;

public class ObjectBrowserVbaLibraryTests
{
    private static LanguageServerConnection Connection(LanguageConnectionState state, string caps) =>
        new("server", "Server", LanguageConnectionKind.LanguageServer, state, [".bas"], "vb6",
            JsonDocument.Parse(caps).RootElement.Clone());

    private static (ObjectBrowserToolViewModel Vm, ILspClient Client, ILanguageConnectionRegistry Registry) Make(
        LanguageConnectionState state, string caps, bool throws = false)
    {
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns(new List<ProjectDefinition>());
        var client = Substitute.For<ILspClient>();
        client.RequestBuiltinSymbolsAsync(Arg.Any<CancellationToken>()).Returns(
            throws ? Task.FromException<VbaBuiltinSymbol[]>(new System.Exception("unavailable"))
                : Task.FromResult<VbaBuiltinSymbol[]>([new("Abs", "Function Abs(number)", "Absolute value")]));
        var registry = Substitute.For<ILanguageConnectionRegistry>();
        registry.Connections.Returns([Connection(state, caps)]);
        var loc = Substitute.For<ILocalizationService>();
        loc.GetString(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        return (new ObjectBrowserToolViewModel(projects, client, Substitute.For<IEditorService>(),
            Substitute.For<IComponentRegistry>(), Substitute.For<ITypeLibraryService>(),
            Substitute.For<IFocusedProjectUtil>(), loc, registry), client, registry);
    }

    private const string Capable = """{"experimental":{"vbBuiltinSymbols":true}}""";

    [Fact]
    public void CapableServerPopulatesVbaAndAllLibraries()
    {
        var (vm, _, _) = Make(LanguageConnectionState.Running, Capable);
        var vba = vm.Libraries.Single(l => l.Name == "VBA");
        vm.FilteredClasses.Should().ContainSingle(c => c.LibraryName == "VBA");
        vm.SelectedLibrary = vba;
        vba.IsLoaded.Should().BeTrue();
        vm.FilteredClasses.Should().ContainSingle().Which.Name.Should().Be("Str.Tool.ObjectBrowser.Vba.Globals");
        vm.SelectedClass = vm.FilteredClasses[0];
        vm.FilteredMembers.Should().ContainSingle().Which.Name.Should().Be("Abs");
        vm.FilteredMembers[0].Signature.Should().Be("Function Abs(number)");
        vm.FilteredMembers[0].Description.Should().Be("Absolute value");
    }

    [Theory]
    [InlineData(LanguageConnectionState.NotStarted, "NoServer")]
    [InlineData(LanguageConnectionState.Running, "NoProvider")]
    public void UnavailableStatusAppearsOnlyUnderVba(LanguageConnectionState state, string status)
    {
        var (vm, _, _) = Make(state, "{}");
        var vba = vm.Libraries.Single(l => l.Name == "VBA");
        vm.SelectedLibrary = vba;
        vm.FilteredClasses.Should().ContainSingle().Which.Name.Should().Be("Str.Tool.ObjectBrowser.Vba." + status);

        vm.SelectedLibrary = vm.Libraries[0];
        vm.FilteredClasses.Should().NotContain(c => c.LibraryName == "VBA");
        vba.Classes.Should().ContainSingle().Which.Name.Should().Be("Str.Tool.ObjectBrowser.Vba." + status);
    }

    [Theory]
    [InlineData(LanguageConnectionState.NotStarted, "{}", "NoServer")]
    [InlineData(LanguageConnectionState.Running, "{}", "NoProvider")]
    [InlineData(LanguageConnectionState.Running, "{\"experimental\":{\"vbBuiltinSymbols\":false}}", "NoProvider")]
    [InlineData(LanguageConnectionState.Stopped, Capable, "NoServer")]
    public async Task UnavailableServerDoesNotLatchAndSelectionRetries(LanguageConnectionState state, string caps, string status)
    {
        var (vm, client, registry) = Make(state, caps);
        var vba = vm.Libraries.Single(l => l.Name == "VBA");
        vba.IsLoaded.Should().BeFalse();
        vba.Classes.Should().ContainSingle().Which.Name.Should().Be("Str.Tool.ObjectBrowser.Vba." + status);
        await client.DidNotReceive().RequestBuiltinSymbolsAsync(Arg.Any<CancellationToken>());
        registry.Connections.Returns([Connection(LanguageConnectionState.Running, Capable)]);
        vm.SelectedLibrary = vba;
        vba.Classes.Should().ContainSingle().Which.Members.Should().ContainSingle();
        vba.IsLoaded.Should().BeTrue();
    }

    [Fact]
    public void EmptyAnswerLatchesWithItsOwnStatus()
    {
        var (vm, client, registry) = Make(LanguageConnectionState.NotStarted, "{}");
        client.RequestBuiltinSymbolsAsync(Arg.Any<CancellationToken>()).Returns([]);
        registry.Connections.Returns([Connection(LanguageConnectionState.Running, Capable)]);
        vm.SelectedLibrary = vm.Libraries.Single(l => l.Name == "VBA");
        vm.SelectedLibrary.IsLoaded.Should().BeTrue();
        vm.FilteredClasses.Should().ContainSingle().Which.Name.Should().Be("Str.Tool.ObjectBrowser.Vba.Empty");
        vm.SelectedLibrary = vm.Libraries[0];
        vm.FilteredClasses.Should().NotContain(c => c.LibraryName == "VBA");
    }

    [Fact]
    public void FailedRequestDoesNotLatchAndAllLibrariesSelectionRetries()
    {
        var (vm, client, _) = Make(LanguageConnectionState.Running, Capable, throws: true);
        var vba = vm.Libraries.Single(l => l.Name == "VBA");
        vba.IsLoaded.Should().BeFalse();
        vm.SelectedLibrary = vm.Libraries.Single(l => l.Name == "VB");
        client.RequestBuiltinSymbolsAsync(Arg.Any<CancellationToken>()).Returns([new VbaBuiltinSymbol("Abs", "Function Abs(number)", "Absolute value")]);
        vm.SelectedLibrary = vm.Libraries[0];
        vba.IsLoaded.Should().BeTrue();
        vm.FilteredClasses.Should().ContainSingle(c => c.LibraryName == "VBA" && c.Members.Count == 1);
    }
}
