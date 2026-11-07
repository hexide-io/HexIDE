using System;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// What Print writes, for <c>Print #</c> (#1145) and <c>Debug.Print</c> (#1380), which vb6.exe formats the same way, line
/// for line (the oracle's "Sequential files"):
/// <list type="bullet">
/// <item>a number has a space before it where its sign would go and one after, and a date one after; a string, a Boolean
/// and Null, as "Null", have none, and Empty is nothing;</item>
/// <item>a semicolon joins two items, and a comma moves to the next 14-column zone;</item>
/// <item><c>Tab(n)</c> moves to column n, on the next line when the line is already past it, and a bare <c>Tab</c> is a
/// comma; <c>Spc(n)</c> writes n spaces;</item>
/// <item>the line ends with CR LF unless the statement ends in a semicolon or a comma.</item>
/// </list>
/// </summary>
internal static class VbPrint
{
    private const int Zone = 14;

    /// <summary>Where printed text goes, and its column: the characters written since the last line break.</summary>
    internal interface ITarget
    {
        int Column { get; }
        int Width => 0;
        void Write(string text);
    }

    // Error display is accepted by printing and MsgBox, but not by implicit string coercion (#2520, #2613).
    internal static string ErrorText(Vb6Value value) =>
        "Error " + ((ushort)value.Value!).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static string ItemText(Vb6Value value) => value.Value switch
    {
        _ when value.Type == Vb6Value.ValueType.Error => ErrorText(value),
        _ when value.IsNull => "Null",
        null => "",
        string s => s,
        bool b => b ? "True" : "False",
        VbDate => VbDisplay.ToText(value) + " ",
        byte or short or int or long or float or double or decimal =>
            (Convert.ToDouble(value.Value, System.Globalization.CultureInfo.InvariantCulture) < 0 ? "" : " ")
            + VbDisplay.ToText(value) + " ",
        _ => VbDisplay.ToText(value),
    };

    public static void Item(ITarget target, Vb6Value value)
    {
        var text = ItemText(value);
        // Width wraps before the item, never inside it (vb6.exe, #2554).
        if (target.Width > 0 && target.Column > 0 && target.Column + text.Length > target.Width)
            EndLine(target);
        target.Write(text);
    }

    public static void NextZone(ITarget target)
    {
        var column = target.Column;
        var next = (column / Zone + 1) * Zone;
        if (target.Width > 0 && next > target.Width)
            EndLine(target);
        else
            target.Write(new string(' ', next - column));
    }

    /// <summary><c>Tab(column)</c>, counted from 1; one below 1 is 1, as <c>Tab(0)</c> measured.</summary>
    public static void Tab(ITarget target, int column)
    {
        column = Math.Max(1, column);
        if (target.Column + 1 > column)
        {
            target.Write("\r\n");
            target.Write(new string(' ', column - 1));
        }
        else
            target.Write(new string(' ', column - 1 - target.Column));
    }

    /// <summary><c>Spc(count)</c>. A negative count is unmeasured, and writes nothing.</summary>
    public static void Spc(ITarget target, int count) => target.Write(new string(' ', Math.Max(0, count)));

    public static void EndLine(ITarget target) => target.Write("\r\n");

    /// <summary>The column after <paramref name="text"/> is written at <paramref name="column"/>: it restarts at a line
    /// break inside the text (measured for <c>Print #</c>).</summary>
    public static int ColumnAfter(int column, string text)
    {
        var lineBreak = text.LastIndexOf('\n');
        return lineBreak < 0 ? column + text.Length : text.Length - lineBreak - 1;
    }
}
