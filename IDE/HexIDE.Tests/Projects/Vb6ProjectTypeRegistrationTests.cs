using HexIDE.Addins;
using HexIDE.Desktop;
using HexIDE.Projects;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.Projects;

/// <summary>
/// VB6's project type comes from the Basic provider, through its owned host, as any add-in's does (#5625). The composition
/// registers none of its own, <c>--no-vb6</c> turns the provider's registration into nothing, and only the provider's grant
/// gives the type VB6's identity.
/// </summary>
public sealed class Vb6ProjectTypeRegistrationTests : CompositionTestBase
{
    private readonly string vbp = Path.Join(Path.GetTempPath(), "Project1.vbp");

    // These tests make the provider's registration themselves, or not at all.
    protected override bool RegistersVb6 => false;

    private AddinOwnedHost Provider() => new(Composition.HexIdeHost, null!, BasicProviderComposition.Origin);

    [AvaloniaFact]
    public void The_composition_registers_no_VB6_type_of_its_own()
    {
        Composition.ContributedProjects.Types.Should().BeEmpty("the Basic provider registers VB6's type");
    }

    [AvaloniaFact]
    public void The_provider_registers_VB6_s_type_under_VB6_s_identity_and_stays_its_owner()
    {
        var registration = Provider().RegisterProjectType(Vb6ProjectFileFormat.ProjectType());

        var type = Composition.ContributedProjects.Find(vbp)!;
        type.AddinId.Should().Be(Vb6ProjectType.Id);
        type.Origin.Should().Be(Vb6ProjectType.Title);
        type.OwnerId.Should().Be(RegistrationGrants.BasicProvider);
        type.Offers.Should().Be(ProjectOffers.Everything, "VB6's projects run, make and debug as they did");
        registration.Dispose();
        Composition.ContributedProjects.Find(vbp).Should().BeNull("taking the registration out takes the type out");
    }

    [AvaloniaFact]
    public void Without_VB6_the_provider_s_registration_registers_nothing_and_stops_nothing_else()
    {
        var saved = Static.WithoutVb6;
        Static.WithoutVb6 = true;
        try
        {
            var provider = Provider();

            using var registration = provider.RegisterProjectType(Vb6ProjectFileFormat.ProjectType());
            using var other = provider.RegisterProjectType(new AddinProjectType("Other")
            {
                FileNames = ["*.mypro"],
                OpenAsync = (_, _, _) => throw new NotImplementedException(),
            });

            registration.Should().NotBeNull();
            Composition.ContributedProjects.Find(vbp).Should().BeNull();
            Composition.ContributedProjects.Types.Should().ContainSingle().Which.Type.Title.Should().Be("Other");
        }
        finally { Static.WithoutVb6 = saved; }
    }

    [AvaloniaFact]
    public void The_IDE_s_own_host_can_t_register_VB6_s_type()
    {
        var register = () => Composition.HexIdeHost.RegisterProjectType(Vb6ProjectFileFormat.ProjectType());

        register.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("Visual Basic 6.0");
        Composition.ContributedProjects.Types.Should().BeEmpty();
    }
}
