using HexIDE.IDE;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

public class CodeTextFormatTests
{
    [Theory]
    [InlineData("Attribute VB_Name = \"Module1\"\r\nOption Explicit\r\n")]
    [InlineData("VERSION 1.0 CLASS\r\nBEGIN\r\nEND\r\nAttribute VB_Name = \"Class1\"\r\nAttribute VB_Exposed = False\r\nOption Explicit\r\n")]
    [InlineData("VERSION 5.00\r\nBegin VB.Form Form1\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n")]
    public void VbFormatAnswersTheExistingTextRules(string text)
    {
        IProjectFileFormat format = new Vb6ProjectFileFormat();
        format.AttributeBlock(text).Should().Be(FormCodeText.AttributeBlock(text));
        var code = text[text.IndexOf("Attribute ", StringComparison.Ordinal)..];
        format.AttributeBlock(code).Should().Be(FormCodeText.AttributeBlock(code));
        format.ModuleNameFor("Fallback", text).Should().Be(ModuleFileFormat.ModuleNameFor("Fallback", text));
        format.IsDesignerFile(text).Should().Be(ModuleFileFormat.IsDesignerFile(text));
    }
}
