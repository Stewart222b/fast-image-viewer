namespace FastImageViewer.Core
{
    public struct ImagePoint
    {
        public double X { get; private set; }
        public double Y { get; private set; }

        public ImagePoint(double x, double y) : this()
        {
            X = x;
            Y = y;
        }
    }
}
