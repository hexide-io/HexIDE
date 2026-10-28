using System;
using System.IO;
using System.Threading.Tasks;

namespace HexIDE.Tests.Corpus;

public class ProjectSmokeHarnessTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("hexide-smoke-test-").FullName;
    private string Project => Path.Join(root, "Test.vbp");

    public ProjectSmokeHarnessTests() => File.WriteAllText(Project, "Type=Exe\r\nName=\"Test\"\r\n");

    public void Dispose()
    {
        Directory.Delete(root, true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Reference_paths_follow_the_measured_same_drive_other_drive_and_unresolved_rules()
    {
        const string prefix = @"Reference=*\G{00020430-0000-0000-C000-000000000046}#2.0#0#";
        var original = System.Text.Encoding.Latin1.GetBytes(prefix + @"C:\Windows\stdole2.tlb#OLE Automation " + "\r\n"
            + prefix + @"X:\Other.tlb#Other" + "\r\n" + prefix + "#Missing\r\nRetained=1\r\n");
        var expected = System.Text.Encoding.Latin1.GetBytes(prefix + @"..\..\Windows\stdole2.tlb#OLE Automation " + "\r\n"
            + prefix + @"X:\Other.tlb#Other" + "\r\n" + prefix + "#Missing\r\nRetained=1\r\n");
        var actual = ProjectSmokeHarness.ExpectedSave(original, @"C:\corpus\project",
            path => path.Length == 0 ? null : path, out var changes);
        actual.Should().Equal(expected);
        changes.Should().ContainSingle().Which.Should().Contain(@"C:\Windows\stdole2.tlb -> ..\..\Windows\stdole2.tlb");
        File.WriteAllBytes(Project, original);
        var result = await ProjectSmokeHarness.Check(root, Project, path =>
            Task.FromResult<Func<Task<string?>>>(() =>
            {
                File.WriteAllBytes(path, expected);
                return Task.FromResult<string?>(null);
            }), referenceDirectory: @"C:\corpus\project");
        result.Crash.Should().BeNull();
        result.Failed.Should().BeFalse();
        result.Normalisations.Should().Equal(changes);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 2)]
    public async Task Member_names_use_the_host_path_comparison(bool insensitive, int count)
    {
        File.WriteAllText(Project, "Type=Exe\r\nForm=AddIn.frm\r\n");
        File.WriteAllText(Path.Join(root, "ADDIN.FRM"), "form");
        var result = await ProjectSmokeHarness.Check(root, Project, path =>
            Task.FromResult<Func<Task<string?>>>(() =>
            {
                var directory = Path.GetDirectoryName(path)!;
                File.Move(Path.Join(directory, "ADDIN.FRM"), Path.Join(directory, "temporary.frm"));
                File.Move(Path.Join(directory, "temporary.frm"), Path.Join(directory, "AddIn.frm"));
                return Task.FromResult<string?>(null);
            }), caseInsensitivePaths: insensitive);
        result.Crash.Should().BeNull();
        result.Differences.Should().HaveCount(count);
    }

    [Fact]
    public async Task Save_uses_a_copy_and_leaves_the_original_untouched()
    {
        var result = await ProjectSmokeHarness.Check(root, Project, path =>
        {
            path.Should().NotBe(Project);
            File.ReadAllBytes(path).Should().Equal(File.ReadAllBytes(Project));
            return Task.FromResult<Func<Task<string?>>>(() =>
            {
                File.WriteAllText(path, "changed");
                return Task.FromResult<string?>(null);
            });
        });
        result.Differences.Should().Equal("Test.vbp");
        File.ReadAllText(Project).Should().Contain("Type=Exe");
    }

    [Theory]
    [InlineData("change")]
    [InlineData("delete")]
    [InlineData("add")]
    public async Task Evidence_keeps_exact_original_and_saved_bytes(string operation)
    {
        var evidence = Path.Join(root, "evidence");
        var original = File.ReadAllBytes(Project);
        byte[] changed = [0, 255, 13, 10];
        var result = await ProjectSmokeHarness.Check(root, Project, path =>
            Task.FromResult<Func<Task<string?>>>(() =>
            {
                if (operation == "delete") File.Delete(path);
                else File.WriteAllBytes(operation == "add" ? Path.Join(Path.GetDirectoryName(path)!, "Test.frx") : path, changed);
                return Task.FromResult<string?>(null);
            }), evidence);
        result.Crash.Should().BeNull();
        var name = operation == "add" ? "Test.frx" : "Test.vbp";
        var before = Path.Join(evidence, "original", name);
        var after = Path.Join(evidence, "saved", name);
        File.Exists(before).Should().Be(operation != "add");
        File.Exists(after).Should().Be(operation != "delete");
        if (operation != "add") File.ReadAllBytes(before).Should().Equal(original);
        if (operation != "delete") File.ReadAllBytes(after).Should().Equal(changed);
        File.ReadAllBytes(Project).Should().Equal(original);
    }

    [Theory]
    [InlineData("change")]
    [InlineData("delete")]
    [InlineData("add")]
    public async Task Every_file_difference_fails_even_when_a_save_refuses(string operation)
    {
        var result = await ProjectSmokeHarness.Check(root, Project, path =>
            Task.FromResult<Func<Task<string?>>>(() =>
            {
                var companion = Path.Join(Path.GetDirectoryName(path)!, "Test.frx");
                if (operation == "change") File.WriteAllText(path, "changed");
                if (operation == "delete") File.Delete(path);
                if (operation == "add") File.WriteAllBytes(companion, [1, 2]);
                return Task.FromResult<string?>("refused for test");
            }));
        result.Failed.Should().BeTrue();
        result.Differences.Should().ContainSingle();
        result.Save.Should().Be("refused for test");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Exceptions_name_the_stage_and_keep_the_exception(bool duringOpen)
    {
        var exception = new InvalidOperationException("probe");
        var result = await ProjectSmokeHarness.Check(root, Project, _ =>
        {
            if (duringOpen) throw exception;
            return Task.FromResult<Func<Task<string?>>>(() => throw exception);
        });
        result.Failed.Should().BeTrue();
        result.Opened.Should().Be(!duringOpen);
        result.Crash.Should().BeSameAs(exception);
    }

    [Fact]
    public async Task A_clean_refusal_passes_and_preserves_its_reason()
    {
        var result = await ProjectSmokeHarness.Check(root, Project, _ =>
            Task.FromResult<Func<Task<string?>>>(() => Task.FromResult<string?>("unsupported form")));
        result.Opened.Should().BeTrue();
        result.Failed.Should().BeFalse();
        result.Save.Should().Be("unsupported form");
    }

    [Fact]
    public async Task A_project_outside_the_root_is_rejected_before_opening()
    {
        var result = await ProjectSmokeHarness.Check(root, Path.Join(root, "..", "outside.vbp"),
            _ => throw new Exception("must not open"));
        result.Failed.Should().BeTrue();
        result.Opened.Should().BeFalse();
        result.Crash.Should().BeOfType<ArgumentException>();
    }

    [Fact]
    public void Discovery_includes_uppercase_projects_and_deduplicates_overlapping_roots()
    {
        File.WriteAllText(Path.Join(root, "Other.VBP"), "Type=Exe");
        File.WriteAllText(Path.Join(root, "ignored.bas"), "");
        ProjectSmokeHarness.Discover([root, root]).Should().HaveCount(2);
    }
}
