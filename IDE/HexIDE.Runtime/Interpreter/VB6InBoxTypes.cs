using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// The types VB6's in-box libraries declare that a declaration may name after <c>As</c>: their classes and interfaces,
/// which make an object variable, and stdole's aliases, which mostly stand for a scalar (<c>OLE_COLOR</c> is a Long).
/// Read from <c>Interpreter/Data/vb6-inbox-types.json</c>, which <c>scripts/dump-typelib-types.ps1</c> generates from
/// the real type libraries on the oracle VM.
/// </summary>
/// <remarks>
/// A form's declarations run before <c>Form_Initialize</c> (#822), and real forms declare <c>Private mParent As Form</c>,
/// <c>Dim c As Control</c> or <c>Dim f As StdFont</c>. Each of those is an object slot holding Nothing until it is Set,
/// whatever its class, so knowing the name is a type is enough to allocate it.
/// </remarks>
internal static class VB6InBoxTypes
{
    /// <summary>What a type name declares: an object, or the scalar an alias stands for.</summary>
    internal sealed record InBoxType(string Library, string Name, Vb6Value.ValueType? Scalar)
    {
        public bool IsObject => Scalar is null;
    }

    private static readonly Lazy<IReadOnlyDictionary<string, List<InBoxType>>> types = new(Build, isThreadSafe: true);

    /// <summary>
    /// The in-box type named <paramref name="name"/>, in <paramref name="library"/> when one qualifies it, or else the first
    /// in VB6's reference order, which is the order an unqualified name resolves through.
    /// </summary>
    public static bool TryResolve(string? library, string name, out InBoxType type)
    {
        type = null!;
        if (!types.Value.TryGetValue(name, out var candidates))
            return false;
        if (library is not null)
        {
            type = candidates.FirstOrDefault(t => string.Equals(t.Library, library, StringComparison.OrdinalIgnoreCase))!;
            return type is not null;
        }
        foreach (var lib in VB6InBoxLibraries.LibraryOrder)
            if (candidates.FirstOrDefault(t => string.Equals(t.Library, lib, StringComparison.OrdinalIgnoreCase)) is { } found)
            {
                type = found;
                return true;
            }
        return false;
    }

    // An alias's VARTYPE, as the type library records it, read as the VB6 type a variable of it holds.
    private static Vb6Value.ValueType? ScalarOf(int vt) => vt switch
    {
        2 => Vb6Value.ValueType.Integer,      // VT_I2
        3 or 19 or 22 => Vb6Value.ValueType.Long,   // VT_I4, VT_UI4 (OLE_COLOR), VT_INT (OLE_HANDLE)
        4 => Vb6Value.ValueType.Single,       // VT_R4
        5 => Vb6Value.ValueType.Double,       // VT_R8
        6 => Vb6Value.ValueType.Currency,     // VT_CY
        7 => Vb6Value.ValueType.Date,         // VT_DATE
        8 => Vb6Value.ValueType.String,       // VT_BSTR
        11 => Vb6Value.ValueType.Boolean,     // VT_BOOL
        17 => Vb6Value.ValueType.Byte,        // VT_UI1
        _ => null,
    };

    private static IReadOnlyDictionary<string, List<InBoxType>> Build()
    {
        var asm = typeof(VB6InBoxTypes).Assembly;
        var resource = asm.GetManifestResourceNames().Single(n => n.EndsWith("vb6-inbox-types.json", StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(resource)!;
        using var doc = JsonDocument.Parse(stream);

        var byName = new Dictionary<string, List<InBoxType>>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in doc.RootElement.GetProperty("libraries").EnumerateObject())
            foreach (var t in library.Value.EnumerateArray())
            {
                // A restricted type (stdole's IDispatch) can't be named in VB code.
                if (t.GetProperty("restricted").GetBoolean())
                    continue;
                var kind = t.GetProperty("typekind").GetString();
                InBoxType? entry = kind switch
                {
                    "coclass" or "interface" or "dispatch" => new(library.Name, t.GetProperty("name").GetString()!, null),
                    // VT_USERDEFINED: an alias of a dispinterface, such as IPictureDisp, is an object too.
                    "alias" when t.GetProperty("aliasOf").GetInt32() == 29 => new(library.Name, t.GetProperty("name").GetString()!, null),
                    "alias" when ScalarOf(t.GetProperty("aliasOf").GetInt32()) is { } scalar
                        => new(library.Name, t.GetProperty("name").GetString()!, scalar),
                    // Enums are VB6InBoxLibraries'; records and modules declare no variable.
                    _ => null,
                };
                if (entry is null)
                    continue;
                if (!byName.TryGetValue(entry.Name, out var list))
                    byName[entry.Name] = list = new List<InBoxType>();
                list.Add(entry);
            }
        return byName;
    }
}
