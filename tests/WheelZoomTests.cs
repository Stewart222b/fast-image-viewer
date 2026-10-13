using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FastImageViewer;

internal static class WheelZoomTests
{
    private static int passed;
    private static int failed;
    private static readonly Point Anchor = new Point(173, 219);
    [STAThread]
    private static int Main()
    {
        foreach (int delta in new[] { 1, 15, 30, 60, 120 })
        {
            int value = delta;
            Run("fractional delta " + value, delegate {
                Viewport view = View(); var wheel = new WheelZoom(view);
                wheel.Process(value, Anchor, 0, true);
                Near(wheel.TargetScale, Math.Pow(1.18, value / 120.0));
                wheel.Tick(1); Near(view.Scale, Math.Pow(1.18, value / 120.0));
            });
        }
        Run("120 equals eight 15 inputs", delegate {
            Viewport view = View(); var wheel = new WheelZoom(view);
            for (int i = 0; i < 8; i++) wheel.Process(15, Anchor, i * .005, true);
            Near(wheel.TargetScale, 1.18); wheel.Tick(1); Near(view.Scale, 1.18);
        });
        Run("log scale interpolation without overshoot", delegate {
            Viewport view = View(); var wheel = new WheelZoom(view);
            wheel.Process(120, Anchor, 0, true); Near(view.Scale, 1);
            wheel.Tick(.04); Near(view.Scale, Math.Sqrt(1.18));
            wheel.Tick(.08); Near(view.Scale, 1.18); Check(!wheel.IsActive, "completed");
            Check(!wheel.Tick(2), "no extra frames");
        });
        Run("small reversal immediately changes direction", delegate {
            Viewport view = View(); var wheel = new WheelZoom(view);
            wheel.Process(120, Anchor, 0, true); wheel.Tick(.02);
            double scale = view.Scale;
            wheel.Process(-1, Anchor, .02, true);
            Check(wheel.TargetScale < scale, "discard old positive target");
            wheel.Tick(.03); Check(view.Scale < scale, "next frame shrinks");
        });
        Run("anchor pixel preserved across all frames", delegate {
            Viewport view = View(); var wheel = new WheelZoom(view);
            Point pixel = view.ImagePointAt(Anchor.X, Anchor.Y);
            wheel.Process(120, Anchor, 0, true);
            for (int i = 1; i <= 10; i++) {
                wheel.Tick(i * .01); Point now = view.ImagePointAt(Anchor.X, Anchor.Y);
                Near(now.X, pixel.X); Near(now.Y, pixel.Y);
            }
        });
        Run("both zoom bounds", delegate {
            Viewport view = View(); var wheel = new WheelZoom(view);
            wheel.Process(Int32.MaxValue, Anchor, 0, true); wheel.Tick(1); Near(view.Scale, 32);
            wheel.Process(Int32.MinValue, Anchor, 2, true); wheel.Tick(3); Near(view.Scale, .01);
        });
        Run("huge image fit lower bound", delegate {
            Viewport view = View(); view.SetImage(1000000, 1000000); var wheel = new WheelZoom(view);
            wheel.Process(Int32.MinValue, Anchor, 0, true); wheel.Tick(1); Near(view.Scale, .0006);
        });
        Run("zero delta changes no pending state", delegate {
            Viewport view = View(); var wheel = new WheelZoom(view);
            wheel.Process(120, Anchor, 0, true);
            wheel.Process(0, new Point(0, 0), .04, true); Near(view.Scale, 1);
            wheel.Tick(.08); Near(view.Scale, 1.18);
        });
        Run("cancel prevents subsequent view update", delegate {
            Viewport view = View(); var wheel = new WheelZoom(view);
            wheel.Process(120, Anchor, 0, true); wheel.Tick(.02); wheel.Cancel();
            view.Pan(50, 30); double x = view.OffsetX; double scale = view.Scale;
            Check(!wheel.Tick(1), "cancelled"); Near(view.Scale, scale); Near(view.OffsetX, x);
        });
        Run("reduced motion applies fractional delta immediately", delegate {
            Viewport view = View(); var wheel = new WheelZoom(view);
            wheel.Process(1, Anchor, 12345.678, false);
            Near(view.Scale, Math.Pow(1.18, 1 / 120.0)); Check(!wheel.IsActive, "no animation");
        });
        Run("navigation accumulates delta and preserves remainder", delegate {
            var navigation = new WheelNavigation();
            for (int i = 0; i < 7; i++) Check(navigation.Add(15) == 0, "small inputs do not navigate");
            Check(navigation.Add(30) == -1, "crossed threshold");
            Check(navigation.Add(-120) == 0, "signed remainder retained");
            Check(navigation.Add(-15) == 1, "negative threshold");
            Check(navigation.Add(240) == -2, "multiple steps");
            navigation.Add(60); navigation.Reset(); Check(navigation.Add(60) == 0, "reset remainder");
        });
        Run("surface commands cancel pending zoom", delegate {
            var surface = new ImageSurface();
            var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
            surface.SetImage(bitmap, 2000, 1000);
            surface.Measure(new Size(800, 600)); surface.Arrange(new Rect(0, 0, 800, 600));
            Action[] commands = { surface.Fit, surface.ActualSize, delegate { surface.Pan(3, 4); }, delegate { surface.Zoom(1.1); }, delegate { surface.SetImage(bitmap, 2000, 1000); } };
            foreach (Action command in commands) {
                surface.ProcessWheel(120, Anchor, false); command();
                Check(!surface.IsZoomAnimating, "command cancellation");
                double scale = surface.View.Scale; surface.AdvanceWheelZoom(Double.MaxValue); Near(surface.View.Scale, scale);
            }
            surface.ProcessWheel(120, Anchor, false); surface.ClearImage();
            Check(!surface.IsZoomAnimating, "clear cancellation");
        });
        Console.WriteLine("RESULT: " + passed + " passed, " + failed + " failed");
        return failed == 0 ? 0 : 1;
    }
    private static Viewport View() { var view = new Viewport(); view.Resize(800, 600); view.SetImage(2000, 1000); view.ActualSize(); return view; }
    private static void Run(string name, Action test) { try { test(); passed++; Console.WriteLine("PASS " + name); } catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); } }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Near(double value, double expected) { Check(Math.Abs(value - expected) < 1e-9, value + " != " + expected); }
}
