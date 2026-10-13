using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FastImageViewer
{
    internal static class IntegrationTests
    {
        private static MainWindow window;
        private static ImageSurface surface;
        private static string directory, first, second, corrupt, last, empty;
        private static int passed, failed;

        [STAThread]
        public static int Main(string[] args)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            directory = Path.Combine(Path.GetFullPath(args[0]), "integration");
            Directory.CreateDirectory(directory);
            first = Path.Combine(directory, "1.png"); second = Path.Combine(directory, "2.jpg");
            corrupt = Path.Combine(directory, "3.png"); last = Path.Combine(directory, "4.png");
            empty = Path.Combine(directory, "emptyfolder"); Directory.CreateDirectory(empty);
            SaveSample(first, false); SaveSample(second, true); SaveSample(last, false);
            File.WriteAllText(corrupt, "This is a deliberately corrupt generated test image.");
            window = new MainWindow(new string[0]);
            surface = Field<ImageSurface>("surface");
            Layout();
            Run("latest async open wins", delegate
            {
                window.OpenPaths(new[] { first }); window.OpenPaths(new[] { second }); window.OpenPaths(new[] { last });
                WaitLoaded(last); PumpFor(250);
                Check(Loaded().Path == last, "a stale async request replaced the last image");
                Check(Field<TextBlock>("filename").Text == "4.png", "filename does not match latest image");
                Check(Field<TextBlock>("metadata").Text.Contains("900"), "metadata lacks original width");
            });
            Run("navigation boundaries do not wrap", delegate
            {
                window.Navigate(1); Check(Field<int>("index") == 3, "last image wrapped");
                window.OpenPaths(new[] { first }); WaitLoaded(first);
                window.Navigate(-1); Check(Field<int>("index") == 0, "first image wrapped");
                Check(!Field<Button>("previous").IsEnabled, "previous enabled at first image");
                window.Navigate(100); WaitLoaded(last);
                Check(!Field<Button>("next").IsEnabled, "next enabled at last image");
            });
            Run("corrupt image clears bitmap and recovers", delegate
            {
                window.OpenPaths(new[] { corrupt });
                PumpUntil(delegate { return Field<int>("index") == 2 && surface.Bitmap == null && Field<TextBlock>("metadata").Text.Contains("失败"); }, "corrupt image failure state");
                Check(Loaded() == null, "corrupt image retained loaded metadata");
                Check(Field<Border>("message").Visibility == Visibility.Visible, "missing error message");
                Check(Field<TextBlock>("messageText").Text.Contains("3.png"), "error does not identify file");
                window.Navigate(1); WaitLoaded(last);
                Check(Field<Border>("message").Visibility == Visibility.Collapsed, "error message retained after recovery");
            });
            Run("empty folder clears all stale image state", delegate
            {
                string oldName = Field<TextBlock>("filename").Text;
                window.OpenPaths(new[] { empty });
                PumpUntil(delegate { return Loaded() == null && surface.Bitmap == null && Field<TextBlock>("messageText").Text.Contains("没有"); }, "empty folder state");
                Check(Field<TextBlock>("filename").Text != oldName, "empty folder retained filename: " + oldName);
                Check(Field<TextBlock>("filename").ToolTip == null, "empty folder retained filename tooltip");
                Check(!Field<TextBlock>("metadata").Text.Contains("900"), "empty folder retained image dimensions");
                Check(Field<TextBlock>("counter").Text == "", "empty folder retained counter");
                Check(!Field<Button>("previous").IsEnabled && !Field<Button>("next").IsEnabled, "empty folder retained navigation");
            });
            Run("wheel event zoom and handled", delegate
            {
                window.OpenPaths(new[] { second }); WaitLoaded(second); Layout();
                surface.ActualSize(); double before = surface.View.Scale;
                var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = Mouse.MouseWheelEvent };
                surface.RaiseEvent(wheel);
                Check(wheel.Handled, "wheel event was not handled");
                FinishWheel();
                Near(surface.View.Scale, before * 1.18, "wheel factor");
                var reverse = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.MouseWheelEvent };
                surface.RaiseEvent(reverse); FinishWheel(); Near(surface.View.Scale, before, "reverse wheel factor");
            });
            Run("small wheel deltas retain fractional zoom", delegate
            {
                surface.ActualSize(); double before = surface.View.Scale;
                RaiseWheel(1); FinishWheel();
                Near(surface.View.Scale, before * Math.Pow(1.18, 1.0 / 120), "single delta zoom");
                surface.ActualSize();
                for (int i = 0; i < 8; i++) RaiseWheel(15);
                FinishWheel(); Near(surface.View.Scale, before * 1.18, "eight fractional deltas");
            });
            Run("fit and image changes cancel pending wheel zoom", delegate
            {
                RaiseWheel(120); surface.Fit(); double fitted = surface.View.Scale;
                FinishWheel(); Near(surface.View.Scale, fitted, "Fit allowed stale wheel animation");
                Check(surface.View.IsFit && !surface.IsZoomAnimating, "Fit retained animation");
                RaiseWheel(120); window.OpenPaths(new[] { first }); WaitLoaded(first);
                double opened = surface.View.Scale;
                FinishWheel(); Near(surface.View.Scale, opened, "new image allowed stale wheel animation");
                Check(surface.View.IsFit && !surface.IsZoomAnimating, "new image retained animation");
                window.OpenPaths(new[] { second }); WaitLoaded(second);
            });
            Run("percentage input applies precise zoom", delegate
            {
                var input = Field<TextBox>("zoomInput"); input.Text = "125.5%";
                Check((bool)Call("TryApplyZoom"), "percentage input was rejected");
                Near(surface.View.Scale * surface.DpiScale, 1.255, "percentage zoom rounded to preset");
                double scale = surface.View.Scale;
                input.Text = "invalid";
                Check(!(bool)Call("TryApplyZoom"), "invalid percentage accepted");
                Near(surface.View.Scale, scale, "invalid percentage changed zoom");
                surface.ActualSize();
                // Simulate focus state without showing a window or changing desktop keyboard focus.
                SetOffscreenFocusWithin(input, true);
                try
                {
                    Check(!Field<bool>("zoomTextDirty"), "synchronized input retained dirty state");
                    surface.Zoom(1.18);
                    Check(input.Text == "118%", "clean focused input did not follow zoom");
                    scale = surface.View.Scale;
                    Check((bool)Call("TryApplyZoom"), "clean input application failed");
                    Near(surface.View.Scale, scale, "clean input restored stale zoom");
                    input.Text = "125.5%";
                    Check(Field<bool>("zoomTextDirty"), "edited percentage was not dirty");
                    RaiseWheel(120); FinishWheel();
                    Check(input.Text == "125.5%", "wheel animation replaced focused draft");
                    var enter = new KeyEventArgs(Keyboard.PrimaryDevice, new TestPresentationSource(), Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = input };
                    Invoke("OnKey", input, enter);
                    Check(enter.Handled, "Enter did not apply percentage draft");
                    Near(surface.View.Scale * surface.DpiScale, 1.255, "Enter applied wheel scale instead of percentage draft");
                    Check(!Field<bool>("zoomTextDirty"), "applied draft retained dirty state");
                }
                finally { SetOffscreenFocusWithin(input, false); }
                surface.ActualSize();
            });
            Run("typing does not trigger image shortcuts", delegate
            {
                var input = Field<TextBox>("zoomInput");
                surface.Zoom(1.255); double scale = surface.View.Scale;
                int width = surface.Bitmap.PixelWidth;
                foreach (Key key in new[] { Key.F, Key.R, Key.D0 })
                {
                    var e = new KeyEventArgs(Keyboard.PrimaryDevice, new TestPresentationSource(), Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = input };
                    Invoke("OnKey", input, e);
                    Check(!e.Handled, "text entry shortcut was handled: " + key);
                    Near(surface.View.Scale, scale, "text entry changed zoom: " + key);
                    Check(surface.Bitmap.PixelWidth == width, "text entry rotated image");
                }
            });
            Run("fit and actual-size surface wrappers", delegate
            {
                surface.Zoom(2); surface.Fit(); Check(surface.View.IsFit, "Fit wrapper did not enter fit mode");
                surface.ActualSize(); Check(!surface.View.IsFit, "ActualSize wrapper retained fit mode");
                Near(surface.View.Scale * surface.DpiScale, 1, "physical actual size");
                Check(Field<TextBox>("zoomInput").Text == "100%", "actual-size status input incorrect");
            });
            Run("rotation transforms display only", delegate
            {
                byte[] before = File.ReadAllBytes(second);
                Invoke("Rotate", 1);
                Check(surface.Bitmap.PixelWidth == 520 && surface.Bitmap.PixelHeight == 900, "rotated bitmap dimensions incorrect");
                Check(Loaded().PixelWidth == 900 && Loaded().PixelHeight == 520, "rotation changed source dimensions");
                Invoke("Rotate", -1);
                Check(surface.Bitmap.PixelWidth == 900 && surface.Bitmap.PixelHeight == 520, "inverse rotation did not restore image");
                Check(Convert.ToBase64String(before) == Convert.ToBase64String(File.ReadAllBytes(second)), "rotation modified file contents");
            });
            Run("information panel refreshes during load and recovery", delegate
            {
                Invoke("TogglePanel", "info");
                var body = Field<StackPanel>("panelBody");
                Check(PanelText(body).Contains("2.jpg"), "initial information panel lacks current file");
                window.OpenPaths(new[] { corrupt });
                Check(!PanelText(body).Contains("2.jpg"), "loading retained stale image information");
                PumpUntil(delegate { return Loaded() == null && Field<TextBlock>("metadata").Text.Contains("失败"); }, "information panel failure state");
                Check(!PanelText(body).Contains(second), "failure retained old copy path information");
                window.Navigate(1); WaitLoaded(last);
                Check(PanelText(body).Contains("4.png"), "recovery did not refresh information panel");
                Invoke("TogglePanel", "info");
            });
            Run("compact and full-size layout remains usable", delegate
            {
                foreach (Size size in new[] { new Size(700, 440), new Size(1100, 720) })
                {
                    Layout(size.Width, size.Height); CheckToolbar();
                    var bottom = (FrameworkElement)Field<Grid>("root").Children[2];
                    Rect modeBounds = Bounds(Field<Button>("displayMode"), bottom);
                    Check(modeBounds.Left >= 0 && modeBounds.Right <= bottom.ActualWidth + 1 && modeBounds.Bottom <= bottom.ActualHeight + 1, "display mode indicator exceeds status bar");
                    Invoke("TogglePanel", "help"); Layout(size.Width, size.Height); CheckToolbar(); CheckDockedPanel();
                    Invoke("TogglePanel", "help");
                    Invoke("TogglePanel", "info"); Layout(size.Width, size.Height); CheckDockedPanel();
                    Invoke("TogglePanel", "info"); Layout(size.Width, size.Height);
                }
            });
            Run("display mode is visible and synchronized across commands", delegate
            {
                Action<bool> verify = delegate(bool pixel)
                {
                    Check(surface.PixelMode == pixel, "render mode differs from selected mode");
                    Check(Convert.ToString(Field<Button>("displayMode").Content) == (pixel ? "像素模式" : "平滑模式"), "status bar shows incorrect mode");
                    foreach (MenuItem item in Field<System.Collections.Generic.List<MenuItem>>("smoothModeItems")) Check(item.IsChecked == !pixel, "smooth mode menu check is stale");
                    foreach (MenuItem item in Field<System.Collections.Generic.List<MenuItem>>("pixelModeItems")) Check(item.IsChecked == pixel, "pixel mode menu check is stale");
                };
                verify(false);
                var key = new KeyEventArgs(Keyboard.PrimaryDevice, new TestPresentationSource(), Environment.TickCount, Key.P) { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = surface };
                Invoke("OnKey", surface, key);
                Check(key.Handled, "P shortcut was not handled"); verify(true);
                window.OpenPaths(new[] { first }); WaitLoaded(first); verify(true);
                var selected = Field<System.Collections.Generic.List<MenuItem>>("pixelModeItems")[0];
                selected.IsChecked = false;
                selected.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); verify(true);
                Field<System.Collections.Generic.List<MenuItem>>("smoothModeItems")[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); verify(false);
                var indicator = Field<Button>("displayMode");
                indicator.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); verify(true);
                indicator.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); verify(false);
            });
            Run("loaded and empty offscreen previews", delegate
            {
                window.OpenPaths(new[] { second }); WaitLoaded(second); Layout(); surface.Fit();
                SavePreview(Path.Combine(Path.GetFullPath(args[0]), "viewer-preview.png"));
                var emptyWindow = new MainWindow(new string[0]);
                SavePreview(Path.Combine(Path.GetFullPath(args[0]), "viewer-empty-preview.png"), emptyWindow);
                emptyWindow.Close();
            });
            window.Close(); app.Shutdown();
            Console.WriteLine("RESULT: " + passed + " passed, " + failed + " failed");
            return failed == 0 ? 0 : 1;
        }

        private static string PanelText(DependencyObject parent)
        {
            var text = parent as TextBlock;
            if (text != null) return text.Text;
            string result = "";
            foreach (object child in LogicalTreeHelper.GetChildren(parent))
            {
                var element = child as DependencyObject;
                if (element != null) result += " " + PanelText(element);
            }
            return result;
        }
        private static LoadedImage Loaded() { return Field<LoadedImage>("loaded"); }
        private static T Field<T>(string name) { return (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window); }
        private static object Call(string name, params object[] args) { return typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, args); }
        private static void Invoke(string name, params object[] args) { Call(name, args); }
        private static void Check(bool valid, string message) { if (!valid) throw new Exception(message); }
        private static void Near(double actual, double expected, string message) { Check(Math.Abs(actual - expected) < 0.000001, message + ": actual=" + actual + ", expected=" + expected); }
        private static void Run(string name, Action action)
        {
            try { action(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); }
        }
        private static void Layout(double width = 1100, double height = 720)
        {
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        }
        private static void RaiseWheel(int delta)
        {
            var e = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = Mouse.MouseWheelEvent };
            surface.RaiseEvent(e); Check(e.Handled, "fractional wheel was not handled");
        }
        private static void SetOffscreenFocusWithin(UIElement element, bool focused)
        {
            // WPF's CLR getter reads this cache rather than the read-only dependency property.
            MethodInfo writeFlag = typeof(UIElement).GetMethod("WriteFlag", BindingFlags.Instance | BindingFlags.NonPublic);
            Type flags = writeFlag.GetParameters()[0].ParameterType;
            writeFlag.Invoke(element, new[] { Enum.Parse(flags, "IsKeyboardFocusWithinCache"), (object)focused });
            Check(element.IsKeyboardFocusWithin == focused, "offscreen focus state simulation failed");
        }
        private static void FinishWheel()
        {
            surface.AdvanceWheelZoom((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency + 5);
            Check(!surface.IsZoomAnimating, "wheel animation did not settle");
        }
        private static Rect Bounds(FrameworkElement element, Visual ancestor)
        {
            return element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));
        }
        private static void CheckToolbar()
        {
            var root = Field<Grid>("root"); var toolbar = (FrameworkElement)root.Children[0];
            var actionable = new System.Collections.Generic.List<FrameworkElement>();
            CollectControls(toolbar, actionable);
            for (int i = 0; i < actionable.Count; i++)
            {
                FrameworkElement control = actionable[i]; Rect bounds = Bounds(control, toolbar);
                Check(bounds.Width > 0 && bounds.Height > 0, "toolbar control has no size");
                Check(bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= toolbar.ActualWidth + 1 && bounds.Bottom <= toolbar.ActualHeight + 1, "toolbar control clipped: " + ControlName(control) + " " + bounds);
                CheckContentFits(control);
                for (int j = 0; j < i; j++)
                {
                    Rect overlap = Rect.Intersect(bounds, Bounds(actionable[j], toolbar));
                    Check(overlap.IsEmpty || overlap.Width < 1 || overlap.Height < 1, "toolbar controls overlap: " + ControlName(control) + " / " + ControlName(actionable[j]));
                }
            }
            Check(actionable.Count > 8, "toolbar controls missing from layout");
        }
        private static string ControlName(FrameworkElement control)
        {
            var button = control as Button; return button != null ? Convert.ToString(button.Content) : "zoomInput";
        }
        private static void CheckContentFits(DependencyObject node)
        {
            var presenter = node as ContentPresenter;
            if (presenter != null) Check(presenter.ActualWidth + 1 >= presenter.DesiredSize.Width && presenter.ActualHeight + 1 >= presenter.DesiredSize.Height, "toolbar button content clipped");
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) CheckContentFits(VisualTreeHelper.GetChild(node, i));
        }
        private static void CollectControls(DependencyObject node, System.Collections.Generic.List<FrameworkElement> controls)
        {
            var element = node as FrameworkElement;
            if (element != null && element.Visibility != Visibility.Visible) return;
            if (node is Button || node is TextBox) { controls.Add(element); return; }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) CollectControls(VisualTreeHelper.GetChild(node, i), controls);
        }
        private static void CheckDockedPanel()
        {
            Grid stage = Field<Grid>("stage"); Border panel = Field<Border>("panel");
            Rect panelBounds = Bounds(panel, stage), imageBounds = Bounds(surface, stage);
            Check(panelBounds.Left >= 0 && panelBounds.Right <= stage.ActualWidth + 1 && panelBounds.Bottom <= stage.ActualHeight + 1, "side panel exceeds stage");
            Check(imageBounds.Width >= 300, "side panel leaves insufficient image area");
            Rect overlap = Rect.Intersect(panelBounds, imageBounds);
            Check(overlap.IsEmpty || overlap.Width < 1, "side panel overlays image surface");
        }
        private static void SavePreview(string path, MainWindow target = null)
        {
            var content = (FrameworkElement)(target ?? window).Content;
            content.Measure(new Size(1100, 720)); content.Arrange(new Rect(0, 0, 1100, 720)); content.UpdateLayout();
            var preview = new RenderTargetBitmap(1100, 720, 96, 96, PixelFormats.Pbgra32); preview.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(preview));
            using (var stream = File.Create(path)) encoder.Save(stream);
            Check(new FileInfo(path).Length > 5000, "offscreen preview appears empty");
            Console.WriteLine("PREVIEW " + path + " (offscreen WPF render, no desktop screenshot)");
        }
        private sealed class TestPresentationSource : PresentationSource
        {
            public override Visual RootVisual { get; set; }
            public override bool IsDisposed { get { return false; } }
            protected override CompositionTarget GetCompositionTargetCore() { return null; }
        }
        private static void WaitLoaded(string path)
        {
            PumpUntil(delegate { return Loaded() != null && Loaded().Path == path && surface.Bitmap != null; }, "load " + Path.GetFileName(path));
        }
        private static void PumpFor(int milliseconds)
        {
            var watch = Stopwatch.StartNew(); PumpUntil(delegate { return watch.ElapsedMilliseconds >= milliseconds; }, "dispatcher settle");
        }
        private static void PumpUntil(Func<bool> condition, string description)
        {
            var watch = Stopwatch.StartNew(); var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += delegate { if (condition() || watch.ElapsedMilliseconds > 10000) frame.Continue = false; };
            timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
            Check(condition(), "timeout waiting for " + description + "; metadata=" + Field<TextBlock>("metadata").Text + "; message=" + Field<TextBlock>("messageText").Text);
        }
        private static void SaveSample(string path, bool jpeg)
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(92, 153, 198), Color.FromRgb(229, 194, 153), 90), null, new Rect(0, 0, 900, 520));
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(255, 227, 174)), null, new Point(690, 140), 54, 54);
                var mountains = new StreamGeometry();
                using (StreamGeometryContext context = mountains.Open())
                {
                    context.BeginFigure(new Point(0, 390), true, true);
                    context.LineTo(new Point(230, 180), true, false); context.LineTo(new Point(410, 360), true, false);
                    context.LineTo(new Point(600, 250), true, false); context.LineTo(new Point(900, 430), true, false);
                    context.LineTo(new Point(900, 520), true, false); context.LineTo(new Point(0, 520), true, false);
                }
                dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(50, 77, 88)), null, mountains);
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(36, 66, 73)), null, new Rect(0, 450, 900, 70));
            }
            var bitmap = new RenderTargetBitmap(900, 520, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            BitmapEncoder encoder = jpeg ? (BitmapEncoder)new JpegBitmapEncoder { QualityLevel = 90 } : new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var stream = File.Create(path)) encoder.Save(stream);
        }
    }
}
