using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using D2Companion.App;
using D2Companion.Brain;
using Xunit;

namespace D2Companion.Tests;

public sealed class ScreenshotTests
{
    [Fact]
    public void BuildImageContent_PutsImageFirstThenText()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };

        var content = D2Brain.BuildImageContent("What do you see?", bytes, "image/jpeg");

        Assert.Equal(2, content.Count);
        // Serialize rather than poke union internals: this is exactly the wire shape.
        var imageJson = JsonSerializer.Serialize(content[0]);
        Assert.Contains("image/jpeg", imageJson);
        Assert.Contains(Convert.ToBase64String(bytes), imageJson);
        var textJson = JsonSerializer.Serialize(content[1]);
        Assert.Contains("What do you see?", textJson);
    }

    [Fact]
    public void ToJpeg_DownscalesToTheMaxLongEdge()
    {
        var wide = SolidBitmap(3200, 800);

        var jpeg = ScreenshotEncoder.ToJpeg(wide);

        var decoded = Decode(jpeg);
        Assert.Equal(ScreenshotEncoder.MaxLongEdge, decoded.PixelWidth);
        Assert.Equal(392, decoded.PixelHeight); // 800 * (1568 / 3200)
    }

    [Fact]
    public void ToJpeg_LeavesSmallImagesAlone()
    {
        var small = SolidBitmap(640, 480);

        var decoded = Decode(ScreenshotEncoder.ToJpeg(small));

        Assert.Equal(640, decoded.PixelWidth);
        Assert.Equal(480, decoded.PixelHeight);
    }

    [Fact]
    public void ViewScreenshotTool_IsDefined_WithoutRationale()
    {
        var tool = Assert.Single(D2Tools.Definitions, t => t.Name == "view_screenshot");
        Assert.False(tool.InputSchema?.Properties?.ContainsKey("rationale") ?? false,
            "view_screenshot is read-only and shouldn't take a rationale.");
    }

    [Fact]
    public void BuildScreenshotResult_WithNoImage_ExplainsTheEmptyClipboard()
    {
        var content = D2Brain.BuildScreenshotResult(null);

        Assert.True(content.TryPickString(out var message));
        Assert.Contains("clipboard", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildScreenshotResult_WithImage_ReturnsImageThenTextBlocks()
    {
        var bytes = new byte[] { 9, 8, 7 };

        var content = D2Brain.BuildScreenshotResult(new CapturedImage(bytes, "image/jpeg"));

        Assert.True(content.TryPickBlocks(out var blocks));
        Assert.Equal(2, blocks!.Count);
        var imageJson = JsonSerializer.Serialize(blocks[0]);
        Assert.Contains("image/jpeg", imageJson);
        Assert.Contains(Convert.ToBase64String(bytes), imageJson);
    }

    [Fact]
    public void ViewScreenshot_WithoutSource_FallsBackToAskingThePlayer()
    {
        var service = new D2Companion.Core.CharacterService(
            new D2Companion.Core.Persistence.SqliteCharacterStore(
                Path.Combine(Path.GetTempPath(), $"d2c-{Guid.NewGuid():N}.db")));

        var result = D2Tools.Execute(service, "view_screenshot",
            new Dictionary<string, JsonElement>());

        Assert.Contains("describe", result);
    }

    private static BitmapSource SolidBitmap(int width, int height)
    {
        var stride = width * 4;
        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null,
            new byte[stride * height], stride);
    }

    private static BitmapFrame Decode(byte[] jpeg)
    {
        using var stream = new MemoryStream(jpeg);
        return BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
    }
}
