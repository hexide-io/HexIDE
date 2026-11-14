using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Forms.ViewModels.Options;

namespace HexIDE.Tests.Addins;

public sealed class DuplicateAddinTests
{
    [AvaloniaTheory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void OnlyTheWinningPackageInitializes(bool firstVerified, bool secondVerified)
    {
        var root = Path.Combine(Path.GetTempPath(), "hexide-duplicates-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var verifier = Substitute.For<IPackageVerifier>();
            var consent = new ConsentStore(Path.Combine(root, "consent.json"));
            var packages = Path.Combine(root, "addins");
            var folders = new[] { Path.Combine(packages, "a"), Path.Combine(packages, "b") };
            for (var i = 0; i < folders.Length; i++)
            {
                Directory.CreateDirectory(folders[i]);
                const string entry = "DuplicateProbe.dll";
                WriteProbe(Path.Combine(folders[i], entry), Path.GetFileName(folders[i]));
                var verified = i == 0 ? firstVerified : secondVerified;
                verifier.Verify(folders[i]).Returns(new PackageVerificationResult(
                    verified ? PackageVerdict.Verified : PackageVerdict.Untrusted,
                    new AddinManifest { Id = "com.hexide.aichat", Title = "AI Chat", Entry = entry }, null, null, null)
                    { IsFirstParty = verified, ManifestHash = i.ToString() });
                consent.Record("com.hexide.aichat", "", i.ToString(), ConsentDecisions.Allow);
            }
            var settings = Substitute.For<ISettingsService>();
            settings.LoadUnsignedAddins.Returns(true);
            var developer = Substitute.For<IDeveloperModeService>();
            developer.IsEnabled.Returns(true);
            var localization = new LocalizationService();
            var tools = new AddinToolWindowService(localization);
            var menus = new AddinMenuService(localization);
            var host = Substitute.For<IHexIdeHost>();
            host.ToolWindows.Returns(tools);
            host.Menus.Returns(menus);
            using var registry = new AddinRegistry(verifier, settings, developer, Substitute.For<IWindowManager>(), localization,
                consent, new RevocationStore(null, Path.Combine(root, "revocations")), Path.Combine(root, "state.json"), packages);

            registry.LoadAll(host);

            var winner = !firstVerified && secondVerified ? 1 : 0;
            var loser = 1 - winner;
            registry.GetAll().Should().HaveCount(2);
            host.Received(1).RegisterCommand("initialized", Path.GetFileName(folders[winner]), Arg.Any<Action>(), null, null);
            host.DidNotReceive().RegisterCommand("initialized", Path.GetFileName(folders[loser]), Arg.Any<Action>(), null, null);
            registry.GetStatus(folders[winner]).Should().Be(firstVerified || secondVerified ? AddinStatus.Loaded : AddinStatus.DeveloperUnsigned);
            registry.GetStatus(folders[loser]).Should().Be(AddinStatus.Error);
            tools.Tools.Should().ContainSingle().Which.Title.Should().Be(Path.GetFileName(folders[winner]));
            menus.ToolsItems.Should().ContainSingle().Which.Header.Should().Be(Path.GetFileName(folders[winner]));
            var info = registry.GetAll().Single(info => info.AssemblyPath == folders[loser]);
            new AddinOptionsPageViewModel(registry, info).StatusText.Should().Contain("com.hexide.aichat").And.Contain(folders[winner]);
            registry.SetEnabled(folders[loser], false);
            registry.SetEnabled(folders[loser], true);
            registry.GetStatus(folders[loser]).Should().Be(AddinStatus.Error);
        }
        finally { try { Directory.Delete(root, true); } catch { /* best effort: Windows may still hold the loaded probe */ } }
    }

    internal static void WriteProbe(string path, string label)
    {
        var source = $$"""
            using System;
            using HexIDE.Addins;
            public sealed class DuplicateProbe : IAddin
            {
                private IDisposable tool;
                private IDisposable menu;
                public void Initialize(IHexIdeHost host)
                {
                    host.RegisterCommand("initialized", "{{label}}", () => { });
                    tool = host.ToolWindows.Register("probe", "{{label}}", null, () => { host.RegisterCommand("factory", "{{label}}", () => { }); return new object(); });
                    menu = host.Menus.Add("Tools", "{{label}}", null, () => { });
                }
                public void Dispose() { tool?.Dispose(); menu?.Dispose(); }
            }
            """;
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(IAddin).Assembly.Location).Distinct()
            .Select(file => MetadataReference.CreateFromFile(file));
        var compilation = CSharpCompilation.Create("DuplicateProbe", [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var result = compilation.Emit(path);
        result.Success.Should().BeTrue(string.Join("; ", result.Diagnostics));
    }
}
