using Avalonia.Headless.XUnit;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Integration.Tests.Views;

public partial class UserControlKeyTests
{
    // vb6.exe SP6 F5, 2026-11-23: https://github.com/hexide-io/HexIDE/issues/4741.
    [AvaloniaTheory]
    [InlineData("5", "Integer", "5")]
    [InlineData("-32768", "Integer", "-32768")]
    [InlineData("32768", "Long", "32768")]
    [InlineData("-32769", "Long", "-32769")]
    [InlineData("-2147483648", "Long", "-2147483648")]
    [InlineData("2147483648", "Double", "2147483648")]
    [InlineData("999999999999999", "Double", "999999999999999")]
    [InlineData("-999999999999999", "Double", "-999999999999999")]
    [InlineData("1000000000000000", "Decimal", "1000000000000000")]
    [InlineData("-1000000000000000", "Decimal", "-1000000000000000")]
    [InlineData("79228162514264337593543950335", "Decimal", "79228162514264337593543950335")]
    [InlineData("79228162514264337593543950336", "Double", "7.922816251426434E+28")]
    [InlineData("9999999.", "Single", "9999999")]
    [InlineData("8388609.", "Single", "8388609")]
    [InlineData("1.5E+3", "Single", "1500")]
    [InlineData("1e5", "Single", "100000")]
    [InlineData("10000000.", "Double", "10000000")]
    [InlineData("16777216.", "Double", "16777216")]
    [InlineData("1E+14", "Double", "100000000000000")]
    [InlineData("1E+15", "Decimal", "1000000000000000")]
    [InlineData("1E+28", "Decimal", "10000000000000000000000000000")]
    [InlineData("0.0001", "Currency", "0.0001")]
    [InlineData("10000000.5", "Currency", "10000000.5")]
    [InlineData("0.00015", "Decimal", "0.00015")]
    [InlineData("37623.1702083333", "Decimal", "37623.1702083333")]
    [InlineData("&HFFFF", "Long", "65535")]
    [InlineData("&HFFFFFFFF", "Integer", "-1")]
    [InlineData("&H8000000F&", "Long", "-2147483633")]
    [InlineData("&HFF&", "Integer", "255")]
    [InlineData("&O177777", "Long", "65535")]
    [InlineData("-0", "Integer", "0")]
    [InlineData("1.0", "Single", "1")]
    [InlineData("1.00000", "Single", "1")]
    [InlineData("1.5E1", "Single", "15")]
    [InlineData("1D+3", "Single", "1000")]
    [InlineData("1E+300", "Double", "1E+300")]
    [InlineData("1.50", "Currency", "1.5")]
    [InlineData("-1.5E-3", "Currency", "-0.0015")]
    [InlineData("922337203685477.5807", "Currency", "922337203685477.5807")]
    [InlineData("1.23456", "Decimal", "1.23456")]
    [InlineData("1.5E-4", "Decimal", "0.00015")]
    [InlineData("922337203685478.5", "Decimal", "922337203685478.5")]
    [InlineData("1E-30", "Double", "1E-30")]
    [InlineData("0.0", "Currency", "0")]
    [InlineData("0.00", "Currency", "0")]
    [InlineData("-0.0", "Currency", "0")]
    [InlineData("0.", "Single", "0")]
    [InlineData("-1  'True", "Integer", "-1")]
    [InlineData("5 'comment", "Integer", "5")]
    public void UserControl_bag_reads_saved_numeric_types(string text, string type, string value)
    {
        using var run = BagRun("N1 = " + text, "v = PropBag.ReadProperty(\"N1\")\nDebug.Print TypeName(v)\nDebug.Print v");
        run.Faults.Should().BeEmpty();
        run.RunTimeErrors.Should().BeEmpty();
        run.Lib.Printed[0].Should().Be(new Vb6Value(type));
        // Compare numbers, avoiding a claim about VB6's display precision.
        var actual = run.Lib.Printed[1];
        if (type == "Double")
            System.Convert.ToDouble(actual.Value, System.Globalization.CultureInfo.InvariantCulture).Should()
                .Be(double.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
        else
            System.Convert.ToDecimal(actual.Value, System.Globalization.CultureInfo.InvariantCulture).Should()
                .Be(decimal.Parse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture));
    }

    [AvaloniaTheory]
    [InlineData("+5")]
    [InlineData("1#")]
    [InlineData("1!")]
    [InlineData("1@")]
    [InlineData("True")]
    [InlineData("abc")]
    [InlineData("1,5")]
    [InlineData("&H100000000")]
    [InlineData("-&H10")]
    [InlineData("")]
    [InlineData("\"abc")]
    public void UserControl_bag_omits_refused_text(string text)
    {
        using var run = BagRun("N1 = " + text, "On Error Resume Next\nDebug.Print PropBag.ReadProperty(\"N1\")\nDebug.Print Err.Number");
        run.Faults.Should().BeEmpty();
        run.Lib.Printed.Should().Equal(new Vb6Value(327L));
    }

    [AvaloniaTheory]
    [InlineData("Height")]
    [InlineData("Left")]
    [InlineData("Top")]
    [InlineData("Width")]
    [InlineData("TabIndex")]
    [InlineData("Index")]
    [InlineData("Tag")]
    [InlineData("Visible")]
    [InlineData("ToolTipText")]
    [InlineData("TabStop")]
    [InlineData("CausesValidation")]
    [InlineData("DragMode")]
    [InlineData("HelpContextID")]
    [InlineData("WhatsThisHelpID")]
    [InlineData("Name")]
    public void UserControl_bag_excludes_extender_names(string name)
    {
        using var run = BagRun(name + " = 0", $"On Error Resume Next\nDebug.Print PropBag.ReadProperty(\"{name}\")\nDebug.Print Err.Number");
        run.Faults.Should().BeEmpty();
        run.Lib.Printed.Should().Equal(new Vb6Value(327L));
    }

    [AvaloniaTheory]
    [InlineData("\"a\" \"b\"", "a")]
    [InlineData("\"x\" 'comment", "x")]
    [InlineData("\" a \"", " a ")]
    [InlineData("\"a\"\"b\"", "a\"b")]
    public void UserControl_bag_reads_quoted_text(string text, string expected)
    {
        using var run = BagRun("S1 = " + text, "v = PropBag.ReadProperty(\"S1\")\nDebug.Print TypeName(v)\nDebug.Print v");
        run.Faults.Should().BeEmpty();
        run.Lib.Printed.Should().Equal(new Vb6Value("String"), new Vb6Value(expected));
    }

    [AvaloniaTheory]
    [InlineData("N1 = 5\nN1 = 9", "PropBag.ReadProperty(\"n1\")", "Integer", "5")]
    [InlineData("N1 = 5", "PropBag.ReadProperty(\"N1\", \"9\")", "Integer", "5")]
    [InlineData("N1 = 5", "PropBag.ReadProperty(\"N1\", 9.5)", "Integer", "5")]
    [InlineData("S4 = \"12\"", "PropBag.ReadProperty(\"S4\", 0)", "String", "12")]
    [InlineData("B1 = -1", "PropBag.ReadProperty(\"B1\", False)", "Integer", "-1")]
    [InlineData("", "PropBag.ReadProperty(\"U1\", 7)", "Integer", "7")]
    [InlineData("", "PropBag.ReadProperty(\"U1\", \"x\")", "String", "x")]
    [InlineData("", "PropBag.ReadProperty(\"U1\", Empty)", "Empty", "")]
    [InlineData("_ExtentX = 2646", "PropBag.ReadProperty(\"_ExtentX\")", "Integer", "2646")]
    [InlineData("_ExtentY = 2646", "PropBag.ReadProperty(\"_ExtentY\")", "Integer", "2646")]
    public void UserControl_bag_reads_names_and_defaults(string lines, string expression, string type, string value)
    {
        using var run = BagRun(lines, $"v = {expression}\nDebug.Print TypeName(v)\nDebug.Print CStr(v)");
        run.Faults.Should().BeEmpty();
        run.Lib.Printed.Should().Equal(new Vb6Value(type), new Vb6Value(value));
    }

    [AvaloniaTheory]
    [InlineData("Debug.Print PropBag.ReadProperty(\"U1\")", 327, "Data value named 'U1' not found")]
    [InlineData("PropBag.WriteProperty \"N1\", 9", 425, "Invalid object use")]
    [InlineData("v = PropBag.Contents", 70, "Permission denied")]
    [InlineData("PropBag.Contents = Empty", 70, "Permission denied")]
    public void UserControl_bag_reports_measured_errors(string statement, int number, string description)
    {
        using var run = BagRun("N1 = 5", $"On Error Resume Next\n{statement}\nDebug.Print Err.Number\nDebug.Print Err.Description\nDebug.Print PropBag.ReadProperty(\"N1\")");
        run.Faults.Should().BeEmpty();
        run.Lib.Printed.Should().Equal(new Vb6Value((long)number), new Vb6Value(description), new Vb6Value(5));
    }

    [AvaloniaFact]
    public void UserControl_bag_has_PropertyBag_typename()
    {
        using var run = BagRun("", "Debug.Print TypeName(PropBag)");
        run.Lib.Printed.Should().Equal(new Vb6Value("PropertyBag"));
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task UserControl_bag_loaded_element_reads_template_lines()
    {
        using var run = BagRun("Index = 0\nN1 = 5", "Debug.Print PropBag.ReadProperty(\"N1\")");
        await run.Code("Load uc1(1)");
        run.Faults.Should().BeEmpty();
        run.Lib.Printed.Should().Equal(new Vb6Value(5), new Vb6Value(5));
    }

    // vb6.exe reads this block as Arial 12 Bold Italic (measured for #4741). The standalone stream table can't
    // realize it, so it stays refused until #5533 builds the Font from the block's fields.
    [AvaloniaFact]
    public void UserControl_bag_refuses_a_saved_Font_the_stream_table_cannot_realize()
    {
        using var run = BagRun("BeginProperty Font {0BE35203-8F91-11CE-9DE3-00AA004BB851}\nName = \"Arial\"\nSize = 12\nCharset = 0\nWeight = 700\nUnderline = 0 'False\nItalic = -1 'True\nStrikethrough = 0 'False\nEndProperty",
            "Set v = PropBag.ReadProperty(\"FONT\")");
        run.Faults.Should().ContainSingle().Which.Should().BeOfType<System.NotImplementedException>()
            .Which.Message.Should().Contain("#5533");
    }

    [AvaloniaTheory]
    [InlineData("N1 = \"Form1.frx\":0000")]
    [InlineData("BeginProperty Other\nName = \"Arial\"\nEndProperty")]
    [InlineData("BeginProperty Font\nName = \"Arial\"\nEndProperty")]
    public void UserControl_bag_refuses_unmeasured_saved_shapes(string lines)
    {
        var name = lines.StartsWith("BeginProperty Font", System.StringComparison.Ordinal) ? "Font"
            : lines.StartsWith("BeginProperty Other", System.StringComparison.Ordinal) ? "Other" : "N1";
        using var run = BagRun(lines, $"Debug.Print PropBag.ReadProperty(\"{name}\")");
        run.Faults.Should().ContainSingle().Which.Should().BeOfType<System.NotImplementedException>()
            .Which.Message.Should().Contain("#4741");
    }

    [AvaloniaTheory]
    [InlineData("v = PropBag.ReadProperty()")]
    [InlineData("v = PropBag.ReadProperty(\"N1\", 1, 2)")]
    [InlineData("v = PropBag.ReadProperty(5)")]
    [InlineData("PropBag.WriteProperty \"N1\"")]
    [InlineData("PropBag.WriteProperty 5, 1")]
    public void UserControl_bag_refuses_unmeasured_calls(string statement)
    {
        using var run = BagRun("N1 = 5", statement);
        run.Faults.Should().ContainSingle().Which.Should().BeOfType<System.NotImplementedException>()
            .Which.Message.Should().Contain("#4741");
    }

    [AvaloniaTheory]
    [InlineData("PropBag.Foo")]
    [InlineData("v = PropBag.Foo")]
    [InlineData("PropBag.Foo = 1")]
    public void UserControl_bag_unknown_member_is_a_compile_error(string statement)
    {
        using var run = BagRun("", statement);
        run.CompileErrors.Should().ContainSingle().Which.Message.Should().Contain("Method or data member not found");
    }

    // Regression guard: designer-data leniency must not reach executable code (passes on main).
    [AvaloniaFact]
    public void UserControl_bag_does_not_hide_an_unterminated_string_in_code()
    {
        var act = () => BagRun("N1 = 5", "Debug.Print \"abc");
        act.Should().Throw<VBCompileErrorException>().WithMessage("*Syntax error*");
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task UserControl_bag_does_not_raise_WriteProperties_on_unload()
    {
        using var run = new Run("", blocks: Block("P.Dial", "uc1", 0, extra: "N1 = 5"), controlCode:
            "Private Sub UserControl_ReadProperties(PropBag As PropertyBag)\nDebug.Print PropBag.ReadProperty(\"N1\")\nEnd Sub\n"
            + "Private Sub UserControl_WriteProperties(PropBag As PropertyBag)\nDebug.Print \"write\"\nEnd Sub\n");
        await run.Context.UnloadAsync(1);
        run.Lib.Printed.Should().Equal(new Vb6Value(5));
    }

    // A saved file indents the line after an unterminated string, here the block's End (#4741's review).
    [AvaloniaFact]
    public void UserControl_bag_omits_an_unterminated_string_before_an_indented_End()
    {
        using var run = new Run("", blocks: "Begin P.Dial uc1\n   Height = 375\n   Width = 1200\n   Top = 0\n   Left = 120\n"
            + "   TabIndex = 0\n   N1 = \"abc\n   End\n",
            controlCode: "Private Sub UserControl_ReadProperties(PropBag As PropertyBag)\nDebug.Print PropBag.ReadProperty(\"N1\", \"omitted\")\nEnd Sub\n");
        run.Faults.Should().BeEmpty();
        run.Lib.Printed.Should().Equal(new Vb6Value("omitted"));
    }

    private static Run BagRun(string lines, string code) => new("", blocks: Block("P.Dial", "uc1", 0, extra: lines),
        controlCode: "Private Sub UserControl_ReadProperties(PropBag As PropertyBag)\nDim v As Variant\n" + code + "\nEnd Sub\n",
        frxBlobs: new System.Collections.Generic.Dictionary<int, byte[]> { [0] = [0] });
}
