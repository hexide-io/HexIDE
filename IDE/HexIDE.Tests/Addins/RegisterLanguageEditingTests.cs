using HexIDE.Addins;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Language;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Tests.Sidecar;
using Serilog.Events;
using LspRange = HexIDE.Lsp.Messages.Range;

namespace HexIDE.Tests.Addins;

/// <summary>
/// A language's editing helpers register through the host an add-in is given (#5403): which lines an <c>#If</c> leaves out,
/// where the parentheses are, and what a module declares. Internal for phase 2, and checked as add-in API would be.
/// </summary>
public sealed class RegisterLanguageEditingTests
{
    private static readonly AddinOrigin Probe = new("com.example.probe", "Probe") { IsComposed = true };
    private static readonly AddinOrigin Other = new("com.example.other", "Other") { IsComposed = true };

    // HexIDE's own grants plus the two probes', so the tests don't depend on what the real table grants anyone but HexIDE.Basic.
    private static AddinLanguageEditingService Service() => new()
    {
        Grants =
        [
            .. RegistrationGrants.All,
            new(Probe.Id, Composed: true) { LanguageEditing = true },
            new(Other.Id, Composed: true) { LanguageEditing = true },
        ],
    };

    private static HexIdeHost Ide(AddinLanguageEditingService editing) =>
        new(null!, null!, null!, Substitute.For<IAddinRegistry>(), null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, new AddinResourceService(), new AddinLanguagePackService(), editing);

    private static ILanguageEditingRegistration Owned(AddinLanguageEditingService editing, AddinOrigin owner) =>
        new AddinOwnedHost(Ide(editing), null!, owner);

    private static readonly LspRange Here = new(new Position(0, 0), new Position(0, 1));

    private sealed class Helpers(string tag) : IConditionalCodeReader, IBracketReader, IDeclarationReader
    {
        public int Calls { get; private set; }
        public IReadOnlyList<LineRange> InactiveLines(string source, ConditionalConstants constants)
        {
            Calls++;
            return [new LineRange(1, source.Length)];
        }
        public IReadOnlyList<BracketMark> Brackets(string source)
        {
            Calls++;
            return [new BracketMark(source.Length, 0)];
        }
        public IReadOnlyList<DocumentSymbol> Declarations(string source)
        {
            Calls++;
            return [new DocumentSymbol(tag, SymbolKind.Method, Here, Here)];
        }
    }

    private sealed class Throwing : IConditionalCodeReader, IBracketReader, IDeclarationReader
    {
        public bool Throws { get; set; } = true;
        public string? DefaultTarget(ProjectDefinition project) =>
            Throws ? throw new InvalidOperationException("default target broke") : "probe";
        public IReadOnlyList<LineRange> InactiveLines(string source, ConditionalConstants constants) =>
            Throws ? throw new InvalidOperationException("#If broke") : [new LineRange(2, 2)];
        public IReadOnlyList<BracketMark> Brackets(string source) =>
            Throws ? throw new InvalidOperationException("brackets broke") : [new BracketMark(0, 0)];
        public IReadOnlyList<DocumentSymbol> Declarations(string source) =>
            Throws ? throw new InvalidOperationException("declarations broke") : [new DocumentSymbol("Sub1", SymbolKind.Method, Here, Here)];
    }

    private static LanguageEditing For(object helpers, params string[] languages) => new(languages)
    {
        ConditionalCode = (IConditionalCodeReader)helpers,
        Brackets = (IBracketReader)helpers,
        Declarations = (IDeclarationReader)helpers,
    };

    private static readonly ConditionalConstants NoConstants = new(ConditionalConstants.NoArguments);

    [Fact]
    public void A_granted_owner_s_helpers_answer_for_each_of_its_languages_in_any_case()
    {
        var editing = Service();
        var helpers = new Helpers("Probe");

        Owned(editing, Probe).RegisterLanguageEditing(For(helpers, "acme", "acme-script"));

        editing.ConditionalCodeFor("ACME").InactiveLines("abc", NoConstants).Should().Equal(new LineRange(1, 3));
        editing.BracketsFor("acme").Brackets("ab").Should().Equal(new BracketMark(2, 0));
        editing.DeclarationsFor("Acme-Script").Declarations("x").Select(s => s.Name).Should().Equal("Probe");
        helpers.Calls.Should().Be(3);
    }

    [Fact]
    public void An_owner_without_the_grant_is_refused_and_logged_and_its_helpers_never_answer()
    {
        using var log = new CapturedLog();
        var editing = Service();
        var helpers = new Helpers("Stranger");
        var stranger = new AddinOrigin("com.example.stranger", "Stranger") { IsComposed = true };

        var register = () => Owned(editing, stranger).RegisterLanguageEditing(For(helpers, "acme"));

        register.Should().Throw<InvalidOperationException>().WithMessage("Stranger isn't one HexIDE lets register*");
        log.Messages.Should().Contain(m => m.Contains("refused \"Stranger\"", StringComparison.Ordinal)
            && m.Contains("holds no grant", StringComparison.Ordinal));
        editing.BracketsFor("acme").Brackets("()").Should().BeEmpty();
        helpers.Calls.Should().Be(0);
    }

    // The grant's name alone isn't enough: a package naming itself hexide.basic is neither composed nor signed first-party.
    [Fact]
    public void A_package_naming_itself_the_Basic_provider_is_refused()
    {
        var editing = new AddinLanguageEditingService();
        var impostor = new AddinOrigin(RegistrationGrants.BasicProvider, "HexIDE.Basic");

        var register = () => Owned(editing, impostor).RegisterLanguageEditing(For(new Helpers("Impostor"), "vb6"));

        register.Should().Throw<InvalidOperationException>();
        editing.DeclarationsFor("vb6").Declarations("Sub A()\nEnd Sub").Should().BeEmpty();
    }

    [Fact]
    public void The_IDE_s_own_host_refuses_it()
    {
        var editing = Service();
        ILanguageEditingRegistration ide = Ide(editing);

        var register = () => ide.RegisterLanguageEditing(For(new Helpers("Probe"), "acme"));

        register.Should().Throw<InvalidOperationException>().WithMessage("*only through the host an add-in is given*");
        editing.BracketsFor("acme").Brackets("()").Should().BeEmpty();
    }

    [Fact]
    public void A_language_another_owner_serves_is_refused_whole_naming_both_owners_and_the_first_owner_s_stay()
    {
        using var log = new CapturedLog();
        var editing = Service();
        Owned(editing, Probe).RegisterLanguageEditing(For(new Helpers("Probe"), "acme"));

        var register = () => Owned(editing, Other).RegisterLanguageEditing(For(new Helpers("Other"), "fresh", "ACME"));

        register.Should().Throw<InvalidOperationException>()
            .WithMessage("Other can't register editing helpers for ACME: Probe (com.example.probe) serves it already.");
        log.Messages.Should().Contain(m => m.Contains("refused \"Other\"", StringComparison.Ordinal)
            && m.Contains("\"Probe\" (\"com.example.probe\") serves it already", StringComparison.Ordinal));
        editing.DeclarationsFor("acme").Declarations("").Select(s => s.Name).Should().Equal("Probe");
        editing.DeclarationsFor("fresh").Declarations("").Should().BeEmpty("nothing of a refused registration is kept");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("plaintext")]
    public void A_language_with_nothing_registered_gets_no_greying_brackets_or_declarations(string? language)
    {
        var editing = Service();
        Owned(editing, Probe).RegisterLanguageEditing(For(new Helpers("Probe"), "acme"));

        editing.ConditionalCodeFor(language).InactiveLines("#If 0 Then\nx\n#End If", NoConstants).Should().BeEmpty();
        editing.BracketsFor(language).Brackets("(x)").Should().BeEmpty();
        editing.DeclarationsFor(language).Declarations("Sub A()\nEnd Sub").Should().BeEmpty();
    }

    [Fact]
    public void A_language_with_some_helpers_registered_gets_only_those()
    {
        var editing = Service();
        Owned(editing, Probe).RegisterLanguageEditing(new LanguageEditing(["acme"]) { Brackets = new Helpers("Probe") });

        editing.BracketsFor("acme").Brackets("()").Should().ContainSingle();
        editing.ConditionalCodeFor("acme").InactiveLines("x", NoConstants).Should().BeEmpty();
        editing.DeclarationsFor("acme").Declarations("x").Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("acme| ")]
    public void A_registration_naming_no_language_or_a_blank_one_is_refused(string named)
    {
        var editing = Service();
        string[] languages = named.Length == 0 ? [] : named.Split('|');

        var register = () => Owned(editing, Probe).RegisterLanguageEditing(For(new Helpers("Probe"), languages));

        register.Should().Throw<ArgumentException>();
        editing.BracketsFor("acme").Brackets("()").Should().BeEmpty();
    }

    // The code window asks on every edit, so a helper that throws answers nothing for that call, and the next call asks again.
    [Fact]
    public void A_throwing_helper_answers_nothing_for_that_call_logs_a_warning_once_and_stays_registered()
    {
        using var log = new CapturedLog();
        var editing = Service();
        var helpers = new Throwing();
        Owned(editing, Probe).RegisterLanguageEditing(For(helpers, "acme"));
        var conditional = editing.ConditionalCodeFor("acme");
        var brackets = editing.BracketsFor("acme");
        var declarations = editing.DeclarationsFor("acme");
        var project = new ProjectDefinition(VBProjectType.EXE, "P");

        for (var i = 0; i < 2; i++)
        {
            conditional.InactiveLines("x", NoConstants).Should().BeEmpty();
            conditional.DefaultTarget(project).Should().BeNull();
            brackets.Brackets("()").Should().BeEmpty();
            declarations.Declarations("x").Should().BeEmpty();
        }

        var thrown = log.Events.Where(e => e.Exception is InvalidOperationException).ToList();
        thrown.Where(e => e.Level == LogEventLevel.Warning).Select(e => e.Exception!.Message)
            .Should().Equal("#If broke", "brackets broke", "declarations broke");
        thrown.Where(e => e.Level == LogEventLevel.Debug).Select(e => e.Exception!.Message)
            .Should().Equal("default target broke", "#If broke", "default target broke", "brackets broke", "declarations broke");
        thrown.Should().OnlyContain(e => e.RenderMessage(null).Contains("\"Probe\"", StringComparison.Ordinal));

        helpers.Throws = false;
        conditional.DefaultTarget(project).Should().Be("probe");
        conditional.InactiveLines("x", NoConstants).Should().Equal(new LineRange(2, 2));
        brackets.Brackets("()").Should().Equal(new BracketMark(0, 0));
        declarations.Declarations("x").Select(s => s.Name).Should().Equal("Sub1");
    }

    [Fact]
    public void A_helper_answering_null_answers_nothing()
    {
        var editing = Service();
        var nulls = Substitute.For<IConditionalCodeReader, IBracketReader, IDeclarationReader>();
        ((IConditionalCodeReader)nulls).InactiveLines(default!, default!).ReturnsForAnyArgs((IReadOnlyList<LineRange>)null!);
        ((IBracketReader)nulls).Brackets(default!).ReturnsForAnyArgs((IReadOnlyList<BracketMark>)null!);
        ((IDeclarationReader)nulls).Declarations(default!).ReturnsForAnyArgs((IReadOnlyList<DocumentSymbol>)null!);
        Owned(editing, Probe).RegisterLanguageEditing(For(nulls, "acme"));

        editing.ConditionalCodeFor("acme").InactiveLines("x", NoConstants).Should().BeEmpty();
        editing.BracketsFor("acme").Brackets("()").Should().BeEmpty();
        editing.DeclarationsFor("acme").Declarations("x").Should().BeEmpty();
    }

    [Fact]
    public void A_reader_taken_before_a_registration_follows_it_in_and_out()
    {
        var editing = Service();
        var brackets = editing.BracketsFor("acme");
        brackets.Brackets("()").Should().BeEmpty();

        var registration = Owned(editing, Probe).RegisterLanguageEditing(For(new Helpers("Probe"), "acme"));
        brackets.Brackets("()").Should().ContainSingle();

        registration.Dispose();
        brackets.Brackets("()").Should().BeEmpty();
    }

    [Fact]
    public void A_handle_disposed_again_or_late_leaves_a_registration_made_after_it()
    {
        var editing = Service();
        var first = Owned(editing, Probe).RegisterLanguageEditing(For(new Helpers("First"), "acme"));
        first.Dispose();
        Owned(editing, Other).RegisterLanguageEditing(For(new Helpers("Second"), "acme"));

        first.Dispose();

        editing.DeclarationsFor("acme").Declarations("").Select(s => s.Name).Should().Equal("Second");
    }

    // The add-in registry withdraws a registration under its own lock, so Changed must come after the registry's is released:
    // a handler, or a thread it waits for, that asks for a helper would otherwise deadlock on it.
    [Fact]
    public void Changed_is_raised_on_register_and_withdraw_with_the_registry_s_lock_released()
    {
        var editing = Service();
        var answered = new List<int>();
        editing.Changed += () =>
        {
            var asked = Task.Run(() => editing.BracketsFor("acme").Brackets("()").Count);
            asked.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken).Should().BeTrue("the registry's lock is free");
            answered.Add(asked.Result);
        };

        var registration = Owned(editing, Probe).RegisterLanguageEditing(For(new Helpers("Probe"), "acme"));
        registration.Dispose();
        registration.Dispose();

        answered.Should().Equal(1, 0);
    }

    [Fact]
    public void A_refused_registration_raises_no_Changed()
    {
        var editing = Service();
        Owned(editing, Probe).RegisterLanguageEditing(For(new Helpers("Probe"), "acme"));
        var raised = 0;
        editing.Changed += () => raised++;

        var register = () => Owned(editing, Other).RegisterLanguageEditing(For(new Helpers("Other"), "acme"));

        register.Should().Throw<InvalidOperationException>();
        raised.Should().Be(0);
    }

    [Fact]
    public void The_Basic_provider_registers_its_helpers_for_vb6_and_twinbasic_through_the_owned_host()
    {
        var editing = new AddinLanguageEditingService();
        var host = Substitute.For<IHexIdeHost>();
        host.RegisterResources(Arg.Any<Uri>()).Returns(_ => Substitute.For<IDisposable>());
        host.RegisterLanguagePacks(Arg.Any<Uri>()).Returns(_ => Substitute.For<IDisposable>());
        host.RegisterComponent(Arg.Any<IComponentClass>()).Returns(_ => Substitute.For<IDisposable>());
        host.RegisterToolboxItem(Arg.Any<IComponentClass>(), Arg.Any<string?>()).Returns(_ => Substitute.For<IDisposable>());
        host.RegisterControlLibrary(Arg.Any<IControlLibrary>(), Arg.Any<Func<IReadOnlyList<IComponentClass>>>())
            .Returns(_ => Substitute.For<IDisposable>());
        host.RegisterDesignerPictures(Arg.Any<Func<IDesignerDrawingSurface, IDesignerPictures>>()).Returns(_ => Substitute.For<IDisposable>());
        var owned = new ProviderHost(host, Owned(editing, InBoxRegistry.Provider));
        using var provider = new BasicProvider();

        provider.Initialize(owned);

        BasicProvider.LanguageEditing.LanguageIds.Should().Equal("vb6", "twinbasic");
        foreach (var language in new[] { "vb6", "twinbasic" })
        {
            editing.BracketsFor(language).Brackets("(x)").Should().HaveCount(2);
            editing.ConditionalCodeFor(language).InactiveLines("#If 0 Then\nx\n#End If\n", NoConstants).Should().NotBeEmpty();
        }
        editing.DeclarationsFor("vb6").Declarations("Sub Bump()\nEnd Sub").Select(s => s.Name).Should().Equal("Bump");

        provider.Dispose();
        editing.BracketsFor("vb6").Brackets("(x)").Should().BeEmpty();
    }

    // The provider's other registrations go to a substitute, which can't take Core's internal interface; its language editing
    // goes through an owned host over the IDE's, as HexIDE.Desktop composes it.
    private sealed class ProviderHost(IHexIdeHost host, ILanguageEditingRegistration editing) : IHexIdeHost, ILanguageEditingRegistration
    {
        public IDisposable RegisterLanguageEditing(LanguageEditing languageEditing) => editing.RegisterLanguageEditing(languageEditing);
        public IMenuContributor Menus => host.Menus;
        public IToolWindowContributor ToolWindows => host.ToolWindows;
        public IAddinEvents Events => host.Events;
        public IAddinRegistry Registry => host.Registry;
        public IEditorAccess Editor => host.Editor;
        public IProjectAccess Project => host.Project;
        public IDiagnosticsAccess Diagnostics => host.Diagnostics;
        public IOptionsPageContributor Options => host.Options;
        public IServerChatTools ServerChatTools => host.ServerChatTools;
        public IDisposable RegisterCommand(string id, string label, Action onInvoke, string? gesture = null, Func<bool>? canExecute = null) =>
            host.RegisterCommand(id, label, onInvoke, gesture, canExecute);
        public IDisposable RegisterCommand(string id, AddinTitle label, Action onInvoke, string? gesture = null, Func<bool>? canExecute = null) =>
            host.RegisterCommand(id, label, onInvoke, gesture, canExecute);
        public IDisposable RegisterComponent(IComponentClass component) => host.RegisterComponent(component);
        public IDisposable RegisterToolboxItem(IComponentClass component, string? iconKey = null) => host.RegisterToolboxItem(component, iconKey);
        public IDisposable RegisterControlLibrary(IControlLibrary library, Func<IReadOnlyList<IComponentClass>> controls) =>
            host.RegisterControlLibrary(library, controls);
        public IDisposable RegisterProjectTemplate(IAddinProjectTemplate template) => host.RegisterProjectTemplate(template);
        public bool RunProject() => host.RunProject();
        public bool StopProject() => host.StopProject();
        public IAddinServerConnection RegisterLanguageServer(AddinLanguageServer server) => host.RegisterLanguageServer(server);
        public IDisposable RegisterDebugLaunch(AddinDebugLaunch launch) => host.RegisterDebugLaunch(launch);
        public IDisposable RegisterProjectType(AddinProjectType type) => host.RegisterProjectType(type);
        public IDisposable RegisterResources(Uri dictionary) => host.RegisterResources(dictionary);
        public IDisposable RegisterLanguagePacks(Uri folder) => host.RegisterLanguagePacks(folder);
        public IDisposable RegisterDesignerPictures(Func<IDesignerDrawingSurface, IDesignerPictures> pictures) =>
            host.RegisterDesignerPictures(pictures);
        public IDisposable RegisterDesignPositions(IDesignPositions positions) => host.RegisterDesignPositions(positions);
    }

    // As VB6 lists them in the Watch dialog when no code window has a server's outline: with nothing registered for vb6, none.
    [Fact]
    public async Task The_Watch_dialog_lists_no_procedures_for_a_module_when_nothing_is_registered_for_vb6()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode("Sub Bump()\nEnd Sub\n");
        project.AddModule(module);
        var projects = Substitute.For<IProjectManager>();
        projects.StartupProject.Returns(project);
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns(Array.Empty<BaseEditorWindowViewModel>());

        var none = await new HexIDE.Tools.WatchDialogContextSource(dock, projects, new AddinLanguageEditingService())
            .ReadAsync("(All Procedures)");
        var registered = await new HexIDE.Tools.WatchDialogContextSource(dock, projects, InBoxRegistry.LanguageEditing())
            .ReadAsync("(All Procedures)");

        none.Modules.Single().Procedures.Should().BeEmpty();
        registered.Modules.Single().Procedures.Should().Equal("Bump");
    }
}
