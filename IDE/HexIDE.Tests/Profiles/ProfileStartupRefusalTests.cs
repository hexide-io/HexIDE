using HexIDE.Profiles;
using HexIDE.Tests.Infrastructure;
using DesktopProgram = HexIDE.Desktop.Program;

namespace HexIDE.Tests.Profiles;

/// <summary>
/// A start whose profile can't be loaded is refused through the reporter a broken brand.json uses, which shows the
/// "Cannot start" box when there's no console. It used to write only to the console, so a branded product whose default
/// profile was missing, started by double-click, exited with code 2 and showed nothing (#4169).
/// </summary>
public sealed class ProfileStartupRefusalTests : IDisposable
{
    private readonly string root = ResolvedTemp.CreateDirectory("hexide-profile-refusal-");

    public void Dispose() => Directory.Delete(root, true);

    private ProfileResolver Resolver => new(Path.Combine(root, "user"), root);

    [Fact]
    public void A_profile_that_cannot_be_loaded_reaches_the_start_refusal_reporter_with_the_profile_and_its_file()
    {
        var missing = Path.Combine(root, "branding", "nothere");
        var reported = new List<string>();

        var loaded = DesktopProgram.TryLoadProfile(() => Resolver.Resolve(missing), reported.Add, out var profile);

        loaded.Should().BeFalse();
        profile.Should().BeNull();
        reported.Should().ContainSingle()
            .Which.Should().Contain(missing).And.Contain("profile.json");
    }

    [Fact]
    public void A_profile_that_loads_reports_nothing()
    {
        var reported = new List<string>();

        var loaded = DesktopProgram.TryLoadProfile(() => Resolver.Resolve("vb6"), reported.Add, out var profile);

        loaded.Should().BeTrue();
        profile!.Id.Should().Be("vb6");
        reported.Should().BeEmpty();
    }

    [Fact]
    public void No_profile_selected_loads_as_none_and_reports_nothing()
    {
        var reported = new List<string>();

        var loaded = DesktopProgram.TryLoadProfile(() => null, reported.Add, out var profile);

        loaded.Should().BeTrue();
        profile.Should().BeNull();
        reported.Should().BeEmpty();
    }
}
