using System.Buffers.Binary;

namespace HexIDE.Runtime.Media;

// MSCOMCT2 Animation formats measured in #1096. Frames are top-down opaque BGRA.
internal sealed record AnimationClip(int Width, int Height, TimeSpan FrameDuration, IReadOnlyList<byte[]> Frames)
{
    internal static AnimationClip Read(byte[] data) => new Reader(data).Read();

    private sealed class Reader(byte[] data)
    {
        private int width, height, depth, compression, videoStream = -1;
        private uint scale, rate;
        private byte[] palette = [];
        private readonly List<byte[]> frames = [];
        private readonly List<(int Offset, int Length)> chunks = [];
        private bool indexed;

        internal AnimationClip Read()
        {
            try
            {
                if (data.Length < 12 || Id(0) != "RIFF" || Id(8) != "AVI ") throw Invalid();
                int end = checked(8 + (int)U32(4));
                if (end > data.Length) throw Invalid();
                Walk(12, end, false);
                if (videoStream < 0 || width <= 0 || height <= 0 || rate == 0 || scale == 0 || chunks.Count == 0) throw Invalid();
                if (!indexed) throw new AnimationClipException(unchecked((int)0x80010108));
                byte[] previous = new byte[checked(width * height * 4)];
                if (compression == 1)
                    for (int pixel = 0; pixel < previous.Length; pixel += 4) Color(previous, pixel, 0);
                foreach (var chunk in chunks)
                {
                    var frame = (byte[])previous.Clone();
                    if (chunk.Length != 0)
                    {
                        if (compression == 1) DecodeRle(chunk.Offset, chunk.Length, frame);
                        else DecodeRgb(chunk.Offset, chunk.Length, frame);
                    }
                    frames.Add(frame);
                    previous = frame;
                }
                return new AnimationClip(width, height, TimeSpan.FromSeconds((double)scale / rate), frames);
            }
            catch (Exception e) when (e is OverflowException or ArgumentOutOfRangeException or IndexOutOfRangeException)
            {
                throw Invalid();
            }
        }

        private void Walk(int start, int end, bool movie, int nesting = 0)
        {
            if (nesting > 64) throw Invalid();
            int stream = -1;
            for (int p = start; p < end;)
            {
                if (end - p < 8) throw Invalid();
                string id = Id(p);
                int size = checked((int)U32(p + 4)), body = p + 8, next = checked(body + size);
                if (next > end) throw Invalid();
                if (id == "LIST")
                {
                    if (size < 4) throw Invalid();
                    string kind = Id(body);
                    if (kind == "strl")
                    {
                        stream = streamCount++;
                        ReadStream(body + 4, next, stream);
                    }
                    else Walk(body + 4, next, movie || kind == "movi" || kind == "rec ", nesting + 1);
                }
                else if (id == "idx1")
                {
                    if (size == 0 || size % 16 != 0) throw Invalid();
                    indexed = true;
                }
                else if (movie && videoStream >= 0 && id.Length == 4 &&
                         id[..2] == videoStream.ToString("D2", System.Globalization.CultureInfo.InvariantCulture) &&
                         (id[2..] == "db" || id[2..] == "dc")) chunks.Add((body, size));
                p = checked(next + (size & 1));
                if (p > end) throw Invalid();
            }
        }

        private int streamCount;

        private void ReadStream(int start, int end, int stream)
        {
            bool video = false;
            for (int p = start; p < end;)
            {
                if (end - p < 8) throw Invalid();
                int size = checked((int)U32(p + 4)), body = p + 8, next = checked(body + size);
                if (next > end) throw Invalid();
                switch (Id(p))
                {
                    case "strh":
                        if (size < 48) throw Invalid();
                        video = Id(body) == "vids" && videoStream < 0;
                        if (video)
                        {
                            videoStream = stream;
                            scale = U32(body + 20);
                            rate = U32(body + 24);
                        }
                        break;
                    case "strf" when video:
                        if (size < 40 || U32(body) != 40) throw Invalid();
                        width = I32(body + 4);
                        height = I32(body + 8);
                        depth = U16(body + 14);
                        compression = I32(body + 16);
                        if (U16(body + 12) != 1 || width <= 0 || height <= 0 ||
                            !(compression == 0 && depth is 4 or 8 or 16 or 24 or 32 || compression == 1 && depth == 8)) throw Invalid();
                        if (depth <= 8)
                        {
                            int colors = U32(body + 32) == 0 ? 1 << depth : checked((int)U32(body + 32));
                            if (colors > 1 << depth || size - 40 < colors * 4) throw Invalid();
                            palette = data.AsSpan(body + 40, colors * 4).ToArray();
                        }
                        break;
                }
                p = checked(next + (size & 1));
                if (p > end) throw Invalid();
            }
        }

        private void DecodeRgb(int offset, int length, byte[] frame)
        {
            int stride = checked((width * depth + 31) / 32 * 4);
            if (length < checked(stride * height)) throw Invalid();
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int source = offset + y * stride;
                int target = ((height - 1 - y) * width + x) * 4;
                if (depth <= 8)
                {
                    int index = depth == 8 ? data[source + x] : (data[source + x / 2] >> (x % 2 == 0 ? 4 : 0)) & 15;
                    Color(frame, target, index);
                }
                else if (depth == 16)
                {
                    int pixel = U16(source + x * 2);
                    frame[target] = Expand(pixel & 31);
                    frame[target + 1] = Expand((pixel >> 5) & 31);
                    frame[target + 2] = Expand((pixel >> 10) & 31);
                }
                else data.AsSpan(source + x * (depth / 8), 3).CopyTo(frame.AsSpan(target, 3));
                frame[target + 3] = 255;
            }
        }

        private static byte Expand(int value) => (byte)((value << 3) | (value >> 2));

        private void Color(byte[] frame, int target, int index)
        {
            if (index * 4 + 3 >= palette.Length) throw Invalid();
            palette.AsSpan(index * 4, 3).CopyTo(frame.AsSpan(target, 3));
            frame[target + 3] = 255;
        }

        private void DecodeRle(int offset, int length, byte[] frame)
        {
            int x = 0, y = 0, p = offset, end = checked(offset + length);
            while (p < end)
            {
                if (end - p < 2) throw Invalid();
                int count = data[p++], value = data[p++];
                if (count != 0)
                {
                    for (int i = 0; i < count; i++) Put(value);
                }
                else if (value == 0) { x = 0; y++; }
                else if (value == 1) return;
                else if (value == 2)
                {
                    if (end - p < 2) throw Invalid();
                    x += data[p++]; y += data[p++];
                    if (x > width || y >= height) throw Invalid();
                }
                else
                {
                    if (end - p < value + (value & 1)) throw Invalid();
                    for (int i = 0; i < value; i++) Put(data[p++]);
                    p += value & 1;
                }
            }
            throw Invalid();

            void Put(int index)
            {
                if (x >= width || y >= height) throw Invalid();
                Color(frame, ((height - 1 - y) * width + x++) * 4, index);
            }
        }

        private string Id(int p) => System.Text.Encoding.ASCII.GetString(data, p, 4);
        private uint U32(int p) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p, 4));
        private int I32(int p) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p, 4));
        private ushort U16(int p) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p, 2));
        private static AnimationClipException Invalid() => new(35752);
    }
}

internal sealed class AnimationClipException(int errorNumber) : Exception
{
    internal int ErrorNumber { get; } = errorNumber;
}
