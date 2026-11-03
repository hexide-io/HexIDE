using System.Text;
using HexIDE.Runtime.Media;

namespace HexIDE.Tests;

public class AnimationClipTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    public void Uncompressed_DecodesBottomUpRowsAndPadding(int depth)
    {
        byte[] pixels = depth switch
        {
            4 => [0x10, 0, 0, 0, 0x00, 0, 0, 0],
            8 => [1, 0, 0, 0, 0, 0, 0, 0],
            16 => [0, 124, 0, 0, 0, 0, 0, 0],
            24 => [0, 0, 255, 0, 0, 0, 0, 0],
            _ => [0, 0, 255, 7, 0, 0, 0, 9]
        };
        var clip = AnimationClip.Read(Avi(depth, 0, [pixels]));
        clip.Width.Should().Be(1);
        clip.Height.Should().Be(2);
        clip.FrameDuration.Should().Be(TimeSpan.FromSeconds(1.0 / 25));
        clip.Frames.Should().ContainSingle();
        clip.Frames[0].Should().Equal(0, 0, 0, 255, 0, 0, 255, 255);
    }

    [Fact]
    public void Rle8_DecodesRunsLiteralsDeltaAndRetainedPixels()
    {
        var clip = AnimationClip.Read(Avi(8, 1,
            [[0, 3, 1, 0, 1, 0, 0, 0, 3, 1, 0, 1], [0, 2, 1, 0, 1, 1, 0, 1]], width: 3));
        clip.Frames.Count.Should().Be(2);
        clip.Frames[0].Should().Equal(
            0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255,
            0, 0, 255, 255, 0, 0, 0, 255, 0, 0, 255, 255);
        clip.Frames[1].Should().Equal(
            0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255,
            0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255);
        var sparse = AnimationClip.Read(Avi(8, 1, [[0, 2, 0, 1, 1, 1, 0, 1]]));
        sparse.Frames[0].Should().Equal(0, 0, 255, 255, 0, 0, 0, 255);
    }

    [Fact]
    public void AudioStreamBeforeVideo_IsIgnored()
    {
        var clip = AnimationClip.Read(Avi(8, 0, [[1, 0, 0, 0, 0, 0, 0, 0]], audio: true));
        clip.Frames[0].Should().Equal(0, 0, 0, 255, 0, 0, 255, 255);
    }

    [Theory]
    [InlineData(4, 2)] // RLE4
    [InlineData(24, 0x64697663)] // cvid
    public void UnsupportedCodec_IsRefused(int depth, int compression)
    {
        Action read = () => AnimationClip.Read(Avi(depth, compression, [[0, 1]]));
        read.Should().Throw<AnimationClipException>().Which.ErrorNumber.Should().Be(35752);
    }

    [Fact]
    public void MissingIndex_UsesMeasuredFreshControlError()
    {
        Action read = () => AnimationClip.Read(Avi(8, 0, [[0, 0, 0, 0, 0, 0, 0, 0]], index: false));
        read.Should().Throw<AnimationClipException>().Which.ErrorNumber.Should().Be(-2147417848);
    }

    [Fact]
    public void NonAvi_IsRefused()
    {
        Action read = () => AnimationClip.Read(Encoding.ASCII.GetBytes("not an AVI file"));
        read.Should().Throw<AnimationClipException>().Which.ErrorNumber.Should().Be(35752);
    }

    [Fact]
    public void TruncatedChunksAndPixels_AreRefused()
    {
        byte[] valid = Avi(8, 0, [[0, 0, 0, 0, 0, 0, 0, 0]]);
        for (int length = 0; length < valid.Length; length++)
        {
            byte[] truncated = valid[..length];
            Action read = () => AnimationClip.Read(truncated);
            read.Should().Throw<AnimationClipException>();
        }
        Action shortFrame = () => AnimationClip.Read(Avi(8, 0, [[1]]));
        shortFrame.Should().Throw<AnimationClipException>().Which.ErrorNumber.Should().Be(35752);
        byte[] nested = Chunk("LIST", Encoding.ASCII.GetBytes("movi"));
        for (int i = 0; i < 100; i++) nested = Chunk("LIST", Join(Encoding.ASCII.GetBytes("rec "), nested));
        byte[] recursive = Chunk("RIFF", Join(Encoding.ASCII.GetBytes("AVI "), nested));
        Action deepLists = () => AnimationClip.Read(recursive);
        deepLists.Should().Throw<AnimationClipException>().Which.ErrorNumber.Should().Be(35752);
    }

    [Fact]
    public void RleRunOutsideFrame_IsRefused()
    {
        Action read = () => AnimationClip.Read(Avi(8, 1, [[2, 1, 0, 1]]));
        read.Should().Throw<AnimationClipException>().Which.ErrorNumber.Should().Be(35752);
    }

    private static byte[] Avi(int depth, int compression, byte[][] frames, int width = 1, bool audio = false, bool index = true)
    {
        byte[] header = new byte[56];
        Encoding.ASCII.GetBytes("vids").CopyTo(header, 0);
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
        byte[] video = Chunk("LIST", Join(Encoding.ASCII.GetBytes("strl"), Chunk("strh", header), Chunk("strf", format)));
        byte[] audioHeader = new byte[56];
        Encoding.ASCII.GetBytes("auds").CopyTo(audioHeader, 0);
        byte[] audioStream = audio ? Chunk("LIST", Join(Encoding.ASCII.GetBytes("strl"), Chunk("strh", audioHeader))) : [];
        var movie = new List<byte[]> { Encoding.ASCII.GetBytes("movi") };
        if (audio) movie.Add(Chunk("00wb", [1, 2, 3]));
        var entries = new List<byte[]>();
        int offset = 4;
        foreach (byte[] frame in frames)
        {
            string id = audio ? "01db" : "00db";
            byte[] entry = new byte[16];
            Encoding.ASCII.GetBytes(id).CopyTo(entry, 0);
            Write(entry, 4, 16);
            Write(entry, 8, offset);
            Write(entry, 12, frame.Length);
            entries.Add(entry);
            byte[] chunk = Chunk(id, frame);
            movie.Add(chunk);
            offset += chunk.Length;
        }
        return Chunk("RIFF", Join(Encoding.ASCII.GetBytes("AVI "),
            Chunk("LIST", Join(Encoding.ASCII.GetBytes("hdrl"), audioStream, video)),
            Chunk("LIST", Join(movie.ToArray())), index ? Chunk("idx1", Join(entries.ToArray())) : []));
    }

    private static byte[] Chunk(string id, byte[] body)
    {
        byte[] result = new byte[8 + body.Length + (body.Length & 1)];
        Encoding.ASCII.GetBytes(id).CopyTo(result, 0);
        Write(result, 4, body.Length);
        body.CopyTo(result, 8);
        return result;
    }

    private static byte[] Join(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
    private static void Write(byte[] data, int offset, int value) =>
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, 4), value);
}
