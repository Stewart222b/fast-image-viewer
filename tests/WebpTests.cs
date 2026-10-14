using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using FastImageViewer;

internal static class WebpTests
{
    private static int failures;
    private static string fixtures;
    private static string output;
    [STAThread]
    private static int Main(string[] args)
    {
        fixtures = args[0]; output = args[1];
        Run("embedded codec replaces invalid cached DLL", TestExtraction);
        Run("lossless BGRA transparency and frozen output", TestLossless);
        Run("lossy WebP and detached file ownership", TestLossy);
        Run("animated WebP first-frame canvas and offsets", TestAnimated);
        Run("large WebP native bounded preview", TestLarge);
        Run("WebP EXIF orientation", TestOrientation);
        Run("corrupt and truncated WebP rejection", TestCorrupt);
        Console.WriteLine("RESULT: " + (7 - failures) + " passed, " + failures + " failed");
        return failures == 0 ? 0 : 1;
    }
    private static void Run(string name, Action action)
    {
        try { action(); Console.WriteLine("PASS " + name); }
        catch (Exception error) { failures++; Console.WriteLine("FAIL " + name + ": " + error); }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static LoadedImage Load(string name) { return ImageLoader.Load(Path.Combine(fixtures, name)); }
    private static byte[] Pixels(LoadedImage image)
    {
        var bytes = new byte[image.Bitmap.PixelWidth * image.Bitmap.PixelHeight * 4];
        image.Bitmap.CopyPixels(bytes, image.Bitmap.PixelWidth * 4, 0); return bytes;
    }
    private static void TestExtraction()
    {
        string hash;
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FastImageViewer.Codecs.Webp"))
        using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        string directory = Path.Combine(Environment.GetEnvironmentVariable("FAST_IMAGE_VIEWER_CODEC_DIRECTORY"), "libwebp-1.6.0-" + hash.Substring(0, 12));
        Directory.CreateDirectory(directory);
        string dll = Path.Combine(directory, "FastImageViewerWebp.dll");
        File.WriteAllText(dll, "invalid cache"); Load("lossless.webp");
        using (var stream = File.OpenRead(dll))
        using (var sha = SHA256.Create()) Check(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "") == hash, "DLL was not replaced and verified");
    }
    private static void TestLossless()
    {
        LoadedImage image = Load("lossless.webp"); byte[] p = Pixels(image);
        Check(image.Bitmap.IsFrozen && image.PixelWidth == 2 && image.PixelHeight == 2 && !image.IsPreview, "dimensions or freeze");
        Check(p[0] == 0 && p[1] == 0 && p[2] == 255 && p[3] == 255, "opaque red");
        Check(p[4] == 0 && p[5] == 255 && p[6] == 0 && p[7] == 128, "straight-alpha green");
        Check(p[11] == 0, "transparent pixel");
    }
    private static void TestLossy()
    {
        string copy = Path.Combine(output, "released.webp");
        File.Copy(Path.Combine(fixtures, "lossy.webp"), copy, true);
        LoadedImage image = ImageLoader.Load(copy);
        using (var stream = new FileStream(copy, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) Check(stream.Length == image.FileSize, "file size");
        File.Delete(copy); byte[] p = Pixels(image);
        Check(image.PixelWidth == 2 && p[3] == 255 && p[7] == 128 && p[11] == 0, "lossy alpha or detached bitmap");
    }
    private static void TestAnimated()
    {
        LoadedImage image = Load("animated.webp"); byte[] p = Pixels(image);
        Check(image.PixelWidth == 4 && image.PixelHeight == 4 && image.Bitmap.PixelWidth == 4, "canvas dimensions");
        Check(p[3] == 0, "second frame was used or canvas background is opaque");
        int first = (2 * 4 + 2) * 4;
        Check(p[first + 2] == 255 && p[first + 3] == 255, "first frame offset");
        Check(p[first + 7] == 128, "frame transparency");
    }
    private static void TestLarge()
    {
        LoadedImage image = Load("large.webp");
        Check(image.IsPreview && image.PixelWidth == 6000 && image.PixelHeight == 6000, "original dimensions");
        Check((long)image.Bitmap.PixelWidth * image.Bitmap.PixelHeight <= FastImageViewer.Core.DecodeBudget.MaximumPixels, "preview exceeded budget");
        Check(image.Bitmap.PixelWidth < 6000 && image.Bitmap.PixelHeight < 6000 && image.Bitmap.IsFrozen, "native scaling or freeze");
        byte[] pixel = new byte[4]; image.Bitmap.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
        Check(pixel[0] == 255 && pixel[1] == 255 && pixel[2] == 255 && pixel[3] == 255, "large image decoded pixel");
    }
    private static void TestOrientation()
    {
        LoadedImage image = Load("oriented.webp");
        Check(image.ExifQuarterTurns == 1 && !image.ExifFlipHorizontal, "EXIF rotation was not exposed");
    }
    private static void TestCorrupt()
    {
        string path = Path.Combine(output, "corrupt.webp");
        byte[] valid = File.ReadAllBytes(Path.Combine(fixtures, "lossless.webp"));
        foreach (int length in new[] { 0, 12, valid.Length - 1 })
        {
            byte[] truncated = new byte[length]; Array.Copy(valid, truncated, length); File.WriteAllBytes(path, truncated);
            bool rejected = false;
            try { ImageLoader.Load(path); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "accepted truncated WebP of length " + length);
        }
        File.Delete(path);
    }
}
