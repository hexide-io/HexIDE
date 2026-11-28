using System;
using System.IO;
using System.Threading.Tasks;
using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

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
        read.Target.Should().Be(ProjectTarget.None);
        File.WriteAllText(path, new ProjectSerializer().Serialize(read, path));
        File.ReadAllBytes(path).Should().Equal(bytes);
        original.Should().NotContain("[HexIDE]");
    }

    // The HexIDE metadata contract in #4821, not a claim about a native backend's project format.
    [Theory]
    [InlineData("RDCore")]
    [InlineData("rdcore")]
    [InlineData(" RDCore ; ")]
    public async Task RDCore_target_is_read_and_written_beside_bitness(string target)
    {
        var path = Path.Join(directory, "Test.vbp");
        var project = new ProjectDefinition(VBProjectType.EXE, "Test")
        {
            VbaBitness = VbaBitness.Bit64,
            ExtensionTail = "[Other]\r\nKeep=verbatim\r\n[HexIDE]\r\nFuture=kept\r\n",
        };
        File.WriteAllText(path, new ProjectSerializer().Serialize(project, path) + $"Targets={target}\r\n");
        var read = (await new ProjectLoader().LoadAsync(path, Substitute.For<IDeserializeErrorSink>())).Project;
        read.Target.Should().Be(ProjectTarget.RDCore);
        read.VbaBitness.Should().Be(VbaBitness.Bit64);
        var saved = new ProjectSerializer().Serialize(read, path);
        saved.ReplaceLineEndings("\n").Should().Contain("VbaBitness=64\nTargets=RDCore\n");
        saved.ReplaceLineEndings("\n").Should().Contain("[Other]\nKeep=verbatim\n[HexIDE]\nFuture=kept\n");
        read.Target = ProjectTarget.None;
        var cleared = new ProjectSerializer().Serialize(read, path);
        cleared.Should().NotContain("Targets=");
        cleared.Should().Contain("VbaBitness=64");
    }

    [Fact]
    public async Task Setting_RDCore_survives_fresh_open_and_retains_encoding_records()
    {
        var path = Path.Join(directory, "Test.vbp");
        var project = new ProjectDefinition(VBProjectType.EXE, "Test") { Target = ProjectTarget.RDCore };
        var module = new ModuleDefinition(project, "Main", ModuleKind.StandardModule)
            { TextEncoding = Vb6TextEncoding.Utf8, AbsolutePath = Path.Join(directory, "Main.bas") };
        module.UpdateCode("' café");
        project.AddModule(module);
        File.WriteAllText(Path.Join(directory, "Main.bas"), "Attribute VB_Name = \"Main\"\r\n' café", new System.Text.UTF8Encoding(false));
        File.WriteAllText(path, new ProjectSerializer().Serialize(project, path));
        var read = (await new ProjectLoader().LoadAsync(path, Substitute.For<IDeserializeErrorSink>())).Project;
        read.Target.Should().Be(ProjectTarget.RDCore);
        var saved = new ProjectSerializer().Serialize(read, path);
        saved.ReplaceLineEndings("\n").Should().Contain("Targets=RDCore\n").And.Contain("Main.bas=utf-8\n");
        read.Modules.Single().TextEncoding.Should().Be(Vb6TextEncoding.Utf8);
    }

    [Theory]
    [InlineData("Targets")]
    [InlineData("Targets=Future")]
    public async Task Target_metadata_does_not_consume_a_members_UTF8_record(string memberPath)
    {
        var path = Path.Join(directory, "Test.vbp");
        var project = new ProjectDefinition(VBProjectType.EXE, "Test");
        var modulePath = Path.Join(directory, memberPath);
        var module = new ModuleDefinition(project, "Main", ModuleKind.StandardModule)
            { TextEncoding = Vb6TextEncoding.Utf8, AbsolutePath = modulePath };
        module.UpdateCode("' café");
        project.AddModule(module);
        File.WriteAllText(modulePath, "Attribute VB_Name = \"Main\"\r\n' café", new System.Text.UTF8Encoding(false));
        var original = new ProjectSerializer().Serialize(project, path);
        File.WriteAllText(path, original);
        var read = (await new ProjectLoader().LoadAsync(path, Substitute.For<IDeserializeErrorSink>())).Project;
        read.Target.Should().Be(ProjectTarget.None);
        read.Modules.Single().TextEncoding.Should().Be(Vb6TextEncoding.Utf8);
        read.Modules.Single().Code.Should().Contain("café");
        new ProjectSerializer().Serialize(read, path).Should().Be(original);
        original.ReplaceLineEndings("\n").Split($"{memberPath}=utf-8\n").Should().HaveCount(2);
    }

    // The key is plural, as Jen chose on 2026-11-18, so that several targets never need a second key (#4821). The
    // singular spelling is no key of HexIDE's: it reads as no target and is written back as it was.
    [Fact]
    public async Task The_singular_Target_key_is_not_read()
    {
        var path = Path.Join(directory, "Test.vbp");
        var original = new ProjectSerializer().Serialize(new ProjectDefinition(VBProjectType.EXE, "Test"), path) +
            "\r\n[HexIDE]\r\nTarget=RDCore\r\n";
        File.WriteAllText(path, original);
        var bytes = File.ReadAllBytes(path);
        var read = (await new ProjectLoader().LoadAsync(path, Substitute.For<IDeserializeErrorSink>())).Project;
        read.Target.Should().Be(ProjectTarget.None);
        File.WriteAllText(path, new ProjectSerializer().Serialize(read, path));
        File.ReadAllBytes(path).Should().Equal(bytes);
    }

    [Theory]
    [InlineData("Future Backend")]
    [InlineData("RDCore;Future")]
    [InlineData("RDCore;RDCore")]
    [InlineData("utf-8")]
    [InlineData("Future=utf-8")]
    [InlineData("")]
    public async Task Unknown_target_and_non_HexIDE_target_round_trip_verbatim(string target)
    {
        var path = Path.Join(directory, "Test.vbp");
        var original = new ProjectSerializer().Serialize(new ProjectDefinition(VBProjectType.EXE, "Test"), path) +
            $"\r\n[Other]\r\nTargets=RDCore\r\n[HexIDE]\r\nTargets = {target}  \r\n";
        File.WriteAllText(path, original);
        var bytes = File.ReadAllBytes(path);
        var read = (await new ProjectLoader().LoadAsync(path, Substitute.For<IDeserializeErrorSink>())).Project;
        read.Target.Should().Be(ProjectTarget.None);
        File.WriteAllText(path, new ProjectSerializer().Serialize(read, path));
        File.ReadAllBytes(path).Should().Equal(bytes);
    }

    // #5036: the legacy key is ignored; all values retain their position and spelling.
    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("future")]
    [InlineData("utf-8")]
    [InlineData("")]
    public async Task Win64_option_round_trips_in_place(string value)
    {
        var path = Path.Join(directory, "Test.vbp");
        var original = new ProjectSerializer().Serialize(new ProjectDefinition(VBProjectType.EXE, "Test"), path) +
            $"\r\n[Other]\r\nWin64OnlySetOnWindows=1\r\n[HexIDE]\r\n' before\r\nWin64OnlySetOnWindows = {value}  \r\n' after\r\nTargets=RDCore\r\n";
        File.WriteAllText(path, original);
        var read = (await new ProjectLoader().LoadAsync(path, Substitute.For<IDeserializeErrorSink>())).Project;
        HexIDE.Runtime.Interpreter.ConditionalCompilation.CompilationConstants.ForProject(read,
            System.Runtime.InteropServices.OSPlatform.OSX).BuiltIns["Win64"].Value.Should().Be(1,
                "#5036: the legacy project key is ignored");
        new ProjectSerializer().Serialize(read, path).Should().Be(original);
    }

    [Fact]
    public async Task Win64_option_keeps_neighboring_metadata_and_blank_lines_in_order()
    {
        var path = Path.Join(directory, "Test.vbp");
        var original = new ProjectSerializer().Serialize(new ProjectDefinition(VBProjectType.EXE, "Test"), path) +
            "\r\n[HexIDE]\r\nVbaBitness=64\r\nTargets=RDCore\r\n\r\n' before\r\nWin64OnlySetOnWindows=1\r\n' after\r\n";
        File.WriteAllText(path, original);
        var read = (await new ProjectLoader().LoadAsync(path, Substitute.For<IDeserializeErrorSink>())).Project;
        new ProjectSerializer().Serialize(read, path).Should().Be(original);
        var tail = "[HexIDE]\r\nTargets=RDCore\r\nMain.bas=utf-8\r\n' before\r\nWin64OnlySetOnWindows=1\r\n' after\r\n";
        ProjectEncodingRecords.Write(tail, ["Main.bas"], VbaBitness.None, ProjectTarget.RDCore).Should().Be(tail);
    }

    // #5036: only the installation flag selects the macOS RDCore Win64 row.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_project_line_is_ignored_by_the_installation_flag(bool enabled)
    {
        var path = Path.Join(directory, "Test.vbp");
        var original = new ProjectSerializer().Serialize(new ProjectDefinition(VBProjectType.EXE, "Test"), path) +
            "\r\n[HexIDE]\r\nTargets=RDCore\r\nWin64OnlySetOnWindows=1\r\n";
        File.WriteAllText(path, original);
        var read = (await new ProjectLoader().LoadAsync(path, Substitute.For<IDeserializeErrorSink>())).Project;
        HexIDE.Runtime.Interpreter.ConditionalCompilation.CompilationConstants.ForProject(read,
            System.Runtime.InteropServices.OSPlatform.OSX, enabled).BuiltIns["Win64"].Value.Should().Be(enabled ? 0 : 1);
        new ProjectSerializer().Serialize(read, path).Should().Be(original);
    }

}
