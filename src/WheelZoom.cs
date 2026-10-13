using System;
using System.Windows;

namespace FastImageViewer
{
    // Input deltas remain fractional; only their presentation is interpolated.
    public sealed class WheelZoom
    {
        private readonly Viewport view;
        private Point anchor;
        private double startLog;
        private double targetLog;
        private double started;
        private int direction;
        public const double Duration = 0.08;
        public bool IsActive { get; private set; }
        public double TargetScale { get { return IsActive ? Math.Exp(targetLog) : view.Scale; } }

        public WheelZoom(Viewport viewport) { view = viewport; }

        public void Process(int delta, Point point, double timestamp, bool animate)
        {
            if (delta == 0 || view.ImageWidth <= 0 || view.ImageHeight <= 0) return;
            Tick(timestamp);
            int nextDirection = Math.Sign(delta);
            // Reversal drops unfinished travel in the old direction.
            double baseline = IsActive && direction == nextDirection ? targetLog : Math.Log(view.Scale);
            double fit = 1 / (view.DeviceScale > 0 ? view.DeviceScale : 1);
            if (view.ViewportWidth > 0 && view.ViewportHeight > 0)
                fit = Math.Min(fit, Math.Min(view.ViewportWidth / view.ImageWidth, view.ViewportHeight / view.ImageHeight));
            targetLog = Math.Max(Math.Log(Math.Min(0.01, fit)), Math.Min(Math.Log(32), baseline + Math.Log(1.18) * delta / 120.0));
            startLog = Math.Log(view.Scale);
            started = timestamp;
            anchor = point;
            direction = nextDirection;
            IsActive = Math.Abs(targetLog - startLog) > 1e-14;
            if (!animate && IsActive)
            {
                view.ZoomAt(Math.Exp(targetLog) / view.Scale, anchor.X, anchor.Y);
                IsActive = false;
            }
        }

        public bool Tick(double timestamp)
        {
            if (!IsActive) return false;
            double progress = Math.Max(0, Math.Min(1, (timestamp - started) / Duration));
            if (progress <= 0) return false;
            double scale = Math.Exp(startLog + (targetLog - startLog) * progress);
            view.ZoomAt(scale / view.Scale, anchor.X, anchor.Y);
            if (progress >= 1) IsActive = false;
            return true;
        }

        public void Cancel() { IsActive = false; direction = 0; }
    }

    public sealed class WheelNavigation
    {
        private long remainder;
        public int Add(int delta)
        {
            remainder += delta;
            int steps = (int)(remainder / 120);
            remainder %= 120;
            return -steps;
        }
        public void Reset() { remainder = 0; }
    }
}
