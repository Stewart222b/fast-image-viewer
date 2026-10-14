namespace FastImageViewer
{
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
