using System;
using System.Collections.Generic;
using System.IO;
using BitMiracle.LibTiff.Classic;
using FastImageViewer;
using FastImageViewer.Core;
using FastImageViewer.Imaging;
using SkiaSharp;

namespace FastImageViewer.CrossTests
{
    internal static class CrossTests
    {
        private static int passed;
        private static int failed;
        private static string samples;
        private static readonly ImagePoint Anchor = new ImagePoint(173, 219);

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
            Run("platform path identity and case-distinct files", TestPathIdentity);
            Run("supported file extensions", TestExtensions);
            Run("directory listing", TestDirectory);
            Run("decode budget", TestBudget);
            Run("exif orientation map", TestExifMap);
            foreach (int delta in new[] { 1, 15, 30, 60, 120 })
            {
                int value = delta;
                Run("fractional delta " + value, delegate
                {
                    Viewport view = WheelView();
                    var wheel = new WheelZoom(view);
                    wheel.Process(value, Anchor, 0, true);
                    WheelNear(wheel.TargetScale, Math.Pow(1.18, value / 120.0));
                    wheel.Tick(1);
                    WheelNear(view.Scale, Math.Pow(1.18, value / 120.0));
                });
            }
            Run("120 equals eight 15 inputs", TestEightFifteens);
            Run("log scale interpolation without overshoot", TestInterpolation);
            Run("small reversal immediately changes direction", TestReversal);
            Run("anchor pixel preserved across all frames", TestAnchorFrames);
            Run("both zoom bounds", TestWheelBounds);
            Run("huge image fit lower bound", TestHugeWheel);
            Run("zero delta changes no pending state", TestZeroDelta);
            Run("cancel prevents subsequent view update", TestCancel);
            Run("reduced motion applies fractional delta immediately", TestReducedMotion);
            Run("navigation accumulates delta and preserves remainder", TestNavigation);
            Run("double delta matches int delta", TestDoubleDelta);
            Run("image codecs and released files", TestCodecs);
            Run("exif jpeg orientation", TestExifJpeg);
            Run("tiff orientation stays in the tag", TestTiffOrientation);
            Run("tiff alpha survives decode, rotation and compositing", TestTiffAlpha);
            Run("large image bounded preview", TestLargeImage);
            Run("corrupt and missing images", TestFailures);
            Run("orientation pixel transform", TestPixelTransform);
            Run("checkerboard work stays within visible bounds", DesktopRegressionTests.TestCheckerboard);
            Run("monitor scaling refreshes fit and physical zoom", DesktopRegressionTests.TestScaling);
            Run("detached canvas ignores former monitor scaling", DesktopRegressionTests.TestDetach);
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

        private static void WheelNear(double actual, double expected)
        {
            Check(Math.Abs(actual - expected) < 1e-9, actual + " != " + expected);
        }

        private static void SamePoint(ImagePoint actual, ImagePoint expected, string message)
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

        private static Viewport WheelView()
        {
            var view = new Viewport();
            view.Resize(800, 600);
            view.SetImage(2000, 1000);
            view.ActualSize();
            return view;
        }

        private static void TestZoom()
        {
            var view = LargeViewport();
            ImagePoint anchor = view.ImagePointAt(170, 220);
            view.ZoomAt(1.5, 170, 220);
            Near(view.Scale, 0.75, "zoom scale");
            SamePoint(view.ImagePointAt(170, 220), anchor, "mouse anchored pixel");
            Check(!view.IsFit, "zoom exits fit mode");
            view.ActualSize();
            Near(view.Scale, 1, "actual size");
            SamePoint(view.ImagePointAt(400, 300), new ImagePoint(800, 600), "actual size centers image");
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
            ImagePoint center = view.ImagePointAt(400, 300);
            double scale = view.Scale;
            view.Resize(1000, 700);
            Near(view.Scale, scale, "custom resize keeps scale");
            SamePoint(view.ImagePointAt(500, 350), center, "custom resize keeps central pixel");
        }

        private static void TestZoomBounds()
        {
            var view = LargeViewport();
            ImagePoint anchor = view.ImagePointAt(317, 211);
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
            ImagePoint anchor = view.ImagePointAt(170, 230);
            view.ZoomAt(1.1, 170, 230);
            Near(view.Scale, 0.0055, "huge image zoom remains gradual");
            SamePoint(view.ImagePointAt(170, 230), anchor, "huge image zoom anchor");
            view.ZoomAt(0.0001, 170, 230);
            Near(view.Scale, 0.005, "huge image lower bound retains fit scale");
            SamePoint(view.ImagePointAt(170, 230), anchor, "huge image minimum zoom anchor");
        }

        private static void TestSorting()
        {
            string prefix = "\u56fe\u7247";
            string[] names = { prefix + "10.png", prefix + "999999999999999999999999.png", prefix + "2.png", prefix + "1000000000000000000000000.png" };
            var paths = new List<string>();
            foreach (string name in names) paths.Add(Path.Combine(samples, name));
            paths.Add(paths[0]);
            paths.Add(Path.Combine(samples, "ignored.txt"));
            List<string> sorted = ImageFiles.FromPaths(paths);
            Check(sorted.Count == 4, "filter unsupported and duplicate paths");
            Check(Path.GetFileName(sorted[0]) == names[2], "2 comes before 10");
            Check(Path.GetFileName(sorted[1]) == names[0], "10 comes second");
            Check(Path.GetFileName(sorted[2]) == names[1], "huge integer ordering");
            Check(Path.GetFileName(sorted[3]) == names[3], "huge integer ordering by digit count");
        }

        private static void TestExtensions()
        {
            foreach (string extension in new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".ico", ".wdp", ".jxr" })
                Check(ImageFiles.IsSupported("image" + extension.ToUpperInvariant()), "supported extension " + extension);
            foreach (string path in new[] { "image.txt", "image.svg", "image", "", null })
                Check(!ImageFiles.IsSupported(path), "unsupported path " + path);
        }

        private static void TestPathIdentity()
        {
            string lower = Path.Combine(samples, "case-files", "a.png");
            string upper = Path.Combine(samples, "case-files", "A.png");
            var listed = ImageFiles.FromPaths(new[] { lower, upper, lower });
            bool windows = Path.DirectorySeparatorChar == '\\';
            Check(listed.Count == (windows ? 1 : 2), "case-distinct paths must survive on Unix");
            Check(ImageFiles.PathComparer.Equals(lower, upper) == windows, "selection uses the same path identity");
            if (!windows)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(lower));
                File.WriteAllBytes(lower, new byte[] { 1 });
                File.WriteAllBytes(upper, new byte[] { 2 });
                // Only assert the two-file directory case when the volume itself preserves it.
                if (File.ReadAllBytes(lower)[0] == 1)
                    Check(ImageFiles.InDirectory(lower).Count == 2, "case-sensitive directory must list both files");
            }
        }

        private static void TestTiffAlpha()
        {
            foreach (ExtraSample alphaType in new[] { ExtraSample.ASSOCALPHA, ExtraSample.UNASSALPHA })
            {
                string path = Path.Combine(samples, "alpha-" + alphaType + ".tiff");
                using (Tiff file = Tiff.Open(path, "w"))
                {
                    file.SetField(TiffTag.IMAGEWIDTH, 3);
                    file.SetField(TiffTag.IMAGELENGTH, 1);
                    file.SetField(TiffTag.BITSPERSAMPLE, 8);
                    file.SetField(TiffTag.SAMPLESPERPIXEL, 4);
                    file.SetField(TiffTag.PHOTOMETRIC, Photometric.RGB);
                    file.SetField(TiffTag.PLANARCONFIG, PlanarConfig.CONTIG);
                    file.SetField(TiffTag.ROWSPERSTRIP, 1);
                    file.SetField(TiffTag.EXTRASAMPLES, 1, new short[] { (short)alphaType });
                    byte[] rgba = alphaType == ExtraSample.ASSOCALPHA
                        ? new byte[] { 100, 50, 25, 128, 0, 0, 0, 0, 200, 100, 50, 255 }
                        : new byte[] { 200, 100, 50, 128, 200, 100, 50, 0, 200, 100, 50, 255 };
                    Check(file.WriteScanline(rgba, 0), "write alpha TIFF");
                }
                using (Picture picture = PictureLoader.Load(path))
                using (SKBitmap rotated = ImageOrientation.Apply(picture.Pixels, 0, false, 1))
                using (var target = new SKBitmap(new SKImageInfo(1, 3)))
                using (var canvas = new SKCanvas(target))
                {
                    Check(picture.Pixels.AlphaType == SKAlphaType.Unpremul, "straight alpha pipeline");
                    SKColor translucent = rotated.GetPixel(0, 0);
                    Check(Math.Abs(translucent.Red - 200) <= 2 && Math.Abs(translucent.Green - 100) <= 2 &&
                        Math.Abs(translucent.Blue - 50) <= 2 && translucent.Alpha == 128,
                        "translucent colors must not be multiplied twice: " + translucent);
                    Check(rotated.GetPixel(0, 1).Alpha == 0, "fully transparent alpha");
                    Check(rotated.GetPixel(0, 2) == new SKColor(200, 100, 50), "opaque color");
                    canvas.Clear(SKColors.White);
                    canvas.DrawBitmap(rotated, 0, 0);
                    SKColor blended = target.GetPixel(0, 0);
                    Check(Math.Abs(blended.Red - 227) <= 2 && Math.Abs(blended.Green - 177) <= 2 &&
                        Math.Abs(blended.Blue - 152) <= 2, "white-background compositing: " + blended);
                    Check(target.GetPixel(0, 1) == SKColors.White, "transparent pixel leaves background intact");
                }
            }
        }

        private static void TestDirectory()
        {
            string directory = Path.Combine(samples, "list");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "b.png"), new byte[0]);
            File.WriteAllBytes(Path.Combine(directory, "a.jpg"), new byte[0]);
            File.WriteAllBytes(Path.Combine(directory, "note.txt"), new byte[0]);
            List<string> listed = ImageFiles.InDirectory(directory);
            Check(listed.Count == 2, "directory keeps supported files");
            Check(Path.GetFileName(listed[0]) == "a.jpg", "natural name order");
            Check(ImageFiles.InDirectory(Path.Combine(directory, "b.png")).Count == 2, "file path discovers siblings");
        }

        private static void TestBudget()
        {
            Check(!DecodeBudget.NeedsPreview(64, 48), "small image");
            Check(DecodeBudget.NeedsPreview(8000, 4001), "just over 32 million pixels");
            int width, height;
            DecodeBudget.SelectDecodePixels(8000, 4001, out width, out height);
            Check(width == 7998 && height == 0, "8000x4001 decode width " + width + "x" + height);
            DecodeBudget.SelectDecodePixels(1, 40000000, out width, out height);
            Check(width == 0 && height == 32000000, "thin image uses decode height");
        }

        private static void TestExifMap()
        {
            int[] turns = { 0, 0, 2, 2, 1, 1, 3, 3 };
            bool[] flips = { false, true, false, true, true, false, true, false };
            for (int orientation = 1; orientation <= 8; orientation++)
            {
                int quarterTurns;
                bool flip;
                ExifOrientation.Map(orientation, out quarterTurns, out flip);
                Check(quarterTurns == turns[orientation - 1], "quarter turns " + orientation);
                Check(flip == flips[orientation - 1], "flip " + orientation);
            }
        }

        private static void TestEightFifteens()
        {
            Viewport view = WheelView();
            var wheel = new WheelZoom(view);
            for (int i = 0; i < 8; i++) wheel.Process(15, Anchor, i * .005, true);
            WheelNear(wheel.TargetScale, 1.18);
            wheel.Tick(1);
            WheelNear(view.Scale, 1.18);
        }

        private static void TestInterpolation()
        {
            Viewport view = WheelView();
            var wheel = new WheelZoom(view);
            wheel.Process(120, Anchor, 0, true);
            WheelNear(view.Scale, 1);
            wheel.Tick(.04);
            WheelNear(view.Scale, Math.Sqrt(1.18));
            wheel.Tick(.08);
            WheelNear(view.Scale, 1.18);
            Check(!wheel.IsActive, "completed");
            Check(!wheel.Tick(2), "no extra frames");
        }

        private static void TestReversal()
        {
            Viewport view = WheelView();
            var wheel = new WheelZoom(view);
            wheel.Process(120, Anchor, 0, true);
            wheel.Tick(.02);
            double scale = view.Scale;
            wheel.Process(-1, Anchor, .02, true);
            Check(wheel.TargetScale < scale, "discard old positive target");
            wheel.Tick(.03);
            Check(view.Scale < scale, "next frame shrinks");
        }

        private static void TestAnchorFrames()
        {
            Viewport view = WheelView();
            var wheel = new WheelZoom(view);
            ImagePoint pixel = view.ImagePointAt(Anchor.X, Anchor.Y);
            wheel.Process(120, Anchor, 0, true);
            for (int i = 1; i <= 10; i++)
            {
                wheel.Tick(i * .01);
                ImagePoint now = view.ImagePointAt(Anchor.X, Anchor.Y);
                WheelNear(now.X, pixel.X);
                WheelNear(now.Y, pixel.Y);
            }
        }

        private static void TestWheelBounds()
        {
            Viewport view = WheelView();
            var wheel = new WheelZoom(view);
            wheel.Process(Int32.MaxValue, Anchor, 0, true);
            wheel.Tick(1);
            WheelNear(view.Scale, 32);
            wheel.Process(Int32.MinValue, Anchor, 2, true);
            wheel.Tick(3);
            WheelNear(view.Scale, .01);
        }

        private static void TestHugeWheel()
        {
            Viewport view = WheelView();
            view.SetImage(1000000, 1000000);
            var wheel = new WheelZoom(view);
            wheel.Process(Int32.MinValue, Anchor, 0, true);
            wheel.Tick(1);
            WheelNear(view.Scale, .0006);
        }

        private static void TestZeroDelta()
        {
            Viewport view = WheelView();
            var wheel = new WheelZoom(view);
            wheel.Process(120, Anchor, 0, true);
            wheel.Process(0, new ImagePoint(0, 0), .04, true);
            WheelNear(view.Scale, 1);
            wheel.Tick(.08);
            WheelNear(view.Scale, 1.18);
        }

        private static void TestCancel()
        {
            Viewport view = WheelView();
            var wheel = new WheelZoom(view);
            wheel.Process(120, Anchor, 0, true);
            wheel.Tick(.02);
            wheel.Cancel();
            view.Pan(50, 30);
            double x = view.OffsetX;
            double scale = view.Scale;
            Check(!wheel.Tick(1), "cancelled");
            WheelNear(view.Scale, scale);
            WheelNear(view.OffsetX, x);
        }

        private static void TestReducedMotion()
        {
            Viewport view = WheelView();
            var wheel = new WheelZoom(view);
            wheel.Process(1, Anchor, 12345.678, false);
            WheelNear(view.Scale, Math.Pow(1.18, 1 / 120.0));
            Check(!wheel.IsActive, "no animation");
        }

        private static void TestNavigation()
        {
            var navigation = new WheelNavigation();
            for (int i = 0; i < 7; i++) Check(navigation.Add(15) == 0, "small inputs do not navigate");
            Check(navigation.Add(30) == -1, "crossed threshold");
            Check(navigation.Add(-120) == 0, "signed remainder retained");
            Check(navigation.Add(-15) == 1, "negative threshold");
            Check(navigation.Add(240) == -2, "multiple steps");
            navigation.Add(60);
            navigation.Reset();
            Check(navigation.Add(60) == 0, "reset remainder");
        }

        private static void TestDoubleDelta()
        {
            Viewport left = WheelView();
            Viewport right = WheelView();
            var a = new WheelZoom(left);
            var b = new WheelZoom(right);
            a.Process(15, Anchor, 0, false);
            b.Process(15.0, Anchor, 0, false);
            WheelNear(left.Scale, right.Scale);
            b.Process(7.5, Anchor, 1, false);
            Check(right.Scale > left.Scale, "fractional delta still zooms");
            Check(right.Scale < left.Scale * 1.18, "half notch is below a full notch");
        }

        private static void TestCodecs()
        {
            Save(Sample(64, 48, SKColors.Red, new SKColor(10, 180, 30)), SKEncodedImageFormat.Png, Path.Combine(samples, "codec.png"));
            Save(Sample(64, 48, SKColors.Blue, SKColors.White), SKEncodedImageFormat.Jpeg, Path.Combine(samples, "codec.jpg"));
            WriteBmp(Path.Combine(samples, "codec.bmp"), 64, 48, 20, 180, 40);
            WriteGif(Path.Combine(samples, "codec.gif"), 64, 48, 240, 220, 40);
            WriteTiff(Path.Combine(samples, "codec.tiff"), 64, 48, 40);
            string[] extensions = { ".png", ".jpg", ".bmp", ".gif", ".tiff" };
            foreach (string extension in extensions)
            {
                string path = Path.Combine(samples, "codec" + extension);
                Picture loaded = null;
                try { loaded = PictureLoader.Load(path); }
                catch (Exception ex) { throw new Exception(extension + ": " + ex.Message, ex); }
                using (loaded)
                {
                    Check(loaded.Path == Path.GetFullPath(path), "full file path");
                    Check(loaded.PixelWidth == 64 && loaded.PixelHeight == 48, "original pixel dimensions " + extension);
                    Check(loaded.Pixels.Width == 64 && loaded.Pixels.Height == 48, "decoded pixel dimensions " + extension);
                    Check(!loaded.IsPreview, "small image is full resolution");
                    Check(loaded.FileSize == new FileInfo(path).Length, "file size");
                    if (extension == ".png")
                    {
                        SKColor pixel = loaded.Pixels.GetPixel(0, 0);
                        Check(pixel.Red > 200 && pixel.Green < 40 && pixel.Blue < 40, "png pixel " + pixel);
                    }
                    if (extension == ".tiff")
                    {
                        SKColor pixel = loaded.Pixels.GetPixel(0, 0);
                        Check(pixel.Red == 40 && pixel.Green == 40 && pixel.Blue == 40 && pixel.Alpha == 255, "tiff gray pixel " + pixel);
                    }
                }
                using (var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    Check(exclusive.Length > 0, "file can be exclusively reopened after load");
            }
            Check(ImageFiles.InDirectory(samples).Count >= 5, "directory discovers image files");
        }

        private static void TestExifJpeg()
        {
            int[] turns = { 0, 0, 2, 2, 1, 1, 3, 3 };
            bool[] flips = { false, true, false, true, true, false, true, false };
            string plain = Path.Combine(samples, "orientation-base.jpg");
            Save(Sample(8, 6, SKColors.Red, SKColors.Blue), SKEncodedImageFormat.Jpeg, plain);
            byte[] jpeg = File.ReadAllBytes(plain);
            for (int orientation = 1; orientation <= 8; orientation++)
            {
                string path = Path.Combine(samples, "orientation" + orientation + ".jpg");
                File.WriteAllBytes(path, InsertExif(jpeg, (ushort)orientation));
                using (Picture loaded = PictureLoader.Load(path))
                {
                    Check(loaded.ExifQuarterTurns == turns[orientation - 1], "EXIF quarter turns " + orientation);
                    Check(loaded.ExifFlipHorizontal == flips[orientation - 1], "EXIF horizontal flip " + orientation);
                    Check(loaded.Pixels.Width == 8 && loaded.Pixels.Height == 6, "decode keeps stored dimensions " + orientation);
                }
            }
        }

        private static void TestTiffOrientation()
        {
            string path = Path.Combine(samples, "orientation.tif");
            WriteRgbTiff(path, 6);
            using (Picture loaded = PictureLoader.Load(path))
            {
                Check(loaded.PixelWidth == 4 && loaded.PixelHeight == 2, "tiff keeps stored dimensions");
                Check(loaded.ExifQuarterTurns == 1 && !loaded.ExifFlipHorizontal, "tiff orientation 6 is a quarter turn");
                SKColor pixel = loaded.Pixels.GetPixel(0, 0);
                Check(pixel.Red == 255 && pixel.Green == 0 && pixel.Blue == 0, "tiff does not bake orientation into pixels " + pixel);
                Check(loaded.Pixels.GetPixel(3, 1).Red == 255 && loaded.Pixels.GetPixel(3, 1).Green == 255, "tiff keeps the stored bottom-right pixel");
            }
            using (var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Check(exclusive.Length > 0, "tiff can be exclusively reopened after load");
        }

        private static void TestLargeImage()
        {
            string path = Path.Combine(samples, "large-preview.png");
            Save(Sample(8000, 4001, SKColors.Gray, SKColors.Gray), SKEncodedImageFormat.Png, path);
            using (Picture loaded = PictureLoader.Load(path))
            {
                Check(loaded.IsPreview, "image above 32 million pixels uses preview");
                Check(loaded.PixelWidth == 8000 && loaded.PixelHeight == 4001, "preview retains original pixel dimensions");
                Check((long)loaded.Pixels.Width * loaded.Pixels.Height <= DecodeBudget.MaximumPixels, "preview stays within memory pixel budget");
                Check((long)loaded.Pixels.Width * loaded.Pixels.Height < (long)loaded.PixelWidth * loaded.PixelHeight, "preview is smaller than the original");
            }
            using (var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        }

        private static void TestFailures()
        {
            string corrupt = Path.Combine(samples, "corrupt.png");
            File.WriteAllText(corrupt, "This is not an image.");
            MustThrow(delegate { PictureLoader.Load(corrupt); }, "corrupt image should fail");
            using (var exclusive = new FileStream(corrupt, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            MustThrow(delegate { PictureLoader.Load(Path.Combine(samples, "missing.png")); }, "missing image should fail");
            string wdp = Path.Combine(samples, "unsupported.wdp");
            File.WriteAllText(wdp, "not a jxr");
            MustThrow(delegate { PictureLoader.Load(wdp); }, "wdp without a decoder should fail");
        }

        private static void TestPixelTransform()
        {
            var source = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Bgra8888, SKAlphaType.Unpremul));
            source.SetPixel(0, 0, new SKColor(255, 0, 0));
            source.SetPixel(1, 0, new SKColor(0, 255, 0));
            source.SetPixel(0, 1, new SKColor(0, 0, 255));
            source.SetPixel(1, 1, new SKColor(255, 255, 255));
            using (SKBitmap turned = ImageOrientation.Rotate(source, 1))
            {
                Check(turned.Width == 2 && turned.Height == 2, "square stays square");
                Check(Same(turned.GetPixel(1, 0), 255, 0, 0), "90 CW moves top-left to top-right");
                Check(Same(turned.GetPixel(0, 0), 0, 0, 255), "90 CW moves bottom-left to top-left");
            }
            using (SKBitmap ordered = ImageOrientation.Apply(source, 1, true, 0))
            {
                Check(Same(ordered.GetPixel(0, 0), 255, 0, 0), "rotate then flip");
                Check(Same(ordered.GetPixel(1, 0), 0, 0, 255), "rotate then flip keeps the other corner");
                Check(Same(ordered.GetPixel(0, 1), 0, 255, 0), "rotate then flip bottom");
            }
            source.Dispose();
        }

        private static bool Same(SKColor color, byte red, byte green, byte blue)
        {
            return color.Red == red && color.Green == green && color.Blue == blue && color.Alpha == 255;
        }

        private static void MustThrow(Action operation, string message)
        {
            bool threw = false;
            try { operation(); }
            catch { threw = true; }
            Check(threw, message);
        }

        private static SKBitmap Sample(int width, int height, SKColor first, SKColor rest)
        {
            var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
            bitmap.Erase(rest);
            bitmap.SetPixel(0, 0, first);
            return bitmap;
        }

        private static void Save(SKBitmap bitmap, SKEncodedImageFormat format, string path)
        {
            using (bitmap)
            using (SKData data = bitmap.Encode(format, 100))
            {
                if (data == null) throw new Exception("encoder returned no data for " + format);
                File.WriteAllBytes(path, data.ToArray());
            }
        }

        private static void WriteGif(string path, int width, int height, byte red, byte green, byte blue)
        {
            // SkiaSharp 3.119 cannot encode GIF. This is a 64x48 GIF89a frame.
            if (width != 64 || height != 48) throw new ArgumentException("fixture is 64x48");
            File.WriteAllBytes(path, Convert.FromBase64String("R0lGODlhQAAwAPcfMQAAACQAAEgAAGwAAJAAALQAANgAAPwAAAAkACQkAEgkAGwkAJAkALQkANgkAPwkAABIACRIAEhIAGxIAJBIALRIANhIAPxIAABsACRsAEhsAGxsAJBsALRsANhsAPxsAACQACSQAEiQAGyQAJCQALSQANiQAPyQAAC0ACS0AEi0AGy0AJC0ALS0ANi0APy0AADYACTYAEjYAGzYAJDYALTYANjYAPzYAAD8ACT8AEj8AGz8AJD8ALT8ANj8APz8AAAAVSQAVUgAVWwAVZAAVbQAVdgAVfwAVQAkVSQkVUgkVWwkVZAkVbQkVdgkVfwkVQBIVSRIVUhIVWxIVZBIVbRIVdhIVfxIVQBsVSRsVUhsVWxsVZBsVbRsVdhsVfxsVQCQVSSQVUiQVWyQVZCQVbSQVdiQVfyQVQC0VSS0VUi0VWy0VZC0VbS0Vdi0Vfy0VQDYVSTYVUjYVWzYVZDYVbTYVdjYVfzYVQD8VST8VUj8VWz8VZD8VbT8Vdj8Vfz8VQAAqiQAqkgAqmwAqpAAqrQAqtgAqvwAqgAkqiQkqkgkqmwkqpAkqrQkqtgkqvwkqgBIqiRIqkhIqmxIqpBIqrRIqthIqvxIqgBsqiRsqkhsqmxsqpBsqrRsqthsqvxsqgCQqiSQqkiQqmyQqpCQqrSQqtiQqvyQqgC0qiS0qki0qmy0qpC0qrS0qti0qvy0qgDYqiTYqkjYqmzYqpDYqrTYqtjYqvzYqgD8qiT8qkj8qmz8qpD8qrT8qtj8qvz8qgAA/yQA/0gA/2wA/5AA/7QA/9gA//wA/wAk/yQk/0gk/2wk/5Ak/7Qk/9gk//wk/wBI/yRI/0hI/2xI/5BI/7RI/9hI//xI/wBs/yRs/0hs/2xs/5Bs/7Rs/9hs//xs/wCQ/ySQ/0iQ/2yQ/5CQ/7SQ/9iQ//yQ/wC0/yS0/0i0/2y0/5C0/7S0/9i0//y0/wDY/yTY/0jY/2zY/5DY/7TY/9jY//zY/wD8/yT8/0j8/2z8/5D8/7T8/9j8//z8/yH/C05FVFNDQVBFMi4wAwEAAAAh+QQEBAAfACwAAAAAQAAwAAAI/wCjRBpIMJLAggMPIlRYkCFBhwkRRpQ4MFEki4gIWsSo8WKkjBU9gvTIMWRJkh87nqTIsqXLlzARjpxJkOZAmylv1typs2fOnyNjCh1KVOLJjT+RjlSqMqnIpkufhixKtapLnFh5AtWa1WdXgxQhgpUotmzYsyyZmnR6VCrKqG3j/rRKl+7Xu1zzetU7t65fqnLhsh0suDDUjn8TD8W7t/FWx4zHLkQ7mSzlhpcRqn17eK1hz505Kx79MrJpvqdJq24ZOPTm125ht11NG7VtyLcf6zZruXfl35h9G41NvDXo46KT116dWnfzyMtpG08um7Br4tGZ536+nS9v4A8zT0oEP3749OqfqRfvmz0xd9zwnWttT/r8+vTor9Mf/V5+/MjfBUeeZAIWGF5a911n34Ls7WdXd/9B6J+DijGIX4LIwUbhX/11mFtAAAA7"));
        }

        private static void WriteBmp(string path, int width, int height, byte red, byte green, byte blue)
        {
            int rowStride = (width * 3 + 3) & ~3;
            int pixelBytes = rowStride * height;
            int header = 54;
            byte[] file = new byte[header + pixelBytes];
            file[0] = (byte)'B';
            file[1] = (byte)'M';
            WriteInt(file, 2, file.Length);
            WriteInt(file, 10, header);
            WriteInt(file, 14, 40);
            WriteInt(file, 18, width);
            WriteInt(file, 22, height);
            file[26] = 1;
            file[28] = 24;
            WriteInt(file, 34, pixelBytes);
            for (int y = 0; y < height; y++)
            {
                int row = header + y * rowStride;
                for (int x = 0; x < width; x++)
                {
                    file[row + x * 3] = blue;
                    file[row + x * 3 + 1] = green;
                    file[row + x * 3 + 2] = red;
                }
            }
            File.WriteAllBytes(path, file);
        }

        private static void WriteInt(byte[] file, int offset, int value)
        {
            file[offset] = (byte)value;
            file[offset + 1] = (byte)(value >> 8);
            file[offset + 2] = (byte)(value >> 16);
            file[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteRgbTiff(string path, ushort orientation)
        {
            const int width = 4;
            const int height = 2;
            int bitsOffset = 8 + 2 + 10 * 12 + 4;
            int dataOffset = bitsOffset + 6;
            byte[] file = new byte[dataOffset + width * height * 3];
            file[0] = (byte)'I';
            file[1] = (byte)'I';
            file[2] = 42;
            file[4] = 8;
            file[8] = 10;
            int entry = 10;
            Entry(file, ref entry, 256, 4, 1, width);
            Entry(file, ref entry, 257, 4, 1, height);
            Entry(file, ref entry, 258, 3, 3, (uint)bitsOffset);
            Entry(file, ref entry, 259, 3, 1, 1);
            Entry(file, ref entry, 262, 3, 1, 2);
            Entry(file, ref entry, 273, 4, 1, (uint)dataOffset);
            Entry(file, ref entry, 274, 3, 1, orientation);
            Entry(file, ref entry, 277, 3, 1, 3);
            Entry(file, ref entry, 278, 4, 1, height);
            Entry(file, ref entry, 279, 4, 1, (uint)(width * height * 3));
            file[bitsOffset] = 8;
            file[bitsOffset + 2] = 8;
            file[bitsOffset + 4] = 8;
            byte[] pixels = { 255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255, 0, 0, 0, 0, 0, 0, 0, 0, 0, 255, 255, 0 };
            Buffer.BlockCopy(pixels, 0, file, dataOffset, pixels.Length);
            File.WriteAllBytes(path, file);
        }

        private static void WriteTiff(string path, int width, int height, byte gray)
        {
            int dataBytes = width * height;
            int dataOffset = 8 + 2 + 9 * 12 + 4;
            byte[] file = new byte[dataOffset + dataBytes];
            file[0] = (byte)'I';
            file[1] = (byte)'I';
            file[2] = 42;
            file[4] = 8;
            file[8] = 9;
            int entry = 10;
            Entry(file, ref entry, 256, 4, 1, (uint)width);
            Entry(file, ref entry, 257, 4, 1, (uint)height);
            Entry(file, ref entry, 258, 3, 1, 8);
            Entry(file, ref entry, 259, 3, 1, 1);
            Entry(file, ref entry, 262, 3, 1, 1);
            Entry(file, ref entry, 273, 4, 1, (uint)dataOffset);
            Entry(file, ref entry, 277, 3, 1, 1);
            Entry(file, ref entry, 278, 4, 1, (uint)height);
            Entry(file, ref entry, 279, 4, 1, (uint)dataBytes);
            for (int i = 0; i < dataBytes; i++) file[dataOffset + i] = gray;
            File.WriteAllBytes(path, file);
        }

        private static void Entry(byte[] file, ref int offset, ushort tag, ushort type, uint count, uint value)
        {
            file[offset] = (byte)tag;
            file[offset + 1] = (byte)(tag >> 8);
            file[offset + 2] = (byte)type;
            file[offset + 3] = (byte)(type >> 8);
            file[offset + 4] = (byte)count;
            file[offset + 5] = (byte)(count >> 8);
            file[offset + 6] = (byte)(count >> 16);
            file[offset + 7] = (byte)(count >> 24);
            file[offset + 8] = (byte)value;
            file[offset + 9] = (byte)(value >> 8);
            file[offset + 10] = (byte)(value >> 16);
            file[offset + 11] = (byte)(value >> 24);
            offset += 12;
        }

        private static byte[] InsertExif(byte[] jpeg, ushort orientation)
        {
            if (jpeg.Length < 2 || jpeg[0] != 0xFF || jpeg[1] != 0xD8) throw new Exception("missing JPEG SOI");
            byte[] tiff = new byte[26];
            tiff[0] = (byte)'I';
            tiff[1] = (byte)'I';
            tiff[2] = 42;
            tiff[4] = 8;
            tiff[8] = 1;
            tiff[10] = 0x12;
            tiff[11] = 0x01;
            tiff[12] = 3;
            tiff[14] = 1;
            tiff[18] = (byte)(orientation & 0xFF);
            tiff[19] = (byte)(orientation >> 8);
            int payload = 2 + 6 + tiff.Length;
            byte[] app = new byte[2 + payload];
            app[0] = 0xFF;
            app[1] = 0xE1;
            app[2] = (byte)(payload >> 8);
            app[3] = (byte)(payload & 0xFF);
            app[4] = (byte)'E';
            app[5] = (byte)'x';
            app[6] = (byte)'i';
            app[7] = (byte)'f';
            Buffer.BlockCopy(tiff, 0, app, 10, tiff.Length);
            byte[] output = new byte[jpeg.Length + app.Length];
            output[0] = 0xFF;
            output[1] = 0xD8;
            Buffer.BlockCopy(app, 0, output, 2, app.Length);
            Buffer.BlockCopy(jpeg, 2, output, 2 + app.Length, jpeg.Length - 2);
            return output;
        }
    }
}
