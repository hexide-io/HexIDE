using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Runtime;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.Addins;

/// <summary>
/// Who may register a control library, what it may hold, and when it's asked what it offers (#5599). Each test reads what
/// the registry then holds, not only what the call returned.
/// </summary>
public class RegisterControlLibraryTests
{
    // A signed third-party package: neither first-party, nor developer-unsigned, nor composed.
    private static readonly AddinOrigin Signed = new("signed.addin", "Signed Add-in");

    private sealed class Library(string name, string guid = "{00000000-0000-0000-0000-000000000001}") : IControlLibrary
    {
        public string Description => name + " controls";
        public string ProjectLine => $"Object={guid}#1.0#0; {name}.OCX";
        public string FormLine => $"Object = \"{guid}#1.0#0\"; \"{name}.OCX\"";
        public string Name => name;
        public string Guid => guid;
        public string File => name + ".OCX";
        public string TypeName(string className) => $"{name}.{className}";
        public bool IsNamedBy(ObjectReference reference) => false;
        public bool IsNamedBy(VbReference reference) => false;
    }

    private sealed class Probe(string typeName) : IComponentClass
    {
        public string Name => typeName;
        public string VBTypeName => typeName;
        public string ClassName => typeName[(typeName.LastIndexOf('.') + 1)..];
        public bool IsVisual => true;
        public IReadOnlyList<PropertyClass> Properties => [];
        public IReadOnlyDictionary<string, PropertyClass> PropertiesByName => new Dictionary<string, PropertyClass>();
        public IReadOnlyList<EventClass> Events => [];
        public IReadOnlyList<PropertyChange> ApplyDesignerConsequences(ComponentInstance instance, PropertyClass property) => [];
    }

    private static HexIdeHost Ide(ComponentRegistry registry) =>
        new(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, registry, null!, null!, null!, null!,
            new AddinResourceService(), new AddinLanguagePackService(), new AddinLanguageEditingService());

    private static IHexIdeHost Host(ComponentRegistry registry, AddinOrigin origin) => new AddinOwnedHost(Ide(registry), null!, origin);

    // Two owners whose grants both keep Acme, as no real grants do, to reach the held-library check.
    private static ComponentRegistry Shared() => new()
    {
        Grants =
        [
            new("a", Composed: true) { TypeNamespaces = ["Acme", "Acme2"] },
            new("b", Composed: true) { TypeNamespaces = ["Acme", "Acme2"] },
        ],
    };

    private static AddinOrigin Composed(string id) => new(id, "Owner " + id) { IsComposed = true };

    [Fact]
    public void A_signed_third_party_add_in_is_refused_a_control_library()
    {
        var registry = new ComponentRegistry();

        FluentActions.Invoking(() => Host(registry, Signed).RegisterControlLibrary(new Library("Acme"), () => []))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Signed Add-in isn't one HexIDE lets register the Acme control library.");

        registry.Libraries.Should().BeEmpty();
    }

    [Fact]
    public void The_provider_is_refused_a_library_under_a_namespace_its_grant_doesn_t_keep()
    {
        var registry = new ComponentRegistry();

        FluentActions.Invoking(() => Host(registry, InBoxRegistry.Provider).RegisterControlLibrary(new Library("Acme"), () => []))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("HexIDE.Basic isn't one HexIDE lets register the Acme control library.");

        registry.Libraries.Should().BeEmpty();
    }

    [Fact]
    public void The_provider_registers_its_libraries_in_order_and_nothing_asks_what_they_offer_until_something_needs_to()
    {
        var registry = new ComponentRegistry();
        var host = Host(registry, InBoxRegistry.Provider);
        var asked = 0;

        foreach (var library in BasicProvider.Libraries)
            host.RegisterControlLibrary(library, () => { asked++; return BasicProvider.ControlsOf(library); });

        registry.Libraries.Should().Equal(BasicProvider.Libraries);
        asked.Should().Be(0);
        registry.LibraryControls.Should().NotBeEmpty();
        asked.Should().Be(BasicProvider.Libraries.Count);
        registry.LibraryControls.Should().NotBeEmpty();
        asked.Should().Be(BasicProvider.Libraries.Count, "what they offer is kept until a library comes or goes");
    }

    [Fact]
    public void A_held_library_is_refused_by_its_name_or_GUID_naming_both_owners_and_the_first_stands()
    {
        var registry = Shared();
        var first = new Library("Acme", "{11111111-1111-1111-1111-111111111111}");
        using var held = Host(registry, Composed("a")).RegisterControlLibrary(first, () => []);

        FluentActions.Invoking(() => Host(registry, Composed("b")).RegisterControlLibrary(
                new Library("acme", "{22222222-2222-2222-2222-222222222222}"), () => []))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Owner b can't register the acme control library: Owner a (a) holds Acme, by its name or GUID.");
        FluentActions.Invoking(() => Host(registry, Composed("b")).RegisterControlLibrary(
                new Library("Acme2", "{11111111-1111-1111-1111-111111111111}"), () => []))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Owner b can't register the Acme2 control library: Owner a (a) holds Acme, by its name or GUID.");

        registry.Libraries.Should().ContainSingle().Which.Should().BeSameAs(first);
    }

    // The supervisor's order: a namespace a grant keeps is its owner's before its libraries load, and after.
    [Fact]
    public void A_third_party_is_refused_a_type_name_in_the_provider_s_library_namespace_before_and_after_it_registers()
    {
        var registry = new ComponentRegistry();

        FluentActions.Invoking(() => Host(registry, Signed).RegisterComponent(new Probe("MSComctlLib.Gauge")))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Signed Add-in can't register MSComctlLib.Gauge: the MSComctlLib namespace is kept for hexide.basic.");

        foreach (var library in BasicProvider.Libraries)
            Host(registry, InBoxRegistry.Provider).RegisterControlLibrary(library, () => BasicProvider.ControlsOf(library));

        FluentActions.Invoking(() => Host(registry, Signed).RegisterComponent(new Probe("mscomctllib.gauge")))
            .Should().Throw<InvalidOperationException>().WithMessage("*the mscomctllib namespace is kept for hexide.basic.");
        registry.Components.Should().BeEmpty();
    }

    [Fact]
    public void A_library_that_can_t_say_what_it_offers_offers_nothing_and_the_others_still_do()
    {
        var registry = Shared();
        var gauge = new Probe("Acme.Gauge");
        using var broken = Host(registry, Composed("a")).RegisterControlLibrary(new Library("Acme"),
            () => throw new InvalidOperationException("broken"));
        using var working = Host(registry, Composed("b")).RegisterControlLibrary(
            new Library("Acme2", "{33333333-3333-3333-3333-333333333333}"), () => [gauge]);

        registry.LibraryControls.Should().ContainSingle().Which.Should().Be(
            new LibraryControl(gauge, registry.Libraries[1], "Gauge", "Acme2.Gauge", "Geo.Object"));
    }

    [Fact]
    public void Withdrawing_a_library_takes_it_and_its_controls_out_once_and_says_so()
    {
        var registry = Shared();
        var changed = 0;
        registry.LibrariesChanged += () => changed++;
        registry.LibraryControls.Should().BeEmpty();
        var registration = Host(registry, Composed("a")).RegisterControlLibrary(new Library("Acme"), () => [new Probe("Acme.Gauge")]);
        registry.LibraryControls.Should().ContainSingle("what the libraries offer is asked again once one comes");

        registration.Dispose();
        registration.Dispose();

        registry.Libraries.Should().BeEmpty();
        registry.LibraryControls.Should().BeEmpty();
        changed.Should().Be(2);
    }

    [Fact]
    public void The_IDEs_own_host_refuses_a_control_library_with_no_owner_to_check()
    {
        var registry = new ComponentRegistry();

        FluentActions.Invoking(() => ((IHexIdeHost)Ide(registry)).RegisterControlLibrary(new Library("Acme"), () => []))
            .Should().Throw<InvalidOperationException>().WithMessage("*only through the host an add-in is given.");

        registry.Libraries.Should().BeEmpty();
    }

    [Fact]
    public void The_provider_registers_its_libraries_after_its_toolbox_items()
    {
        var host = Substitute.For<IHexIdeHost>();
        using var provider = new BasicProvider();

        provider.Initialize(host);

        var calls = host.ReceivedCalls().Select(call => call.GetMethodInfo().Name)
            .Where(name => name is nameof(IHexIdeHost.RegisterToolboxItem) or nameof(IHexIdeHost.RegisterControlLibrary)).ToList();
        calls.Should().Equal([
            .. BasicProvider.Toolbox.Select(_ => nameof(IHexIdeHost.RegisterToolboxItem)),
            .. BasicProvider.Libraries.Select(_ => nameof(IHexIdeHost.RegisterControlLibrary)),
        ]);
        host.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(IHexIdeHost.RegisterControlLibrary))
            .Select(call => call.GetArguments()[0]).Should().Equal(BasicProvider.Libraries);
    }

    // The grant names exactly VB and the provider's libraries, so the list in HexIDE's source can't drift from them.
    [Fact]
    public void The_provider_s_grant_keeps_VB_and_its_libraries_names()
    {
        RegistrationGrants.All.Single(grant => grant.OwnerId == RegistrationGrants.BasicProvider).TypeNamespaces
            .Should().Equal(["VB", .. BasicProvider.Libraries.Select(library => library.Name)]);
    }
}
