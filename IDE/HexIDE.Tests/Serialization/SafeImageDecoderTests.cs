using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.Serialization;

/// <summary>
/// The Avalonia-free PNG safety gate (<see cref="SafeImageDecoder.IsAcceptablePng"/>): a publisher logo
/// is decoded only if it is a non-empty PNG within the byte cap whose IHDR dimensions are positive and
/// within the per-axis cap. This is the decompression-bomb defense — the dimensions are read from the
/// header and rejected <i>before</i> any pixel buffer is allocated.
/// </summary>
public class SafeImageDecoderTests
{
    // A 24-byte PNG signature + IHDR (no IDAT) — enough to exercise the header-only dimension gate.
    private static byte[] PngHeader(int width, int height)
    {
        var b = new byte[24];
        byte[] magic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        magic.CopyTo(b, 0);
        b[8] = 0; b[9] = 0; b[10] = 0; b[11] = 13;          // IHDR length = 13
        b[12] = (byte)'I'; b[13] = (byte)'H'; b[14] = (byte)'D'; b[15] = (byte)'R';
        b[16] = (byte)(width >> 24); b[17] = (byte)(width >> 16); b[18] = (byte)(width >> 8); b[19] = (byte)width;
        b[20] = (byte)(height >> 24); b[21] = (byte)(height >> 16); b[22] = (byte)(height >> 8); b[23] = (byte)height;
        return b;
    }

    [Fact]
    public void Null_or_empty_is_rejected()
    {
        SafeImageDecoder.IsAcceptablePng(null).Should().BeFalse();
        SafeImageDecoder.IsAcceptablePng([]).Should().BeFalse();
    }

    [Fact]
    public void A_well_formed_small_png_header_passes()
    {
        SafeImageDecoder.IsAcceptablePng(PngHeader(64, 64)).Should().BeTrue();
        SafeImageDecoder.IsAcceptablePng(PngHeader(1024, 1024)).Should().BeTrue();   // exactly at the cap
    }

    [Fact]
    public void Non_png_magic_is_rejected()
    {
        var bmp = PngHeader(64, 64);
        bmp[0] = 0x42; bmp[1] = 0x4D;   // "BM" — not PNG
        SafeImageDecoder.IsAcceptablePng(bmp).Should().BeFalse();
        SafeImageDecoder.IsAcceptablePng(new byte[24]).Should().BeFalse();   // all-zero: no PNG magic
    }

    [Fact]
    public void Truncated_header_is_rejected()
    {
        SafeImageDecoder.IsAcceptablePng(new byte[10]).Should().BeFalse();
    }

    [Fact]
    public void Over_byte_cap_is_rejected()
    {
        // 2 MB of zeros: rejected purely on size, before any header parse.
        SafeImageDecoder.IsAcceptablePng(new byte[2 * 1024 * 1024]).Should().BeFalse();
        // A valid small header rejected by a tighter custom byte cap.
        SafeImageDecoder.IsAcceptablePng(PngHeader(64, 64), maxBytes: 10).Should().BeFalse();
    }

    [Fact]
    public void Over_dimension_cap_is_rejected()
    {
        SafeImageDecoder.IsAcceptablePng(PngHeader(1025, 64)).Should().BeFalse();    // width over the cap
        SafeImageDecoder.IsAcceptablePng(PngHeader(64, 4000)).Should().BeFalse();    // height over — the bomb shape
    }

    [Fact]
    public void Pathological_dimensions_are_rejected()
    {
        SafeImageDecoder.IsAcceptablePng(PngHeader(0, 64)).Should().BeFalse();                            // zero
        SafeImageDecoder.IsAcceptablePng(PngHeader(unchecked((int)0xFFFFFFFF), 64)).Should().BeFalse();   // bit-31 set → negative
        SafeImageDecoder.IsAcceptablePng(PngHeader(int.MaxValue, 64)).Should().BeFalse();                 // huge positive
    }

    [Fact]
    public void Decode_returns_null_for_rejected_input_without_throwing()
    {
        // All rejected by the gate before any Bitmap is constructed, so this is safe with no Avalonia
        // platform. The valid-PNG happy path (a real Bitmap) is proven by the live Options-page snapshot.
        SafeImageDecoder.DecodeBoundedPng(null).Should().BeNull();
        SafeImageDecoder.DecodeBoundedPng(new byte[10]).Should().BeNull();
        SafeImageDecoder.DecodeBoundedPng(PngHeader(5000, 5000)).Should().BeNull();
    }
}
