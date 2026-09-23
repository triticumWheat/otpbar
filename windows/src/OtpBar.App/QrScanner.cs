using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OtpBar.Core;
using ZXing;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace OtpBar.App;

/// <summary>A piece of the desktop as BGRA pixels, ready to decode.</summary>
internal sealed record ScreenShot(byte[] Pixels, int Width, int Height);

internal static class QrScanner
{
    public static string Decode(byte[] bgra, int width, int height)
    {
        var reader = new BarcodeReaderGeneric { AutoRotate = true };
        reader.Options.TryHarder = true;
        reader.Options.PossibleFormats = [BarcodeFormat.QR_CODE];
        var result = reader.Decode(new RGBLuminanceSource(bgra, width, height, RGBLuminanceSource.BitmapFormat.BGRA32));
        return result?.Text ?? throw OtpException.NoCodeFound();
    }

    public static string Decode(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return Decode(pixels, converted.PixelWidth, converted.PixelHeight);
    }

    public static string DecodeFile(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        return Decode(decoder.Frames[0]);
    }

    public static string Decode(ScreenShot shot) => Decode(shot.Pixels, shot.Width, shot.Height);

    public static Drawing.Rectangle VirtualScreen => Forms.SystemInformation.VirtualScreen;

    /// <summary>
    /// Copies one rectangle of the desktop, in physical pixels. Only the selected area is read:
    /// a whole 4K-wide virtual desktop would be tens of megabytes per attempt.
    /// </summary>
    public static ScreenShot Capture(Drawing.Rectangle area)
    {
        if (area.Width <= 0 || area.Height <= 0)
        {
            throw OtpException.NoCodeFound();
        }
        using var bitmap = new Drawing.Bitmap(area.Width, area.Height, Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(area.X, area.Y, 0, 0, bitmap.Size, Drawing.CopyPixelOperation.SourceCopy);
        }
        var locked = bitmap.LockBits(
            new Drawing.Rectangle(0, 0, area.Width, area.Height),
            Drawing.Imaging.ImageLockMode.ReadOnly,
            Drawing.Imaging.PixelFormat.Format32bppArgb);
        var pixels = new byte[area.Width * area.Height * 4];
        Marshal.Copy(locked.Scan0, pixels, 0, pixels.Length);
        bitmap.UnlockBits(locked);
        return new ScreenShot(pixels, area.Width, area.Height);
    }
}
