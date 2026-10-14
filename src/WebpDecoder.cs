using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FastImageViewer
{
    // The native decoder is an embedded, versioned libwebp build. It never
    // depends on a machine-installed WIC codec or searches PATH for DLLs.
    internal static class WebpDecoder
    {
        private const string ResourceName = "FastImageViewer.Codecs.Webp";
        private const string CodecHash = "FAA03C3299BFA19EAD2DB3D8E76B35B81CE60F23E8DB54F2523B70D9A373932E";
        private static readonly object Gate = new object();
        private static InfoDelegate info;
        private static DecodeDelegate decode;
        private static IntPtr module;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int InfoDelegate(byte[] data, UIntPtr size, out int width, out int height, out int orientation);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int DecodeDelegate(byte[] data, UIntPtr size, [Out] byte[] pixels, UIntPtr pixelSize, int width, int height);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr moduleHandle, string name);

        internal static LoadedImage Load(string path)
        {
            byte[] encoded;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                // Keep malicious or unusually large compressed inputs bounded.
                if (stream.Length < 12 || stream.Length > 256L * 1024 * 1024)
                    throw new InvalidDataException("WebP 文件为空、损坏或超过 256 MiB 输入限制。");
                encoded = new byte[(int)stream.Length];
                int offset = 0;
                while (offset < encoded.Length)
                {
                    int count = stream.Read(encoded, offset, encoded.Length - offset);
                    if (count == 0) throw new EndOfStreamException("WebP 文件读取不完整。");
                    offset += count;
                }
            }
            EnsureCodec();
            int width, height, orientation;
            if (info(encoded, (UIntPtr)(uint)encoded.Length, out width, out height, out orientation) == 0 || width <= 0 || height <= 0)
                throw new InvalidDataException("WebP 图片数据无效。");
            bool preview = Core.DecodeBudget.NeedsPreview(width, height);
            int outputWidth = width, outputHeight = height;
            if (preview)
            {
                int targetWidth, targetHeight;
                Core.DecodeBudget.SelectDecodePixels(width, height, out targetWidth, out targetHeight);
                if (targetHeight > 0)
                {
                    outputHeight = targetHeight;
                    outputWidth = Math.Max(1, (int)((long)width * targetHeight / height));
                }
                else
                {
                    outputWidth = targetWidth;
                    outputHeight = Math.Max(1, (int)((long)height * targetWidth / width));
                }
            }
            int stride = checked(outputWidth * 4);
            byte[] pixels = new byte[checked(stride * outputHeight)];
            if (decode(encoded, (UIntPtr)(uint)encoded.Length, pixels, (UIntPtr)(uint)pixels.Length, outputWidth, outputHeight) == 0)
                throw new InvalidDataException("WebP 图片无法解码。");
            var bitmap = BitmapSource.Create(outputWidth, outputHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            bitmap.Freeze();
            return new LoadedImage(bitmap, path, encoded.Length, width, height, preview, orientation);
        }

        private static void EnsureCodec()
        {
            lock (Gate)
            {
                if (decode != null) return;
                string directory = Environment.GetEnvironmentVariable("FAST_IMAGE_VIEWER_CODEC_DIRECTORY");
                if (String.IsNullOrWhiteSpace(directory))
                    directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FastImageViewer", "codecs");
                directory = Path.Combine(Path.GetFullPath(directory), "libwebp-1.6.0-" + CodecHash.Substring(0, 12));
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "FastImageViewerWebp.dll");
                if (!ValidHash(path))
                {
                    string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
                        {
                            if (source == null) throw new InvalidOperationException("程序没有包含 WebP 解码器资源，请重新构建。");
                            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                                source.CopyTo(output);
                        }
                        if (!ValidHash(temporary)) throw new InvalidDataException("WebP 解码器校验失败。");
                        if (File.Exists(path)) File.Delete(path);
                        try { File.Move(temporary, path); }
                        catch (IOException) { if (!ValidHash(path)) throw; }
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                // Only the exact extracted DLL and Windows system dependencies.
                // Keep a read-only handle open through LoadLibraryEx so the
                // verified bytes cannot be replaced between verification/load.
                using (var verified = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var sha = SHA256.Create())
                {
                    string hash = BitConverter.ToString(sha.ComputeHash(verified)).Replace("-", "");
                    if (!hash.Equals(CodecHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("WebP 解码器校验失败。");
                    module = LoadLibraryEx(path, IntPtr.Zero, 0x00000100 | 0x00000800);
                }
                if (module == IntPtr.Zero) throw new InvalidOperationException("无法加载 WebP 解码器，Windows 错误 " + Marshal.GetLastWin32Error() + "。");
                info = (InfoDelegate)Marshal.GetDelegateForFunctionPointer(GetProcAddress(module, "fiv_webp_info"), typeof(InfoDelegate));
                decode = (DecodeDelegate)Marshal.GetDelegateForFunctionPointer(GetProcAddress(module, "fiv_webp_decode"), typeof(DecodeDelegate));
            }
        }

        private static bool ValidHash(string path)
        {
            if (!File.Exists(path)) return false;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").Equals(CodecHash, StringComparison.OrdinalIgnoreCase);
        }
    }
}
