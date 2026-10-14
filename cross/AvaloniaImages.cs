using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FastImageViewer.Imaging;
using SkiaSharp;

namespace FastImageViewer.Desktop
{
    internal static class AvaloniaImages
    {
        public static WriteableBitmap From(SKBitmap bitmap)
        {
            var image = new WriteableBitmap(new PixelSize(bitmap.Width, bitmap.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            using (var buffer = image.Lock())
                PixelCopy.CopyBgra(bitmap, buffer.Address, buffer.RowBytes);
            return image;
        }
    }
}
