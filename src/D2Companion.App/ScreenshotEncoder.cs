using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace D2Companion.App;

/// <summary>
/// Turns a screenshot into a JPEG sized for the vision API: anything over ~1568px on the
/// long edge is downscaled (Anthropic's guidance — larger is resized server-side anyway,
/// so sending more pixels only costs upload time and tokens).
/// </summary>
public static class ScreenshotEncoder
{
    public const int MaxLongEdge = 1568;

    public static byte[] ToJpeg(BitmapSource image, int maxLongEdge = MaxLongEdge, int quality = 80)
    {
        ArgumentNullException.ThrowIfNull(image);

        BitmapSource frame = image;
        var longEdge = Math.Max(image.PixelWidth, image.PixelHeight);
        if (longEdge > maxLongEdge)
        {
            var scale = (double)maxLongEdge / longEdge;
            frame = new TransformedBitmap(image, new ScaleTransform(scale, scale));
        }

        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(frame));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
