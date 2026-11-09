using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using HexIDE.IDE;

namespace HexIDE.Repack;

/// <summary>
/// The installer's script and its brand's inputs: HexIDE's own script, unchanged, with this product's identity defined
/// ahead of it, so there is one installer definition rather than two to keep in step.
/// </summary>
internal static class InstallerInputs
{
    public const string ScriptName = "brand.iss";

    private const string SharedScriptName = "HexIDE.iss";

    // Fixed for good. Changing it would change every brand's AppId, and an installed product would no longer be found.
    private static readonly Guid AppIdNamespace = new("6f3c1a52-8e4d-4b7a-9c2e-5d10a7e3f481");

    public static void Write(string directory, DistributionIdentity brand, string executableName, FileVersionInfo stock)
    {
        Directory.CreateDirectory(directory);

        using (var shared = typeof(InstallerInputs).Assembly.GetManifestResourceStream(SharedScriptName)
               ?? throw new InvalidOperationException(SharedScriptName))
        using (var copy = File.Create(Path.Combine(directory, SharedScriptName)))
            shared.CopyTo(copy);

        // The build's numbers without the source revision, which is the form stock's installer is given.
        var version = (stock.ProductVersion ?? "").Split('+')[0];
        File.WriteAllText(Path.Combine(directory, ScriptName), Script(brand, executableName, version), new UTF8Encoding(true));
    }

    internal static string Script(DistributionIdentity brand, string executableName, string version)
    {
        // A doubled opening brace is how Inno Setup writes a literal one.
        var appId = "{" + AppId(brand.Id);
        return $"""
                ; Written by HexIDE.Repack for the distribution '{brand.Id}'. Compile this file with Inno Setup's ISCC.
                #define AppId "{appId}"
                #define AppName "{brand.Name}"
                #define AppExe "{executableName}"
                #define AppVersion "{version}"
                #define AppUserModelId "{brand.AppUserModelId}"
                #define PublishDir "..\app"
                #define SetupOutputDir "output"
                #define SetupBaseName "{brand.Id}-windows-amd64-setup"
                #include "{SharedScriptName}"

                """.ReplaceLineEndings("\r\n");
    }

    /// <summary>
    /// A name-based GUID (RFC 9562, version 5) of the distribution id, in braces as Inno Setup's AppId takes one. The
    /// same id always gives the same value, so an upgrade finds the installed product, and it can't be stock's.
    /// </summary>
    internal static string AppId(string distributionId)
    {
        var name = Encoding.UTF8.GetBytes(distributionId);
        var input = new byte[16 + name.Length];
        AppIdNamespace.TryWriteBytes(input, bigEndian: true, out _);
        name.CopyTo(input, 16);

        var hash = SHA1.HashData(input).AsSpan(0, 16).ToArray();
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return "{" + new Guid(hash, bigEndian: true).ToString("D").ToUpperInvariant() + "}";
    }
}
