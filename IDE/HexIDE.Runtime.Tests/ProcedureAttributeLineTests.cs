using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// An <c>Attribute</c> line inside a procedure is metadata, and the procedure runs as if it were not there (#832).
/// Calling one stopped the run with "Attribute not implemented". Every expectation was measured on vb6.exe, native and
/// in the IDE; the probes are in docs/vb6-fidelity-oracle/attribute-lines-inside-a-procedure.md, "Attribute lines inside a procedure".
/// </summary>
public class ProcedureAttributeLineTests : BaseVBTestFixture
{
    private const string Widget = """
        Private mCaption As String
        Public Tag As String
        Attribute Tag.VB_VarDescription = "A tag"
        Attribute Tag.VB_VarHelpID = 5
        Public Flagged As Long
        Attribute Flagged.VB_VarMemberFlags = "40"
        Public Function Value() As Long
        Attribute Value.VB_UserMemId = 0
            Value = 42
        End Function
        Public Property Get Caption() As String
        Attribute Caption.VB_Description = "The caption"
        Attribute Caption.VB_ProcData.VB_Invoke_Property = ";Appearance"
            Caption = "cap:" & mCaption
        End Property
        Public Property Let Caption(ByVal v As String)
        Attribute Caption.VB_Description = "The caption"
            mCaption = v
        End Property
        Public Function Note() As String
        Attribute Note.VB_Description = "Has a description"
        Attribute Note.VB_HelpID = 12
        Attribute Note.VB_MemberFlags = "40"
            Note = "noted"
        End Function

        """;

    [Fact]
    public async Task A_class_member_carrying_attribute_lines_runs_as_VB6_runs_it()
    {
        await RunClasses("""
            Dim w As Widget
            Set w = New Widget
            Debug.Print w.Value
            Debug.Print w.Caption
            w.Caption = "set"
            Debug.Print w.Caption
            Debug.Print w.Note
            w.Tag = "tagged"
            Debug.Print w.Tag
            w.Flagged = 5
            Debug.Print w.Flagged

            """.Replace("\r\n", "\n"), ("Widget", Widget.Replace("\r\n", "\n")));
        AssertDebugLog([new Vb6Value(42L), new Vb6Value("cap:"), new Vb6Value("cap:set"), new Vb6Value("noted"),
            new Vb6Value("tagged"), new Vb6Value(5L)]);
    }

    [Fact]
    public async Task A_module_variable_with_a_description_holds_its_value()
    {
        // VB_VarDescription follows the variable it describes, among the module's declarations.
        await Run("""
            Public Counter As Long
            Attribute Counter.VB_VarDescription = "How many"
            Counter = 3
            Debug.Print Counter

            """.Replace("\r\n", "\n"));
        AssertDebugLog([new Vb6Value(3L)]);
    }

    [Fact]
    public async Task A_module_function_with_a_description_runs()
    {
        await Run("""
            Debug.Print Described()
            Function Described() As Long
            Attribute Described.VB_Description = "A module function with a description"
                Described = 7
            End Function

            """.Replace("\r\n", "\n"));
        AssertDebugLog([new Vb6Value(7L)]);
    }
}
