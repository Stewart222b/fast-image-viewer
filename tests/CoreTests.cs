using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FastImageViewer;

internal static class CoreTests
{
    private static int passed;
    private static int failed;
    private static string samples;

    [STAThread]
    private static int Main(string[] args)
    {
        samples = Path.Combine(args[0], "samples");
        Directory.CreateDirectory(samples);
        Run("mouse zoom anchor", TestZoom);
        Run("fit and resize", TestFit);
        Run("custom resize center and pan", TestPanResize);
        Run("zoom bounds preserve anchor", TestZoomBounds);
        Run("high DPI fit and gradual huge-image zoom", TestDeviceScale);
        Run("natural sorting and duplicate paths", TestSorting);
        Run("supported file extensions", TestExtensions);
        Run("image codecs, pixels, freeze and released files", TestCodecs);
        Run("large image bounded preview", TestLargeImage);
        Run("EXIF orientation metadata", TestOrientation);
        Run("corrupt and missing images", TestFailures);
        Run("generated landscape sample", CreateLandscape);
        Console.WriteLine("RESULT: " + passed + " passed, " + failed + " failed");
        return failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Near(double actual, double expected, string message)
    {
        Check(Math.Abs(actual - expected) < 0.000001, message + ": " + actual + " != " + expected);
    }

    private static void SamePoint(Point actual, Point expected, string message)
    {
        Near(actual.X, expected.X, message + " X");
        Near(actual.Y, expected.Y, message + " Y");
    }

    private static Viewport LargeViewport()
    {
        var view = new Viewport();
        view.Resize(800, 600);
        view.SetImage(1600, 1200);
        return view;
    }

    private static void TestZoom()
    {
        var view = LargeViewport();
        Point anchor = view.ImagePointAt(170, 220);
        view.ZoomAt(1.5, 170, 220);
        Near(view.Scale, 0.75, "zoom scale");
        SamePoint(view.ImagePointAt(170, 220), anchor, "mouse anchored pixel");
        Check(!view.IsFit, "zoom exits fit mode");
        view.ActualSize();
        Near(view.Scale, 1, "actual size");
        SamePoint(view.ImagePointAt(400, 300), new Point(800, 600), "actual size centers image");
    }

    private static void TestFit()
    {
        var view = LargeViewport();
        Near(view.Scale, 0.5, "fit scale");
        view.Resize(400, 600);
        Near(view.Scale, 0.25, "fit responds to resize");
        Near(view.OffsetX, 0, "fit horizontal center");
        Near(view.OffsetY, 150, "fit vertical center");
        Check(view.IsFit, "resize keeps fit mode");
        view.SetImage(100, 60);
        Near(view.Scale, 1, "fit must not enlarge small images");
        Near(view.OffsetX, 150, "small image centered horizontally");
        Near(view.OffsetY, 270, "small image centered vertically");
    }

    private static void TestPanResize()
    {
        var view = LargeViewport();
        view.ZoomAt(2, 123, 234);
        double x = view.OffsetX, y = view.OffsetY;
        view.Pan(71, -29);
        Near(view.OffsetX, x + 71, "pan X");
        Near(view.OffsetY, y - 29, "pan Y");
        Point center = view.ImagePointAt(400, 300);
        double scale = view.Scale;
        view.Resize(1000, 700);
        Near(view.Scale, scale, "custom resize keeps scale");
        SamePoint(view.ImagePointAt(500, 350), center, "custom resize keeps central pixel");
    }

    private static void TestZoomBounds()
    {
        var view = LargeViewport();
        Point anchor = view.ImagePointAt(317, 211);
        view.ZoomAt(1000000, 317, 211);
        Near(view.Scale, 32, "maximum scale");
        SamePoint(view.ImagePointAt(317, 211), anchor, "maximum zoom anchor");
        view.ZoomAt(10, 317, 211);
        SamePoint(view.ImagePointAt(317, 211), anchor, "already maximum zoom anchor");
        view.ZoomAt(0.00000001, 317, 211);
        Near(view.Scale, 0.01, "minimum scale");
        SamePoint(view.ImagePointAt(317, 211), anchor, "minimum zoom anchor");
        view.ZoomAt(0.1, 317, 211);
        SamePoint(view.ImagePointAt(317, 211), anchor, "already minimum zoom anchor");
    }

    private static void TestSorting()
    {
        string prefix = "\u56fe\u7247";
        string[] names = { prefix + "10.png", prefix + "999999999999999999999999.png", prefix + "2.png", prefix + "1000000000000000000000000.png" };
        var paths = new List<string>();
        foreach (string name in names) paths.Add(Path.Combine(samples, name));
        paths.Add(paths[0].ToUpperInvariant());
        paths.Add(Path.Combine(samples, "ignored.txt"));
        List<string> sorted = ImageFiles.FromPaths(paths);
        Check(sorted.Count == 4, "filter unsupported and duplicate paths");
        Check(Path.GetFileName(sorted[0]) == names[2], "2 comes before 10");
        Check(Path.GetFileName(sorted[1]) == names[0], "10 comes second");
        Check(Path.GetFileName(sorted[2]) == names[1], "huge integer ordering");
        Check(Path.GetFileName(sorted[3]) == names[3], "huge integer ordering by digit count");
    }

    private static void TestDeviceScale()
    {
        var view = new Viewport();
        view.DeviceScale = 1.5;
        view.Resize(800, 600);
        view.SetImage(100, 60);
        Near(view.Scale, 2.0 / 3, "small image fits at native physical pixels on 150% DPI");
        view.DeviceScale = 1;
        view.SetImage(160000, 120000);
        Near(view.Scale, 0.005, "huge image can fit below one percent");
        Point anchor = view.ImagePointAt(170, 230);
        view.ZoomAt(1.1, 170, 230);
        Near(view.Scale, 0.0055, "huge image zoom remains gradual");
        SamePoint(view.ImagePointAt(170, 230), anchor, "huge image zoom anchor");
        view.ZoomAt(0.0001, 170, 230);
        Near(view.Scale, 0.005, "huge image lower bound retains fit scale");
        SamePoint(view.ImagePointAt(170, 230), anchor, "huge image minimum zoom anchor");
    }

    private static void TestExtensions()
    {
        foreach (string extension in new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".ico", ".wdp", ".jxr" })
            Check(ImageFiles.IsSupported("image" + extension.ToUpperInvariant()), "supported extension " + extension);
        foreach (string path in new[] { "image.txt", "image.svg", "image", "", null })
            Check(!ImageFiles.IsSupported(path), "unsupported path " + path);
    }

    private static BitmapSource Pixels()
    {
        const int width = 64, height = 48;
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int i = (y * width + x) * 4;
            pixels[i] = (byte)(x * 4); pixels[i + 1] = (byte)(y * 5); pixels[i + 2] = 190; pixels[i + 3] = 255;
        }
        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
    }

    private static void Save(BitmapEncoder encoder, BitmapSource bitmap, string path)
    {
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None)) encoder.Save(stream);
    }

    private static void TestCodecs()
    {
        BitmapEncoder[] encoders = { new PngBitmapEncoder(), new JpegBitmapEncoder(), new BmpBitmapEncoder(), new GifBitmapEncoder(), new TiffBitmapEncoder() };
        string[] extensions = { ".png", ".jpg", ".bmp", ".gif", ".tiff" };
        for (int i = 0; i < encoders.Length; i++)
        {
            string path = Path.Combine(samples, "codec" + extensions[i]);
            Save(encoders[i], Pixels(), path);
            LoadedImage loaded = ImageLoader.Load(path);
            Check(loaded.Path == Path.GetFullPath(path), "full file path");
            Check(loaded.PixelWidth == 64 && loaded.PixelHeight == 48, "original pixel dimensions " + extensions[i]);
            Check(loaded.Bitmap.PixelWidth == 64 && loaded.Bitmap.PixelHeight == 48, "decoded pixel dimensions " + extensions[i]);
            Check(loaded.Bitmap.IsFrozen, "decoded bitmap frozen " + extensions[i]);
            Check(!loaded.IsPreview, "small image is full resolution");
            Check(loaded.FileSize == new FileInfo(path).Length, "file size");
            if (extensions[i] == ".png")
            {
                Check(Math.Abs(loaded.Bitmap.DpiX - 96) < 0.02, "PNG DPI rounds to 96 within pHYs precision");
                Check(Math.Abs(loaded.Bitmap.Width - 64) < 0.02, "PNG DIP width matches pixels within pHYs precision");
            }
            using (var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Check(exclusive.Length > 0, "file can be exclusively reopened after load");
            string renamed = Path.Combine(samples, "renamed" + extensions[i]);
            if (File.Exists(renamed)) File.Delete(renamed);
            File.Move(path, renamed);
            File.Move(renamed, path);
            byte[] pixel = new byte[loaded.Bitmap.PixelWidth * loaded.Bitmap.PixelHeight * ((loaded.Bitmap.Format.BitsPerPixel + 7) / 8)];
            loaded.Bitmap.CopyPixels(pixel, loaded.Bitmap.PixelWidth * ((loaded.Bitmap.Format.BitsPerPixel + 7) / 8), 0);
        }
        Check(ImageFiles.InDirectory(samples).Count >= 5, "directory discovers image files");
        Check(ImageFiles.InDirectory(Path.Combine(samples, "codec.png")).Count >= 5, "file path discovers sibling images");
    }

    private static void MustThrow(Action operation, string message)
    {
        bool threw = false;
        try { operation(); } catch { threw = true; }
        Check(threw, message);
    }

    private static void TestLargeImage()
    {
        const int width = 8000, height = 4001;
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, new byte[width * height], width);
        string path = Path.Combine(samples, "large-preview.png");
        Save(new PngBitmapEncoder(), bitmap, path);
        LoadedImage loaded = ImageLoader.Load(path);
        Check(loaded.IsPreview, "image above 32 million pixels uses preview");
        Check(loaded.PixelWidth == width && loaded.PixelHeight == height, "preview retains original pixel dimensions");
        Check((long)loaded.Bitmap.PixelWidth * loaded.Bitmap.PixelHeight <= 32000000, "preview stays within memory pixel budget");
        Check(loaded.Bitmap.IsFrozen, "preview frozen");
        using (var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    }

    private static void TestOrientation()
    {
        int[] turns = { 0, 0, 2, 2, 1, 1, 3, 3 };
        bool[] flips = { false, true, false, true, true, false, true, false };
        for (int orientation = 1; orientation <= 8; orientation++)
        {
            string path = Path.Combine(samples, "orientation" + orientation + ".jpg");
            var metadata = new BitmapMetadata("jpg");
            metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)orientation);
            var encoder = new JpegBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(Pixels(), null, metadata, null));
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None)) encoder.Save(stream);
            LoadedImage loaded = ImageLoader.Load(path);
            Check(loaded.ExifQuarterTurns == turns[orientation - 1], "EXIF quarter turns " + orientation);
            Check(loaded.ExifFlipHorizontal == flips[orientation - 1], "EXIF horizontal flip " + orientation);
        }
    }

    private static void TestFailures()
    {
        string corrupt = Path.Combine(samples, "corrupt.png");
        File.WriteAllText(corrupt, "This is not an image.");
        MustThrow(delegate { ImageLoader.Load(corrupt); }, "corrupt image should fail");
        using (var exclusive = new FileStream(corrupt, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        MustThrow(delegate { ImageLoader.Load(Path.Combine(samples, "missing.png")); }, "missing image should fail");
    }

    private static void CreateLandscape()
    {
        var visual = new DrawingVisual();
        using (DrawingContext drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new LinearGradientBrush(Color.FromRgb(27, 50, 88), Color.FromRgb(235, 172, 125), 90), null, new Rect(0, 0, 1600, 1000));
            drawing.DrawEllipse(new SolidColorBrush(Color.FromRgb(255, 219, 163)), null, new Point(1160, 290), 85, 85);
            DrawMountains(drawing, Color.FromRgb(102, 105, 132), new[] { new Point(0, 720), new Point(300, 340), new Point(540, 650), new Point(830, 290), new Point(1160, 650), new Point(1460, 420), new Point(1600, 640) });
            DrawMountains(drawing, Color.FromRgb(48, 74, 100), new[] { new Point(0, 760), new Point(240, 570), new Point(590, 810), new Point(1010, 500), new Point(1360, 760), new Point(1600, 640) });
            drawing.DrawRectangle(new LinearGradientBrush(Color.FromRgb(60, 96, 114), Color.FromRgb(17, 42, 66), 90), null, new Rect(0, 820, 1600, 180));
            for (int i = 0; i < 10; i++)
                drawing.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(90, 239, 188, 138)), 3), new Point(1030 - i * 12, 840 + i * 14), new Point(1240 + i * 12, 840 + i * 14));
        }
        var bitmap = new RenderTargetBitmap(1600, 1000, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        Save(new PngBitmapEncoder(), bitmap, Path.Combine(samples, "mountain-lake.png"));
    }

    private static void DrawMountains(DrawingContext drawing, Color color, Point[] points)
    {
        var shape = new StreamGeometry();
        using (StreamGeometryContext geometry = shape.Open())
        {
            geometry.BeginFigure(new Point(0, 1000), true, true);
            geometry.PolyLineTo(points, true, false);
            geometry.LineTo(new Point(1600, 1000), true, false);
        }
        drawing.DrawGeometry(new SolidColorBrush(color), null, shape);
    }
}
