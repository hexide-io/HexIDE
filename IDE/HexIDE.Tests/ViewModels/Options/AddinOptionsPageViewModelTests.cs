using System;
using HexIDE.Forms.ViewModels.Options;

namespace HexIDE.Tests.ViewModels.Options;

public class AddinOptionsPageViewModelTests
{
    private readonly IAddinRegistry _registry = Substitute.For<IAddinRegistry>();
    private static readonly AddinInfo Info = new("/addins/X.dll", "AI Chat", "Chatbot", "1.0.0", "HexIDE Team");

    [Fact]
    public void Header_PopulatedFromAddinInfo()
    {
        _registry.GetStatus(Info.AssemblyPath).Returns(AddinStatus.Loaded);

        var sut = new AddinOptionsPageViewModel(_registry, Info);

        sut.Title.Should().Be("AI Chat");
        sut.Description.Should().Be("Chatbot");
        sut.Version.Should().Be("1.0.0");
        sut.Author.Should().Be("HexIDE Team");
        sut.StatusText.Should().Be("Loaded");
        sut.IsAddinEnabled.Should().BeTrue();
        sut.ToggleLabel.Should().Be("Deactivate");
        sut.RestartNoteVisible.Should().BeFalse();
    }

    [Fact]
    public void EmptyMetadata_FallsBackToPlaceholders()
    {
        _registry.GetStatus(Arg.Any<string>()).Returns(AddinStatus.Loaded);

        var sut = new AddinOptionsPageViewModel(_registry, new AddinInfo("/x.dll", "", "", "", ""));

        sut.Title.Should().Be("(unnamed add-in)");
        sut.Description.Should().Be("(no description provided)");
        sut.Version.Should().Be("—");
        sut.Author.Should().Be("—");
    }

    [Fact]
    public void Toggle_FromEnabled_Deactivates_AndShowsRestartNote()
    {
        _registry.GetStatus(Info.AssemblyPath).Returns(AddinStatus.Loaded);
        var sut = new AddinOptionsPageViewModel(_registry, Info);

        sut.ToggleEnabledCommand.Execute(null);

        _registry.Received(1).SetEnabled(Info.AssemblyPath, false);
        sut.RestartNoteVisible.Should().BeTrue();
    }

    [Fact]
    public void Toggle_FromDisabled_Activates()
    {
        _registry.GetStatus(Info.AssemblyPath).Returns(AddinStatus.Disabled);
        var sut = new AddinOptionsPageViewModel(_registry, Info);

        sut.IsAddinEnabled.Should().BeFalse();
        sut.ToggleLabel.Should().Be("Activate");

        sut.ToggleEnabledCommand.Execute(null);

        _registry.Received(1).SetEnabled(Info.AssemblyPath, true);
    }

    // ── Contributed content (Phase 5) ───────────────────────────

    [Fact]
    public void ContributedFactory_BecomesContributedContent()
    {
        _registry.GetStatus(Info.AssemblyPath).Returns(AddinStatus.Loaded);
        object control = new();

        var sut = new AddinOptionsPageViewModel(_registry, Info, () => control);

        sut.HasContributedContent.Should().BeTrue();
        sut.ContributedContent.Should().BeSameAs(control);
    }

    [Fact]
    public void NoFactory_HasNoContributedContent()
    {
        _registry.GetStatus(Info.AssemblyPath).Returns(AddinStatus.Loaded);

        var sut = new AddinOptionsPageViewModel(_registry, Info);

        sut.HasContributedContent.Should().BeFalse();
        sut.ContributedContent.Should().BeNull();
    }

    [Fact]
    public void ThrowingFactory_IsIsolated_NoCrash()
    {
        _registry.GetStatus(Info.AssemblyPath).Returns(AddinStatus.Loaded);

        var sut = new AddinOptionsPageViewModel(_registry, Info, () => throw new InvalidOperationException("boom"));

        sut.HasContributedContent.Should().BeFalse();
    }

    // ── Trust / consent (Phase 10) ──────────────────────────────

    private static AddinInfo Signed(string hash = "hash1", string publisher = "Example Ltd") =>
        new("/a", "A", "", "1.0", "Auth", publisher, "com.ex.a", hash);

    [Fact]
    public void Verified_ShowsPublisher_AndAllowed_AndCanRevoke()
    {
        _registry.GetStatus(Arg.Any<string>()).Returns(AddinStatus.Loaded);

        var sut = new AddinOptionsPageViewModel(_registry, Signed());

        sut.Publisher.Should().Be("Verified: Example Ltd");
        sut.ConsentText.Should().Be("Allowed");
        sut.CanRevokeConsent.Should().BeTrue();
    }

    [Fact]
    public void Unsigned_ShowsUnsigned()
    {
        _registry.GetStatus(Arg.Any<string>()).Returns(AddinStatus.DeveloperUnsigned);

        var sut = new AddinOptionsPageViewModel(_registry, Signed(publisher: ""));

        sut.Publisher.Should().Be("Unsigned (developer build)");
        sut.ConsentText.Should().Be("Allowed");
    }

    [Fact]
    public void Blocked_ShowsBlocked()
    {
        _registry.GetStatus(Arg.Any<string>()).Returns(AddinStatus.ConsentDenied);

        var sut = new AddinOptionsPageViewModel(_registry, Signed());

        sut.ConsentText.Should().Be("Blocked");
        sut.CanRevokeConsent.Should().BeTrue();
    }

    [Fact]
    public void RevokeConsent_CallsRegistry_WithManifestHash_AndShowsRestartNote()
    {
        _registry.GetStatus(Arg.Any<string>()).Returns(AddinStatus.Loaded);
        var sut = new AddinOptionsPageViewModel(_registry, Signed("the-hash"));

        sut.RevokeConsentCommand.Execute(null);

        _registry.Received(1).RevokeConsent("the-hash");
        sut.RestartNoteVisible.Should().BeTrue();
    }

    [Fact]
    public void CanRevokeConsent_FalseWithoutManifestHash()
    {
        _registry.GetStatus(Arg.Any<string>()).Returns(AddinStatus.Loaded);

        var sut = new AddinOptionsPageViewModel(_registry, new AddinInfo("/a", "A", "", "1.0", "Auth"));

        sut.CanRevokeConsent.Should().BeFalse();
    }

    [Fact]
    public void RevokeConsent_HidesButton_AfterClick()
    {
        _registry.GetStatus(Arg.Any<string>()).Returns(AddinStatus.Loaded);
        var sut = new AddinOptionsPageViewModel(_registry, Signed());
        sut.CanRevokeConsent.Should().BeTrue();

        sut.RevokeConsentCommand.Execute(null);

        sut.CanRevokeConsent.Should().BeFalse();
    }
}
