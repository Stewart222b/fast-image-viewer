namespace FastImageViewer.Core
{
    public static class ExifOrientation
    {
        public static void Map(int orientation, out int quarterTurns, out bool flipHorizontal)
        {
            quarterTurns = 0;
            flipHorizontal = false;
            switch (orientation)
            {
                case 2: flipHorizontal = true; break;
                case 3: quarterTurns = 2; break;
                case 4: quarterTurns = 2; flipHorizontal = true; break;
                case 5: quarterTurns = 1; flipHorizontal = true; break;
                case 6: quarterTurns = 1; break;
                case 7: quarterTurns = 3; flipHorizontal = true; break;
                case 8: quarterTurns = 3; break;
            }
        }
    }
}
