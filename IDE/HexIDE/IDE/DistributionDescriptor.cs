using System.Buffers.Binary;
using System.Globalization;
using System.IO;
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
        ["schemaVersion", "id", "name", "tagline", "copyright", "credits", "links", "logo"];

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

            var tagline = OptionalString(root, "tagline", file);
            if (tagline != null) ValidateText(tagline, "tagline", MaximumLineLength, file, allowNewlines: false);

            var copyright = OptionalString(root, "copyright", file);
            if (copyright != null) ValidateText(copyright, "copyright", MaximumLineLength, file, allowNewlines: false);

            var credits = ReadCredits(root, file);
            var links = ReadLinks(root, file);
            var logo = ReadLogo(root, file, readAsset);

            return new DistributionIdentity(id, name, tagline, copyright, credits,
                links.GetValueOrDefault("home"), links.GetValueOrDefault("support"), links.GetValueOrDefault("repository"), logo);
        }
    }

    private static List<string> ReadCredits(JsonElement root, string file)
    {
        var credits = new List<string>();
        if (!root.TryGetProperty("credits", out var element))
            return credits;
        if (element.ValueKind != JsonValueKind.Array)
            throw Refuse(file, "'credits' must be an array of strings");
        if (element.GetArrayLength() > MaximumCredits)
            throw Refuse(file, $"'credits' has more than {MaximumCredits} entries");

        foreach (var entry in element.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String)
                throw Refuse(file, "'credits' must be an array of strings");
            var text = entry.GetString()!;
            ValidateText(text, "credits", MaximumCreditLength, file, allowNewlines: true);
            credits.Add(text);
        }

        return credits;
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
        if (!reference.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            throw Refuse(file, "'logo' must name a .png file");

        var bytes = readAsset(reference);

        // The signature, the IHDR length and type, then the two big-endian dimensions. Bounded before anything
        // decodes it: the reader caps the bytes, and this caps the pixels a small file can ask for.
        if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(PngSignature)
            || !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            throw Refuse(file, $"'logo' names '{reference}', which is not a PNG image");

        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
        if (width is 0 or > MaximumLogoDimension || height is 0 or > MaximumLogoDimension)
            throw Refuse(file, $"'logo' is {width}x{height}; each side must be from 1 to {MaximumLogoDimension} pixels");

        return bytes;
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
