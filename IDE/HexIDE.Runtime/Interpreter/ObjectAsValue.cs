using Antlr4.Runtime;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// A class instance used where a value is wanted. VB6 reads its default member, the member carrying
/// <c>Attribute X.VB_UserMemId = 0</c>, and raises 438 for an object that has none.
/// </summary>
/// <remarks>
/// <para>Measured on vb6.exe, with <c>w</c> a class whose <c>Value</c> is its default member and returns 42, and <c>k</c> a
/// class with none. Each of these sees 42 for <c>w</c> and raises 438 for <c>k</c>:</para>
/// <list type="bullet">
/// <item>a Let into a Long, String, Double, Boolean or Variant;</item>
/// <item>arithmetic, negation, a comparison, concatenation;</item>
/// <item><c>CStr</c>, <c>CDbl</c>, <c>CBool</c>, <c>Val</c>;</item>
/// <item>a ByVal Long or String argument;</item>
/// <item><c>If w Then</c>, <c>Select Case w</c>, a For bound, and <c>Print #</c>.</item>
/// </list>
/// <para>A ByVal Variant argument takes the object itself. HexIDE does not call a class's default member yet (#174), so
/// it refuses untrappably: a trappable error would let <c>On Error Resume Next</c> carry on without the value VB6
/// gives. An object with no default member raises VB6's 438, which a program may trap.</para>
/// <para>Controls stand for their default property too, and are #790's. <c>Nothing</c> is left to each caller.</para>
/// </remarks>
internal static class ObjectAsValue
{
    public static void Refuse(Vb6Value value, ParserRuleContext? context)
    {
        if (value.Value is not VbObject obj)
            return;
        if (obj.ClassDef.PrePass.DeclaresDefaultMember)
            throw new NotImplementedException(
                $"Using a {obj.ClassDef.Name} as a value reads its default member, which is not implemented for a "
                + "class yet (#174).");
        throw new VBRunTimeException(context, VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
    }
}
