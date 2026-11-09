using System.ComponentModel;
using System.Runtime.InteropServices;

namespace HexIDE.Repack;

/// <summary>Replaces an executable's resources through Windows' own functions, which are why this tool is Windows-only.</summary>
internal static partial class NativeResources
{
    // An antivirus scanner opens a newly written executable, and these are what the update then fails with.
    private static readonly int[] Transient =
    [
        5,    // ERROR_ACCESS_DENIED
        32,   // ERROR_SHARING_VIOLATION
        33,   // ERROR_LOCK_VIOLATION
        110,  // ERROR_OPEN_FAILED
        1224, // ERROR_USER_MAPPED_FILE
    ];

    private const int Attempts = 20;
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(100);

    public static void Replace(string path, IReadOnlyList<NativeResource> remove, IReadOnlyList<NativeResource> add)
    {
        for (var attempt = 1; ; attempt++)
        {
            var error = TryReplace(path, remove, add);
            if (error == 0)
                return;
            if (attempt == Attempts || !Transient.Contains(error))
                throw new RepackException($"Windows could not update {path}: {new Win32Exception(error).Message}");
            Thread.Sleep(Pause);
        }
    }

    private static int TryReplace(string path, IReadOnlyList<NativeResource> remove, IReadOnlyList<NativeResource> add)
    {
        var update = BeginUpdateResource(path, deleteExisting: false);
        if (update == 0)
            return Marshal.GetLastPInvokeError();

        var finished = false;
        try
        {
            // A null block deletes the entry, so a stock icon frame can't outlive the brand's.
            foreach (var resource in remove)
            {
                if (!UpdateResource(update, resource.Type, resource.Name, (ushort)resource.Language, null, 0))
                    return Marshal.GetLastPInvokeError();
            }

            foreach (var resource in add)
            {
                if (!UpdateResource(update, resource.Type, resource.Name, (ushort)resource.Language, resource.Data, (uint)resource.Data.Length))
                    return Marshal.GetLastPInvokeError();
            }

            finished = true;
            return EndUpdateResource(update, discard: false) ? 0 : Marshal.GetLastPInvokeError();
        }
        finally
        {
            if (!finished)
                EndUpdateResource(update, discard: true);
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "BeginUpdateResourceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint BeginUpdateResource(string fileName, [MarshalAs(UnmanagedType.Bool)] bool deleteExisting);

    [LibraryImport("kernel32.dll", EntryPoint = "UpdateResourceW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateResource(nint update, nint type, nint name, ushort language, byte[]? data, uint size);

    [LibraryImport("kernel32.dll", EntryPoint = "EndUpdateResourceW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EndUpdateResource(nint update, [MarshalAs(UnmanagedType.Bool)] bool discard);
}
