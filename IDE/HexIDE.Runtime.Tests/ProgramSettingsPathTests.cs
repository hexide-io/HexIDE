using HexIDE.IDE;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// #4070, measured with vb6.exe SP6 in oracle PR #4072: IDE, native and p-code EXEs share settings.
public sealed class ProgramSettingsPathTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("hexide_program_settings_").FullName;
    private const string FileName = "program-settings.json";

    private UserDataRoot Root(DistributionIdentity identity) => new(
        () => Path.Combine(directory, identity.StateDirectoryName),
        () => Path.Combine(directory, DistributionIdentity.Stock.StateDirectoryName));

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void ABrandedIdeAndStockStandaloneResolveToTheSameFile()
    {
        Root(new DistributionIdentity("brand-a", "Brand A")).ForPrograms(FileName)
            .Should().Be(Root(DistributionIdentity.Stock).ForPrograms(FileName));
    }

    [Fact]
    public void TwoBrandsResolveToTheSameFile()
    {
        Root(new DistributionIdentity("brand-a", "Brand A")).ForPrograms(FileName)
            .Should().Be(Root(new DistributionIdentity("brand-b", "Brand B")).ForPrograms(FileName));
    }

    [Fact]
    public void ARedirectIsolatesProgramSettingsToo()
    {
        var root = Root(new DistributionIdentity("brand-a", "Brand A"));
        var redirect = Path.Combine(directory, "demo");
        root.RedirectTo(redirect);
        root.ForPrograms(FileName).Should().Be(Path.Combine(redirect, FileName));
        var store = new FileVbSettingsStore(root.ForPrograms(FileName));
        store.SetValue(["App", "Section"], "Key", "demo");
        new FileVbSettingsStore(Root(DistributionIdentity.Stock).ForPrograms(FileName))
            .Values(["App", "Section"]).Should().BeNull();
    }

    [Fact]
    public void IdeAndStandaloneReadEachOthersValues()
    {
        var ide = new FileVbSettingsStore(Root(new DistributionIdentity("brand-a", "Brand A")).ForPrograms(FileName));
        var standalone = new FileVbSettingsStore(Root(DistributionIdentity.Stock).ForPrograms(FileName));
        ide.SetValue(["App", "Section"], "Key", "IDE");
        standalone.Values(["App", "Section"]).Should().Equal(new VbSettingValue("Key", "IDE"));
        standalone.SetValue(["App", "Section"], "Key", "EXE");
        ide.Values(["App", "Section"]).Should().Equal(new VbSettingValue("Key", "EXE"));
    }

    [Fact]
    public void ReadingAProgramPathPreventsALateRedirect()
    {
        var root = Root(DistributionIdentity.Stock);
        _ = root.ForPrograms(FileName);
        var redirect = () => root.RedirectTo(Path.Combine(directory, "late"));
        redirect.Should().Throw<InvalidOperationException>();
    }
}
