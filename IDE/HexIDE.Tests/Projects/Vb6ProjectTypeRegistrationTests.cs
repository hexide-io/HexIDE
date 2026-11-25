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
public sealed class Vb6ProjectTypeRegistrationTests : IAsyncDisposable
{
    private readonly string vbp = Path.Join(Path.GetTempPath(), "Project1.vbp");
    private DISetup? setup;

    public async ValueTask DisposeAsync()
    {
        if (setup is not null) await setup.LspClient.DisposeAsync();
    }

    private AddinOwnedHost Provider(DISetup composition) => new(composition.HexIdeHost, null!, BasicProviderComposition.Origin);

    [AvaloniaFact]
    public void The_composition_registers_no_VB6_type_of_its_own()
    {
        setup = new DISetup();

        setup.ContributedProjects.Types.Should().BeEmpty("the Basic provider registers VB6's type");
    }

    [AvaloniaFact]
    public void The_provider_registers_VB6_s_type_under_VB6_s_identity_and_stays_its_owner()
    {
        setup = new DISetup();

        var registration = Provider(setup).RegisterProjectType(Vb6ProjectFileFormat.ProjectType());

        var type = setup.ContributedProjects.Find(vbp)!;
        type.AddinId.Should().Be(Vb6ProjectType.Id);
        type.Origin.Should().Be(Vb6ProjectType.Title);
        type.OwnerId.Should().Be(RegistrationGrants.BasicProvider);
        type.Offers.Should().Be(ProjectOffers.Everything, "VB6's projects run, make and debug as they did");
        registration.Dispose();
        setup.ContributedProjects.Find(vbp).Should().BeNull("taking the registration out takes the type out");
    }

    [AvaloniaFact]
    public void Without_VB6_the_provider_s_registration_registers_nothing_and_stops_nothing_else()
    {
        var saved = Static.WithoutVb6;
        Static.WithoutVb6 = true;
        try
        {
            setup = new DISetup();
            var provider = Provider(setup);

            using var registration = provider.RegisterProjectType(Vb6ProjectFileFormat.ProjectType());
            using var other = provider.RegisterProjectType(new AddinProjectType("Other")
            {
                FileNames = ["*.mypro"],
                OpenAsync = (_, _, _) => throw new NotImplementedException(),
            });

            registration.Should().NotBeNull();
            setup.ContributedProjects.Find(vbp).Should().BeNull();
            setup.ContributedProjects.Types.Should().ContainSingle().Which.Type.Title.Should().Be("Other");
        }
        finally { Static.WithoutVb6 = saved; }
    }

    [AvaloniaFact]
    public void The_IDE_s_own_host_can_t_register_VB6_s_type()
    {
        var composition = setup = new DISetup();

        var register = () => composition.HexIdeHost.RegisterProjectType(Vb6ProjectFileFormat.ProjectType());

        register.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("Visual Basic 6.0");
        composition.ContributedProjects.Types.Should().BeEmpty();
    }
}
