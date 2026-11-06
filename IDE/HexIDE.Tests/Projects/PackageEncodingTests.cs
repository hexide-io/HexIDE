using System.Linq;
using System;
using System.IO;
using System.Reflection;
using HexIDE.IDE;
using HexIDE.Projects;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.Projects;

public sealed class PackageEncodingTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-package-" + Guid.NewGuid().ToString("N"));

    public PackageEncodingTests() => Directory.CreateDirectory(dir);

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string error) => throw new InvalidOperationException(error);
    }

    [Theory]
    [InlineData(1252, "Café € “text”")]
    [InlineData(932, "日本語")]
    public async Task Package_sources_declare_utf8_independently_of_the_source_code_page(int codePage, string text)
    {
        var encoding = new Vb6TextEncoding(codePage, codePage == 65001);
        var vbp = Path.Join(dir, "Test.vbp");
        File.WriteAllBytes(vbp, Vb6TextFile.Encode($"Type=Exe\r\nForm=Form1.frm\r\nModule=Module1; Module1.bas\r\nName=\"Test\"\r\nDescription=\"{text}\"\r\n", encoding));
        File.WriteAllBytes(Path.Join(dir, "Form1.frm"), Vb6TextFile.Encode(
            $"VERSION 5.00\r\nBegin VB.Form Form1\r\n   Caption = \"{text}\"\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n", encoding));
        File.WriteAllBytes(Path.Join(dir, "Module1.bas"), Vb6TextFile.Encode(
            $"Attribute VB_Name = \"Module1\"\r\n' {text}\r\n", encoding));
        var project = (await new ProjectLoader { HostAnsi = encoding }.LoadAsync(vbp, new Sink())).Project;
        var originals = Directory.GetFiles(dir);
        var expected = originals.ToDictionary(path => path, path => Vb6TextFile.Decode(File.ReadAllBytes(path), encoding));

        var companion = Path.Join(dir, "Form1.frx");
        File.WriteAllBytes(companion, [0xE9, 0xFF, 0x00]);

        ProjectService.EncodePackageSources(project);

        foreach (var path in originals)
        {
            File.ReadAllBytes(path).Take(3).Should().Equal([0xEF, 0xBB, 0xBF]);
            File.ReadAllText(path).ReplaceLineEndings().Should().Be(expected[path].ReplaceLineEndings());
        }
        File.ReadAllBytes(companion).Should().Equal([0xE9, 0xFF, 0x00]);
        project.Forms.Single().TextEncoding.Should().Be(encoding);
        project.Modules.Single().TextEncoding.Should().Be(encoding);
        project.TextEncoding.Should().Be(encoding);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Check_reads_source_project_and_form_in_the_IDEs_encoding(bool form)
    {
        var vbp = Path.Join(dir, "Test.vbp");
        File.WriteAllBytes(vbp, Vb6TextFile.Encode(
            $"Type=Exe\r\nForm={(form ? "Form1" : "Café")}.frm\r\nName=\"{(form ? "Test" : "Café")}\"\r\n", new(1252)));
        File.WriteAllBytes(Path.Join(dir, form ? "Form1.frm" : "Café.frm"), Vb6TextFile.Encode(
            $"VERSION 5.00\r\nBegin VB.Form Form1\r\n{(form ? "Begin VB.Café Thing\r\nEnd\r\n" : "")}End\r\n", new(1252)));
        var method = typeof(HexIDE.Standalone.Program).GetMethod("RunCheck", BindingFlags.NonPublic | BindingFlags.Static)!;
        using var output = new StringWriter();
        var previous = Console.Out;
        try
        {
            Console.SetOut(output);
            method.Invoke(null, [vbp]).Should().Be(form ? 1 : 0);
            output.ToString().Should().Contain(form ? "VB.Café" : "Project: Café").And.NotContain("�");
        }
        finally { Console.SetOut(previous); }
    }
}
