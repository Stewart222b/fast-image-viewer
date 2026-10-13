using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FastImageViewer
{
    // The viewport works in original image pixels; the bitmap can be a bounded preview.
    public sealed class ImageSurface : FrameworkElement
    {
        public readonly Viewport View = new Viewport();
        public BitmapSource Bitmap { get; private set; }
        public event Action ViewChanged;
        public event Action<int> Navigate;
        public event Action ToggleActualSize;
        private bool pixelMode;
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
        public bool IsDragging { get; private set; }
        private Point lastPoint;
        private bool checkerboard;
        private readonly DrawingBrush checker;
        private readonly WheelZoom wheelZoom;
        private readonly WheelNavigation wheelNavigation = new WheelNavigation();
        private bool renderingSubscribed;
        public bool IsZoomAnimating { get { return wheelZoom.IsActive; } }
        private static double Timestamp { get { return (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency; } }

        public ImageSurface()
        {
            wheelZoom = new WheelZoom(View);
            Focusable = true;
            ClipToBounds = true;
            var drawing = new DrawingGroup();
            drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(56, 56, 56)), null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
            drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(68, 68, 68)), null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
            drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(68, 68, 68)), null, new RectangleGeometry(new Rect(8, 8, 8, 8))));
            checker = new DrawingBrush(drawing) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
            checker.Freeze();
            SizeChanged += delegate { CancelWheelZoom(); View.DeviceScale = DpiScale; View.Resize(ActualWidth, ActualHeight); Changed(); };
            Unloaded += delegate { CancelWheelZoom(); wheelNavigation.Reset(); };
            LostMouseCapture += delegate { IsDragging = false; Cursor = Bitmap == null ? Cursors.Arrow : Cursors.Hand; };
        }

        public double DpiScale
        {
            get
            {
                var source = PresentationSource.FromVisual(this);
                return source == null || source.CompositionTarget == null ? 1 : source.CompositionTarget.TransformToDevice.M11;
            }
        }

        public void SetImage(BitmapSource bitmap, int width, int height)
        {
            CancelWheelZoom();
            Bitmap = bitmap;
            View.DeviceScale = DpiScale;
            View.SetImage(width, height);
            Cursor = Cursors.Hand;
            Changed();
        }

        public void ClearImage()
        {
            CancelWheelZoom();
            wheelNavigation.Reset();
            Bitmap = null;
            if (IsMouseCaptured) ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
            Changed();
        }

        public void Fit() { CancelWheelZoom(); View.DeviceScale = DpiScale; View.Fit(); Changed(); }
        public void ActualSize()
        {
            CancelWheelZoom();
            View.DeviceScale = DpiScale;
            View.ActualSize();
            View.ZoomAt(1 / DpiScale, ActualWidth / 2, ActualHeight / 2);
            Changed();
        }
        public void Zoom(double factor) { CancelWheelZoom(); if (Bitmap == null) return; View.ZoomAt(factor, ActualWidth / 2, ActualHeight / 2); Changed(); }
        public void Pan(double x, double y) { CancelWheelZoom(); if (Bitmap == null) return; View.Pan(x, y); Changed(); }
        public void ToggleCheckerboard() { checkerboard = !checkerboard; InvalidateVisual(); }
        public void Changed() { InvalidateVisual(); if (ViewChanged != null) ViewChanged(); }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(32, 32, 32)), null, new Rect(RenderSize));
            if (Bitmap == null) return;
            var rect = new Rect(View.OffsetX, View.OffsetY, View.ImageWidth * View.Scale, View.ImageHeight * View.Scale);
            if (checkerboard) dc.DrawRectangle(checker, null, rect);
            RenderOptions.SetBitmapScalingMode(this, PixelMode ? BitmapScalingMode.NearestNeighbor : (IsZoomAnimating ? BitmapScalingMode.LowQuality : BitmapScalingMode.HighQuality));
            dc.DrawImage(Bitmap, rect);
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (Bitmap == null) return;
            ProcessWheel(e.Delta, e.GetPosition(this), (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            e.Handled = true;
        }

        public void ProcessWheel(int delta, Point anchor, bool navigate)
        {
            if (Bitmap == null || delta == 0) return;
            if (navigate)
            {
                CancelWheelZoom();
                int steps = wheelNavigation.Add(delta);
                for (int i = 0; i < Math.Abs(steps); i++)
                    if (Navigate != null) Navigate(Math.Sign(steps));
                return;
            }
            wheelNavigation.Reset();
            wheelZoom.Process(delta, anchor, Timestamp, SystemParameters.ClientAreaAnimation);
            UpdateRenderingSubscription();
            Changed();
        }

        public void AdvanceWheelZoom(double timestamp)
        {
            bool changed = wheelZoom.Tick(timestamp);
            UpdateRenderingSubscription();
            if (changed) Changed();
        }

        public void CancelWheelZoom()
        {
            bool active = wheelZoom.IsActive;
            wheelZoom.Cancel();
            UpdateRenderingSubscription();
            if (active) InvalidateVisual();
        }

        private void UpdateRenderingSubscription()
        {
            if (wheelZoom.IsActive == renderingSubscribed) return;
            if (wheelZoom.IsActive) CompositionTarget.Rendering += RenderWheelZoom;
            else CompositionTarget.Rendering -= RenderWheelZoom;
            renderingSubscribed = wheelZoom.IsActive;
        }

        private void RenderWheelZoom(object sender, EventArgs e) { AdvanceWheelZoom(Timestamp); }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            CancelWheelZoom();
            Focus();
            if (e.ChangedButton == MouseButton.XButton1 || e.ChangedButton == MouseButton.XButton2)
            {
                if (Navigate != null) Navigate(e.ChangedButton == MouseButton.XButton1 ? -1 : 1);
                e.Handled = true;
            }
            if (Bitmap == null || e.ChangedButton != MouseButton.Left) return;
            if (e.ClickCount == 2)
            {
                if (ToggleActualSize != null) ToggleActualSize();
            }
            else
            {
                lastPoint = e.GetPosition(this);
                IsDragging = CaptureMouse();
                Cursor = Cursors.ScrollAll;
            }
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!IsDragging) return;
            Point point = e.GetPosition(this);
            Pan(point.X - lastPoint.X, point.Y - lastPoint.Y);
            lastPoint = point;
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.ChangedButton == MouseButton.Left && IsMouseCaptured) ReleaseMouseCapture();
        }
    }
}
