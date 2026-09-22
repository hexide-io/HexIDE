namespace HexIDE.Desktop.Server;

/// <summary>
/// Declares that a tool's <c>[Description]</c> names every member of <paramref name="enumType"/>, because the
/// tool's reply renders that enum as a string and the description is the only place a caller can learn
/// what the strings mean. <c>ToolDescriptionEnumTests</c> fails the build when a member is missing.
/// </summary>
/// <remarks>
/// Opt-in rather than inferred, so ordinary prose in a description never trips the check (hexide-io/HexIDE#400).
/// The guard exists because this drifted: <c>list_lsp_messages</c> named seven of nine entry kinds for
/// weeks, and the two it missed were the ones no code path yet produced, which is exactly where nobody
/// looks. Put this on any tool whose reply carries an enum's <c>ToString()</c>.
/// </remarks>
/// <param name="enumType">The enum the reply renders.</param>
/// <param name="notRendered">Members the reply never shows (for example one rendered as null), which the
/// description need not name.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class DescribesEnumAttribute(Type enumType, params string[] notRendered) : Attribute
{
    public Type EnumType { get; } = enumType;
    public string[] NotRendered { get; } = notRendered;
}
