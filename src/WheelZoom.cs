using System.Windows;
using CoreWheelZoom = FastImageViewer.Core.WheelZoom;

namespace FastImageViewer
{
    // WPF surface. Wheel math lives in FastImageViewer.Core.WheelZoom.
    public sealed class WheelZoom
    {
        private readonly CoreWheelZoom inner;
        public const double Duration = FastImageViewer.Core.WheelZoom.Duration;
        public bool IsActive { get { return inner.IsActive; } }
        public double TargetScale { get { return inner.TargetScale; } }

        public WheelZoom(Viewport viewport) { inner = new CoreWheelZoom(viewport.Inner); }

        public void Process(int delta, Point point, double timestamp, bool animate)
        {
            inner.Process(delta, new FastImageViewer.Core.ImagePoint(point.X, point.Y), timestamp, animate);
        }

        public bool Tick(double timestamp) { return inner.Tick(timestamp); }

        public void Cancel() { inner.Cancel(); }
    }
}
