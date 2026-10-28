using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HexIDE.IDE.Profiles;

// Standalone byte reader; pack validation and multi-file snapshot loading remain separate.
internal static partial class ProfileFileReader
{
    private const int MaximumBytes = 1024 * 1024;
    public static byte[] Read(string directory, string reference, Action? beforeOpen = null)
    {
        if (!ProfilePaths.IsSafeReference(reference) || ProfilePaths.HasLink(directory, reference))
            throw new InvalidDataException("UnsafePath");
        beforeOpen?.Invoke();
        if (OperatingSystem.IsLinux())
            return ReadLinux(directory, reference);
        if (OperatingSystem.IsWindows())
            return ReadWindows(directory, reference);
        throw new PlatformNotSupportedException();
    }

    private static byte[] ReadLinux(string directory, string reference)
    {
        const int noFollow = 0x20000, directoryOnly = 0x10000, closeOnExec = 0x80000;
        var descriptors = new List<SafeFileHandle>();
        try
        {
            var parent = OpenAt(-100, "/", directoryOnly | noFollow | closeOnExec);
            descriptors.Add(parent);
            foreach (var component in Path.GetFullPath(directory).Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                parent = OpenAt(parent.DangerousGetHandle().ToInt32(), component,
                    directoryOnly | noFollow | closeOnExec);
                descriptors.Add(parent);
            }
            var components = ProfilePaths.Components(reference);
            for (var index = 0; index < components.Length; index++)
            {
                // O_NONBLOCK also prevents a malicious FIFO from hanging the reader at open.
                var flags = noFollow | closeOnExec | 0x800;
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

    private static SafeFileHandle OpenWindows(string path, bool directory)
    {
        const uint readAttributes = 0x80, genericRead = 0x80000000;
        const uint openReparsePoint = 0x200000, backupSemantics = 0x2000000;
        var handle = CreateFile(path, directory ? readAttributes : genericRead, 1, IntPtr.Zero, 3,
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
        if (OperatingSystem.IsLinux())
        {
            var platform = architecture ?? RuntimeInformation.ProcessArchitecture;
            if (platform is Architecture.X64 or Architecture.Arm64)
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
            throw new PlatformNotSupportedException();
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
