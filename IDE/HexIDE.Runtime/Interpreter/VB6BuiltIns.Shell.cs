using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace HexIDE.Runtime.Interpreter;

public partial class VB6BuiltIns
{
    private static Vb6Value Shell(IReadOnlyList<Vb6Value> args)
    {
        if (args[0].IsNull)
            throw InvalidUseOfNull();
        if (args[0].Type != Vb6Value.ValueType.String)
            throw new NotImplementedException("Shell: non-string PathName conversion has not been measured (#2522)");
        var command = AsStr(args[0]).TrimStart(' ', '\t');
        var style = Supplied(args, 1)
            ? Convert.ToInt32(NarrowNum(args[1], Vb6Value.ValueType.Integer).Value) : (int?)null;
        if (style is -1 or 10 || command.Length == 0)
            throw InvalidCall();
        if (style is not (null or 0 or 1 or 2 or 3 or 4 or 6 or 7))
            throw new NotImplementedException("Shell: this WindowStyle has not been measured (#2522)");

        var end = command[0] == '"' ? command.IndexOf('"', 1) : command.IndexOfAny([' ', '\t']);
        var program = end < 0 ? command : command[0] == '"' ? command[1..end] : command[..end];
        if (Directory.Exists(command) || Directory.Exists(program))
            throw InvalidCall();

        if (!style.HasValue)
        {
            // Missing programs are measured even with no style. A successful launch's default show state is not.
            var found = File.Exists(program);
            var names = OperatingSystem.IsWindows() ? new[] { program, program + ".exe" } : new[] { program };
            foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
                foreach (var name in names)
                    found |= File.Exists(Path.Combine(directory, name));
            if (!found)
                throw new VBRunTimeException(VBStandardError.FileNotFound);
            throw new NotImplementedException("Shell: omitted WindowStyle show state has not been measured (#2563)");
        }

        if (OperatingSystem.IsWindows())
        {
            var startup = new ShellStartupInfo
            {
                Size = Marshal.SizeOf<ShellStartupInfo>(), Flags = 1, ShowWindow = (short)style.Value
            };
            // CreateProcess receives the original command line, including quoting and argument boundaries.
            var buffer = (command + '\0').ToCharArray();
            if (!ShellCreateProcess(null, buffer, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, null,
                    ref startup, out var process))
            {
                if (Marshal.GetLastPInvokeError() is 2 or 3)
                    throw new VBRunTimeException(VBStandardError.FileNotFound);
                throw new NotImplementedException("Shell: this process launch failure has not been measured (#2522)");
            }
            ShellCloseHandle(process.Thread);
            ShellCloseHandle(process.Process);
            return new Vb6Value((double)process.ProcessId);
        }

        // Unix has no Windows show state. Launch directly; do not interpret shell operators ourselves.
        try
        {
            using var process = Process.Start(new ProcessStartInfo(program, end < 0 ? "" : command[(end + 1)..])
            {
                UseShellExecute = false
            });
            return new Vb6Value((double)process!.Id);
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 2)
        {
            throw new VBRunTimeException(VBStandardError.FileNotFound);
        }
        catch (Win32Exception)
        {
            throw new NotImplementedException("Shell: this process launch failure has not been measured (#2522)");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShellStartupInfo
    {
        public int Size;
        public IntPtr Reserved, Desktop, Title;
        public int X, Y, Width, Height, XChars, YChars, Fill, Flags;
        public short ShowWindow, ReservedSize;
        public IntPtr ReservedBytes, Input, Output, Error;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShellProcessInfo
    {
        public IntPtr Process, Thread;
        public uint ProcessId, ThreadId;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShellCreateProcess(string? application, [In, Out] char[] command,
        IntPtr processAttributes, IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint flags, IntPtr environment, string? directory, ref ShellStartupInfo startup, out ShellProcessInfo process);

    [LibraryImport("kernel32.dll", EntryPoint = "CloseHandle")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShellCloseHandle(IntPtr handle);
}
