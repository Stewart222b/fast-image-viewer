using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CoreViewport = FastImageViewer.Core.Viewport;

namespace FastImageViewer
{
    // WPF surface. The viewport math lives in FastImageViewer.Core.Viewport.
    public sealed class Viewport
    {
        internal readonly CoreViewport Inner = new CoreViewport();

        public double ImageWidth { get { return Inner.ImageWidth; } }
        public double ImageHeight { get { return Inner.ImageHeight; } }
        public double ViewportWidth { get { return Inner.ViewportWidth; } }
        public double ViewportHeight { get { return Inner.ViewportHeight; } }
        public double Scale { get { return Inner.Scale; } }
        public double OffsetX { get { return Inner.OffsetX; } }
        public double OffsetY { get { return Inner.OffsetY; } }
        public bool IsFit { get { return Inner.IsFit; } }
        public double DeviceScale { get { return Inner.DeviceScale; } set { Inner.DeviceScale = value; } }

        public void SetImage(double width, double height) { Inner.SetImage(width, height); }
        public void Resize(double width, double height) { Inner.Resize(width, height); }
        public void Fit() { Inner.Fit(); }
        public void ActualSize() { Inner.ActualSize(); }
        public void ZoomAt(double factor, double x, double y) { Inner.ZoomAt(factor, x, y); }
        public void Pan(double dx, double dy) { Inner.Pan(dx, dy); }

        public Point ImagePointAt(double x, double y)
        {
            FastImageViewer.Core.ImagePoint point = Inner.ImagePointAt(x, y);
            return new Point(point.X, point.Y);
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
            int quarterTurns;
            bool flipHorizontal;
            FastImageViewer.Core.ExifOrientation.Map(orientation, out quarterTurns, out flipHorizontal);
            ExifQuarterTurns = quarterTurns;
            ExifFlipHorizontal = flipHorizontal;
        }
    }

    public static class ImageLoader
    {
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
            bool preview = FastImageViewer.Core.DecodeBudget.NeedsPreview(width, height);
            var bitmap = new BitmapImage();
            using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                bitmap.StreamSource = stream;
                if (preview)
                {
                    int decodeWidth, decodeHeight;
                    FastImageViewer.Core.DecodeBudget.SelectDecodePixels(width, height, out decodeWidth, out decodeHeight);
                    if (decodeHeight > 0) bitmap.DecodePixelHeight = decodeHeight;
                    else bitmap.DecodePixelWidth = decodeWidth;
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
