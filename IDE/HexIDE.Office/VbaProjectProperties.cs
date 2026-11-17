using System.Buffers.Binary;

namespace HexIDE.Office;

/// <summary>PROJECT text properties (MS-OVBA 2.3.1). Protection fields remain opaque except GC.</summary>
public sealed record VbaProjectProperties(IReadOnlyDictionary<string, VbaModuleKind> ModuleKinds,
    string? EncryptedProtectionState, string? EncryptedPassword, string? EncryptedVisibilityState, bool IsLocked)
{
    public static VbaProjectProperties Read(string text)
    {
        var kinds = new Dictionary<string, VbaModuleKind>(StringComparer.OrdinalIgnoreCase);
        string? protection = null, password = null, visibility = null;
        foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.StartsWith('['))
                break;
            var equals = line.IndexOf('=');
            if (equals < 0)
                continue;
            var key = line[..equals];
            var value = line[(equals + 1)..];
            var kind = key switch
            {
                "Module" => VbaModuleKind.Standard,
                "Class" => VbaModuleKind.Class,
                "Document" => VbaModuleKind.Document,
                "BaseClass" => VbaModuleKind.UserForm,
                _ => (VbaModuleKind?)null
            };
            if (kind != null)
            {
                var name = kind == VbaModuleKind.Document ? value.Split('/')[0] : value;
                if (!kinds.TryAdd(name, kind.Value))
                    throw new OfficeFileException("Duplicate VBA PROJECT module declaration.");
            }
            else if (key is "CMG" or "DPB" or "GC")
            {
                if (value.Length < 2 || value[0] != '"' || value[^1] != '"')
                    throw new OfficeFileException("Invalid quoted VBA protection field.");
                value = value[1..^1];
                switch (key)
                {
                    case "CMG": protection = value; break;
                    case "DPB": password = value; break;
                    case "GC": visibility = value; break;
                }
            }
        }
        var locked = false;
        if (visibility != null)
        {
            var data = DecryptVisibility(visibility);
            locked = data == 0;
        }
        return new VbaProjectProperties(new System.Collections.ObjectModel.ReadOnlyDictionary<string, VbaModuleKind>(kinds),
            protection, password, visibility, locked);
    }

    private static byte DecryptVisibility(string hex)
    {
        byte[] encrypted;
        try { encrypted = Convert.FromHexString(hex); }
        catch (FormatException exception)
        { throw new OfficeFileException("Invalid hexadecimal VBA visibility field.", exception); }
        if (encrypted.Length < 8 || encrypted.Length > 11 || (encrypted[0] ^ encrypted[1]) != 2)
            throw new OfficeFileException("Invalid VBA visibility encryption header.");
        var ignored = (encrypted[0] & 6) / 2;
        if (encrypted.Length != 8 + ignored)
            throw new OfficeFileException("Invalid VBA visibility encrypted length.");
        var plain = new byte[encrypted.Length - 3];
        var previousPlain = encrypted[0] ^ encrypted[2];
        for (var i = 3; i < encrypted.Length; ++i)
        {
            var value = (byte)(encrypted[i] ^ ((encrypted[i - 2] + previousPlain) & 255));
            plain[i - 3] = value;
            previousPlain = value;
        }
        if (BinaryPrimitives.ReadUInt32LittleEndian(plain.AsSpan(ignored, 4)) != 1 || plain[^1] is not (0 or 255))
            throw new OfficeFileException("Invalid VBA project visibility state.");
        return plain[^1];
    }
}
