using System.Text;

namespace HexIDE.Runtime.BuiltinTypes;

/// <summary>A caption's shown text and access key, shared by the provider and the IDE (#5464).</summary>
public static class VBCaptionText
{
    public static (string Shown, int? AccessKey) Parse(string caption, bool firstKey = false)
    {
        if (!caption.Contains('&'))
            return (caption, null);

        var shown = new StringBuilder(caption.Length);
        int? key = null;
        for (var i = 0; i < caption.Length; i++)
        {
            if (caption[i] != '&')
            {
                shown.Append(caption[i]);
            }
            else if (i + 1 < caption.Length && caption[i + 1] == '&')
            {
                shown.Append('&');
                i++;
            }
            else if (i + 1 < caption.Length && (!firstKey || key is null))
            {
                // The marked character is appended on the next pass.
                key = shown.Length;
            }
            // A trailing &, with nothing to mark, is not shown. A Label draws an underline where its character would be,
            // which a button and a menu do not; that is not reproduced.
        }
        return (shown.ToString(), key);
    }

    public static string Shown(string caption) => Parse(caption).Shown;
}
