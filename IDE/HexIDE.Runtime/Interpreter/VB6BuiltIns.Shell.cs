using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using HexIDE.IDE;

namespace HexIDE.Runtime.Interpreter;

public partial class VB6BuiltIns
{
    private static Vb6Value Shell(IReadOnlyList<Vb6Value> args)
    {
        if (args[0].IsNull)
            throw InvalidUseOfNull();
        // vb6.exe SP6, native and p-code: #4406 (oracle PR #4721).
        if (args[0].Type == Vb6Value.ValueType.Object
            || args[0].Type == Vb6Value.ValueType.Control
            || args[0].Type == Vb6Value.ValueType.CSharpProxyObject)
        {
            if (args[0].Value is null)
                throw new VBRunTimeException(VBStandardError.ObjectVariableOrWithBlockVariableNotSet);
            if (args[0].Value is VbCollection)
                throw new VBRunTimeException(VBStandardError.WrongNumberOfArgumentsOrInvalidPropertyAssignment);
            throw new NotImplementedException("Shell: this object's PathName default member has not been measured (#4406)");
        }
        if (args[0].Type == Vb6Value.ValueType.UserDefinedType || args[0].Value is VbUdt
            || args[0].Type == Vb6Value.ValueType.Missing
            || args[0].Type == Vb6Value.ValueType.AutomationUnsignedLong
            || args[0].Type == Vb6Value.ValueType.Color
            || args[0].Type == Vb6Value.ValueType.File
            || args[0].Type == Vb6Value.ValueType.Nothing)
            throw new NotImplementedException("Shell: this PathName conversion has not been measured (#4406)");
        var command = AsStr(args[0]).TrimStart(' ', '\t');
        var style = Supplied(args, 1)
            ? Convert.ToInt32(NarrowNum(args[1], Vb6Value.ValueType.Integer).Value) : 2;
        if (style is < 0 or > 9 || command.Length == 0)
            throw InvalidCall();

        var end = command[0] == '"' ? command.IndexOf('"', 1) : command.IndexOfAny([' ', '\t']);
        var program = end < 0 ? command : command[0] == '"' ? command[1..end] : command[..end];
        if (Directory.Exists(command) || Directory.Exists(program))
            throw InvalidCall();

        if (OperatingSystem.IsWindows())
        {
            var startup = new ShellStartupInfo
            {
                Size = Marshal.SizeOf<ShellStartupInfo>(), Flags = 1, ShowWindow = (short)style
            };
            // Batch files need the command interpreter; other programs receive the original command line.
            var launchCommand = File.Exists(program) && Path.GetExtension(program).Equals(".bat", StringComparison.OrdinalIgnoreCase)
                ? "\"" + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe")
                    + "\" /c \"" + command + "\""
                : command;
            var buffer = (launchCommand + '\0').ToCharArray();
            if (!ShellCreateProcess(null, buffer, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, null,
                    ref startup, out var process))
            {
                var error = Marshal.GetLastPInvokeError();
                if (error is 2 or 3)
                    throw new VBRunTimeException(VBStandardError.FileNotFound);
                if (error is 5 or 193 or 216)
                    throw InvalidCall();
                throw new NotImplementedException($"Shell: process launch error {error} has not been measured (#4406)");
            }
            ShellCloseHandle(process.Thread);
            ShellCloseHandle(process.Process);
            ShellLaunches.Record((int)process.ProcessId, command, style);
            return new Vb6Value((double)process.ProcessId);
        }

        // Unix has no Windows show state. Launch directly; do not interpret shell operators ourselves.
        try
        {
            using var process = Process.Start(new ProcessStartInfo(program, end < 0 ? "" : command[(end + 1)..])
            {
                UseShellExecute = false
            });
            ShellLaunches.Record(process!.Id, command, style);
            return new Vb6Value((double)process.Id);
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 2 && !ShellProgramExists(program))
        {
            throw new VBRunTimeException(VBStandardError.FileNotFound);
        }
        catch (Win32Exception e) when (!OperatingSystem.IsWindows() && e.NativeErrorCode is 8 or 13 && ShellProgramExists(program))
        {
            throw InvalidCall();
        }
        catch (Win32Exception e)
        {
            throw new NotImplementedException($"Shell: process launch error {e.NativeErrorCode} has not been measured (#4406)");
        }
    }

    private static bool ShellProgramExists(string program)
    {
        if (File.Exists(program))
            return true;
        if (program.Contains(Path.DirectorySeparatorChar))
            return false;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (File.Exists(Path.Combine(directory, program)))
                return true;
        return false;
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
