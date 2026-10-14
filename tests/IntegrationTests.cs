using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        private static readonly MemoryClipboard clipboard = new MemoryClipboard();
        private static string revealedPath;

        private sealed class MemoryClipboard : IViewerClipboard
        {
            public IDataObject Data;
            public IDataObject Read() { return Data; }
            public void Write(IDataObject data) { Data = data; }
        }

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
            window = new MainWindow(new string[0], false, clipboard, path => revealedPath = path);
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
            Run("themes update all surfaces without changing the image view", delegate
            {
                surface.Zoom(1.255); surface.Pan(37, -21); surface.PixelMode = true;
                BitmapSource source = surface.Bitmap;
                double scale = surface.View.Scale, x = surface.View.OffsetX, y = surface.View.OffsetY;
                Action<bool> verify = delegate(bool dark)
                {
                    Check(Field<ViewerTheme>("theme").IsDark == dark, "theme state is incorrect");
                    Check(ColorOf(surface.Background) == (dark ? "#202020" : "#FAFAFA"), "canvas theme is stale");
                    var toolbar = (Grid)Field<Grid>("root").Children[0];
                    Check(ColorOf(toolbar.Background) == (dark ? "#292929" : "#F3F3F3"), "toolbar theme is stale");
                    Check(ColorOf(Field<Grid>("stage").Background) == ColorOf(surface.Background), "stage background differs from canvas");
                    Check(ColorOf(Field<TextBox>("zoomInput").Background) == (dark ? "#202020" : "#FFFFFF"), "zoom input background is stale");
                    Check(ColorOf(Field<TextBox>("zoomInput").Foreground) == (dark ? "#E8E8E8" : "#262626"), "zoom input text is stale");
                    Check(ColorOf(Field<Border>("panel").Background) == ColorOf(toolbar.Background), "panel theme is stale");
                    var themeButton = Field<Button>("themeToggle");
                    Check(ColorOf(((ViewerIcon)themeButton.Content).Foreground) == (dark ? "#E8E8E8" : "#262626"), "icon contrast is stale");
                    foreach (MenuItem item in Field<System.Collections.Generic.List<MenuItem>>("lightThemeItems")) Check(item.IsChecked == !dark, "light theme check is stale");
                    foreach (MenuItem item in Field<System.Collections.Generic.List<MenuItem>>("darkThemeItems")) Check(item.IsChecked == dark, "dark theme check is stale");
                    Check(Object.ReferenceEquals(surface.Bitmap, source), "theme reloaded or replaced the image");
                    Near(surface.View.Scale, scale, "theme changed zoom"); Near(surface.View.OffsetX, x, "theme changed X"); Near(surface.View.OffsetY, y, "theme changed Y");
                    Check(surface.PixelMode, "theme changed display mode");
                    var menu = surface.ContextMenu; menu.ApplyTemplate(); menu.Measure(new Size(420, 650));
                    Check(ColorOf(menu.Background) == ColorOf(toolbar.Background), "popup theme is stale");
                    var tip = (ToolTip)themeButton.ToolTip; tip.ApplyTemplate(); tip.Measure(new Size(420, 100));
                    Check(ColorOf(tip.Background) == ColorOf(toolbar.Background), "tooltip background is stale");
                    Check(ColorOf(tip.Foreground) == (dark ? "#E8E8E8" : "#262626"), "tooltip text is stale");
                    double contrast = Contrast((SolidColorBrush)themeButton.Foreground, (SolidColorBrush)toolbar.Background);
                    Check(contrast >= 4.5, "theme text has insufficient contrast");
                    Check(Contrast((SolidColorBrush)Field<TextBlock>("metadata").Foreground, (SolidColorBrush)toolbar.Background) >= 4.5, "secondary text has insufficient contrast");
                };
                Invoke("SetTheme", true); verify(true);
                Invoke("SetTheme", false); verify(false);
                var key = new KeyEventArgs(Keyboard.PrimaryDevice, new TestPresentationSource(), Environment.TickCount, Key.D) { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = surface };
                Invoke("OnKey", surface, key); Check(key.Handled, "theme shortcut was not handled"); verify(true);
                Field<Button>("themeToggle").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); verify(false);
                Field<System.Collections.Generic.List<MenuItem>>("darkThemeItems")[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); verify(true);
                Field<System.Collections.Generic.List<MenuItem>>("lightThemeItems")[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); verify(false);
                surface.PixelMode = false;
            });
            Run("theme changes preserve wheel animation and typed zoom drafts", delegate
            {
                RaiseWheel(120); bool animating = surface.IsZoomAnimating;
                Invoke("SetTheme", true); Check(surface.IsZoomAnimating == animating, "theme cancelled pending wheel animation");
                FinishWheel();
                var input = Field<TextBox>("zoomInput"); input.Text = "127.5%";
                var key = new KeyEventArgs(Keyboard.PrimaryDevice, new TestPresentationSource(), Environment.TickCount, Key.D) { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = input };
                Invoke("OnKey", input, key); Check(!key.Handled && Field<ViewerTheme>("theme").IsDark, "text input triggered theme shortcut");
                Check(input.Text == "127.5%", "theme changed zoom draft");
                Check((bool)Call("TryApplyZoom"), "theme prevented applying zoom draft");
                Invoke("SetTheme", false);
            });
            Run("theme preferences round trip in isolated test files", delegate
            {
                string preference = Path.Combine(directory, "theme-preference.txt");
                Check(ThemePreference.Save(preference, true) && ThemePreference.Load(preference), "dark preference did not round trip");
                Check(ThemePreference.Save(preference, false) && !ThemePreference.Load(preference), "light preference did not round trip");
                File.WriteAllText(preference, "unknown"); Check(!ThemePreference.Load(preference), "invalid preference did not fall back to light");
                Check(!ThemePreference.Load(Path.Combine(directory, "missing-theme.txt")), "missing preference did not fall back to light");
                string blocked = Path.Combine(directory, "theme-blocker"); File.WriteAllText(blocked, "unchanged");
                Check(!ThemePreference.Save(Path.Combine(blocked, "theme.txt"), true), "unwritable preference was reported as saved");
                Check(File.ReadAllText(blocked) == "unchanged", "preference failure modified blocking file");
            });
            Run("themed scrollbar supports paging and thumb movement", delegate
            {
                Invoke("SetTheme", true); Invoke("TogglePanel", "help"); Layout(700, 440);
                var viewer = (ScrollViewer)Field<Border>("panel").Child;
                ScrollBar bar = FindVerticalScrollBar(viewer);
                Check(bar != null && bar.Maximum > 0, "help scrollbar is missing");
                Track track = (Track)bar.Template.FindName("PART_Track", bar);
                Check(track != null && track.Thumb != null, "themed scrollbar track is missing");
                Check(ColorOf(bar.Background) == "#292929", "scrollbar retained light background");
                ScrollBar.PageDownCommand.Execute(null, bar); PumpFor(40); Layout(700, 440);
                Check(viewer.VerticalOffset > 0, "page down command did not scroll");
                viewer.ScrollToVerticalOffset(0); PumpFor(40); Layout(700, 440);
                track.Thumb.RaiseEvent(new DragDeltaEventArgs(0, 25) { RoutedEvent = Thumb.DragDeltaEvent });
                PumpFor(40); Layout(700, 440);
                Check(viewer.VerticalOffset > 0, "thumb movement did not scroll");
                Invoke("SetTheme", false); Check(ColorOf(bar.Background) == "#F3F3F3", "scrollbar did not return to light");
                Invoke("TogglePanel", "help"); Layout();
            });
            Run("navigation uses a preloaded neighbour and retains source ownership", delegate
            {
                window.OpenPaths(new[] { first }); WaitLoaded(first);
                var cache = Field<ImageLoadCache<LoadedImage>>("imageCache");
                var task = cache.LoadAsync(second);
                PumpUntil(delegate { return task.IsCompleted; }, "neighbour preload");
                using (var lease = task.Result)
                {
                    window.Navigate(1); WaitLoaded(second);
                    Check(Object.ReferenceEquals(lease.Value, Loaded()), "navigation decoded a cached neighbour again");
                    Check(surface.Bitmap.IsFrozen, "cached image lost frozen state");
                }
            });
            Run("keep-view option preserves manual zoom and synchronizes menus", delegate
            {
                window.OpenPaths(new[] { first }); WaitLoaded(first); Layout();
                Invoke("SetKeepView", true); surface.ActualSize(); surface.Zoom(1.3); surface.Pan(37, -19);
                Point before = surface.View.ImagePointAt(surface.ActualWidth / 2, surface.ActualHeight / 2);
                double scale = surface.View.Scale;
                window.Navigate(1); WaitLoaded(second);
                Near(surface.View.Scale, scale, "kept zoom");
                Point after = surface.View.ImagePointAt(surface.ActualWidth / 2, surface.ActualHeight / 2);
                Near(after.X, before.X, "kept center X"); Near(after.Y, before.Y, "kept center Y");
                foreach (MenuItem item in Field<System.Collections.Generic.List<MenuItem>>("keepViewItems")) Check(item.IsChecked, "menu state is not synchronized");
                window.OpenPaths(new[] { second }); WaitLoaded(second);
                Check(surface.View.IsFit, "opening a new source inherited old view");
                Invoke("SetKeepView", false); window.Navigate(-1); WaitLoaded(first);
                Check(surface.View.IsFit, "default navigation did not fit");
            });
            Run("clipboard PNG retains alpha, rotates and clears disk navigation", delegate
            {
                byte[] pixels = { 255, 0, 0, 128, 0, 0, 255, 255, 0, 255, 0, 0, 255, 255, 255, 255, 1, 2, 3, 255, 4, 5, 6, 255 };
                BitmapSource sample = BitmapSource.Create(3, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 12); sample.Freeze();
                var payload = ClipboardImages.CopyData(sample, ClipboardImages.EncodePng(sample));
                window.OpenPaths(new[] { second });
                Check(window.OpenClipboardData(payload), "clipboard image was not accepted"); PumpFor(150);
                Check(Loaded() == null && Field<int>("index") == -1, "disk source was retained");
                Check(Field<TextBlock>("counter").Text == "" && Field<TextBlock>("filename").Text == "剪贴板图片", "clipboard source labels are wrong");
                Check(!Field<Button>("next").IsEnabled && !Field<Button>("previous").IsEnabled, "clipboard retained navigation");
                clipboard.Data = null; Invoke("Rotate", 1); surface.ActualSize(); surface.Zoom(2); surface.ToggleCheckerboard();
                BitmapSource expected = surface.Bitmap;
                window.CopyImage(); PumpUntil(delegate { return clipboard.Data != null; }, "clipboard copy");
                ClipboardImage copied = ClipboardImages.Read(clipboard.Data);
                Check(copied.Bitmap.PixelWidth == 2 && copied.Bitmap.PixelHeight == 3, "copied viewport instead of oriented pixels");
                var a = new FormatConvertedBitmap(expected, PixelFormats.Bgra32, null, 0);
                var b = new FormatConvertedBitmap(copied.Bitmap, PixelFormats.Bgra32, null, 0);
                byte[] wanted = new byte[24], actualPixels = new byte[24]; a.CopyPixels(wanted, 8, 0); b.CopyPixels(actualPixels, 8, 0);
                for (int i = 0; i < wanted.Length; i++) Check(Math.Abs(wanted[i] - actualPixels[i]) <= 1, "pixel/alpha mismatch at " + i);
                foreach (MenuItem item in Field<System.Collections.Generic.List<MenuItem>>("revealFileItems")) Check(!item.IsEnabled, "memory image offers file location");
                Invoke("TogglePanel", "info"); Check(PanelText(Field<Border>("panel")).Contains("剪贴板"), "clipboard info source missing"); Invoke("TogglePanel", "info");
                surface.ToggleCheckerboard();
            });
            Run("paste accepts files and paths, and invalid contents preserve the view", delegate
            {
                var fileData = new DataObject(DataFormats.FileDrop, new[] { last }); clipboard.Data = fileData;
                window.PasteImage(); WaitLoaded(last);
                window.RevealFile(); Check(revealedPath == last, "file reveal uses wrong source");
                var pathData = new DataObject(DataFormats.UnicodeText, "\"" + first + "\"");
                Check(window.OpenClipboardData(pathData), "quoted path was not accepted"); WaitLoaded(first);
                BitmapSource original = surface.Bitmap;
                Check(!window.OpenClipboardData(new DataObject(DataFormats.UnicodeText, "not a path")), "unrelated text treated as image");
                Check(Object.ReferenceEquals(original, surface.Bitmap) && Loaded().Path == first, "invalid clipboard cleared current image");
            });
            Run("new clipboard shortcuts preserve text editing", delegate
            {
                window.OpenPaths(new[] { first }); WaitLoaded(first);
                var input = Field<TextBox>("zoomInput"); SetOffscreenFocusWithin(input, true);
                bool before = Field<bool>("keepView");
                var key = new KeyEventArgs(Keyboard.PrimaryDevice, new TestPresentationSource(), Environment.TickCount, Key.K) { RoutedEvent = Keyboard.PreviewKeyDownEvent, Source = input };
                window.RaiseEvent(key); Check(Field<bool>("keepView") == before && !key.Handled, "K interfered with text input");
                SetOffscreenFocusWithin(input, false);
            });
            Run("WebP opens through the viewer and participates in folder navigation", delegate
            {
                string webpFolder = Path.Combine(directory, "webp-folder"); Directory.CreateDirectory(webpFolder);
                string webp = Path.Combine(webpFolder, "1.webp");
                string other = Path.Combine(webpFolder, "2.WEBP");
                string fixture = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0])), "tests", "fixtures", "webp", "lossless.webp");
                File.Copy(fixture, webp, true); File.Copy(fixture, other, true);
                window.OpenPaths(new[] { webp }); WaitLoaded(webp);
                Check(surface.Bitmap.IsFrozen && Field<TextBlock>("counter").Text == "1 / 2", "WebP viewer/list integration failed");
                window.Navigate(1); WaitLoaded(other);
                Check(Field<TextBlock>("counter").Text == "2 / 2", "WebP folder navigation failed");
            });
            Run("loaded and empty offscreen previews", delegate
            {
                window.OpenPaths(new[] { second }); WaitLoaded(second); Layout(); surface.Fit();
                SavePreview(Path.Combine(Path.GetFullPath(args[0]), "viewer-preview.png"));
                SavePreview(Path.Combine(Path.GetFullPath(args[0]), "viewer-light-preview.png"));
                Invoke("SetTheme", true);
                SavePreview(Path.Combine(Path.GetFullPath(args[0]), "viewer-dark-preview.png"));
                Invoke("TogglePanel", "info");
                SavePreview(Path.Combine(Path.GetFullPath(args[0]), "viewer-dark-info-preview.png"));
                Invoke("TogglePanel", "info");
                Invoke("TogglePanel", "help");
                SavePreview(Path.Combine(Path.GetFullPath(args[0]), "viewer-dark-help-preview.png"));
                Invoke("TogglePanel", "help"); Invoke("SetTheme", false);
                var emptyWindow = new MainWindow(new string[0], false);
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
        private static string ColorOf(Brush brush)
        {
            Color color = ((SolidColorBrush)brush).Color;
            return "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
        }
        private static ScrollBar FindVerticalScrollBar(DependencyObject parent)
        {
            var bar = parent as ScrollBar;
            if (bar != null && bar.Orientation == Orientation.Vertical && bar.Visibility == Visibility.Visible) return bar;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var found = FindVerticalScrollBar(VisualTreeHelper.GetChild(parent, i));
                if (found != null) return found;
            }
            return null;
        }
        private static double Contrast(SolidColorBrush a, SolidColorBrush b)
        {
            Func<Color, double> luminance = delegate(Color c)
            {
                Func<byte, double> linear = delegate(byte value) { double v = value / 255.0; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); };
                return .2126 * linear(c.R) + .7152 * linear(c.G) + .0722 * linear(c.B);
            };
            double x = luminance(a.Color), y = luminance(b.Color);
            return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
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
