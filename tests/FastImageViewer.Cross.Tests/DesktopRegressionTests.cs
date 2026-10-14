using System;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FastImageViewer.Desktop;

namespace FastImageViewer.CrossTests
{
    internal static class DesktopRegressionTests
    {
        private static bool started;

        private static Window CreateWindow(out ImageCanvas canvas)
        {
            if (!started)
            {
                AppBuilder.Configure<Application>()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
                    .SetupWithoutStarting();
                started = true;
            }
            canvas = new ImageCanvas();
            var window = new Window { Width = 800, Height = 600, Content = canvas };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            canvas.SetImage(new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Unpremul), 1000, 1000);
            return window;
        }

        public static void TestCheckerboard()
        {
            ImageCanvas canvas;
            Window window = CreateWindow(out canvas);
            try
            {
                canvas.ActualSize();
                canvas.Zoom(32);
                canvas.ToggleCheckerboard();
                int count = CountCheckerTiles(canvas);
                int maximum = ((int)Math.Ceiling(canvas.Bounds.Width / 8) + 1) *
                    ((int)Math.Ceiling(canvas.Bounds.Height / 8) + 1);
                Check(count > 0 && count <= maximum, "tiles must depend on viewport size, not scaled image: " + count);
                canvas.Pan(100000, 100000);
                Check(CountCheckerTiles(canvas) == 0, "offscreen image must not draw checker tiles");
            }
            finally { canvas.ClearImage(); window.Close(); }
        }

        public static void TestScaling()
        {
            ImageCanvas canvas;
            Window window = CreateWindow(out canvas);
            try
            {
                canvas.SetImage(new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96)), 100, 60);
                Size before = canvas.Bounds.Size;
                window.SetRenderScaling(2);
                Check(canvas.Bounds.Size == before, "test requires unchanged logical bounds");
                Near(canvas.View.DeviceScale, 2, "new monitor scale");
                Near(canvas.View.Scale, .5, "small fitted image has native physical pixels");
                Check(canvas.View.IsFit, "fit mode is preserved");
                canvas.ActualSize();
                canvas.Zoom(1.5);
                canvas.Pan(31, -17);
                var center = canvas.View.ImagePointAt(before.Width / 2, before.Height / 2);
                window.SetRenderScaling(1.5);
                Near(canvas.View.Scale * canvas.DpiScale, 1.5, "custom physical zoom is preserved");
                var after = canvas.View.ImagePointAt(before.Width / 2, before.Height / 2);
                Near(after.X, center.X, "central pixel X");
                Near(after.Y, center.Y, "central pixel Y");
                canvas.ActualSize();
                window.SetRenderScaling(1);
                Near(canvas.View.Scale * canvas.DpiScale, 1, "actual size survives another scaling change");
                canvas.ProcessWheel(120, new Point(123, 234), false);
                Check(canvas.IsZoomAnimating, "wheel animation started");
                window.SetRenderScaling(2);
                Check(!canvas.IsZoomAnimating, "old-DPI wheel animation is cancelled");
            }
            finally { canvas.ClearImage(); window.Close(); }
        }

        public static void TestDetach()
        {
            ImageCanvas canvas;
            Window window = CreateWindow(out canvas);
            try
            {
                window.Content = null;
                double oldDpi = canvas.View.DeviceScale;
                window.SetRenderScaling(2);
                Near(canvas.View.DeviceScale, oldDpi, "former top level no longer updates detached canvas");
                window.Content = canvas;
                Dispatcher.UIThread.RunJobs();
                Near(canvas.View.DeviceScale, 2, "reattachment subscribes and refreshes scaling");
            }
            finally { canvas.ClearImage(); window.Close(); }
        }

        private static int CountCheckerTiles(ImageCanvas canvas)
        {
            // DrawingGroup records geometry but does not implement bitmap drawing. Record the
            // production checker pass directly, before the image pass, to count submitted tiles.
            var drawing = new DrawingGroup();
            var view = canvas.View;
            var rect = new Rect(view.OffsetX, view.OffsetY, view.ImageWidth * view.Scale, view.ImageHeight * view.Scale);
            using (DrawingContext context = drawing.Open())
                typeof(ImageCanvas).GetMethod("DrawChecker", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(canvas, new object[] { context, rect });
            return CountRectangles(drawing);
        }

        private static int CountRectangles(Drawing drawing)
        {
            var group = drawing as DrawingGroup;
            if (group == null) return drawing is GeometryDrawing ? 1 : 0;
            int count = 0;
            foreach (Drawing child in group.Children) count += CountRectangles(child);
            return count;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void Near(double actual, double expected, string message)
        {
            Check(Math.Abs(actual - expected) < 1e-6, message + ": " + actual + " != " + expected);
        }
    }
}
