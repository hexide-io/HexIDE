using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

public class AnimationRuntimeTests
{
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           ClientHeight = 3015
           ClientWidth = 4560
           Begin MSComCtl2.Animation Animation1
              Left = 120
              Top = 120
              Width = 2400
              Height = 1200
              _Version = 393216
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private static async Task<string[]> Run(string code)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var printed = await ContainerRuntimeHarness.Run(ctx, env, "On Error Resume Next\n" + code);
        return printed.Select(v => Convert.ToString(v.Value, CultureInfo.InvariantCulture) ?? "").ToArray();
    }

    [AvaloniaFact]
    public async Task Defaults()
    {
        (await Run("""
            Debug.Print Animation1.AutoPlay, Animation1.Center, Animation1.BackStyle, Animation1.Enabled
            Debug.Print Animation1.BackColor, Animation1.OLEDropMode, Animation1.TabStop
            Debug.Print Animation1.CausesValidation, Animation1.ToolTipText, Animation1.Tag, Animation1.HelpContextID
            Debug.Print Animation1.Width, Animation1.Height
            """)).Should().Equal("False", "False", "0", "True", "-2147483633", "0", "True", "True", "", "", "0", "2400", "1200");
    }

    [AvaloniaTheory]
    [InlineData("Animation1.Play", "35755")]
    [InlineData("Animation1.Stop", "0")]
    [InlineData("Animation1.Close: Animation1.Close", "0")]
    [InlineData("Animation1.Center = True", "382")]
    [InlineData("Animation1.BackStyle = 1", "382")]
    [InlineData("Animation1.hWnd = 0", "450")]
    [InlineData("Dim v As Variant: v = Animation1", "438")]
    [InlineData("Animation1.OLEDropMode = 2", "380")]
    [InlineData("Animation1.Open \"\"", "52")]
    [InlineData("Animation1.Open \"missing-animation-1096.avi\"", "53")]
    public async Task Measured_errors(string code, string error)
    {
        (await Run(code + "\nDebug.Print Err.Number, Animation1.Center, Animation1.BackStyle")).Should().Equal(error, "False", "0");
    }

    [AvaloniaTheory]
    [InlineData("AutoPlay", 1)]
    [InlineData("AutoPlay", 2)]
    [InlineData("AutoPlay", -1)]
    [InlineData("Enabled", 2)]
    public async Task Numeric_booleans(string property, int number)
    {
        (await Run($"Animation1.{property} = {number}\nDebug.Print Animation1.{property}")).Should().Equal("True");
    }

    [AvaloniaFact]
    public async Task Folder_and_invalid_file_errors_leave_no_clip()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".avi");
        File.WriteAllText(path, "not an AVI file");
        try
        {
            (await Run($"Animation1.Open \"{Path.GetDirectoryName(path)}\"\nDebug.Print Err.Number\nAnimation1.Open \"{path}\"\nDebug.Print Err.Number\nAnimation1.Play\nDebug.Print Err.Number"))
                .Should().Equal("53", "35752", "35755");
        }
        finally { File.Delete(path); }
    }
    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("32767")]
    [InlineData("32768")]
    [InlineData("65535")]
    [InlineData("65536")]
    [InlineData("1.5")]
    [InlineData("1, 17, 65536")]
    [InlineData("1, 5, 2")]
    [InlineData("1, , 5")]
    [InlineData(", 3")]
    public async Task Play_accepts_measured_arguments_and_Close_clears_size(string arguments)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".avi");
        File.WriteAllBytes(path, Avi(8, 0, Enumerable.Repeat(new byte[8], 17).ToArray()));
        try
        {
            var code = $"""
                Animation1.Open "{path}"
                Debug.Print Animation1.Width, Animation1.Height
                Animation1.Play {arguments}
                Debug.Print Err.Number
                Animation1.Close
                Debug.Print Animation1.Width, Animation1.Height
                Animation1.Play
                Debug.Print Err.Number
                """;
            (await Run(code)).Should().Equal("2400", "1200", "0", "0", "0", "35755");
        }
        finally { File.Delete(path); }
    }

    [AvaloniaTheory]
    [InlineData("1, -1", "380")]
    [InlineData("\"x\"", "13")]
    [InlineData("Null", "13")]
    public async Task Play_validates_arguments_on_an_open_clip(string arguments, string error)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".avi");
        File.WriteAllBytes(path, Avi(8, 0, [new byte[8]]));
        try
        {
            var code = $"""
                Animation1.Open "{path}"
                Animation1.Play {arguments}
                Debug.Print Err.Number
                Animation1.Close
                """;
            (await Run(code)).Should().Equal(error);
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public void Playback_uses_header_rate_and_Stop_freezes_the_current_frame()
    {
        var (_, canvas, _, _) = ContainerRuntimeHarness.Spawn(Form);
        var animation = (VBAnimation)ContainerRuntimeHarness.Child(canvas, "Animation1");
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".avi");
        File.WriteAllBytes(path, Avi(8, 0, Enumerable.Repeat(new byte[8], 4).ToArray(), audio: true));
        try
        {
            animation.Call("Open", [new Vb6Value(path)]);
            animation.FrameDuration.Should().Be(TimeSpan.FromSeconds(1.0 / 25));
            animation.Call("Play", [new Vb6Value(2L), new Vb6Value(1L), new Vb6Value(3L)]);
            animation.CurrentFrame.Should().Be(1);
            animation.AdvanceFrame();
            animation.CurrentFrame.Should().Be(2);
            animation.Call("Stop", []);
            animation.IsPlaying.Should().BeFalse();
            animation.AdvanceFrame();
            animation.CurrentFrame.Should().Be(2);
            animation.Call("Play", [new Vb6Value(2L), new Vb6Value(1L), new Vb6Value(3L)]);
            for (var tick = 0; tick < 6; tick++) animation.AdvanceFrame();
            animation.CurrentFrame.Should().Be(3);
            animation.IsPlaying.Should().BeFalse();
            animation.Call("Close", []);
            animation.LoadedClip.Should().BeNull();
        }
        finally { animation.Call("Close", []); File.Delete(path); }
    }

    [AvaloniaFact]
    public void AutoPlay_Open_replacement_and_missing_index_recovery()
    {
        var (_, canvas, _, _) = ContainerRuntimeHarness.Spawn(Form);
        var animation = (VBAnimation)ContainerRuntimeHarness.Child(canvas, "Animation1");
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".avi");
        var valid = Avi(8, 0, Enumerable.Repeat(new byte[8], 4).ToArray());
        File.WriteAllBytes(path, valid);
        try
        {
            animation.TrySetProperty("AutoPlay", new Vb6Value(true));
            animation.Call("Open", [new Vb6Value(path)]);
            animation.IsPlaying.Should().BeTrue();
            animation.TrySetProperty("AutoPlay", new Vb6Value(false));
            animation.IsPlaying.Should().BeTrue();
            animation.TrySetProperty("AutoPlay", new Vb6Value(true));
            animation.Call("Open", [new Vb6Value(path)]);
            animation.Width.Should().Be(0);
            animation.Height.Should().Be(0);
            animation.Call("Close", []);
            File.WriteAllBytes(path, Avi(8, 0, [new byte[8]], index: false));
            Action open = () => animation.Call("Open", [new Vb6Value(path)]);
            open.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(-2147417848);
            animation.LoadedClip.Should().BeNull();
            animation.IsPlaying.Should().BeFalse();
            Action play = () => animation.Call("Play", []);
            play.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(35755);
            File.WriteAllBytes(path, valid);
            animation.Call("Open", [new Vb6Value(path)]);
            animation.IsPlaying.Should().BeTrue();
        }
        finally { animation.Call("Close", []); File.Delete(path); }
    }

    [AvaloniaTheory]
    [InlineData("Debug.Print Animation1.hWnd")]
    [InlineData("Animation1.OLEDrag")]
    [InlineData("Animation1.AboutBox")]
    [InlineData("Animation1.AutoPlay = Null")]
    [InlineData("Animation1.CausesValidation = False")]
    [InlineData("Animation1.BackColor = 255")]
    [InlineData("Animation1.HelpContextID = 1")]
    public async Task Unmeasured_members_refuse(string code)
    {
        var act = () => Run(code);
        await act.Should().ThrowAsync<NotImplementedException>().WithMessage("*Animation*not implemented*");
    }

    internal static byte[] Avi(int depth, int compression, byte[][] frames, int width = 1, bool audio = false, bool index = true)
    {
        byte[] header = new byte[56];
        System.Text.Encoding.ASCII.GetBytes("vids").CopyTo(header, 0);
        Write(header, 20, 1);
        Write(header, 24, 25);
        Write(header, 32, frames.Length);
        byte[] format = new byte[40 + (depth <= 8 ? (1 << depth) * 4 : 0)];
        Write(format, 0, 40);
        Write(format, 4, width);
        Write(format, 8, 2);
        format[12] = 1;
        format[14] = (byte)depth;
        Write(format, 16, compression);
        if (depth <= 8) format[46] = 255;
        byte[] video = Chunk("LIST", Join(System.Text.Encoding.ASCII.GetBytes("strl"), Chunk("strh", header), Chunk("strf", format)));
        byte[] audioHeader = new byte[56];
        System.Text.Encoding.ASCII.GetBytes("auds").CopyTo(audioHeader, 0);
        byte[] audioStream = audio ? Chunk("LIST", Join(System.Text.Encoding.ASCII.GetBytes("strl"), Chunk("strh", audioHeader))) : [];
        var movie = new List<byte[]> { System.Text.Encoding.ASCII.GetBytes("movi") };
        if (audio) movie.Add(Chunk("00wb", [1, 2, 3]));
        var entries = new List<byte[]>();
        int offset = 4;
        foreach (byte[] frame in frames)
        {
            string id = audio ? "01db" : "00db";
            byte[] entry = new byte[16];
            System.Text.Encoding.ASCII.GetBytes(id).CopyTo(entry, 0);
            Write(entry, 4, 16);
            Write(entry, 8, offset);
            Write(entry, 12, frame.Length);
            entries.Add(entry);
            byte[] chunk = Chunk(id, frame);
            movie.Add(chunk);
            offset += chunk.Length;
        }
        return Chunk("RIFF", Join(System.Text.Encoding.ASCII.GetBytes("AVI "),
            Chunk("LIST", Join(System.Text.Encoding.ASCII.GetBytes("hdrl"), audioStream, video)),
            Chunk("LIST", Join(movie.ToArray())), index ? Chunk("idx1", Join(entries.ToArray())) : []));
    }

    private static byte[] Chunk(string id, byte[] body)
    {
        byte[] result = new byte[8 + body.Length + (body.Length & 1)];
        System.Text.Encoding.ASCII.GetBytes(id).CopyTo(result, 0);
        Write(result, 4, body.Length);
        body.CopyTo(result, 8);
        return result;
    }

    private static byte[] Join(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
    private static void Write(byte[] data, int offset, int value) =>
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, 4), value);
}
