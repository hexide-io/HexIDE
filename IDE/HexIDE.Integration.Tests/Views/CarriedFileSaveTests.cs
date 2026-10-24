using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Lsp;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Views;

public class CarriedFileSaveTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hexide-carried-save-" + Guid.NewGuid().ToString("N"));

    public CarriedFileSaveTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    private RelatedDocumentEditorViewModel Open(byte[] bytes)
    {
        var path = Path.Combine(_dir, "carried.dsr");
        File.WriteAllBytes(path, bytes);
        return new RelatedDocumentEditorViewModel(Substitute.For<ILspClient>())
            .Initialize(new RelatedDocumentDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "carried.dsr", path));
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task InvalidUtf8BytesSurviveSaving(bool bom, bool edit)
    {
        byte[] original = bom ? [0xEF, 0xBB, 0xBF, 0x63, 0xE9, 0x0D, 0x0A] : [0x63, 0xE9, 0x0D, 0x0A];
        using var vm = Open(original);
        if (edit) vm.Document.Replace(0, 1, "d");

        await vm.SaveAsync();

        var expected = (byte[])original.Clone();
        if (edit) expected[bom ? 3 : 0] = 0x64;
        File.ReadAllBytes(vm.RelatedDocument!.AbsolutePath!).Should().Equal(expected);
        vm.IsDirty.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task SavingTextRestoredToItsOriginalValueDoesNotWrite()
    {
        using var vm = Open([0x63]);
        var path = vm.RelatedDocument!.AbsolutePath!;
        var timestamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, timestamp);
        vm.Document.Text = "edited";
        vm.Document.Text = "c";

        await vm.SaveAsync();

        File.GetLastWriteTimeUtc(path).Should().Be(timestamp);
    }

    [AvaloniaFact]
    public async Task AnEditLatin1CannotRepresentLeavesTheOriginalBytesIntact()
    {
        byte[] original = [0x63, 0xE9];
        using var vm = Open(original);
        vm.Document.Text += "\u0100";

        var save = () => vm.SaveAsync();

        await save.Should().ThrowAsync<System.Text.EncoderFallbackException>();
        File.ReadAllBytes(vm.RelatedDocument!.AbsolutePath!).Should().Equal(original);
        vm.IsDirty.Should().BeTrue();
    }
}
