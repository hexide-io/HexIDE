using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Runtime;
using HexIDE.Runtime.Components;

namespace HexIDE.Tests.Addins;

/// <summary>
/// Who may register which component classes and toolbox items, and what a registration may take (#5594). Each test reads
/// what the registry then holds, not only what the call returned.
/// </summary>
public class RegisterComponentTests
{
    // A signed third-party package: neither first-party, nor developer-unsigned, nor composed.
    private static readonly AddinOrigin Signed = new("signed.addin", "Signed Add-in");
    private static readonly AddinOrigin Other = new("other.addin", "Other Add-in");

    private sealed class Probe(string typeName, ComponentRole role = ComponentRole.Control, bool container = false) : IComponentClass
    {
        public string Name => typeName;
        public string VBTypeName => typeName;
        public string ClassName => typeName[(typeName.LastIndexOf('.') + 1)..];
        public ComponentRole Role => role;
        public bool IsContainer => container;
        public bool IsVisual => true;
        public IReadOnlyList<PropertyClass> Properties => [];
        public IReadOnlyDictionary<string, PropertyClass> PropertiesByName => new Dictionary<string, PropertyClass>();
        public IReadOnlyList<EventClass> Events => [];
        public IReadOnlyList<PropertyChange> ApplyDesignerConsequences(ComponentInstance instance, PropertyClass property) => [];
    }

    private static HexIdeHost Ide(ComponentRegistry registry) =>
        new(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, registry, null!, null!, null!, null!,
            new AddinResourceService(), new AddinLanguagePackService());

    // What an add-in is given once its consent is settled, as the provider is at startup.
    private static IHexIdeHost Host(ComponentRegistry registry, AddinOrigin origin) => new AddinOwnedHost(Ide(registry), null!, origin);

    [Theory]
    [InlineData(ComponentRole.Form, false, "a form component")]
    [InlineData(ComponentRole.Menu, false, "a menu component")]
    [InlineData(ComponentRole.Line, false, "a line component")]
    [InlineData(ComponentRole.Control, true, "a container")]
    public void A_signed_third_party_add_in_is_refused_a_form_menu_line_or_container(ComponentRole role, bool container, string what)
    {
        var registry = new ComponentRegistry();

        FluentActions.Invoking(() => Host(registry, Signed).RegisterComponent(new Probe("Signed.Thing", role, container)))
            .Should().Throw<InvalidOperationException>()
            .WithMessage($"Signed Add-in isn't one HexIDE lets register Signed.Thing, which is {what}.");

        registry.Components.Should().BeEmpty();
    }

    [Fact]
    public void A_package_that_names_itself_after_the_provider_is_refused_a_form()
    {
        var registry = new ComponentRegistry();
        var impostor = new AddinOrigin(RegistrationGrants.BasicProvider, "Impostor", IsFirstParty: true);

        FluentActions.Invoking(() => Host(registry, impostor).RegisterComponent(new Probe("Impostor.Form", ComponentRole.Form)))
            .Should().Throw<InvalidOperationException>().WithMessage("Impostor isn't one HexIDE lets register*");

        registry.Components.Should().BeEmpty();
    }

    [Fact]
    public void A_plain_control_registers_from_any_add_in()
    {
        var registry = new ComponentRegistry();
        var gauge = new Probe("Signed.Gauge");

        using var registration = Host(registry, Signed).RegisterComponent(gauge);

        registry.Components.Should().Equal(gauge);
    }

    [Fact]
    public void The_provider_registers_its_form_and_containers()
    {
        var registry = new ComponentRegistry();
        var host = Host(registry, InBoxRegistry.Provider);

        foreach (var component in BasicProvider.Components)
            host.RegisterComponent(component);

        registry.Components.Should().Equal(BasicProvider.Components);
        registry.Components.Should().Contain([FormComponentClass.Instance, FrameComponentClass.Instance, PictureBoxComponentClass.Instance]);
    }

    [Fact]
    public void A_held_type_name_is_refused_naming_both_owners_and_the_first_registration_stands()
    {
        var registry = new ComponentRegistry();
        var first = new Probe("Stand.Gauge");
        using var held = Host(registry, Signed).RegisterComponent(first);

        FluentActions.Invoking(() => Host(registry, Other).RegisterComponent(new Probe("stand.gauge")))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Other Add-in can't register stand.gauge: Signed Add-in (signed.addin) holds that type name already.");
        FluentActions.Invoking(() => Host(registry, Signed).RegisterComponent(first))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Signed Add-in can't register Stand.Gauge: Signed Add-in (signed.addin) holds that type name already.");

        registry.Components.Should().ContainSingle().Which.Should().BeSameAs(first);
    }

    [Fact]
    public void An_add_in_is_refused_a_type_name_the_provider_holds()
    {
        var registry = InBoxRegistry.Create();

        FluentActions.Invoking(() => Host(registry, Signed).RegisterComponent(new Probe("VB.CommandButton")))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Signed Add-in can't register VB.CommandButton: HexIDE.Basic (hexide.basic) holds that type name already.");

        registry.Components.Should().Equal(BasicProvider.Components);
    }

    [Fact]
    public void A_type_name_in_a_control_library_s_namespace_is_refused()
    {
        var registry = new ComponentRegistry();

        FluentActions.Invoking(() => Host(registry, Signed).RegisterComponent(new Probe("MSComctlLib.Gauge")))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Signed Add-in can't register MSComctlLib.Gauge: that type name is the MSComctlLib control library's.");

        registry.Components.Should().BeEmpty();
    }

    [Fact]
    public void A_toolbox_item_is_refused_for_a_class_another_owner_registered_or_none_did()
    {
        var registry = new ComponentRegistry();
        var theirs = new Probe("Signed.Gauge");
        using var registered = Host(registry, Signed).RegisterComponent(theirs);

        FluentActions.Invoking(() => Host(registry, Other).RegisterToolboxItem(theirs, "Geo.Timer"))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Other Add-in can't put Signed.Gauge in the toolbox: Signed Add-in (signed.addin) registered it.");
        FluentActions.Invoking(() => Host(registry, Other).RegisterToolboxItem(new Probe("Other.Dial")))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Other Add-in can't put Other.Dial in the toolbox: it isn't registered.");

        registry.Toolbox.Should().BeEmpty();
    }

    [Fact]
    public void An_add_in_is_refused_a_toolbox_item_for_the_provider_s_class()
    {
        var registry = new ComponentRegistry();
        using var provider = Host(registry, InBoxRegistry.Provider).RegisterComponent(CommandButtonComponentClass.Instance);

        FluentActions.Invoking(() => Host(registry, Signed).RegisterToolboxItem(CommandButtonComponentClass.Instance))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Signed Add-in can't put VB.CommandButton in the toolbox: HexIDE.Basic (hexide.basic) registered it.");

        registry.Toolbox.Should().BeEmpty();
    }

    [Fact]
    public void A_package_that_names_itself_after_the_provider_is_refused_a_toolbox_item_for_the_provider_s_class()
    {
        var registry = new ComponentRegistry();
        using var provider = Host(registry, InBoxRegistry.Provider).RegisterComponent(CommandButtonComponentClass.Instance);
        var impostor = new AddinOrigin(RegistrationGrants.BasicProvider, "Impostor", IsFirstParty: true);

        FluentActions.Invoking(() => Host(registry, impostor).RegisterToolboxItem(CommandButtonComponentClass.Instance))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Impostor can't put VB.CommandButton in the toolbox: HexIDE.Basic (hexide.basic) registered it.");

        registry.Toolbox.Should().BeEmpty();
    }

    [Fact]
    public void An_owner_s_toolbox_items_come_in_its_order_once_each_and_go_with_their_class()
    {
        var registry = new ComponentRegistry();
        var added = new List<ToolboxEntry>();
        var removed = new List<ToolboxEntry>();
        var unregistered = 0;
        registry.ToolboxItemAdded += added.Add;
        registry.ToolboxItemRemoved += removed.Add;
        registry.ComponentUnregistered += _ => unregistered++;
        var host = Host(registry, Signed);
        var gauge = new Probe("Signed.Gauge");
        var dial = new Probe("Signed.Dial");
        var gaugeRegistration = host.RegisterComponent(gauge);
        using var dialRegistration = host.RegisterComponent(dial);

        host.RegisterToolboxItem(gauge, "Geo.Timer");
        host.RegisterToolboxItem(dial);

        registry.Toolbox.Should().Equal(new ToolboxEntry(gauge, "Geo.Timer"), new ToolboxEntry(dial, "Geo.Object"));
        added.Should().Equal(registry.Toolbox);
        FluentActions.Invoking(() => host.RegisterToolboxItem(gauge))
            .Should().Throw<InvalidOperationException>().WithMessage("Signed Add-in can't put Signed.Gauge in the toolbox: it's there already.");

        gaugeRegistration.Dispose();
        gaugeRegistration.Dispose();

        registry.Toolbox.Should().Equal(new ToolboxEntry(dial, "Geo.Object"));
        removed.Should().Equal(new ToolboxEntry(gauge, "Geo.Timer"));
        unregistered.Should().Be(1);
        registry.Components.Should().Equal(dial);
    }

    [Fact]
    public void A_registered_class_is_not_in_the_toolbox_until_its_owner_puts_it_there()
    {
        var registry = new ComponentRegistry();

        using var registration = Host(registry, Signed).RegisterComponent(new Probe("Signed.Gauge"));

        registry.Toolbox.Should().BeEmpty();
    }

    [Fact]
    public void The_IDEs_own_host_refuses_a_component_and_a_toolbox_item_with_no_owner_to_check()
    {
        var registry = new ComponentRegistry();
        IHexIdeHost ide = Ide(registry);

        FluentActions.Invoking(() => ide.RegisterComponent(new Probe("Signed.Gauge")))
            .Should().Throw<InvalidOperationException>().WithMessage("*only through the host an add-in is given.");
        FluentActions.Invoking(() => ide.RegisterToolboxItem(new Probe("Signed.Gauge")))
            .Should().Throw<InvalidOperationException>().WithMessage("*only through the host an add-in is given.");

        registry.Components.Should().BeEmpty();
    }

    [Fact]
    public void The_provider_registers_its_classes_then_their_toolbox_items_in_order()
    {
        var host = Substitute.For<IHexIdeHost>();
        using var provider = new BasicProvider();

        provider.Initialize(host);

        var calls = host.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name is nameof(IHexIdeHost.RegisterComponent) or nameof(IHexIdeHost.RegisterToolboxItem))
            .Select(call => (call.GetMethodInfo().Name, call.GetArguments()[0], call.GetArguments().ElementAtOrDefault(1)))
            .ToList();
        calls.Should().Equal([
            .. BasicProvider.Components.Select(c => (nameof(IHexIdeHost.RegisterComponent), (object?)c, (object?)null)),
            .. BasicProvider.Toolbox.Select(t => (nameof(IHexIdeHost.RegisterToolboxItem), (object?)t.Class, (object?)t.IconKey)),
        ]);
        BasicProvider.Toolbox.Select(t => t.Class).Should().Equal(BasicProvider.Components.Where(c => c.Role != ComponentRole.Form));
    }
}
