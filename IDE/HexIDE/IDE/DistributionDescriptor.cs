using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using HexIDE.IDE.Profiles;

namespace HexIDE.IDE;

/// <summary>An installation descriptor that cannot be used. The message names the file and what is wrong.</summary>
public sealed class DistributionDescriptorException(string message) : Exception(message);

/// <summary>
/// Reads <c>brand.json</c> from beside the executable: the declaration that makes this installation another
/// product (#2042; <c>docs/white-label.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>A missing file means stock HexIDE. A broken one means no start at all.</b> Falling back to stock on a bad
/// descriptor would open HexIDE's own settings and offer its recovery files inside somebody else's product.
/// </para>
/// <para>
/// A member this reader does not know is refused rather than ignored. That is what keeps executable hooks, state
/// paths and trust anchors out: there is no member for them, so asking for one is an error and not a no-op.
/// </para>
/// </remarks>
public static partial class DistributionDescriptor
{
    public const string FileName = "brand.json";
    public const int SupportedSchemaVersion = 1;

    private const int MaximumNameLength = 64;
    private const int MaximumLineLength = 300;
    private const int MaximumCredits = 32;
    private const int MaximumCreditLength = 2000;
    private const int MaximumUrlLength = 2048;
    private const int MaximumLogoDimension = 1024;

    private static readonly string[] Members =
        ["schemaVersion", "id", "name", "tagline", "copyright", "credits", "links", "logo", "defaultProfile"];

    private static readonly string[] LinkRoles = ["home", "support", "repository"];

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    [GeneratedRegex("^(?:con|prn|aux|nul|com[0-9]|lpt[0-9])$", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsReservedName();

    /// <summary>The identity declared in <paramref name="installationDirectory"/>, or stock when nothing is.</summary>
    /// <exception cref="DistributionDescriptorException">A descriptor exists and cannot be used.</exception>
    public static DistributionIdentity Resolve(string installationDirectory)
    {
        var path = Path.Combine(installationDirectory, FileName);
        var info = new FileInfo(path);

        // A dangling link reports Exists false and is still a descriptor somebody put there: refused, below.
        if (!info.Exists && info.LinkTarget is null)
            return DistributionIdentity.Stock;

        return Parse(ReadContained(installationDirectory, FileName, path), path,
            reference => ReadContained(installationDirectory, reference, path));
    }

    private static byte[] ReadContained(string directory, string reference, string descriptor)
    {
        try
        {
            return ProfileFileReader.Read(directory, reference);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            throw new DistributionDescriptorException(
                $"{descriptor}: '{reference}' could not be read as a regular file inside the installation folder ({ex.Message}).");
        }
    }

    /// <summary>
    /// Validates <paramref name="json"/>. <paramref name="readAsset"/> reads a reference relative to the
    /// installation, already contained and bounded.
    /// </summary>
    internal static DistributionIdentity Parse(byte[] json, string file, Func<string, byte[]> readAsset)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowDuplicateProperties = false,
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8,
            });
        }
        catch (JsonException ex)
        {
            throw Refuse(file, $"it is not valid JSON, or repeats a member ({ex.Message})");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw Refuse(file, "it must be a JSON object");

            RefuseUnknownMembers(root, Members, file, "the descriptor");

            if (!root.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out var schema) || schema != SupportedSchemaVersion)
                throw Refuse(file, $"'schemaVersion' must be {SupportedSchemaVersion}");

            var id = RequiredString(root, "id", file);
            if (!IdPattern().IsMatch(id) || WindowsReservedName().IsMatch(id))
                throw Refuse(file, $"'id' is '{id}', which is not usable as a folder name on every platform: lower-case "
                    + "letters, digits and inner hyphens, up to 64 characters, and not a name Windows reserves");
            if (id == DistributionIdentity.StockId)
                throw Refuse(file, $"'id' may not be '{DistributionIdentity.StockId}': it would share HexIDE's own settings, logs and recovery files");

            var name = RequiredString(root, "name", file);
            ValidateText(name, "name", MaximumNameLength, file, allowNewlines: false);

            // An access key and a format placeholder respectively, in every string the name is substituted into.
            if (name.Contains('_') || name.Contains('{') || name.Contains('}'))
                throw Refuse(file, "'name' may not contain '_', '{' or '}', which mean something in the IDE's own strings");
            if (name != name.Trim())
                throw Refuse(file, "'name' has spaces at its start or end");

            var tagline = root.TryGetProperty("tagline", out var taglineElement)
                ? ReadText(taglineElement, "tagline", MaximumLineLength, file, allowNewlines: false) : null;

            var copyright = OptionalString(root, "copyright", file);
            if (copyright != null) ValidateText(copyright, "copyright", MaximumLineLength, file, allowNewlines: false);

            var credits = ReadCredits(root, file);
            var links = ReadLinks(root, file);
            var logo = ReadLogo(root, file, readAsset);
            var defaultProfile = ReadDefaultProfile(root, file);

            return new DistributionIdentity(id, name, tagline?.Text, copyright, null,
                links.GetValueOrDefault("home"), links.GetValueOrDefault("support"), links.GetValueOrDefault("repository"),
                logo, defaultProfile, tagline, credits);
        }
    }

    private static List<DistributionText> ReadCredits(JsonElement root, string file)
    {
        var credits = new List<DistributionText>();
        if (!root.TryGetProperty("credits", out var element))
            return credits;
        if (element.ValueKind != JsonValueKind.Array)
            throw Refuse(file, "'credits' must be an array of strings");
        if (element.GetArrayLength() > MaximumCredits)
            throw Refuse(file, $"'credits' has more than {MaximumCredits} entries");

        foreach (var entry in element.EnumerateArray())
        {
            credits.Add(ReadText(entry, "credits", MaximumCreditLength, file, allowNewlines: true));
        }

        return credits;
    }

    private static DistributionText ReadText(JsonElement element, string member, int maximum, string file, bool allowNewlines)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var plain = element.GetString()!;
            ValidateText(plain, member, maximum, file, allowNewlines);
            return new DistributionText(plain);
        }
        if (element.ValueKind != JsonValueKind.Object)
            throw Refuse(file, $"'{member}' must be a string or a text/translations object");

        RefuseUnknownMembers(element, ["text", "translations"], file, member);
        var text = RequiredString(element, "text", file);
        ValidateText(text, member, maximum, file, allowNewlines);
        var translations = new Dictionary<string, string>(StringComparer.Ordinal);
        if (element.TryGetProperty("translations", out var map))
        {
            if (map.ValueKind != JsonValueKind.Object)
                throw Refuse(file, $"'{member}.translations' must be an object");
            foreach (var entry in map.EnumerateObject())
            {
                if (!Regex.IsMatch(entry.Name, "^[a-z]{2,3}(?:-[A-Za-z0-9]{2,8})*$", RegexOptions.CultureInvariant))
                    throw Refuse(file, $"'{member}.translations' has an invalid language id '{entry.Name}'");
                if (entry.Value.ValueKind != JsonValueKind.String)
                    throw Refuse(file, $"'{member}.translations.{entry.Name}' must be a string");
                var translation = entry.Value.GetString()!;
                ValidateText(translation, member, maximum, file, allowNewlines);
                translations.Add(entry.Name, translation);
            }
        }
        return new DistributionText(text, translations);
    }

    private static Dictionary<string, string> ReadLinks(JsonElement root, string file)
    {
        var links = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!root.TryGetProperty("links", out var element))
            return links;
        if (element.ValueKind != JsonValueKind.Object)
            throw Refuse(file, "'links' must be an object");

        RefuseUnknownMembers(element, LinkRoles, file, "'links'");
        foreach (var role in LinkRoles)
        {
            if (!element.TryGetProperty(role, out var value))
                continue;
            if (value.ValueKind != JsonValueKind.String)
                throw Refuse(file, $"'links.{role}' must be a string");

            var text = value.GetString()!;
            if (text.Length > MaximumUrlLength || !Uri.TryCreate(text, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0 || uri.Host.Length == 0)
                throw Refuse(file, $"'links.{role}' must be an absolute https URL with no user name");
            links[role] = text;
        }

        return links;
    }

    private static byte[]? ReadLogo(JsonElement root, string file, Func<string, byte[]> readAsset)
    {
        var reference = OptionalString(root, "logo", file);
        if (reference == null)
            return null;
        if (reference.Contains('\\'))
            throw Refuse(file, $"'logo' is '{reference}'; it must be written with '/'");
        if (!reference.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            throw Refuse(file, "'logo' must name a .png file");

        var bytes = readAsset(reference);

        // Gate dimensions before inflating: the reader caps the bytes, and this caps the pixels.
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(PngSignature)
            || !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            throw Refuse(file, $"'logo' names '{reference}', which is not a PNG image");

        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
        if (width is 0 or > MaximumLogoDimension || height is 0 or > MaximumLogoDimension)
            throw Refuse(file, $"'logo' is {width}x{height}; each side must be from 1 to {MaximumLogoDimension} pixels");

        if (!IsCompletePng(bytes, (int)width, (int)height))
            throw Refuse(file, $"'logo' names '{reference}', which is not a PNG image");

        return bytes;
    }

    private static bool IsCompletePng(byte[] bytes, int width, int height)
    {
        if (bytes.Length < 33 || BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8)) != 13)
            return false;
        var depth = bytes[24];
        var colour = bytes[25];
        var channels = colour switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
        var validDepth = colour switch
        {
            0 => depth is 1 or 2 or 4 or 8 or 16,
            3 => depth is 1 or 2 or 4 or 8,
            2 or 4 or 6 => depth is 8 or 16,
            _ => false
        };
        if (!validDepth || bytes[26] != 0 || bytes[27] != 0 || bytes[28] > 1)
            return false;

        using var compressed = new MemoryStream();
        var palette = false;
        var dataSeen = false;
        var dataEnded = false;
        var ended = false;
        for (var offset = 8; offset < bytes.Length;)
        {
            if (bytes.Length - offset < 12) return false;
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset));
            if (length > bytes.Length - offset - 12) return false;
            var count = (int)length;
            var type = bytes.AsSpan(offset + 4, 4);
            var chunk = bytes.AsSpan(offset + 4, count + 4);
            if (PngCrc(chunk) != BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8 + count)))
                return false;
            foreach (var letter in type)
                if (letter is not (>= (byte)'A' and <= (byte)'Z') and not (>= (byte)'a' and <= (byte)'z'))
                    return false;
            if ((type[2] & 32) != 0) return false;
            if (type.SequenceEqual("IHDR"u8))
            {
                if (offset != 8) return false;
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                if (palette || dataSeen || colour is 0 or 4 || count == 0 || count % 3 != 0
                    || count > 768 || (colour == 3 && count / 3 > (1 << depth))) return false;
                palette = true;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (dataEnded || (colour == 3 && !palette)) return false;
                dataSeen = true;
                compressed.Write(bytes, offset + 8, count);
            }
            else
            {
                if (dataSeen) dataEnded = true;
                if (type.SequenceEqual("IEND"u8))
                {
                    if (count != 0 || offset + 12 != bytes.Length) return false;
                    ended = true;
                }
                else if ((type[0] & 32) == 0) return false;
            }
            offset += count + 12;
        }
        if (!ended || !dataSeen || compressed.Length < 6) return false;

        compressed.Position = 0;
        try
        {
            using var inflated = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true);
            uint adlerA = 1, adlerB = 0;
            // Adam7 passes, or a single full image. Empty passes contribute no filter byte.
            int[] startX = bytes[28] == 0 ? [0] : [0, 4, 0, 2, 0, 1, 0];
            int[] startY = bytes[28] == 0 ? [0] : [0, 0, 4, 0, 2, 0, 1];
            int[] stepX = bytes[28] == 0 ? [1] : [8, 8, 4, 4, 2, 2, 1];
            int[] stepY = bytes[28] == 0 ? [1] : [8, 8, 8, 4, 4, 2, 2];
            for (var pass = 0; pass < startX.Length; pass++)
            {
                var columns = Math.Max(0, (width - startX[pass] + stepX[pass] - 1) / stepX[pass]);
                var rows = Math.Max(0, (height - startY[pass] + stepY[pass] - 1) / stepY[pass]);
                if (columns == 0) continue;
                var rowBytes = (columns * channels * depth + 7) / 8;
                var row = new byte[rowBytes + 1];
                for (var y = 0; y < rows; y++)
                {
                    inflated.ReadExactly(row);
                    if (row[0] > 4) return false;
                    foreach (var value in row)
                    {
                        adlerA = (adlerA + value) % 65521;
                        adlerB = (adlerB + adlerA) % 65521;
                    }
                }
            }
            if (inflated.ReadByte() != -1) return false;
            // Also refuse truncated zlib trailers, which a stream can otherwise treat as EOF.
            var data = compressed.GetBuffer().AsSpan(0, (int)compressed.Length);
            return BinaryPrimitives.ReadUInt32BigEndian(data[^4..]) == (adlerB << 16 | adlerA);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return false;
        }
    }

    private static uint PngCrc(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xEDB88320u);
        }
        return ~crc;
    }

    // A name, or a folder inside the installation. "none" is the user's recovery route, and a brand can't start there.
    private static string? ReadDefaultProfile(JsonElement root, string file)
    {
        var reference = OptionalString(root, "defaultProfile", file);
        if (reference == null)
            return null;

        var isFolder = reference.Contains('/') && !reference.Contains('\\') && !Path.IsPathRooted(reference)
            && ProfilePaths.IsSafeReference(reference);
        if (!isFolder && !ProfileCatalog.IsBundleId(reference))
            throw Refuse(file, $"'defaultProfile' is '{reference}'; it must be a profile's name, or a folder inside the installation written with '/'");

        return reference;
    }

    private static void RefuseUnknownMembers(JsonElement element, string[] known, string file, string where)
    {
        var unknown = element.EnumerateObject().Select(p => p.Name).FirstOrDefault(n => !known.Contains(n, StringComparer.Ordinal));
        if (unknown != null)
            throw Refuse(file, $"{where} has a member this version does not know, '{unknown}'. Known members: {string.Join(", ", known)}");
    }

    private static string RequiredString(JsonElement root, string member, string file) =>
        OptionalString(root, member, file) is { Length: > 0 } text
            ? text
            : throw Refuse(file, $"'{member}' is required");

    private static string? OptionalString(JsonElement root, string member, string file)
    {
        if (!root.TryGetProperty(member, out var value))
            return null;
        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : throw Refuse(file, $"'{member}' must be a string");
    }

    private static void ValidateText(string text, string member, int maximum, string file, bool allowNewlines)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw Refuse(file, $"'{member}' is empty");
        if (text.Length > maximum)
            throw Refuse(file, $"'{member}' is longer than {maximum} characters");

        // Format characters include the bidirectional overrides, which could make a name read as something else.
        foreach (var c in text)
        {
            var category = char.GetUnicodeCategory(c);
            if (category == UnicodeCategory.Format || (category == UnicodeCategory.Control && !(allowNewlines && c == '\n')))
                throw Refuse(file, $"'{member}' contains a control or format character");
        }
    }

    private static DistributionDescriptorException Refuse(string file, string reason) => new($"{file}: {reason}.");
}
