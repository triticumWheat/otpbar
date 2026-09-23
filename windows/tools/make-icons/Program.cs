// One-time: turns the upstream PNG assets into multi-size Windows .ico files.
// Small sizes are written as 32bpp DIB entries (the format the tray loader is happiest with),
// 128/256 as PNG entries to keep the file small.
using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

// Defaults assume it is run from the repository root:
//   dotnet run --project windows/tools/make-icons
var source = args.Length > 0 ? args[0] : Path.Combine("assets", "icon");
var target = args.Length > 1 ? args[1] : Path.Combine("windows", "src", "OtpBar.App", "Assets");
Directory.CreateDirectory(target);

int[] traySizes = [16, 20, 24, 32, 48, 64];
int[] appSizes = [16, 20, 24, 32, 48, 64, 128, 256];

using (var glyph = new Bitmap(Path.Combine(source, "otpbar.png")))
{
    WriteIcon(Path.Combine(target, "tray-light.ico"), Recolor(glyph, Color.FromArgb(255, 255, 255)), traySizes);
    WriteIcon(Path.Combine(target, "tray-dark.ico"), Recolor(glyph, Color.FromArgb(28, 28, 30)), traySizes);
}
using (var app = new Bitmap(Path.Combine(source, "otpbar-app.png")))
{
    WriteIcon(Path.Combine(target, "otpbar.ico"), app, appSizes);
}
Console.WriteLine("wrote tray-light.ico, tray-dark.ico, otpbar.ico");

static Bitmap Recolor(Bitmap source, Color color)
{
    var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
    for (var y = 0; y < source.Height; y++)
    {
        for (var x = 0; x < source.Width; x++)
        {
            result.SetPixel(x, y, Color.FromArgb(source.GetPixel(x, y).A, color));
        }
    }
    return result;
}

static Bitmap Scale(Bitmap source, int size)
{
    var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var graphics = Graphics.FromImage(result);
    graphics.CompositingMode = CompositingMode.SourceCopy;
    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
    graphics.DrawImage(source, new Rectangle(0, 0, size, size));
    return result;
}

static byte[] DibEntry(Bitmap scaled)
{
    var size = scaled.Width;
    var stride = size * 4;
    var pixels = new byte[stride * size];
    for (var y = 0; y < size; y++)
    {
        for (var x = 0; x < size; x++)
        {
            var colour = scaled.GetPixel(x, y);
            var offset = (size - 1 - y) * stride + x * 4; // DIB rows run bottom-up
            pixels[offset] = colour.B;
            pixels[offset + 1] = colour.G;
            pixels[offset + 2] = colour.R;
            pixels[offset + 3] = colour.A;
        }
    }
    var mask = new byte[(size + 31) / 32 * 4 * size]; // all zero: alpha carries the transparency

    var header = new byte[40];
    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0), 40);
    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), size);
    BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), size * 2); // colour rows + mask rows
    BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(12), 1);
    BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(14), 32);
    return [.. header, .. pixels, .. mask];
}

static byte[] PngEntry(Bitmap scaled)
{
    using var buffer = new MemoryStream();
    scaled.Save(buffer, ImageFormat.Png);
    return buffer.ToArray();
}

static void WriteIcon(string path, Bitmap source, int[] sizes)
{
    var images = sizes.Select(size =>
    {
        using var scaled = Scale(source, size);
        return (Size: size, Data: size >= 128 ? PngEntry(scaled) : DibEntry(scaled));
    }).ToList();

    using var file = File.Create(path);
    using var writer = new BinaryWriter(file);
    writer.Write((ushort)0);
    writer.Write((ushort)1);
    writer.Write((ushort)images.Count);

    var offset = 6 + images.Count * 16;
    foreach (var (size, data) in images)
    {
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)(size >= 256 ? 0 : size));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(data.Length);
        writer.Write(offset);
        offset += data.Length;
    }
    foreach (var (_, data) in images)
    {
        writer.Write(data);
    }
}
