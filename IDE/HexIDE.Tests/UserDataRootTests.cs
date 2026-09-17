using HexIDE.IDE;

namespace HexIDE.Tests;

/// <summary>
/// <c>--user-data-dir</c>'s one rule: a session's per-user files live in exactly one directory.
/// </summary>
/// <remarks>
/// Against a private <see cref="UserDataRoot"/>, never the process's own <see cref="UserDataPath"/>: every
/// test in this assembly shares that one, and redirecting it would move every other test's files.
/// </remarks>
public class UserDataRootTests
{
    private static readonly string PlatformDefault = Path.Combine(Path.GetTempPath(), "platform-default", "HexIDE");
    private static readonly string Profile = Path.Combine(Path.GetTempPath(), "a-demo-profile");

    private static UserDataRoot NewRoot() => new(() => PlatformDefault);

    [Fact]
    public void WithoutARedirect_ItIsThePlatformDefault()
    {
        var root = NewRoot();

        root.Directory.Should().Be(PlatformDefault);
        root.IsRedirected.Should().BeFalse();
    }

    [Fact]
    public void ARedirectMadeBeforeAnyRead_IsWhatEveryReadReturns()
    {
        var root = NewRoot();

        root.RedirectTo(Profile);

        root.Directory.Should().Be(Profile);
        root.Directory.Should().Be(Profile, "the answer does not drift between reads");
        root.IsRedirected.Should().BeTrue();
    }

    [Fact]
    public void ARedirectAfterTheDirectoryHasBeenRead_IsRefused()
    {
        // THE ONE THAT MATTERS. Allowed, this splits one session between two directories: settings read from
        // the default, then written to the profile — each half silently wrong.
        var root = NewRoot();
        _ = root.Directory;

        var act = () => root.RedirectTo(Profile);

        act.Should().Throw<InvalidOperationException>().WithMessage("*already read*");
        root.Directory.Should().Be(PlatformDefault, "a refused redirect changes nothing");
    }

    [Fact]
    public void ASecondRedirect_IsRefused()
    {
        var root = NewRoot();
        root.RedirectTo(Profile);

        var act = () => root.RedirectTo(Path.Combine(Path.GetTempPath(), "another-profile"));

        act.Should().Throw<InvalidOperationException>();
        root.Directory.Should().Be(Profile);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("profile")]
    [InlineData("demo/profile")]
    public void ARelativeOrEmptyPath_IsRefused(string directory)
    {
        // Relative to what is a question only the command-line parser can answer, because startup has moved
        // the working directory to the executable's folder by the time this runs.
        var act = () => NewRoot().RedirectTo(directory);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ATrailingSeparator_IsNotPartOfTheDirectory()
    {
        // So Path.Combine(Directory, file) and a comparison against Directory agree whichever way it was typed.
        var root = NewRoot();

        root.RedirectTo(Profile + Path.DirectorySeparatorChar);

        root.Directory.Should().Be(Profile);
    }
}
