using System.Reflection;
using HexIDE.Desktop;
using HexIDE.IDE;
using HexIDE.Projects;
using HexIDE.Profiles;

namespace HexIDE.Tests.Profiles;

public sealed class ProfileMigrationTests : IDisposable
{
    private readonly string root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(), "hexide-profile-launch-" + Guid.NewGuid());
    private readonly Dictionary<PropertyInfo, object?> previous = typeof(ServerOptions).GetProperties(BindingFlags.Public | BindingFlags.Static)
        .Where(p => p.SetMethod != null).ToDictionary(p => p, p => p.GetValue(null));

    public ProfileMigrationTests()
    {
        Directory.CreateDirectory(root);
        foreach (var property in previous.Keys)
            property.SetValue(null, property.PropertyType.IsValueType ? Activator.CreateInstance(property.PropertyType) : null);
    }

    [Theory]
    [InlineData("vb6", true, false, 8)]
    [InlineData("vbaode", false, true, 1)]
    [InlineData("vba", false, true, 0)]
    public void BuiltInsPreservePinnedPersonalityTable(string id, bool project, bool tools, int count)
    {
        var snapshot = new ProfileResolver(root, root).Resolve(id)!;
        var service = new ProfileService(snapshot);
        service.IsProjectReferencesVisible.Should().Be(project);
        service.IsToolsReferencesVisible.Should().Be(tools);
        service.AvailableProjectTypes.Should().Equal(id == "vb6" ? IProjectTemplate.Templates
            : id == "vbaode" ? [IProjectTemplate.StandardEXE] : []);
        snapshot.MenuVisibility["project.references"].Should().Be(project);
        snapshot.MenuVisibility["tools.references"].Should().Be(tools);
        snapshot.ProjectTypes!.Value.Should().HaveCount(count);
        snapshot.Theme.Should().BeNull();
        snapshot.Keymap.Should().BeNull();
        snapshot.SettingsDefaults.Should().BeNull();
        snapshot.LanguageOverrides.Should().BeEmpty();
        snapshot.ToolbarVisibility.Should().BeEmpty();
    }

    [Theory]
    [InlineData("--profile")]
    [InlineData("/profile")]
    public void ProfileIsAcceptedAndReplacesPersonalityInHelp(string flag)
    {
        ServerOptions.ParseArgs([flag, "vbaode"], root);
        ServerOptions.Profile.Should().Be("vbaode");
        ServerOptions.Options.Should().Contain(o => o.Name == "profile" &&
            o.Summary == "Base profile: name, directory path, or none (safe mode).");
        ServerOptions.Options.Should().NotContain(o => o.Name == "personality");
    }

    [Theory]
    [InlineData("--personality")]
    [InlineData("/PERSONALITY")]
    public void RemovedFlagIsRefusedEvenBesideValidProfile(string flag)
    {
        ServerOptions.ParseArgs(["--profile", "vb6", flag, "vba"], root);
        ServerOptions.ProfileArgumentError.Should().Be("Str.Profiles.PersonalityRemoved");
    }

    [Theory]
    [InlineData("--profile", null, null, null)]
    [InlineData("/profile", "/newproject", null, null)]
    [InlineData("--profile", "--newproject", null, null)]
    [InlineData("--profile", "vb6", "/profile", "vba")]
    public void MalformedOrRepeatedSelectorIsRefused(string first, string? second, string? third, string? fourth)
    {
        ServerOptions.ParseArgs(new[] { first, second, third, fourth }.OfType<string>().ToArray(), root);
        ServerOptions.ProfileArgumentError.Should().Be("Str.Profiles.SelectorRequired");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InstalledLookupUsesUserDataFlagInEitherOrder(bool profileFirst)
    {
        var user = Path.Combine(root, "isolated");
        var bundle = Path.Combine(user, "profiles", "custom");
        Directory.CreateDirectory(bundle);
        File.WriteAllText(Path.Combine(bundle, "profile.json"), """{"schemaVersion":1,"id":"custom","projectTypes":[]}""");
        ServerOptions.ParseArgs(profileFirst
            ? ["--profile", "custom", "--user-data-dir", "isolated"]
            : ["/user-data-dir", "isolated", "/profile", "custom"], root);
        var snapshot = ServerOptions.ResolveProfile(root, Path.Combine(root, "unused"))!;
        snapshot.Source.Should().Be(bundle);
        new ProfileService(snapshot).AvailableProjectTypes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, "embedded:vb6")]
    [InlineData("vb6", "embedded:vb6")]
    [InlineData("none", null)]
    [InlineData("./bundle", "bundle")]
    public void SelectionUsesInvocationDirectoryAndDefault(string? selector, string? source)
    {
        Directory.CreateDirectory(Path.Combine(root, "bundle"));
        File.WriteAllText(Path.Combine(root, "bundle", "profile.json"), """{"schemaVersion":1,"id":"vb6"}""");
        ServerOptions.ParseArgs(selector == null ? [] : ["--profile", selector], root);
        var snapshot = ServerOptions.ResolveProfile(root, root);
        (snapshot?.Source).Should().Be(source == "bundle" ? Path.Combine(root, source) : source);
        if (selector == "none") snapshot.Should().BeNull();
    }

    [Fact]
    public void PartialBundleUsesBaselineAndPreservesTemplateOrderAndDisabledEntries()
    {
        File.WriteAllText(Path.Combine(root, "profile.json"),
            """{"schemaVersion":1,"id":"custom","projectTypes":["wizard-manager","standard-exe","activex-dll"],"menuVisibility":{"project.references":false}}""");
        var service = new ProfileService(new ProfileResolver(root, root).Resolve(root));
        service.IsProjectReferencesVisible.Should().BeFalse();
        service.IsToolsReferencesVisible.Should().BeFalse();
        service.AvailableProjectTypes.Should().Equal(IProjectTemplate.Templates[5], IProjectTemplate.StandardEXE, IProjectTemplate.ActiveXDLL);
        service.AvailableProjectTypes.Select(t => t.Supported).Should().Equal(false, true, false);
        new ProfileService(null).AvailableProjectTypes.Should().Equal(IProjectTemplate.Templates);
        new ProfileService(null).IsProjectReferencesVisible.Should().BeTrue();
    }

    [Fact]
    public void InvalidSelectionDoesNotReplaceSessionSnapshot()
    {
        var previousProfile = Static.Profile;
        try
        {
            Static.Profile = new ProfileResolver(root, root).Resolve("vba");
            ServerOptions.ParseArgs(["--profile", "missing"], root);
            Action resolve = () => Static.Profile = ServerOptions.ResolveProfile(root, root);
            resolve.Should().Throw<ProfileLoadException>().Which.SourceId.Should().Contain("missing");
            Static.Profile!.Id.Should().Be("vba");
        }
        finally { Static.Profile = previousProfile; }
    }

    [Theory]
    [InlineData("--personality", 2)]
    [InlineData("/personality", 2)]
    [InlineData("--profile", 2)]
    [InlineData("--help", 0)]
    public async Task StartupDiagnosticsWorkBeforeAvaloniaAndOpenNoWindow(string flag, int exitCode)
    {
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment.Remove("DISPLAY");
        start.Environment.Remove("WAYLAND_DISPLAY");
        start.ArgumentList.Add(typeof(ServerOptions).Assembly.Location);
        start.ArgumentList.Add("--user-data-dir");
        start.ArgumentList.Add(Path.Combine(root, "user"));
        start.ArgumentList.Add(flag);
        if (flag == "--help")
        {
            start.ArgumentList.Add("--personality");
            start.ArgumentList.Add("vba");
        }
        using var process = System.Diagnostics.Process.Start(start)!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            process.ExitCode.Should().Be(exitCode, await errors);
            var text = await output;
            text.Should().Contain("--profile");
            text.Should().NotContain("Str.Profiles.");
            if (flag.Contains("personality"))
            {
                text.Should().Contain("--profile vb6");
                text.Should().Contain("--profile vbaode");
                text.Should().Contain("--profile vba");
            }
            Directory.Exists(Path.Combine(root, "user")).Should().BeFalse("refusals and help precede user configuration access");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            }
        }
    }

    [Fact]
    public async Task InvalidBundleRefusesStartupWithFileAndSourceAttribution()
    {
        File.WriteAllText(Path.Combine(root, "profile.json"), """{"schemaVersion":100,"id":"custom"}""");
        var start = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment.Remove("DISPLAY");
        start.Environment.Remove("WAYLAND_DISPLAY");
        foreach (var arg in new[] { typeof(ServerOptions).Assembly.Location, "--profile", "./", "--user-data-dir", Path.Combine(root, "user") })
            start.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(start)!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            process.ExitCode.Should().Be(2, await errors);
            var text = await output;
            text.Should().Contain(root).And.Contain("profile.json");
            text.Should().NotContain("Str.Profiles.");
            File.Exists(Path.Combine(root, "user", "settings.json")).Should().BeFalse();
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            }
        }
    }

    [Fact]
    public async Task TemplateAutomationReportsProfileOfferingsAndRetainsAddins()
    {
        var service = new ProfileService(new ProfileResolver(root, root).Resolve("vbaode"));
        var addins = new HexIDE.Addins.AddinProjectTemplateService();
        addins.Register(IProjectTemplate.ActiveXDLL);
        using var context = new HexIDE.Desktop.Server.IdeContext(
            Substitute.For<IProjectManager>(), Substitute.For<IDocumentDockService>(),
            Substitute.For<HexIDE.Lsp.ILspClient>(), null!, null!, null!, null!, null!, null!, null!, null!, null!,
            service, addins, null!, null!, null!, null!, null!);
        var result = await new HexIDE.Desktop.Server.HexIdeTools(context)
            .GetNewProjectTemplatesAsync(TestContext.Current.CancellationToken);
        result.Templates.Select(t => t.Name).Should().Equal("Standard EXE", "ActiveX DLL");
        result.Templates.Select(t => t.Source).Should().Equal("profile", "addin");
        result.Templates.Select(t => t.Supported).Should().Equal(true, false);
    }

    public void Dispose()
    {
        foreach (var (property, value) in previous) property.SetValue(null, value);
        Directory.Delete(root, true);
    }
}
