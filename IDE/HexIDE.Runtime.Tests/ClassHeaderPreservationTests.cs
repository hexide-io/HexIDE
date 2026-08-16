using System;
using System.IO;
using System.Linq;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Guards the class-module header against being regenerated from a literal (issue #18).
///
/// <c>StripHeader</c> discarded the whole BEGIN..END block and every attribute; <c>Header</c> then re-emitted
/// a fixed string on save. So one File ▸ Save Project rewrote every .cls in the project — edited or not —
/// resetting VB_Exposed, VB_Creatable, MultiUse, DataBindingBehavior and DataSourceBehavior. Those are how
/// VB6 encodes a class's Instancing: flipping them changes what the class *is* to every consumer.
/// </summary>
public class ClassHeaderPreservationTests
{
    private static readonly string Vb6Classes =
        Path.Join(Environment.GetEnvironmentVariable("VB6_TEMPLATES")
                  ?? @"C:\Program Files (x86)\Microsoft Visual Studio\VB98\Template", "Classes");

    [Theory]
    [InlineData("Complex Data Consumer.cls")]  // DataBindingBehavior = 2 'vbComplexBound, VB_Exposed = True
    [InlineData("Data Source.cls")]            // DataSourceBehavior  = 1 'vbDataSource,   VB_Exposed = True
    public void A_real_vb6_class_round_trips_byte_for_byte(string fileName)
    {
        var path = Path.Join(Vb6Classes, fileName);
        if (!File.Exists(path)) return; // VB6 not installed (CI)

        var original = File.ReadAllText(path);
        var (header, body) = ModuleFileFormat.SplitHeader(original, ModuleKind.ClassModule);
        var name = ReadVbName(original)!;

        ModuleFileFormat.ToFileContent(body, name, ModuleKind.ClassModule, header)
            .Should().Be(original);
    }

    [Theory]
    [InlineData("Complex Data Consumer.cls")]
    [InlineData("Data Source.cls")]
    public void Instancing_and_data_binding_settings_survive(string fileName)
    {
        var path = Path.Join(Vb6Classes, fileName);
        if (!File.Exists(path)) return;

        var original = File.ReadAllText(path);
        var (header, body) = ModuleFileFormat.SplitHeader(original, ModuleKind.ClassModule);
        var rebuilt = ModuleFileFormat.ToFileContent(body, ReadVbName(original)!, ModuleKind.ClassModule, header);

        // Named explicitly rather than relying on the byte compare, so a failure says WHICH semantic moved.
        foreach (var key in new[] { "MultiUse", "Persistable", "DataBindingBehavior", "DataSourceBehavior",
                                    "VB_Creatable", "VB_PredeclaredId", "VB_Exposed", "VB_GlobalNameSpace" })
        {
            LineFor(rebuilt, key).Should().Be(LineFor(original, key), $"{key} must survive a save");
        }
    }

    [Fact]
    public void A_renamed_module_retargets_VB_Name_but_keeps_everything_else()
    {
        var path = Path.Join(Vb6Classes, "Data Source.cls");
        if (!File.Exists(path)) return;

        var original = File.ReadAllText(path);
        var (header, body) = ModuleFileFormat.SplitHeader(original, ModuleKind.ClassModule);

        var rebuilt = ModuleFileFormat.ToFileContent(body, "Renamed", ModuleKind.ClassModule, header);

        LineFor(rebuilt, "VB_Name").Should().Contain("\"Renamed\"");
        LineFor(rebuilt, "DataSourceBehavior").Should().Be(LineFor(original, "DataSourceBehavior"));
        LineFor(rebuilt, "VB_Exposed").Should().Be(LineFor(original, "VB_Exposed"));
    }

    [Fact]
    public void A_module_HexIDE_created_still_gets_the_canonical_header()
    {
        // No original to preserve — the literal is correct here, and must keep working.
        var file = ModuleFileFormat.ToFileContent("Option Explicit\r\n", "Widget", ModuleKind.ClassModule, null);

        file.Should().StartWith("VERSION 1.0 CLASS\r\n");
        file.Should().Contain("Attribute VB_Name = \"Widget\"");
        file.Should().EndWith("Option Explicit\r\n");
    }

    [Fact]
    public void A_preserved_header_keeps_attributes_the_literal_does_not_have()
    {
        // VB_Description is the common casualty: the literal emits five attributes and StripHeader ate
        // every contiguous one, so anything beyond those five was deleted on save.
        var original =
            "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\n"
          + "Attribute VB_Name = \"Widget\"\r\n"
          + "Attribute VB_Description = \"Does a thing\"\r\n"
          + "Attribute VB_Exposed = True\r\n"
          + "Option Explicit\r\n";

        var (header, body) = ModuleFileFormat.SplitHeader(original, ModuleKind.ClassModule);
        var rebuilt = ModuleFileFormat.ToFileContent(body, "Widget", ModuleKind.ClassModule, header);

        rebuilt.Should().Be(original);
        rebuilt.Should().Contain("VB_Description");
    }

    private static string? ReadVbName(string source) =>
        source.Split('\n')
              .Select(l => l.Trim())
              .Where(l => l.StartsWith("Attribute VB_Name", StringComparison.OrdinalIgnoreCase))
              .Select(l => l.Substring(l.IndexOf('"') + 1).TrimEnd('"', '\r'))
              .FirstOrDefault();

    private static string LineFor(string content, string key) =>
        content.Split('\n')
               .Select(l => l.TrimEnd('\r'))
               .FirstOrDefault(l => l.TrimStart().StartsWith(key, StringComparison.OrdinalIgnoreCase)
                                 || l.TrimStart().StartsWith("Attribute " + key, StringComparison.OrdinalIgnoreCase))
        ?? $"<{key} absent>";
}
