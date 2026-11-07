using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HexIDE.IDE.Profiles;

// Contained byte reads, with an optional directory lifetime spanning a bundle load.
internal static partial class ProfileFileReader
{
    private const int MaximumBytes = 1024 * 1024;
    public static byte[] Read(string directory, string reference, Action? beforeOpen = null)
    {
        if (!ProfilePaths.IsSafeReference(reference) || ProfilePaths.HasLink(directory, reference))
            throw new InvalidDataException("UnsafePath");
        beforeOpen?.Invoke();
        using var bundle = new BundleRead(directory);
        return bundle.Read(reference);
    }

    internal static (int NoFollow, int DirectoryOnly, int CloseOnExec, int NonBlock) UnixFlags(OSPlatform platform)
    {
        if (platform == OSPlatform.Linux)
            return (0x20000, 0x10000, 0x80000, 0x800);
        // Darwin flag values: apple-oss-distributions/xnu, bsd/sys/fcntl.h:
        // https://github.com/apple-oss-distributions/xnu/blob/main/bsd/sys/fcntl.h
        if (platform == OSPlatform.OSX)
            return (0x100, 0x100000, 0x1000000, 0x4);
        throw new InvalidDataException($"Unsupported platform: {platform}");
    }

    private static OSPlatform UnixPlatform => OperatingSystem.IsLinux() ? OSPlatform.Linux
        : OperatingSystem.IsMacOS() ? OSPlatform.OSX : OSPlatform.Create(RuntimeInformation.OSDescription);

    internal sealed class BundleRead : IDisposable
    {
        private readonly List<SafeFileHandle> handles = new();
        private readonly string directory;

        public BundleRead(string directory)
        {
            this.directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    // Follow trusted ancestors once, retaining the resolved directory while locking its path.
                    var resolved = OpenWindows(this.directory, directory: true, retainDirectory: true);
                    handles.Add(resolved);
                    var path = new char[32768];
                    var length = GetFinalPathNameByHandle(resolved, path, (uint)path.Length, 0);
                    if (length == 0 || length >= path.Length)
                        throw new InvalidDataException("UnreadablePath", new Win32Exception(Marshal.GetLastPInvokeError()));
                    this.directory = new string(path, 0, (int)length);
                    var current = Path.GetPathRoot(this.directory)!;
                    handles.Add(OpenWindows(current, directory: true, retainDirectory: true));
                    foreach (var component in this.directory[current.Length..].Split(Path.DirectorySeparatorChar,
                                 StringSplitOptions.RemoveEmptyEntries))
                    {
                        current = Path.Combine(current, component);
                        handles.Add(OpenWindows(current, directory: true, retainDirectory: true));
                    }
                    // Refuse an ancestor move that raced acquisition of the canonical path handles.
                    length = GetFinalPathNameByHandle(resolved, path, (uint)path.Length, 0);
                    if (length == 0 || length >= path.Length
                        || new string(path, 0, (int)length) != this.directory)
                        throw new InvalidDataException("UnsafePath");
                }
                else
                {
                    var (noFollow, directoryOnly, closeOnExec, _) = UnixFlags(UnixPlatform);
                    // Opening the root resolves trusted ancestors atomically; O_NOFOLLOW protects the root
                    // itself. Every untrusted reference component is then opened relative to this descriptor.
                    handles.Add(OpenAt(-1, this.directory, directoryOnly | noFollow | closeOnExec));
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public byte[] Read(string reference, Action? beforeOpen = null)
        {
            if (!ProfilePaths.IsSafeReference(reference))
                throw new InvalidDataException("UnsafePath");
            beforeOpen?.Invoke();
            // Windows ancestors deny write/delete sharing for the whole session, so path resolution
            // cannot leave this directory. Unix permits rename: use the retained descriptor instead.
            return OperatingSystem.IsWindows() ? ReadWindows(directory, reference)
                : ReadUnixRelative(handles[^1], reference);
        }

        public void Dispose()
        {
            for (var index = handles.Count - 1; index >= 0; index--)
                handles[index].Dispose();
        }
    }

    private static byte[] ReadUnixRelative(SafeFileHandle root, string reference)
    {
        var (noFollow, directoryOnly, closeOnExec, nonBlock) = UnixFlags(UnixPlatform);
        var descriptors = new List<SafeFileHandle>();
        try
        {
            var parent = root;
            var components = ProfilePaths.Components(reference);
            for (var index = 0; index < components.Length; index++)
            {
                // O_NONBLOCK also prevents a malicious FIFO from hanging the reader at open.
                var flags = noFollow | closeOnExec | nonBlock;
                if (index < components.Length - 1)
                    flags |= directoryOnly;
                parent = OpenAt(parent.DangerousGetHandle().ToInt32(), components[index], flags);
                descriptors.Add(parent);
            }
            return ReadHandle(parent);
        }
        finally
        {
            for (var index = descriptors.Count - 1; index >= 0; index--)
                descriptors[index].Dispose();
        }
    }

    private static SafeFileHandle OpenAt(int parent, string name, int flags)
    {
        var descriptor = OpenAtNative(parent, name, flags);
        if (descriptor < 0)
        {
            var error = Marshal.GetLastPInvokeError();
            // A Unix socket cannot be opened as a file (ENXIO).
            throw new InvalidDataException(error == 6 ? "UnsafePath" : "UnreadablePath", new Win32Exception(error));
        }
        return new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
    }

    private static byte[] ReadWindows(string directory, string reference)
    {
        var handles = new List<SafeFileHandle>();
        try
        {
            var absolute = Path.GetFullPath(directory);
            var current = Path.GetPathRoot(absolute)!;
            // Deny write/delete sharing while inspecting and reading: ancestors cannot become junctions.
            handles.Add(OpenWindows(current, directory: true));
            foreach (var component in absolute[current.Length..].Split(Path.DirectorySeparatorChar,
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, component);
                handles.Add(OpenWindows(current, directory: true));
            }
            var components = ProfilePaths.Components(reference);
            for (var index = 0; index < components.Length; index++)
            {
                current = Path.Combine(current, components[index]);
                handles.Add(OpenWindows(current, directory: index < components.Length - 1));
            }
            return ReadHandle(handles[^1]);
        }
        finally
        {
            for (var index = handles.Count - 1; index >= 0; index--)
                handles[index].Dispose();
        }
    }

    private static SafeFileHandle OpenWindows(string path, bool directory, bool retainDirectory = false)
    {
        const uint listDirectory = 0x1, readAttributes = 0x80, genericRead = 0x80000000;
        const uint openReparsePoint = 0x200000, backupSemantics = 0x2000000;
        // Attribute-only access does not enforce sharing; retained directories need FILE_LIST_DIRECTORY.
        var access = directory ? (retainDirectory ? listDirectory : readAttributes) : genericRead;
        var handle = CreateFile(path, access, 1, IntPtr.Zero, 3,
            openReparsePoint | backupSemantics, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new InvalidDataException("UnreadablePath", new Win32Exception(error));
        }
        if (!GetFileInformationByHandleEx(handle, 9, out var info, 8))
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new InvalidDataException("UnreadablePath", new Win32Exception(error));
        }
        if ((info.Attributes & 0x400) != 0 || ((info.Attributes & 0x10) != 0) != directory)
        {
            handle.Dispose();
            throw new InvalidDataException("UnsafePath");
        }
        return handle;
    }

    private static byte[] ReadHandle(SafeFileHandle handle)
    {
        ValidateHandle(handle);
        using var stream = new FileStream(handle, FileAccess.Read);
        if (stream.Length > MaximumBytes)
            throw new InvalidDataException("TooLarge");
        return ReadBytes(stream);
    }

    internal static void ValidateHandle(SafeFileHandle handle,
        Func<SafeFileHandle, Architecture, uint>? readMode = null, Architecture? architecture = null)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var platform = architecture ?? RuntimeInformation.ProcessArchitecture;
            if (OperatingSystem.IsLinux() && platform is Architecture.X64 or Architecture.Arm64)
            {
                try
                {
                    var mode = (readMode ?? ReadLinuxMode)(handle, platform);
                    if ((mode & 0xf000) != 0x8000)
                        throw new InvalidDataException("UnsafePath");
                    return;
                }
                catch (EntryPointNotFoundException)
                {
                    // Older glibc does not export fstat; use the same fallback as unknown layouts.
                }
            }
            // macOS also uses this fallback; Linux stat offsets do not describe Darwin's ABI.
            using var borrowed = new SafeFileHandle(handle.DangerousGetHandle(), ownsHandle: false);
            using var stream = new FileStream(borrowed, FileAccess.Read);
            if (!stream.CanSeek)
                throw new InvalidDataException("UnsafePath");
        }
        else if (OperatingSystem.IsWindows())
        {
            if (GetFileType(handle) != 1)
                throw new InvalidDataException("UnsafePath");
        }
        else
            throw new InvalidDataException($"Unsupported platform: {RuntimeInformation.OSDescription}");
    }

    private static uint ReadLinuxMode(SafeFileHandle handle, Architecture architecture)
    {
        if (FStat(handle, out var stat) != 0)
            throw new InvalidDataException("UnreadablePath", new Win32Exception(Marshal.GetLastPInvokeError()));
        return architecture == Architecture.X64 ? stat.ModeX64 : stat.ModeArm64;
    }

    internal static byte[] ReadBytes(Stream stream)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = stream.Read(buffer, 0, Math.Min(buffer.Length, MaximumBytes + 1 - (int)bytes.Length));
            if (read == 0)
                return bytes.ToArray();
            bytes.Write(buffer, 0, read);
            if (bytes.Length > MaximumBytes)
                throw new InvalidDataException("TooLarge");
        }
    }

    // libc stat is 144 bytes on Linux x64 and 128 on arm64; only st_mode is consumed.
    [StructLayout(LayoutKind.Explicit, Size = 144)]
    private struct LinuxStat
    {
        [FieldOffset(24)] public uint ModeX64;
        [FieldOffset(16)] public uint ModeArm64;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetFinalPathNameByHandle(SafeFileHandle handle, [Out] char[] path, uint length, uint flags);

    [LibraryImport("libc", EntryPoint = "fstat", SetLastError = true)]
    private static partial int FStat(SafeFileHandle handle, out LinuxStat information);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint GetFileType(SafeFileHandle handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeTagInfo
    {
        public uint Attributes;
        public uint ReparseTag;
    }

    [LibraryImport("libc", EntryPoint = "openat", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int OpenAtNative(int directory, string path, int flags);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass,
        out AttributeTagInfo information, uint size);
}
