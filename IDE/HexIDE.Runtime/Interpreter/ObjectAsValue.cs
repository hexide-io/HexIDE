using Antlr4.Runtime;
using HexIDE.Runtime.BuiltinControls;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// An object used where a value is wanted. VB6 reads its default member, the member carrying
/// <c>Attribute X.VB_UserMemId = 0</c>, and raises 438 for an object that has none.
/// </summary>
/// <remarks>
/// <para>Measured on vb6.exe, native and in the IDE, with <c>w</c> a class whose <c>Value</c> is its default member and
/// returns 42, and <c>k</c> a class with none. Each of these sees 42 for <c>w</c> and raises 438 for <c>k</c>:</para>
/// <list type="bullet">
/// <item>a Let into a Long, String, Double, Boolean or Variant;</item>
/// <item>arithmetic, negation, a comparison, concatenation;</item>
/// <item>the arguments of the built-in functions: the conversions, <c>Val</c>, <c>Left</c>, <c>InStr</c>, <c>Chr</c>,
/// <c>Len</c>, <c>UCase</c>, <c>Split</c>, a <c>UBound</c> dimension, a <c>Join</c> delimiter, a <c>Filter</c> switch;</item>
/// <item>a ByVal Long or String argument;</item>
/// <item><c>If w Then</c>, <c>Select Case w</c>, a Case value or range, a For bound;</item>
/// <item><c>Print #</c>, <c>Randomize</c>, <c>Err.Raise</c>, <c>Error</c>, <c>ChDir</c>, <c>ChDrive</c>, and the Mid
/// statement's start and length.</item>
/// </list>
/// <para>One exception, measured: where the default member is a <b>Function</b>, <c>Format</c>, <c>Abs</c> and
/// <c>Not</c> of a Variant holding it raise 450, and so does a Case value compared with an Empty selector in the IDE. A
/// Property Get default member gives 42 on those uses. The rows say which uses give 450, not why.</para>
/// <para>A ByVal Variant argument takes the object itself. HexIDE does not call a class's default member yet (#174), so
/// it refuses, untrappably: a trappable error would let <c>On Error Resume Next</c> carry on without the value VB6
/// gives. An object with no default member raises VB6's 438, which a program may trap.</para>
/// <para>The in-box objects, measured the same way: Err stands for its Number; App, Screen and Clipboard have no default
/// member, so 438; <c>Debug</c> used as a value and a control array without an index are compile errors (read from
/// <c>/make</c>, since a project that does not compile leaves no log in an IDE run).</para>
/// <para>Controls stand for their default property too, and are #790's. <c>Nothing</c> is left to each caller.</para>
/// </remarks>
internal static class ObjectAsValue
{
    /// <summary>The value <paramref name="value"/> stands for where a value is wanted. Anything not an object comes back
    /// as it is.</summary>
    public static Vb6Value Scalar(Vb6Value value, ParserRuleContext? context) => value.Value switch
    {
        VbObject obj when obj.ClassDef.PrePass.DeclaresDefaultMember => throw DefaultMemberRefused(obj),
        VbObject => throw new VBRunTimeException(context, VBStandardError.ObjectDoesntSupportThisPropertyOrMethod),
        VBUpDown upDown => new Vb6Value((long)upDown.Value),
        Components.InBox.TabStripTab tab => new Vb6Value(tab.Tab.Caption),
        VBTabStrip => throw new VBRunTimeException(context, VBStandardError.ObjectDoesntSupportThisPropertyOrMethod),
        VBCommonDialog => throw new VBRunTimeException(new VBStandardError(394, "Property is write-only")),
        ToolbarItem item => item.DefaultValue,
        Components.InBox.TreeNode node => new Vb6Value(node.Text),
        VBMaskEdBox box => new Vb6Value(box.Model.Text),
        VBSSTab => new Vb6Value((short)0),
        VbErr err => new Vb6Value(err.Number),
        VBRichTextBox box => new Vb6Value(box.WriteRtf()),
        StatusBarPanel panel => new Vb6Value(panel.Text),
        // VB6 refuses both before the program runs: Debug is only ever Debug.Print or Debug.Assert ("Syntax error"),
        // and a control array needs an index ("Argument not optional"). Raised here as the statement runs.
        DebugProxy => throw new VBCompileErrorException("Syntax error"),
        ControlArrayGroup => throw new VBCompileErrorException("Argument not optional"),
        // A Collection's default member is Item, which needs an index: `v = c` is 450 (measured, #1471, early-bound and
        // late). In an expression VB6 refuses it before the program runs, as "Argument not optional", where it is early-bound.
        VbCollection => throw new VBRunTimeException(context, VBStandardError.WrongNumberOfArgumentsOrInvalidPropertyAssignment),
        // So is a form's Controls: `v = Controls` is 450, and v stays Empty (measured, #1060).
        FormControls => throw new VBRunTimeException(context, VBStandardError.WrongNumberOfArgumentsOrInvalidPropertyAssignment),
        // And Forms: `v = Forms` is 450, and v stays Empty (measured, #2496).
        VbForms => throw new VBRunTimeException(context, VBStandardError.WrongNumberOfArgumentsOrInvalidPropertyAssignment),
        ICSharpProxy => throw new VBRunTimeException(context, VBStandardError.ObjectDoesntSupportThisPropertyOrMethod),
        _ => value,
    };

    /// <summary>
    /// For <c>VarType</c> and the Is functions, which read an object's default member when it has one, and otherwise
    /// answer about the object itself, with no 438 (measured: <c>VarType(w)</c> is 3 and <c>IsEmpty</c> of a class whose
    /// member returns Empty is True, while <c>VarType(k)</c> is 9). <c>TypeName</c> and <c>IsObject</c> never read it.
    /// </summary>
    public static Vb6Value DefaultMemberIfAny(Vb6Value value) => value.Value switch
    {
        VbObject obj when obj.ClassDef.PrePass.DeclaresDefaultMember => throw DefaultMemberRefused(obj),
        VBUpDown upDown => new Vb6Value((long)upDown.Value),
        ToolbarItem item => item.DefaultValue,
        Components.InBox.TreeNode node => new Vb6Value(node.Text),
        VBMaskEdBox box => new Vb6Value(box.Model.Text),
        VBSSTab => new Vb6Value((short)0),
        VbErr err => new Vb6Value(err.Number),
        VBRichTextBox box => new Vb6Value(box.WriteRtf()),
        StatusBarPanel panel => new Vb6Value(panel.Text),
        _ => value,
    };

    private static NotImplementedException DefaultMemberRefused(VbObject obj) =>
        new($"Using a {obj.ClassDef.Name} as a value reads its default member, which is not implemented for a class yet (#174). Name the member instead.");
}
