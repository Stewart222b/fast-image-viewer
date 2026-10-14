using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering;
using Avalonia.Threading;
using FastImageViewer.Core;

namespace FastImageViewer.Desktop
{
    public sealed class ImageCanvas : Control, ICustomHitTest
    {
        private readonly Viewport view = new Viewport();
        private readonly WheelZoom wheel;
        private readonly WheelNavigation navigation = new WheelNavigation();
        private readonly DispatcherTimer timer;
        private readonly SolidColorBrush canvasBrush = new SolidColorBrush(Color.FromRgb(32, 32, 32));
        private readonly SolidColorBrush checkerDark = new SolidColorBrush(Color.FromRgb(56, 56, 56));
        private readonly SolidColorBrush checkerLight = new SolidColorBrush(Color.FromRgb(68, 68, 68));
        private Bitmap bitmap;
        private bool pixelMode;
        private bool checkerboard;
        private bool dragging;
        private Point lastPoint;
        private TopLevel topLevel;

        public Viewport View { get { return view; } }
        public bool HasImage { get { return bitmap != null; } }
        public bool IsZoomAnimating { get { return wheel.IsActive; } }
        public event Action ViewChanged;
        public event Action<int> Navigate;
        public event Action ToggleActualSize;

        public bool PixelMode
        {
            get { return pixelMode; }
            set
            {
                if (pixelMode == value) return;
                pixelMode = value;
                Changed();
            }
        }

        public double DpiScale
        {
            get
            {
                var top = TopLevel.GetTopLevel(this);
                return top == null || top.RenderScaling <= 0 ? 1 : top.RenderScaling;
            }
        }

        public ImageCanvas()
        {
            wheel = new WheelZoom(view);
            Focusable = true;
            ClipToBounds = true;
            Cursor = new Cursor(StandardCursorType.Arrow);
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += delegate { AdvanceWheelZoom(Timestamp); };
        }

        public void SetImage(Bitmap image, int width, int height, bool preserveView = false)
        {
            CancelWheelZoom();
            ReplaceBitmap(image);
            view.DeviceScale = DpiScale;
            view.SetImage(width, height, preserveView);
            Cursor = new Cursor(StandardCursorType.Hand);
            Changed();
        }

        public void ClearImage()
        {
            CancelWheelZoom();
            navigation.Reset();
            ReplaceBitmap(null);
            dragging = false;
            Cursor = new Cursor(StandardCursorType.Arrow);
            Changed();
        }

        public void Fit()
        {
            CancelWheelZoom();
            view.DeviceScale = DpiScale;
            view.Fit();
            Changed();
        }

        public void ActualSize()
        {
            CancelWheelZoom();
            view.DeviceScale = DpiScale;
            view.ActualSize();
            view.ZoomAt(1 / DpiScale, Bounds.Width / 2, Bounds.Height / 2);
            Changed();
        }

        public void Zoom(double factor)
        {
            CancelWheelZoom();
            if (bitmap == null) return;
            view.ZoomAt(factor, Bounds.Width / 2, Bounds.Height / 2);
            Changed();
        }

        public void Pan(double x, double y)
        {
            CancelWheelZoom();
            if (bitmap == null) return;
            view.Pan(x, y);
            Changed();
        }

        public void ToggleCheckerboard()
        {
            checkerboard = !checkerboard;
            InvalidateVisual();
        }

        public void CancelWheelZoom()
        {
            bool active = wheel.IsActive;
            wheel.Cancel();
            UpdateTimer();
            if (active) InvalidateVisual();
        }

        public void ProcessWheel(double delta, Point anchor, bool navigate)
        {
            if (bitmap == null || delta == 0) return;
            if (navigate)
            {
                CancelWheelZoom();
                int steps = navigation.Add((int)Math.Round(delta));
                for (int i = 0; i < Math.Abs(steps); i++)
                    if (Navigate != null) Navigate(Math.Sign(steps));
                return;
            }
            navigation.Reset();
            wheel.Process(delta, new ImagePoint(anchor.X, anchor.Y), Timestamp, true);
            UpdateTimer();
            Changed();
        }

        public bool HitTest(Point point)
        {
            return point.X >= 0 && point.Y >= 0 && point.X <= Bounds.Width && point.Y <= Bounds.Height;
        }

        public override void Render(DrawingContext context)
        {
            context.FillRectangle(canvasBrush, new Rect(Bounds.Size));
            if (bitmap == null) return;
            var rect = new Rect(view.OffsetX, view.OffsetY, view.ImageWidth * view.Scale, view.ImageHeight * view.Scale);
            if (checkerboard) DrawChecker(context, rect);
            var mode = pixelMode
                ? BitmapInterpolationMode.None
                : (wheel.IsActive ? BitmapInterpolationMode.LowQuality : BitmapInterpolationMode.HighQuality);
            // Setting the attached property during Render invalidates the visual and aborts the pass.
            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = mode }))
                context.DrawImage(bitmap, rect);
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateViewport(e.NewSize);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null) topLevel.ScalingChanged += OnScalingChanged;
            UpdateViewport(Bounds.Size);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (topLevel != null) topLevel.ScalingChanged -= OnScalingChanged;
            topLevel = null;
            CancelWheelZoom();
            base.OnDetachedFromVisualTree(e);
        }

        private void OnScalingChanged(object sender, EventArgs e)
        {
            UpdateViewport(Bounds.Size);
        }

        private void UpdateViewport(Size size)
        {
            CancelWheelZoom();
            double previousDpi = view.DeviceScale;
            view.DeviceScale = DpiScale;
            if (!view.IsFit && previousDpi != view.DeviceScale)
                view.ZoomAt(previousDpi / view.DeviceScale, view.ViewportWidth / 2, view.ViewportHeight / 2);
            view.Resize(size.Width, size.Height);
            Changed();
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            if (bitmap == null) return;
            double steps = e.Delta.Y;
            if (Math.Abs(steps) < 1e-9) steps = e.Delta.X;
            ProcessWheel(steps * 120.0, e.GetPosition(this), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var point = e.GetCurrentPoint(this);
            var properties = point.Properties;
            CancelWheelZoom();
            Focus();
            if (properties.IsXButton1Pressed || properties.IsXButton2Pressed)
            {
                if (Navigate != null) Navigate(properties.IsXButton1Pressed ? -1 : 1);
                e.Handled = true;
                return;
            }
            if (bitmap == null || !properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2)
            {
                dragging = false;
                e.Pointer.Capture(null);
                if (ToggleActualSize != null) ToggleActualSize();
            }
            else
            {
                lastPoint = point.Position;
                e.Pointer.Capture(this);
                dragging = true;
                Cursor = new Cursor(StandardCursorType.SizeAll);
            }
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (!dragging) return;
            var point = e.GetPosition(this);
            double dx = point.X - lastPoint.X;
            double dy = point.Y - lastPoint.Y;
            lastPoint = point;
            if (bitmap == null) return;
            view.Pan(dx, dy);
            Changed();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (e.InitialPressMouseButton == MouseButton.Left)
            {
                dragging = false;
                e.Pointer.Capture(null);
                Cursor = bitmap == null ? new Cursor(StandardCursorType.Arrow) : new Cursor(StandardCursorType.Hand);
            }
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            dragging = false;
            Cursor = bitmap == null ? new Cursor(StandardCursorType.Arrow) : new Cursor(StandardCursorType.Hand);
            base.OnPointerCaptureLost(e);
        }

        private void AdvanceWheelZoom(double timestamp)
        {
            bool changed = wheel.Tick(timestamp);
            UpdateTimer();
            if (changed) Changed();
        }

        private void UpdateTimer()
        {
            if (wheel.IsActive && !timer.IsEnabled) timer.Start();
            else if (!wheel.IsActive && timer.IsEnabled) timer.Stop();
        }

        private void Changed()
        {
            InvalidateVisual();
            if (ViewChanged != null) ViewChanged();
        }

        private void ReplaceBitmap(Bitmap image)
        {
            if (bitmap != null) bitmap.Dispose();
            bitmap = image;
        }

        private void DrawChecker(DrawingContext context, Rect rect)
        {
            Rect visible = rect.Intersect(new Rect(Bounds.Size));
            if (visible.Width <= 0 || visible.Height <= 0) return;
            using (context.PushClip(visible))
            {
                const double size = 8;
                int x0 = (int)Math.Floor(visible.X / size);
                int y0 = (int)Math.Floor(visible.Y / size);
                int x1 = (int)Math.Ceiling(visible.Right / size);
                int y1 = (int)Math.Ceiling(visible.Bottom / size);
                for (int y = y0; y < y1; y++)
                {
                    for (int x = x0; x < x1; x++)
                    {
                        var color = ((x + y) & 1) == 0 ? checkerDark : checkerLight;
                        context.FillRectangle(color, new Rect(x * size, y * size, size, size));
                    }
                }
            }
        }

        private static double Timestamp { get { return (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency; } }
    }
}
