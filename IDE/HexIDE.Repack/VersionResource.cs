using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using HexIDE.IDE;

namespace HexIDE.Repack;

/// <summary>What the executable's version details will say.</summary>
internal sealed record VersionDetails(
    string ProductName,
    string Copyright,
    string ExecutableName,
    string FileVersion,
    string ProductVersion,
    (int Major, int Minor, int Build, int Private) FileParts,
    (int Major, int Minor, int Build, int Private) ProductParts)
{
    /// <summary>
    /// The name and copyright are the brand's. The numbers stay the build's, because that is what the code in it is;
    /// a brand with no copyright line keeps stock's rather than claiming none.
    /// </summary>
    public static VersionDetails ForBrand(DistributionIdentity brand, string executableName, FileVersionInfo stock) =>
        new(brand.Name,
            brand.Copyright ?? stock.LegalCopyright ?? "",
            executableName,
            stock.FileVersion ?? "",
            stock.ProductVersion ?? "",
            (stock.FileMajorPart, stock.FileMinorPart, stock.FileBuildPart, stock.FilePrivatePart),
            (stock.ProductMajorPart, stock.ProductMinorPart, stock.ProductBuildPart, stock.ProductPrivatePart));
}

/// <summary>Builds the executable's version resource, the block Windows reads for a file's details.</summary>
internal static class VersionResource
{
    // Language-neutral text in UTF-16, matching the language stock's resources are filed under.
    private const string TableKey = "000004b0";
    private const uint Translation = 0x04b0_0000;

    private const ushort BinaryValue = 0;
    private const ushort TextValue = 1;

    public static NativeResource Build(VersionDetails details)
    {
        var table = Block(TableKey, TextValue, 0, [],
            Text("CompanyName", details.ProductName),
            Text("FileDescription", details.ProductName),
            Text("FileVersion", details.FileVersion),
            Text("InternalName", details.ExecutableName),
            Text("LegalCopyright", details.Copyright),
            Text("OriginalFilename", details.ExecutableName),
            Text("ProductName", details.ProductName),
            Text("ProductVersion", details.ProductVersion));
        var strings = Block("StringFileInfo", TextValue, 0, [], table);

        var translation = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(translation, Translation);
        var variables = Block("VarFileInfo", TextValue, 0, [], Block("Translation", BinaryValue, 4, translation));

        var fixedInfo = FixedInfo(details);
        var root = Block("VS_VERSION_INFO", BinaryValue, (ushort)fixedInfo.Length, fixedInfo, strings, variables);
        return new NativeResource(PeImage.TypeVersion, 1, 0, root);
    }

    private static byte[] FixedInfo(VersionDetails details)
    {
        var info = new byte[52];
        void Field(int index, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(index * 4), value);

        Field(0, 0xFEEF04BD); // The signature that marks the structure.
        Field(1, 0x0001_0000);
        Field(2, High(details.FileParts.Major, details.FileParts.Minor));
        Field(3, High(details.FileParts.Build, details.FileParts.Private));
        Field(4, High(details.ProductParts.Major, details.ProductParts.Minor));
        Field(5, High(details.ProductParts.Build, details.ProductParts.Private));
        Field(6, 0x3F); // Which flag bits mean anything. None is set.
        Field(8, 0x4);  // VOS__WINDOWS32.
        Field(9, 0x1);  // VFT_APP.
        return info;
    }

    private static uint High(int high, int low) => (uint)((high << 16) | (low & 0xFFFF));

    private static byte[] Text(string key, string value)
    {
        var data = Encoding.Unicode.GetBytes(value + '\0');
        return Block(key, TextValue, (ushort)(value.Length + 1), data);
    }

    // Every block has the same shape: its length, its value's length, its type, its key, then its value and children,
    // each starting on a four-byte boundary.
    private static byte[] Block(string key, ushort type, ushort valueLength, byte[] value, params byte[][] children)
    {
        using var block = new MemoryStream();
        block.Write(new byte[2]); // The length, filled in once it's known.
        Write(block, valueLength);
        Write(block, type);
        block.Write(Encoding.Unicode.GetBytes(key + '\0'));
        Align(block);
        block.Write(value);

        foreach (var child in children)
        {
            Align(block);
            block.Write(child);
        }

        var bytes = block.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)bytes.Length);
        return bytes;
    }

    private static void Write(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void Align(Stream stream)
    {
        while (stream.Position % 4 != 0)
            stream.WriteByte(0);
    }
}
