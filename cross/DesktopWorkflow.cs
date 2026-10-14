using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using FastImageViewer.Imaging;
using SkiaSharp;

namespace FastImageViewer.Desktop
{
    public sealed class ClipboardPicture
    {
        public string[] Paths { get; set; } = new string[0];
        public byte[] Png { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsPreview { get; set; }
    }

    public sealed class DecodedClipboardPicture
    {
        // The caller owns Pixels and must dispose it after replacing the displayed image.
        public SKBitmap Pixels { get; internal set; }
        public int Width { get; internal set; }
        public int Height { get; internal set; }
        public bool IsPreview { get; internal set; }
    }

    public interface IViewerClipboard
    {
        Task<ClipboardPicture> ReadAsync();
        Task WriteImageAsync(byte[] png);
    }

    internal sealed class SystemViewerClipboard : IViewerClipboard
    {
        private readonly Window window;
        // Retain until the next copy: some platform clipboards request bitmap data lazily.
        private Bitmap copied;
        public SystemViewerClipboard(Window window) { this.window = window; }
        private IClipboard Clipboard { get { return TopLevel.GetTopLevel(window)?.Clipboard ?? throw new InvalidOperationException("剪贴板不可用。"); } }
        public async Task<ClipboardPicture> ReadAsync()
        {
            var paths = new List<string>();
            var items = await Clipboard.TryGetFilesAsync();
            if (items != null) foreach (var item in items)
            {
                string path = item.TryGetLocalPath();
                if (!String.IsNullOrEmpty(path)) paths.Add(path);
            }
            if (paths.Count > 0) return new ClipboardPicture { Paths = paths.ToArray() };
            string textPath = DesktopWorkflow.ClipboardPath(await Clipboard.TryGetTextAsync());
            if (textPath != null) return new ClipboardPicture { Paths = new[] { textPath } };
            string pngFormat = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "PNG" :
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "public.png" : "image/png";
            var png = await Clipboard.TryGetValueAsync(DataFormat.CreateBytesPlatformFormat(pngFormat));
            if (png != null) return new ClipboardPicture { Png = png };
            using (var bitmap = await Clipboard.TryGetBitmapAsync())
            {
                if (bitmap == null) return new ClipboardPicture();
                int width = bitmap.PixelSize.Width, height = bitmap.PixelSize.Height;
                int targetWidth, targetHeight;
                DesktopWorkflow.ClipboardTargetSize(width, height, out targetWidth, out targetHeight);
                using (var scaled = targetWidth == width && targetHeight == height ? null :
                    bitmap.CreateScaledBitmap(new PixelSize(targetWidth, targetHeight), BitmapInterpolationMode.HighQuality))
                using (var stream = new MemoryStream())
                {
                    (scaled ?? bitmap).Save(stream, PngBitmapEncoderOptions.Default);
                    return new ClipboardPicture { Png = stream.ToArray(), Width = width, Height = height,
                        IsPreview = scaled != null };
                }
            }
        }
        public async Task WriteImageAsync(byte[] png)
        {
            Bitmap next;
            using (var stream = new MemoryStream(png, false)) next = new Bitmap(stream);
            try { await Clipboard.SetBitmapAsync(next); }
            catch { next.Dispose(); throw; }
            var previous = copied;
            copied = next;
            previous?.Dispose();
        }
    }

    public static class DesktopWorkflow
    {
        public static string ClipboardPath(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return null;
            string path = text.Trim().Trim('"');
            return File.Exists(path) || Directory.Exists(path) ? Path.GetFullPath(path) : null;
        }

        public static byte[] EncodeDisplayed(SKBitmap pixels, int turns, bool flip, int userTurns)
        {
            if (turns % 4 == 0 && !flip && userTurns % 4 == 0) return EncodePng(pixels);
            using (var oriented = ImageOrientation.Apply(pixels, turns, flip, userTurns))
                return EncodePng(oriented);
        }
        private static byte[] EncodePng(SKBitmap pixels)
        {
            using (var encoded = pixels.Encode(SKEncodedImageFormat.Png, 100))
            {
                if (encoded == null) throw new InvalidDataException("无法复制图片。");
                return encoded.ToArray();
            }
        }
        public static SKBitmap DecodeClipboard(byte[] png)
        {
            return DecodeClipboardPicture(png).Pixels;
        }
        public static DecodedClipboardPicture DecodeClipboardPicture(byte[] png, int originalWidth = 0, int originalHeight = 0)
        {
            using (var stream = new MemoryStream(png, false))
            using (var codec = SKCodec.Create(stream))
            {
                if (codec == null) throw new InvalidDataException("剪贴板中没有有效图片。");
                int width = codec.Info.Width, height = codec.Info.Height;
                int targetWidth, targetHeight;
                ClipboardTargetSize(width, height, out targetWidth, out targetHeight);
                SKBitmap pixels = null;
                try
                {
                    SKSizeI scaled = codec.GetScaledDimensions(Math.Min((float)targetWidth / width, (float)targetHeight / height));
                    pixels = new SKBitmap(new SKImageInfo(scaled.Width, scaled.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
                    var result = codec.GetPixels(pixels.Info, pixels.GetPixels());
                    if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
                        throw new InvalidDataException("无法解码剪贴板图片。");
                    if (pixels.Width != targetWidth || pixels.Height != targetHeight)
                    {
                        var resized = pixels.Resize(new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Unpremul), SKSamplingOptions.Default);
                        if (resized == null) throw new InvalidDataException("无法缩小剪贴板图片。");
                        pixels.Dispose(); pixels = resized;
                    }
                    int sourceWidth = originalWidth > 0 ? originalWidth : width;
                    int sourceHeight = originalHeight > 0 ? originalHeight : height;
                    var picture = new DecodedClipboardPicture { Pixels = pixels, Width = sourceWidth, Height = sourceHeight,
                        IsPreview = pixels.Width != sourceWidth || pixels.Height != sourceHeight };
                    pixels = null;
                    return picture;
                }
                finally { pixels?.Dispose(); }
            }
        }
        public static void ClipboardTargetSize(int width, int height, out int targetWidth, out int targetHeight)
        {
            if (width <= 0 || height <= 0) throw new InvalidDataException("剪贴板图片尺寸无效。");
            targetWidth = width; targetHeight = height;
            if (!FastImageViewer.Core.DecodeBudget.NeedsPreview(width, height)) return;
            int dw, dh;
            FastImageViewer.Core.DecodeBudget.SelectDecodePixels(width, height, out dw, out dh);
            if (dw > 0) { targetWidth = dw; targetHeight = Math.Max(1, (int)Math.Ceiling((double)height * dw / width)); }
            else { targetHeight = dh; targetWidth = Math.Max(1, (int)Math.Ceiling((double)width * dh / height)); }
        }
        public static ProcessStartInfo Reveal(string path, string platform)
        {
            if (String.IsNullOrEmpty(path)) throw new ArgumentException("图片没有文件路径。");
            path = Path.GetFullPath(path);
            var start = new ProcessStartInfo { UseShellExecute = false };
            if (platform == "macos") { start.FileName = "open"; start.ArgumentList.Add("-R"); start.ArgumentList.Add(path); }
            else if (platform == "windows") { start.FileName = "explorer.exe"; start.ArgumentList.Add("/select,"); start.ArgumentList.Add(path); }
            else { start.FileName = "xdg-open"; start.ArgumentList.Add(Path.GetDirectoryName(path)); }
            return start;
        }
    }
}
