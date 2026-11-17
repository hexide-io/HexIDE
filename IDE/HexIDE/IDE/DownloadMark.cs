using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace HexIDE.IDE;

/// <summary>
/// Whether the operating system marks a file as downloaded: the evidence <see cref="CodeTrust"/> asks on (#1121).
/// </summary>
/// <remarks>
/// <para>
/// <b>Windows:</b> the Mark of the Web, a <c>Zone.Identifier</c> stream beside the file, which browsers, mail clients and
/// Explorer's own unzipping write and Office reads. A <c>ZoneId</c> of 3 (Internet) or 4 (Restricted) marks the file. A
/// stream that is there but can't be read, or names no zone HexIDE recognises, marks it too: a mark is evidence, and
/// evidence that can't be read is not taken as its absence. A volume that can't hold streams at all, such as FAT, carries
/// no mark.
/// </para>
/// <para>
/// <b>macOS:</b> the <c>com.apple.quarantine</c> extended attribute, which Gatekeeper reads. Any value marks the file.
/// </para>
/// <para>
/// <b>Linux</b> has no mark the system writes, so nothing marks a file there.
/// </para>
/// <para>
/// The stream's other lines, such as <c>HostUrl</c>, are written by whoever supplied the file, so nothing here reads them.
/// </para>
/// </remarks>
internal static partial class DownloadMark
{
    /// <summary>Whether <paramref name="path"/> carries its platform's download mark.</summary>
    public static bool IsDownloaded(string path)
    {
        if (OperatingSystem.IsWindows())
            return WindowsMark(path);
        if (OperatingSystem.IsMacOS())
            return QuarantineMark(path);
        return false;
    }

    /// <summary>The most of a <c>Zone.Identifier</c> stream read: a real one is a few hundred bytes.</summary>
    private const int MostRead = 64 * 1024;

    // ERROR_INVALID_NAME: a volume with no alternate streams, such as FAT, can't name one.
    private const int InvalidName = unchecked((int)0x8007007B);

    private static bool WindowsMark(string path)
    {
        if (!File.Exists(path))
            return false;
        byte[] read;
        try
        {
            using var stream = new FileStream(path + ":Zone.Identifier", FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            read = new byte[(int)Math.Min(stream.Length, MostRead)];
            stream.ReadExactly(read);
        }
        catch (FileNotFoundException) { return false; }
        catch (IOException e) when (e.HResult == InvalidName) { return false; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Serilog.Log.Warning(e, "The download mark on {Path} could not be read, so it is taken as downloaded", path);
            return true;
        }
        return ZoneIsDownloaded(Encoding.UTF8.GetString(read));
    }

    /// <summary>
    /// Whether a <c>Zone.Identifier</c> stream's text marks its file as downloaded: a <c>ZoneId</c> under
    /// <c>[ZoneTransfer]</c> other than 0 (this computer), 1 (intranet) or 2 (trusted sites), or no zone that can be read.
    /// </summary>
    internal static bool ZoneIsDownloaded(string stream)
    {
        var inSection = false;
        foreach (var raw in stream.Split('\n'))
        {
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.StartsWith('['))
            {
                inSection = string.Equals(line, "[ZoneTransfer]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inSection || line.IndexOf('=') is not (> 0 and var equals)
                || !string.Equals(line[..equals].Trim(), "ZoneId", StringComparison.OrdinalIgnoreCase))
                continue;
            return !int.TryParse(line[(equals + 1)..].Trim(), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var zone) || zone > 2;
        }
        return true;
    }

    // errno values on macOS.
    private const int NoSuchFile = 2, NotSupported = 45, NoAttribute = 93;

    private static bool QuarantineMark(string path)
    {
        try
        {
            if (GetAttribute(path, "com.apple.quarantine", 0, 0, 0, 0) >= 0)
                return true;
            var error = Marshal.GetLastPInvokeError();
            if (error is NoAttribute or NotSupported or NoSuchFile)
                return false;
            Serilog.Log.Warning("The quarantine mark on {Path} could not be read (errno {Error}), so it is taken as downloaded", path, error);
            return true;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            Serilog.Log.Warning(e, "The quarantine mark could not be read, so {Path} is taken as downloaded", path);
            return true;
        }
    }

    // macOS's getxattr, which takes a position and options that Linux's does not; called on macOS only.
    [LibraryImport("libc", EntryPoint = "getxattr", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetAttribute(string path, string name, nint value, nuint size, uint position, int options);
}
