using System;
using SkiaSharp;

namespace FastImageViewer.Imaging
{
    public static class PixelCopy
    {
        public static void CopyBgra(SKBitmap bitmap, IntPtr destination, int destinationStride)
        {
            if (bitmap == null) throw new ArgumentNullException("bitmap");
            if (destination == IntPtr.Zero) throw new ArgumentNullException("destination");
            if (bitmap.ColorType != SKColorType.Bgra8888) throw new ArgumentException("需要 BGRA 像素。", "bitmap");
            unsafe
            {
                byte* src = (byte*)bitmap.GetPixels();
                byte* dst = (byte*)destination;
                int copy = bitmap.Width * 4;
                for (int y = 0; y < bitmap.Height; y++)
                    Buffer.MemoryCopy(src + y * bitmap.RowBytes, dst + y * destinationStride, copy, copy);
            }
        }
    }
}
