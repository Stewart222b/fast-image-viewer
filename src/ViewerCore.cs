using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace FastImageViewer
{
    public sealed class Viewport
    {
        public double ImageWidth { get; private set; }
        public double ImageHeight { get; private set; }
        public double ViewportWidth { get; private set; }
        public double ViewportHeight { get; private set; }
        public double Scale { get; private set; }
        public double OffsetX { get; private set; }
        public double OffsetY { get; private set; }
        public bool IsFit { get; private set; }
        public double DeviceScale { get; set; }

        public Viewport() { Scale = 1; IsFit = true; DeviceScale = 1; }

        public void SetImage(double width, double height)
        {
            ImageWidth = ValidSize(width);
            ImageHeight = ValidSize(height);
            Fit();
        }

        public void Resize(double width, double height)
        {
            Point center = ImagePointAt(ViewportWidth / 2, ViewportHeight / 2);
            ViewportWidth = ValidSize(width);
            ViewportHeight = ValidSize(height);
            if (IsFit) Fit();
            else
            {
                OffsetX = ViewportWidth / 2 - center.X * Scale;
                OffsetY = ViewportHeight / 2 - center.Y * Scale;
            }
        }

        public void Fit()
        {
            IsFit = true;
            Scale = FitScale();
            Center();
        }

        public void ActualSize()
        {
            IsFit = false;
            Scale = 1;
            Center();
        }

        public void ZoomAt(double factor, double x, double y)
        {
            if (ImageWidth <= 0 || ImageHeight <= 0 || !IsFinite(factor) || factor <= 0 || !IsFinite(x) || !IsFinite(y)) return;
            Point anchor = ImagePointAt(x, y);
            Scale = Math.Max(Math.Min(0.01, FitScale()), Math.Min(32, Scale * factor));
            OffsetX = x - anchor.X * Scale;
            OffsetY = y - anchor.Y * Scale;
            IsFit = false;
        }

        public void Pan(double dx, double dy)
        {
            if (!IsFinite(dx) || !IsFinite(dy)) return;
            OffsetX += dx;
            OffsetY += dy;
            IsFit = false;
        }

        public Point ImagePointAt(double x, double y)
        {
            return new Point((x - OffsetX) / Scale, (y - OffsetY) / Scale);
        }

        private void Center()
        {
            OffsetX = (ViewportWidth - ImageWidth * Scale) / 2;
            OffsetY = (ViewportHeight - ImageHeight * Scale) / 2;
        }

        private double FitScale()
        {
            double actualSize = 1 / (IsFinite(DeviceScale) && DeviceScale > 0 ? DeviceScale : 1);
            if (ImageWidth > 0 && ImageHeight > 0 && ViewportWidth > 0 && ViewportHeight > 0)
                return Math.Min(actualSize, Math.Min(ViewportWidth / ImageWidth, ViewportHeight / ImageHeight));
            return actualSize;
        }

        private static double ValidSize(double value) { return IsFinite(value) && value > 0 ? value : 0; }
        private static bool IsFinite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
    }

    public static class ImageFiles
    {
        public const string DialogFilter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.ico;*.wdp;*.jxr|所有文件|*.*";
        private static readonly HashSet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".ico", ".wdp", ".jxr"
        };

        public static bool IsSupported(string path)
        {
            return !String.IsNullOrWhiteSpace(path) && Extensions.Contains(System.IO.Path.GetExtension(path));
        }

        public static List<string> InDirectory(string path)
        {
            string directory = Directory.Exists(path) ? path : System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
            return FromPaths(Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly));
        }

        public static List<string> FromPaths(IEnumerable<string> paths)
        {
            if (paths == null) throw new ArgumentNullException("paths");
            var results = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths)
            {
                if (String.IsNullOrWhiteSpace(path)) continue;
                string fullPath = System.IO.Path.GetFullPath(path);
                if (Directory.Exists(fullPath))
                {
                    foreach (string image in InDirectory(fullPath))
                        if (seen.Add(image)) results.Add(image);
                }
                else if (IsSupported(fullPath) && seen.Add(fullPath)) results.Add(fullPath);
            }
            results.Sort(ComparePaths);
            return results;
        }

        private static int ComparePaths(string left, string right)
        {
            string a = System.IO.Path.GetFileName(left), b = System.IO.Path.GetFileName(right);
            // Use one consistent ordering for every pair, including very long numbers.
            int result = NaturalCompare(a, b);
            if (result != 0) return result;
            result = StringComparer.OrdinalIgnoreCase.Compare(left, right);
            return result != 0 ? result : StringComparer.Ordinal.Compare(left, right);
        }

        // Compare numeric runs without parsing integers, including arbitrarily long names.
        private static int NaturalCompare(string a, string b)
        {
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (a[i] >= '0' && a[i] <= '9' && b[j] >= '0' && b[j] <= '9')
                {
                    int endA = i, endB = j;
                    while (endA < a.Length && a[endA] >= '0' && a[endA] <= '9') endA++;
                    while (endB < b.Length && b[endB] >= '0' && b[endB] <= '9') endB++;
                    while (i < endA && a[i] == '0') i++;
                    while (j < endB && b[j] == '0') j++;
                    int size = (endA - i).CompareTo(endB - j);
                    if (size != 0) return size;
                    while (i < endA)
                    {
                        int digit = a[i++].CompareTo(b[j++]);
                        if (digit != 0) return digit;
                    }
                    i = endA; j = endB;
                }
                else
                {
                    int letter = Char.ToUpperInvariant(a[i++]).CompareTo(Char.ToUpperInvariant(b[j++]));
                    if (letter != 0) return letter;
                }
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }

    }

    public sealed class LoadedImage
    {
        public BitmapSource Bitmap { get; private set; }
        public string Path { get; private set; }
        public long FileSize { get; private set; }
        public int PixelWidth { get; private set; }
        public int PixelHeight { get; private set; }
        public bool IsPreview { get; private set; }
        public int ExifQuarterTurns { get; private set; }
        public bool ExifFlipHorizontal { get; private set; }

        internal LoadedImage(BitmapSource bitmap, string path, long fileSize, int width, int height, bool preview, int orientation)
        {
            Bitmap = bitmap; Path = path; FileSize = fileSize;
            PixelWidth = width; PixelHeight = height; IsPreview = preview;
            switch (orientation)
            {
                case 2: ExifFlipHorizontal = true; break;
                case 3: ExifQuarterTurns = 2; break;
                case 4: ExifQuarterTurns = 2; ExifFlipHorizontal = true; break;
                case 5: ExifQuarterTurns = 1; ExifFlipHorizontal = true; break;
                case 6: ExifQuarterTurns = 1; break;
                case 7: ExifQuarterTurns = 3; ExifFlipHorizontal = true; break;
                case 8: ExifQuarterTurns = 3; break;
            }
        }
    }

    public static class ImageLoader
    {
        private const long MaximumPixels = 32000000;

        public static LoadedImage Load(string path)
        {
            if (!ImageFiles.IsSupported(path)) throw new NotSupportedException("不支持该图片格式。");
            string fullPath = System.IO.Path.GetFullPath(path);
            int width, height, orientation;
            long fileSize;
            // DelayCreation reads dimensions and metadata without materializing the full bitmap.
            using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                fileSize = stream.Length;
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                if (decoder.Frames.Count == 0) throw new InvalidDataException("图片没有可显示的帧。");
                BitmapFrame frame = decoder.Frames[0];
                width = frame.PixelWidth; height = frame.PixelHeight;
                orientation = ReadOrientation(frame.Metadata as BitmapMetadata);
            }
            if (width <= 0 || height <= 0) throw new InvalidDataException("图片尺寸无效。");
            bool preview = (long)width * height > MaximumPixels;
            var bitmap = new BitmapImage();
            using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                bitmap.StreamSource = stream;
                if (preview)
                {
                    double ratio = Math.Sqrt((double)MaximumPixels / ((long)width * height));
                    int targetWidth = Math.Max(1, (int)Math.Floor(width * ratio));
                    // Leave room for codecs that round the inferred height upwards.
                    while (targetWidth > 1 && (long)targetWidth * Math.Ceiling((double)height * targetWidth / width) > MaximumPixels)
                        targetWidth--;
                    // A single dimension preserves aspect ratio. Use height for exceptionally thin images.
                    if ((long)targetWidth * Math.Ceiling((double)height * targetWidth / width) > MaximumPixels)
                        bitmap.DecodePixelHeight = (int)Math.Min(height, MaximumPixels);
                    else bitmap.DecodePixelWidth = targetWidth;
                }
                bitmap.EndInit();
                bitmap.Freeze();
            }
            return new LoadedImage(bitmap, fullPath, fileSize, width, height, preview, orientation);
        }

        private static int ReadOrientation(BitmapMetadata metadata)
        {
            if (metadata == null) return 1;
            foreach (string query in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
            {
                try
                {
                    object value = metadata.GetQuery(query);
                    if (value == null) continue;
                    int orientation = Convert.ToInt32(value);
                    if (orientation >= 1 && orientation <= 8) return orientation;
                }
                catch (NotSupportedException) { }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
                catch (FormatException) { }
                catch (OverflowException) { }
            }
            return 1;
        }
    }
}
