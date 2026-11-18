using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

// Container is absent: VB6 SP6, native and p-code, #4516 (oracle PR #4508).
internal static class CommonDialogMembers
{
    internal static void Check(Vb6Value reference, string member)
    {
        if (reference.Value is not VBCommonDialog || !member.Equals("Container", StringComparison.OrdinalIgnoreCase))
            return;
        if (reference.HeldAsVariant)
            throw new NotImplementedException("A Variant-held CommonDialog's Container is not measured (#4148)");
        if (string.Equals(reference.DeclaredAs, "Object", StringComparison.OrdinalIgnoreCase))
            throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
        throw new VBMethodOrDataMemberNotFoundException(member, reference.Type);
    }
}
