using System;

namespace FastImageViewer.Core
{
    public sealed class Viewport
    {
        public double ImageWidth { get; private set; }
        public double ImageHeight { get; private set; }
        public double ViewportWidth { get; private set; }
        public double ViewportHeight { get; private set; }
        public double Scale { get; private set; }
        public double OffsetX { get; private set; }
        public double OffsetY { get; private set; }
        public bool IsFit { get; private set; }
        public double DeviceScale { get; set; }

        public Viewport() { Scale = 1; IsFit = true; DeviceScale = 1; }

        public void SetImage(double width, double height)
        {
            SetImage(width, height, false);
        }

        public void SetImage(double width, double height, bool preserveView)
        {
            bool keep = preserveView && !IsFit && ImageWidth > 0 && ImageHeight > 0;
            ImagePoint center = ImagePointAt(ViewportWidth / 2, ViewportHeight / 2);
            double relativeX = ImageWidth > 0 ? center.X / ImageWidth : .5;
            double relativeY = ImageHeight > 0 ? center.Y / ImageHeight : .5;
            ImageWidth = ValidSize(width);
            ImageHeight = ValidSize(height);
            if (!keep || ImageWidth <= 0 || ImageHeight <= 0) { Fit(); return; }
            Scale = Math.Max(Math.Min(0.01, FitScale()), Math.Min(32, Scale));
            OffsetX = ViewportWidth / 2 - relativeX * ImageWidth * Scale;
            OffsetY = ViewportHeight / 2 - relativeY * ImageHeight * Scale;
            IsFit = false;
        }

        public void Resize(double width, double height)
        {
            ImagePoint center = ImagePointAt(ViewportWidth / 2, ViewportHeight / 2);
            ViewportWidth = ValidSize(width);
            ViewportHeight = ValidSize(height);
            if (IsFit) Fit();
            else
            {
                OffsetX = ViewportWidth / 2 - center.X * Scale;
                OffsetY = ViewportHeight / 2 - center.Y * Scale;
            }
        }

        public void Fit()
        {
            IsFit = true;
            Scale = FitScale();
            Center();
        }

        public void ActualSize()
        {
            IsFit = false;
            Scale = 1;
            Center();
        }

        public void ZoomAt(double factor, double x, double y)
        {
            if (ImageWidth <= 0 || ImageHeight <= 0 || !IsFinite(factor) || factor <= 0 || !IsFinite(x) || !IsFinite(y)) return;
            ImagePoint anchor = ImagePointAt(x, y);
            Scale = Math.Max(Math.Min(0.01, FitScale()), Math.Min(32, Scale * factor));
            OffsetX = x - anchor.X * Scale;
            OffsetY = y - anchor.Y * Scale;
            IsFit = false;
        }

        public void Pan(double dx, double dy)
        {
            if (!IsFinite(dx) || !IsFinite(dy)) return;
            OffsetX += dx;
            OffsetY += dy;
            IsFit = false;
        }

        public ImagePoint ImagePointAt(double x, double y)
        {
            return new ImagePoint((x - OffsetX) / Scale, (y - OffsetY) / Scale);
        }

        private void Center()
        {
            OffsetX = (ViewportWidth - ImageWidth * Scale) / 2;
            OffsetY = (ViewportHeight - ImageHeight * Scale) / 2;
        }

        private double FitScale()
        {
            double actualSize = 1 / (IsFinite(DeviceScale) && DeviceScale > 0 ? DeviceScale : 1);
            if (ImageWidth > 0 && ImageHeight > 0 && ViewportWidth > 0 && ViewportHeight > 0)
                return Math.Min(actualSize, Math.Min(ViewportWidth / ImageWidth, ViewportHeight / ImageHeight));
            return actualSize;
        }

        private static double ValidSize(double value) { return IsFinite(value) && value > 0 ? value : 0; }
        private static bool IsFinite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
    }
}
