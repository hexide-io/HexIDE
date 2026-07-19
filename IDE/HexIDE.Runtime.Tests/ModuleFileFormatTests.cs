using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

public class ModuleFileFormatTests
{
    [Fact]
    public void Class_ToFileContent_HasVb6ClassHeader()
    {
        var file = ModuleFileFormat.ToFileContent("Option Explicit\r\n", "Widget", ModuleKind.ClassModule);
        file.Should().StartWith("VERSION 1.0 CLASS\r\nBEGIN");
        file.Should().Contain("Attribute VB_Name = \"Widget\"");
        file.Should().EndWith("Option Explicit\r\n");
    }

    [Fact]
    public void Standard_ToFileContent_HasAttributeHeader()
    {
        var file = ModuleFileFormat.ToFileContent("Public X As Long\r\n", "Mod1", ModuleKind.StandardModule);
        file.Should().Be("Attribute VB_Name = \"Mod1\"\r\nPublic X As Long\r\n");
    }

    [Theory]
    [InlineData(ModuleKind.ClassModule)]
    [InlineData(ModuleKind.StandardModule)]
    public void StripHeader_IsInverseOf_ToFileContent(ModuleKind kind)
    {
        const string body = "Option Explicit\r\n\r\nPublic Sub Go()\r\nEnd Sub";
        var file = ModuleFileFormat.ToFileContent(body, "Thing", kind);
        ModuleFileFormat.StripHeader(file, kind).Should().Be(body);
    }

    [Fact]
    public void StripHeader_RealVb6ClassFile_ReturnsBody()
    {
        var file =
            "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\n" +
            "Attribute VB_Name = \"clsFoo\"\r\nAttribute VB_GlobalNameSpace = False\r\n" +
            "Attribute VB_Creatable = True\r\nAttribute VB_PredeclaredId = False\r\nAttribute VB_Exposed = False\r\n" +
            "Option Explicit\r\nPrivate x As Long";
        ModuleFileFormat.StripHeader(file, ModuleKind.ClassModule)
            .Should().Be("Option Explicit\r\nPrivate x As Long");
    }

    [Fact]
    public void StripHeader_HeaderlessBody_IsUnchanged()
    {
        const string body = "Option Explicit\r\nPublic Sub Go()\r\nEnd Sub";
        ModuleFileFormat.StripHeader(body, ModuleKind.ClassModule).Should().Be(body);
        ModuleFileFormat.StripHeader(body, ModuleKind.StandardModule).Should().Be(body);
    }

    [Fact]
    public void HandlesHeader_TrueForBasAndCls_FalseForFormParts()
    {
        ModuleFileFormat.HandlesHeader(ModuleKind.StandardModule).Should().BeTrue();
        ModuleFileFormat.HandlesHeader(ModuleKind.ClassModule).Should().BeTrue();
        ModuleFileFormat.HandlesHeader(ModuleKind.UserControl).Should().BeFalse();
        ModuleFileFormat.HandlesHeader(ModuleKind.PropertyPage).Should().BeFalse();
    }
}
