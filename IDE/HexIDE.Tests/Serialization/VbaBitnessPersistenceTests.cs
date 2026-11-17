using System;
using System.IO;
using System.Threading.Tasks;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.Serialization;

// HexIDE's project metadata contract, #4245; this is not a VBA file-format claim.
public class VbaBitnessPersistenceTests : IDisposable
{
    private readonly string directory = Path.Join(Path.GetTempPath(), "hexide-bitness-" + Guid.NewGuid().ToString("N"));
    public VbaBitnessPersistenceTests() => Directory.CreateDirectory(directory);
    public void Dispose() { Directory.Delete(directory, true); GC.SuppressFinalize(this); }

    [Theory]
    [InlineData(VbaBitness.Bit32)]
    [InlineData(VbaBitness.Bit64)]
    public async Task Target_survives_save_and_fresh_open(VbaBitness bitness)
    {
        var path = Path.Join(directory, "Test.vbp");
        var project = new ProjectDefinition(VBProjectType.EXE, "Test")
        {
            VbaBitness = bitness,
            ExtensionTail = "[Other]\r\nKeep=verbatim\r\n[HexIDE]\r\nFuture=kept\r\n",
        };
        File.WriteAllText(path, new ProjectSerializer().Serialize(project, path));
        var read = (await new ProjectLoader().LoadAsync(path, Substitute.For<IDeserializeErrorSink>())).Project;
        read.VbaBitness.Should().Be(bitness);
        var saved = new ProjectSerializer().Serialize(read, path);
        saved.ReplaceLineEndings("\n").Should().Contain($"VbaBitness={(int)bitness}\n");
        saved.ReplaceLineEndings("\n").Should().Contain("[Other]\nKeep=verbatim\n");
        saved.ReplaceLineEndings("\n").Should().Contain("Future=kept\n");
        read.VbaBitness = VbaBitness.None;
        new ProjectSerializer().Serialize(read, path).Should().NotContain("VbaBitness=");
    }

    [Fact]
    public async Task An_unknown_value_reads_as_no_target_and_is_written_back_verbatim()
    {
        var path = Path.Join(directory, "Test.vbp");
        var project = new ProjectDefinition(VBProjectType.EXE, "Test") { ExtensionTail = "[HexIDE]\r\nVbaBitness=128\r\n" };
        File.WriteAllText(path, new ProjectSerializer().Serialize(project, path));
        var read = (await new ProjectLoader().LoadAsync(path, Substitute.For<IDeserializeErrorSink>())).Project;
        read.VbaBitness.Should().Be(VbaBitness.None);
        new ProjectSerializer().Serialize(read, path).ReplaceLineEndings("\n").Should().Contain("VbaBitness=128\n");
    }

    [Fact]
    public async Task Default_VB6_save_is_byte_identical()
    {
        var path = Path.Join(directory, "Test.vbp");
        var original = new ProjectSerializer().Serialize(new ProjectDefinition(VBProjectType.EXE, "Test"), path);
        File.WriteAllText(path, original);
        var bytes = File.ReadAllBytes(path);
        var read = (await new ProjectLoader().LoadAsync(path, Substitute.For<IDeserializeErrorSink>())).Project;
        read.VbaBitness.Should().Be(VbaBitness.None);
        File.WriteAllText(path, new ProjectSerializer().Serialize(read, path));
        File.ReadAllBytes(path).Should().Equal(bytes);
        original.Should().NotContain("[HexIDE]");
    }
}
