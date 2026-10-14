using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FastImageViewer.Core;

namespace FastImageViewer
{
    public interface IViewerClipboard
    {
        IDataObject Read();
        void Write(IDataObject data);
    }

    internal sealed class SystemViewerClipboard : IViewerClipboard
    {
        public IDataObject Read() { return Clipboard.GetDataObject(); }
        public void Write(IDataObject data) { Clipboard.SetDataObject(data, true); }
    }

    public sealed class ClipboardImage
    {
        public BitmapSource Bitmap { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public bool IsPreview { get; private set; }

        internal ClipboardImage(BitmapSource bitmap, int width, int height)
        {
            Bitmap = bitmap; Width = width; Height = height;
            IsPreview = bitmap.PixelWidth != width || bitmap.PixelHeight != height;
        }
    }

    public static class ClipboardImages
    {
        public static byte[] EncodePng(BitmapSource image)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                return stream.ToArray();
            }
        }

        public static IDataObject CopyData(BitmapSource image, byte[] png)
        {
            var data = new DataObject();
            data.SetData("PNG", new MemoryStream(png, false), false);
            data.SetData(DataFormats.Bitmap, image, true);
            return data;
        }

        public static ClipboardImage Read(IDataObject data)
        {
            if (data == null) return null;
            if (data.GetDataPresent("PNG", false))
            {
                object png = data.GetData("PNG", false);
                var bytes = png as byte[];
                var stream = png as Stream;
                if (bytes != null) using (var input = new MemoryStream(bytes, false)) return Decode(input);
                if (stream != null)
                {
                    long position = stream.CanSeek ? stream.Position : 0;
                    try { if (stream.CanSeek) stream.Position = 0; return Decode(stream); }
                    finally { if (stream.CanSeek) stream.Position = position; }
                }
            }
            var bitmap = data.GetData(DataFormats.Bitmap, true) as BitmapSource;
            if (bitmap == null) return null;
            int width = bitmap.PixelWidth, height = bitmap.PixelHeight;
            if (DecodeBudget.NeedsPreview(width, height))
            {
                int dw, dh;
                DecodeBudget.SelectDecodePixels(width, height, out dw, out dh);
                double scale = dw > 0 ? (double)dw / width : (double)dh / height;
                bitmap = new TransformedBitmap(bitmap, new ScaleTransform(scale, scale));
            }
            bitmap = bitmap.CloneCurrentValue();
            bitmap.Freeze();
            return new ClipboardImage(bitmap, width, height);
        }

        private static ClipboardImage Decode(Stream stream)
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (decoder.Frames.Count == 0) throw new InvalidDataException("剪贴板图片没有可显示的帧。");
            int width = decoder.Frames[0].PixelWidth, height = decoder.Frames[0].PixelHeight;
            if (!stream.CanSeek) throw new InvalidDataException("无法读取剪贴板图片数据。");
            stream.Position = 0;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            if (DecodeBudget.NeedsPreview(width, height))
            {
                int dw, dh;
                DecodeBudget.SelectDecodePixels(width, height, out dw, out dh);
                if (dw > 0) bitmap.DecodePixelWidth = dw;
                else bitmap.DecodePixelHeight = dh;
            }
            bitmap.EndInit(); bitmap.Freeze();
            return new ClipboardImage(bitmap, width, height);
        }
    }

    public static class FileLocation
    {
        public static void Reveal(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("原图片文件已不存在。");
            IntPtr pidl;
            uint attributes;
            Marshal.ThrowExceptionForHR(SHParseDisplayName(Path.GetFullPath(path), IntPtr.Zero, out pidl, 0, out attributes));
            try { Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(pidl, 0, IntPtr.Zero, 0)); }
            finally { Marshal.FreeCoTaskMem(pidl); }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr pidl, uint mask, out uint attributes);
        [DllImport("shell32.dll")]
        private static extern int SHOpenFolderAndSelectItems(IntPtr pidl, uint count, IntPtr items, uint flags);
    }
}
