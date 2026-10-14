using System;
using System.IO;
using FastImageViewer.Core;
using SkiaSharp;

namespace FastImageViewer.Imaging
{
    public sealed class Picture : IDisposable
    {
        public string Path { get; private set; }
        public long FileSize { get; private set; }
        public int PixelWidth { get; private set; }
        public int PixelHeight { get; private set; }
        public bool IsPreview { get; private set; }
        public int ExifQuarterTurns { get; private set; }
        public bool ExifFlipHorizontal { get; private set; }
        public SKBitmap Pixels { get; private set; }
        private bool disposed;

        internal Picture(SKBitmap pixels, string path, long fileSize, int width, int height, bool preview, int quarterTurns, bool flipHorizontal)
        {
            Pixels = pixels;
            Path = path;
            FileSize = fileSize;
            PixelWidth = width;
            PixelHeight = height;
            IsPreview = preview;
            ExifQuarterTurns = quarterTurns;
            ExifFlipHorizontal = flipHorizontal;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (Pixels != null) Pixels.Dispose();
            Pixels = null;
        }
    }

    public static class PictureLoader
    {
        public static Picture Load(string path)
        {
            if (!ImageFiles.IsSupported(path)) throw new NotSupportedException("不支持该图片格式。");
            string fullPath = Path.GetFullPath(path);
            var info = new FileInfo(fullPath);
            if (!info.Exists) throw new FileNotFoundException("找不到图片。", fullPath);
            SKBitmap decoded = null;
            try
            {
                int width;
                int height;
                int orientation;
                if (IsTiff(fullPath))
                {
                    decoded = TiffDecoder.Read(fullPath, out width, out height, out orientation);
                }
                else
                {
                    using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var codec = SKCodec.Create(stream))
                    {
                        if (codec == null) throw new InvalidDataException("无法解码该图片。");
                        width = codec.Info.Width;
                        height = codec.Info.Height;
                        if (width <= 0 || height <= 0) throw new InvalidDataException("图片尺寸无效。");
                        orientation = (int)codec.EncodedOrigin;
                        if (orientation < 1 || orientation > 8) orientation = 1;
                        bool preview = DecodeBudget.NeedsPreview(width, height);
                        decoded = Decode(codec, width, height, preview);
                    }
                }
                int quarterTurns;
                bool flip;
                ExifOrientation.Map(orientation, out quarterTurns, out flip);
                var picture = new Picture(decoded, fullPath, info.Length, width, height, DecodeBudget.NeedsPreview(width, height), quarterTurns, flip);
                decoded = null;
                return picture;
            }
            catch
            {
                if (decoded != null) decoded.Dispose();
                throw;
            }
        }

        private static SKBitmap Decode(SKCodec codec, int width, int height, bool preview)
        {
            int targetWidth = width;
            int targetHeight = height;
            if (preview) SelectTarget(width, height, out targetWidth, out targetHeight);

            SKSizeI scaled = new SKSizeI(width, height);
            if (preview)
            {
                float scale = Math.Min((float)targetWidth / width, (float)targetHeight / height);
                scaled = codec.GetScaledDimensions(Math.Max(scale, 0.0001f));
                if (scaled.Width <= 0 || scaled.Height <= 0) scaled = new SKSizeI(width, height);
            }

            var sampled = TryGetPixels(codec, scaled.Width, scaled.Height);
            if (sampled == null) sampled = TryGetPixels(codec, width, height);
            if (sampled == null) throw new InvalidDataException("无法解码该图片。");
            if ((long)sampled.Width * sampled.Height <= DecodeBudget.MaximumPixels &&
                (!preview || (sampled.Width <= targetWidth && sampled.Height <= targetHeight) || (long)sampled.Width * sampled.Height <= (long)targetWidth * targetHeight))
                return sampled;

            return Downscale(sampled, targetWidth, targetHeight);
        }

        private static SKBitmap TryGetPixels(SKCodec codec, int width, int height)
        {
            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            var bitmap = new SKBitmap(info);
            SKCodecResult result;
            try
            {
                result = codec.GetPixels(info, bitmap.GetPixels(), new SKCodecOptions(0));
            }
            catch (Exception)
            {
                bitmap.Dispose();
                return null;
            }
            if (result == SKCodecResult.Success || result == SKCodecResult.IncompleteInput) return bitmap;
            bitmap.Dispose();
            return null;
        }

        internal static void SelectTarget(int width, int height, out int targetWidth, out int targetHeight)
        {
            targetWidth = width;
            targetHeight = height;
            int decodeWidth;
            int decodeHeight;
            DecodeBudget.SelectDecodePixels(width, height, out decodeWidth, out decodeHeight);
            if (decodeWidth > 0)
            {
                targetWidth = decodeWidth;
                targetHeight = Math.Max(1, (int)Math.Round((double)height * decodeWidth / width));
            }
            else
            {
                targetHeight = Math.Max(1, decodeHeight);
                targetWidth = Math.Max(1, (int)Math.Round((double)width * targetHeight / height));
            }
            while ((long)targetWidth * targetHeight > DecodeBudget.MaximumPixels && (targetWidth > 1 || targetHeight > 1))
            {
                if (targetWidth >= targetHeight && targetWidth > 1) targetWidth--;
                else if (targetHeight > 1) targetHeight--;
                else targetWidth = 1;
            }
        }

        internal static SKBitmap Downscale(SKBitmap sampled, int targetWidth, int targetHeight)
        {
            var fitted = new SKBitmap(new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Unpremul));
            using (sampled)
            using (var canvas = new SKCanvas(fitted))
            using (var paint = new SKPaint())
            {
                paint.IsAntialias = false;
                canvas.DrawBitmap(sampled, new SKRect(0, 0, targetWidth, targetHeight), paint);
            }
            return fitted;
        }

        private static bool IsTiff(string path)
        {
            string extension = Path.GetExtension(path);
            return extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) || extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase);
        }
    }
}
