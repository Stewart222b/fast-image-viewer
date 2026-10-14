using System;

namespace FastImageViewer.Core
{
    public static class DecodeBudget
    {
        public const long MaximumPixels = 32000000;

        public static bool NeedsPreview(int width, int height)
        {
            return (long)width * height > MaximumPixels;
        }

        // Matches the WPF DecodePixelWidth / DecodePixelHeight choice in ImageLoader.
        public static void SelectDecodePixels(int width, int height, out int decodeWidth, out int decodeHeight)
        {
            decodeWidth = 0;
            decodeHeight = 0;
            double ratio = Math.Sqrt((double)MaximumPixels / ((long)width * height));
            int targetWidth = Math.Max(1, (int)Math.Floor(width * ratio));
            while (targetWidth > 1 && (long)targetWidth * Math.Ceiling((double)height * targetWidth / width) > MaximumPixels)
                targetWidth--;
            if ((long)targetWidth * Math.Ceiling((double)height * targetWidth / width) > MaximumPixels)
                decodeHeight = (int)Math.Min(height, MaximumPixels);
            else decodeWidth = targetWidth;
        }
    }
}
