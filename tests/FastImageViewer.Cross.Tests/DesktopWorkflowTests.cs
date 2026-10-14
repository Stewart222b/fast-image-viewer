using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FastImageViewer;
using FastImageViewer.Desktop;
using SkiaSharp;

namespace FastImageViewer.CrossTests
{
    internal static class DesktopWorkflowTests
    {
        private sealed class FakeClipboard : IViewerClipboard
        {
            public ClipboardPicture Data = new ClipboardPicture();
            public byte[] Written;
            public int Reads;
            public TaskCompletionSource<ClipboardPicture> Pending;
            public Task<ClipboardPicture> ReadAsync() { Reads++; return Pending == null ? Task.FromResult(Data) : Pending.Task; }
            public Task WriteImageAsync(byte[] png) { Written = png; return Task.CompletedTask; }
        }

        private static ViewerWindow Create(FakeClipboard clipboard)
        {
            if (Application.Current == null)
                AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true }).SetupWithoutStarting();
            var window = new ViewerWindow(new string[0], clipboard);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return window;
        }
        private static T Field<T>(object value, string name)
        {
            return (T)value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(value);
        }
        private static object Call(object value, string name, params object[] args)
        {
            return value.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(value, args);
        }
        private static byte[] Fixture(int width = 3, int height = 2)
        {
            using (var pixels = new SKBitmap(width, height))
            {
                pixels.Erase(SKColors.Transparent);
                pixels.SetPixel(0, 0, SKColors.Red);
                pixels.SetPixel(width - 1, height - 1, new SKColor(20, 180, 60, 128));
                using (var data = pixels.Encode(SKEncodedImageFormat.Png, 100)) return data.ToArray();
            }
        }
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Near(double left, double right, string message) { Check(Math.Abs(left - right) < 1e-6, message + ": " + left + " != " + right); }

        public static void TestClipboardPixels()
        {
            var clipboard = new FakeClipboard { Data = new ClipboardPicture { Png = Fixture() } };
            var window = Create(clipboard);
            try
            {
                window.PasteImage().GetAwaiter().GetResult();
                var canvas = Field<ImageCanvas>(window, "canvas");
                Check(canvas.HasImage && canvas.View.ImageWidth == 3 && canvas.View.ImageHeight == 2, "paste displays the memory image");
                canvas.Zoom(8);
                canvas.Pan(75, -50);
                canvas.ToggleCheckerboard();
                Call(window, "Rotate", 1);
                Complete(window.CopyImage());
                using (var pixels = DesktopWorkflow.DecodeClipboard(clipboard.Written))
                {
                    Check(pixels.Width == 2 && pixels.Height == 3, "copy preserves displayed rotation, excludes scaling");
                    Check(pixels.GetPixel(1, 0).Red > 240, "rotated top-left source pixel appears top-right");
                    Check(pixels.GetPixel(0, 2).Alpha == 128, "transparent source remains transparent without checkerboard");
                }
                Check(Field<List<string>>(window, "files").Count == 0 && Field<int>(window, "index") == -1, "clipboard clears folder navigation");
                Check(!Field<Button>(window, "previous").IsEnabled && !Field<Button>(window, "next").IsEnabled, "clipboard disables navigation");
                Check(Field<TextBlock>(window, "counter").Text == "", "clipboard clears file count");
                foreach (var item in Field<List<MenuItem>>(window, "revealItems")) Check(!item.IsEnabled, "memory image cannot reveal a file");
                Call(window, "TogglePanel", "info");
                var body = Field<StackPanel>(window, "panelBody");
                Check(body.Children[body.Children.Count - 1] is Button copy && !copy.IsEnabled, "copy path is disabled for clipboard source");
            }
            finally { window.Close(); }
        }

        public static void TestPreserveView()
        {
            var window = Create(new FakeClipboard());
            try
            {
                var canvas = Field<ImageCanvas>(window, "canvas");
                Func<WriteableBitmap> bitmap = () => new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
                canvas.SetImage(bitmap(), 1000, 1000);
                canvas.ActualSize();
                canvas.Zoom(2);
                canvas.Pan(100, -80);
                double scale = canvas.View.Scale;
                var center = canvas.View.ImagePointAt(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2);
                canvas.SetImage(bitmap(), 2000, 500, true);
                Near(canvas.View.Scale, scale, "manual zoom survives image change");
                var after = canvas.View.ImagePointAt(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2);
                Near(after.X / 2000, center.X / 1000, "normalized horizontal center survives");
                Near(after.Y / 500, center.Y / 1000, "normalized vertical center survives");
                canvas.Fit();
                canvas.SetImage(bitmap(), 100, 3000, true);
                Check(canvas.View.IsFit, "fit follows next image instead of freezing old zoom");
                canvas.SetImage(bitmap(), 1000, 1000, false);
                Check(canvas.View.IsFit, "default remains fit");
                Call(window, "SetPreserveZoom", true);
                foreach (var item in Field<List<MenuItem>>(window, "preserveItems")) Check(item.IsChecked, "preserve menu check follows state");
            }
            finally { window.Close(); }
        }

        public static void TestRevealArguments()
        {
            string path = Path.GetFullPath(Path.Combine("folder space & dollar$", "sample.webp"));
            var linux = DesktopWorkflow.Reveal(path, "linux");
            Check(!linux.UseShellExecute && linux.FileName == "xdg-open" && linux.ArgumentList.Count == 1 && linux.ArgumentList[0] == Path.GetDirectoryName(path), "Linux opens parent folder as one literal argument");
            var mac = DesktopWorkflow.Reveal(path, "macos");
            Check(mac.FileName == "open" && mac.ArgumentList.Count == 2 && mac.ArgumentList[0] == "-R" && mac.ArgumentList[1] == path, "macOS reveals exact file without shell parsing");
            var win = DesktopWorkflow.Reveal(path, "windows");
            Check(win.ArgumentList.Count == 2 && win.ArgumentList[1] == path, "Windows path remains one argument");
        }

        public static void TestClipboardFailureAndFocus()
        {
            foreach (var size in new[] { new[] { 8000, 4000 }, new[] { 8000, 4001 }, new[] { Int32.MaxValue, Int32.MaxValue }, new[] { 1, Int32.MaxValue } })
            {
                int width, height;
                DesktopWorkflow.ClipboardTargetSize(size[0], size[1], out width, out height);
                Check(width > 0 && height > 0 && (long)width * height <= 32000000, "clipboard target is bounded before bitmap encoding/allocation");
            }
            bool rejected = false;
            try { int width, height; DesktopWorkflow.ClipboardTargetSize(0, 10, out width, out height); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "zero dimensions rejected");
            var clipboard = new FakeClipboard { Data = new ClipboardPicture { Png = Fixture() } };
            var window = Create(clipboard);
            try
            {
                window.PasteImage().GetAwaiter().GetResult();
                var canvas = Field<ImageCanvas>(window, "canvas");
                clipboard.Data = new ClipboardPicture { Png = new byte[] { 1, 2, 3 } };
                window.PasteImage().GetAwaiter().GetResult();
                Check(canvas.HasImage && canvas.View.ImageWidth == 3, "invalid clipboard preserves existing image");
                var preview = DesktopWorkflow.DecodeClipboardPicture(Fixture(), 6000, 4000);
                using (preview.Pixels)
                    Check(preview.Width == 6000 && preview.Height == 4000 && preview.IsPreview, "native clipboard downsample retains original dimension metadata");
                var large = DesktopWorkflow.DecodeClipboardPicture(Fixture(8000, 4001));
                using (large.Pixels)
                {
                    Check(large.Width == 8000 && large.Height == 4001 && large.IsPreview, "oversized PNG clipboard retains original dimensions");
                    Check((long)large.Pixels.Width * large.Pixels.Height <= 32000000, "oversized PNG clipboard returns a bounded preview");
                    Check(large.Pixels.GetPixel(0, 0).Red > 200, "preview keeps source colors");
                }
                var input = Field<TextBox>(window, "zoomInput");
                input.Focus();
                Call(window, "OnPreviewKey", window, new KeyEventArgs { Key = Key.V, KeyModifiers = KeyModifiers.Control });
                Check(clipboard.Reads == 2, "text editing retains Ctrl+V");
                Call(window, "OnPreviewKey", window, new KeyEventArgs { Key = Key.K });
                Check(!Field<bool>(window, "preserveZoom"), "text editing retains K");
                canvas.Focus();
                var oldResponse = new TaskCompletionSource<ClipboardPicture>();
                clipboard.Pending = oldResponse;
                var oldPaste = window.PasteImage();
                clipboard.Pending = null;
                clipboard.Data = new ClipboardPicture { Png = Fixture(5, 4) };
                Complete(window.PasteImage());
                oldResponse.SetResult(new ClipboardPicture { Png = Fixture(7, 6) });
                Complete(oldPaste);
                Check(canvas.View.ImageWidth == 5 && canvas.View.ImageHeight == 4, "old clipboard response cannot overwrite newer paste");
                clipboard.Pending = new TaskCompletionSource<ClipboardPicture>();
                var pending = window.PasteImage();
                window.Close();
                clipboard.Pending.SetResult(new ClipboardPicture { Png = Fixture() });
                Dispatcher.UIThread.RunJobs();
                pending.GetAwaiter().GetResult();
                Check(!canvas.HasImage, "clipboard response cannot revive closed window");
            }
            finally { if (!Field<bool>(window, "closed")) window.Close(); }
        }

        public static void TestWebpDirectory(string samples)
        {
            string folder = Path.GetFullPath(Path.Combine(samples, "webp-workflow"));
            Directory.CreateDirectory(folder);
            string first = Path.Combine(folder, "image2.webp");
            string second = Path.Combine(folder, "image10.WEBP");
            using (var pixels = new SKBitmap(2, 2))
            {
                pixels.Erase(SKColors.Red);
                using (var data = pixels.Encode(SKEncodedImageFormat.Webp, 100))
                {
                    File.WriteAllBytes(first, data.ToArray());
                    File.WriteAllBytes(second, data.ToArray());
                }
            }
            var list = ImageFiles.InDirectory(first);
            Check(list.Count == 2 && list[0] == first && list[1] == second, "mixed-case WebP joins naturally sorted folder browsing");
        }

        public static void TestFileClipboardAndNavigation(string samples)
        {
            string folder = Path.GetFullPath(Path.Combine(samples, "clipboard-files"));
            Directory.CreateDirectory(folder);
            string first = Path.Combine(folder, "image1.png");
            string second = Path.Combine(folder, "image2.png");
            File.WriteAllBytes(first, Fixture(800, 800));
            File.WriteAllBytes(second, Fixture(1000, 500));
            Check(DesktopWorkflow.ClipboardPath("\"" + first + "\"") == first, "quoted clipboard path is accepted");
            Check(DesktopWorkflow.ClipboardPath("not an image file path") == null, "unrelated clipboard text is ignored");
            var clipboard = new FakeClipboard { Data = new ClipboardPicture { Paths = new[] { first }, Png = Fixture() } };
            var window = Create(clipboard);
            try
            {
                window.PasteImage().GetAwaiter().GetResult();
                WaitUntil(() => Field<FastImageViewer.Imaging.Picture>(window, "loaded")?.Path == first);
                var canvas = Field<ImageCanvas>(window, "canvas");
                Check(canvas.View.ImageWidth == 800 && Field<List<string>>(window, "files").Count == 2, "file list takes priority over clipboard bitmap and opens its folder");
                canvas.ActualSize();
                canvas.Zoom(2);
                canvas.Pan(60, -40);
                var center = canvas.View.ImagePointAt(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2);
                double scale = canvas.View.Scale;
                Call(window, "SetPreserveZoom", true);
                window.Navigate(1);
                WaitUntil(() => Field<FastImageViewer.Imaging.Picture>(window, "loaded")?.Path == second);
                var after = canvas.View.ImagePointAt(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2);
                Near(canvas.View.Scale, scale, "navigation UI keeps manual zoom");
                Near(after.X / 1000, center.X / 800, "navigation UI keeps horizontal center");
                Near(after.Y / 500, center.Y / 800, "navigation UI keeps vertical center");
                window.OpenPaths(new[] { first });
                WaitUntil(() => Field<FastImageViewer.Imaging.Picture>(window, "loaded")?.Path == first);
                Check(canvas.View.IsFit, "new open defaults to fit even when keep-view is enabled");
                foreach (var item in Field<List<MenuItem>>(window, "revealItems")) Check(item.IsEnabled, "file-backed image enables reveal");
                clipboard.Pending = new TaskCompletionSource<ClipboardPicture>();
                var pendingPaste = window.PasteImage();
                window.OpenPaths(new[] { second });
                WaitUntil(() => Field<FastImageViewer.Imaging.Picture>(window, "loaded")?.Path == second);
                clipboard.Pending.SetResult(new ClipboardPicture { Png = Fixture() });
                Complete(pendingPaste);
                Check(canvas.View.ImageWidth == 1000 && Field<FastImageViewer.Imaging.Picture>(window, "loaded")?.Path == second,
                    "late clipboard response cannot replace newer opened file");
            }
            finally { window.Close(); }
        }

        private static void WaitUntil(Func<bool> ready)
        {
            var timeout = Stopwatch.StartNew();
            while (!ready())
            {
                Dispatcher.UIThread.RunJobs();
                if (timeout.ElapsedMilliseconds > 5000) throw new Exception("image workflow timed out");
                Thread.Sleep(5);
            }
        }
        private static void Complete(Task task)
        {
            WaitUntil(() => task.IsCompleted);
            task.GetAwaiter().GetResult();
        }
    }
}
