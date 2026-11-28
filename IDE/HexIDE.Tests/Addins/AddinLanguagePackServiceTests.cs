using HexIDE.Localization;

namespace HexIDE.Tests.Addins;

/// <summary>
/// <see cref="IHexIdeHost.RegisterLanguagePacks"/> (#5394): who may register, from where, and what a grant admits. Each test
/// gives its own owner a grant and reads a registry of its own, so HexIDE's real grants and areas are untouched.
/// </summary>
public sealed class AddinLanguagePackServiceTests
{
    private const string OwnerId = "fixture.owner";
    private const string Hello = "Str.Fixture.Area.Hello";
    private static readonly Uri Good = new("avares://HexIDE.Tests/Localization/Areas/Good/");

    private readonly LanguagePackAreas areas = new();
    private readonly AddinOrigin composed = new(OwnerId, "Fixture Owner") { IsComposed = true };

    private AddinLanguagePackService Service(params string[] families) => new()
    {
        Areas = areas,
        Grants = [new RegistrationGrant(OwnerId, Composed: true) { LanguagePackKeys = families }],
    };

    private static IHexIdeHost Owned(AddinLanguagePackService service, AddinOrigin origin, out IHexIdeHost ide)
    {
        ide = new HexIdeHost(null!, null!, null!, Substitute.For<IAddinRegistry>(), null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, new AddinResourceService(), service, new AddinLanguageEditingService());
        return new AddinOwnedHost(ide, null!, origin);
    }

    [Fact]
    public void An_owner_its_grant_covers_registers_its_packs_from_its_own_assembly()
    {
        var host = Owned(Service("Str.Fixture.Area.*"), composed, out _);

        using var registration = host.RegisterLanguagePacks(Good);

        areas.Areas.Should().ContainSingle().Which.OwnerId.Should().Be(OwnerId);
        new LocalizationService { Areas = areas }.GetString(Hello).Should().Be("Hello {0}");
    }

    [Fact]
    public void Disposing_the_handle_takes_the_packs_out()
    {
        var host = Owned(Service("Str.Fixture.Area.*"), composed, out _);

        host.RegisterLanguagePacks(Good).Dispose();

        areas.Areas.Should().BeEmpty();
    }

    [Fact]
    public void An_owner_with_no_grant_is_refused()
    {
        var host = Owned(Service("Str.Fixture.Area.*"), new AddinOrigin("someone.else", "Someone") { IsComposed = true }, out _);

        var register = () => host.RegisterLanguagePacks(Good);

        register.Should().Throw<InvalidOperationException>().WithMessage("Someone isn't one HexIDE lets register language packs.");
        areas.Areas.Should().BeEmpty();
    }

    [Fact]
    public void A_package_naming_itself_after_a_composed_owner_is_refused()
    {
        var host = Owned(Service("Str.Fixture.Area.*"), new AddinOrigin(OwnerId, "Fixture Owner", IsFirstParty: true), out _);

        var register = () => host.RegisterLanguagePacks(Good);

        register.Should().Throw<InvalidOperationException>();
        areas.Areas.Should().BeEmpty();
    }

    [Fact]
    public void A_grant_of_resources_alone_admits_no_language_packs()
    {
        var host = Owned(Service(), composed, out _);

        var register = () => host.RegisterLanguagePacks(Good);

        register.Should().Throw<InvalidOperationException>().WithMessage("*isn't one HexIDE lets register language packs*");
    }

    [Fact]
    public void A_key_outside_the_grants_families_refuses_the_packs()
    {
        var host = Owned(Service(Hello), composed, out _);

        var register = () => host.RegisterLanguagePacks(Good);

        register.Should().Throw<InvalidOperationException>().WithMessage("*Str.Fixture.Area.Bye*outside what its grant names*");
        areas.Areas.Should().BeEmpty();
    }

    [Fact]
    public void Packs_in_another_assembly_are_refused()
    {
        var host = Owned(Service("Str.Fixture.Area.*"), composed, out _);

        var register = () => host.RegisterLanguagePacks(new Uri("avares://HexIDE/Localization/Packs/"));

        register.Should().Throw<ArgumentException>().WithMessage("*only from its own assembly, as avares://HexIDE.Tests/*");
        areas.Areas.Should().BeEmpty();
    }

    [Fact]
    public void A_folder_that_is_not_an_avares_address_is_refused()
    {
        var host = Owned(Service("Str.Fixture.Area.*"), composed, out _);

        var register = () => host.RegisterLanguagePacks(new Uri("https://example.com/packs/"));

        register.Should().Throw<ArgumentException>();
        areas.Areas.Should().BeEmpty();
    }

    [Fact]
    public void The_IDEs_own_host_registers_no_language_packs()
    {
        Owned(Service("Str.Fixture.Area.*"), composed, out var ide);

        var register = () => ide.RegisterLanguagePacks(Good);

        register.Should().Throw<InvalidOperationException>().WithMessage("*only through the host an add-in is given*");
        areas.Areas.Should().BeEmpty();
    }

    [Fact]
    public void HexIDEs_own_grants_admit_only_the_composed_provider()
    {
        RegistrationGrants.All.Where(grant => grant.LanguagePackKeys.Count > 0).Select(grant => (grant.OwnerId, grant.Composed))
            .Should().Equal((RegistrationGrants.BasicProvider, true));
    }
}
