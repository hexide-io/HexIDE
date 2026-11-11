using System;
using System.Collections.Generic;
using HexIDE.Language;

namespace HexIDE.Runtime.Serialization;

/// <summary>VB6's own writes to read-only regions.</summary>
public sealed class Vb6ReadOnlyRegionWriter : IReadOnlyRegionWriter
{
    public IReadOnlyList<TextChange> SaveRewrites(string text, int prefixLength) =>
        MemberAttributeQualifiers.Rewrites(text, prefixLength);

    public TextChange? NameRewrite(string text, int prefixLength, string name)
    {
        var code = text[prefixLength..];
        if (FormCodeText.VbNameLine(code) is not var (start, length)
            || ReferenceEquals(FormCodeText.RetargetVbName(code, name), code))
            return null;
        return new TextChange(prefixLength + start, length, FormCodeText.VbNameAttribute(name));
    }
}
