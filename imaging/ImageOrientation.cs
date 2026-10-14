using System;
using SkiaSharp;

namespace FastImageViewer.Imaging
{
    public static class ImageOrientation
    {
        public static SKBitmap Apply(SKBitmap source, int exifQuarterTurns, bool flipHorizontal, int userQuarterTurns)
        {
            if (source == null) throw new ArgumentNullException("source");
            SKBitmap converted = null;
            SKBitmap working = source;
            if (source.ColorType != SKColorType.Bgra8888 || source.AlphaType != SKAlphaType.Unpremul)
            {
                converted = ConvertToBgra(source);
                working = converted;
            }
            try
            {
                SKBitmap current = working;
                SKBitmap owned = null;
                if (Norm(exifQuarterTurns) != 0)
                {
                    var next = Rotate(current, exifQuarterTurns);
                    Replace(ref current, ref owned, next);
                }
                if (flipHorizontal)
                {
                    var next = FlipHorizontal(current);
                    Replace(ref current, ref owned, next);
                }
                if (Norm(userQuarterTurns) != 0)
                {
                    var next = Rotate(current, userQuarterTurns);
                    Replace(ref current, ref owned, next);
                }
                if (owned == null) return Clone(working);
                return owned;
            }
            finally
            {
                if (converted != null) converted.Dispose();
            }
        }

        public static SKBitmap Rotate(SKBitmap source, int quarterTurns)
        {
            int turns = Norm(quarterTurns);
            if (turns == 0) return Clone(source);
            int width = source.Width;
            int height = source.Height;
            int destWidth = turns == 2 ? width : height;
            int destHeight = turns == 2 ? height : width;
            var dest = new SKBitmap(new SKImageInfo(destWidth, destHeight, SKColorType.Bgra8888, SKAlphaType.Unpremul));
            unsafe
            {
                byte* src = (byte*)source.GetPixels();
                byte* dst = (byte*)dest.GetPixels();
                int srcStride = source.RowBytes;
                int dstStride = dest.RowBytes;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int nx;
                        int ny;
                        if (turns == 1) { nx = height - 1 - y; ny = x; }
                        else if (turns == 2) { nx = width - 1 - x; ny = height - 1 - y; }
                        else { nx = y; ny = width - 1 - x; }
                        byte* from = src + y * srcStride + x * 4;
                        byte* to = dst + ny * dstStride + nx * 4;
                        to[0] = from[0];
                        to[1] = from[1];
                        to[2] = from[2];
                        to[3] = from[3];
                    }
                }
            }
            return dest;
        }

        public static SKBitmap FlipHorizontal(SKBitmap source)
        {
            int width = source.Width;
            int height = source.Height;
            var dest = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
            unsafe
            {
                byte* src = (byte*)source.GetPixels();
                byte* dst = (byte*)dest.GetPixels();
                int srcStride = source.RowBytes;
                int dstStride = dest.RowBytes;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        byte* from = src + y * srcStride + x * 4;
                        byte* to = dst + y * dstStride + (width - 1 - x) * 4;
                        to[0] = from[0];
                        to[1] = from[1];
                        to[2] = from[2];
                        to[3] = from[3];
                    }
                }
            }
            return dest;
        }

        private static void Replace(ref SKBitmap current, ref SKBitmap owned, SKBitmap next)
        {
            if (owned != null) owned.Dispose();
            owned = next;
            current = next;
        }

        private static int Norm(int turns)
        {
            int value = turns % 4;
            return value < 0 ? value + 4 : value;
        }

        private static SKBitmap ConvertToBgra(SKBitmap source)
        {
            var info = new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            var dest = new SKBitmap(info);
            using (var canvas = new SKCanvas(dest))
                canvas.DrawBitmap(source, 0, 0);
            return dest;
        }

        private static SKBitmap Clone(SKBitmap source)
        {
            var dest = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
            unsafe
            {
                byte* src = (byte*)source.GetPixels();
                byte* dst = (byte*)dest.GetPixels();
                int rows = source.Height;
                int copy = source.Width * 4;
                for (int y = 0; y < rows; y++)
                    Buffer.MemoryCopy(src + y * source.RowBytes, dst + y * dest.RowBytes, copy, copy);
            }
            return dest;
        }
    }
}
