using System;
using System.IO;
using BitMiracle.LibTiff.Classic;
using FastImageViewer.Core;
using SkiaSharp;

namespace FastImageViewer.Imaging
{
    static class TiffDecoder
    {
        // A full RGBA raster is 4 bytes per pixel. Above this, refuse instead of allocating
        // a multi-gigabyte buffer. Skia builds used here do not decode TIFF.
        const long MaximumRasterPixels = 160000000L;

        static TiffDecoder()
        {
            Tiff.SetErrorHandler(new QuietHandler());
        }

        public static SKBitmap Read(string path, out int width, out int height, out int orientation)
        {
            using (Tiff tiff = Tiff.Open(path, "r"))
            {
                if (tiff == null) throw new InvalidDataException("无法解码该图片。");
                FieldValue[] widthField = tiff.GetField(TiffTag.IMAGEWIDTH);
                FieldValue[] heightField = tiff.GetField(TiffTag.IMAGELENGTH);
                if (widthField == null || heightField == null) throw new InvalidDataException("图片尺寸无效。");
                width = widthField[0].ToInt();
                height = heightField[0].ToInt();
                if (width <= 0 || height <= 0) throw new InvalidDataException("图片尺寸无效。");
                long pixels = (long)width * height;
                if (pixels > MaximumRasterPixels || pixels > int.MaxValue) throw new InvalidDataException("图片太大，无法解码。");

                orientation = 1;
                FieldValue[] orientationField = tiff.GetField(TiffTag.ORIENTATION);
                if (orientationField != null) orientation = orientationField[0].ToInt();
                if (orientation < 1 || orientation > 8) orientation = 1;
                // Read stored pixels. The viewer applies tag 274 later, same as the Windows EXIF path.
                tiff.SetField(TiffTag.ORIENTATION, Orientation.TOPLEFT);

                string error;
                if (!tiff.RGBAImageOK(out error)) throw new InvalidDataException("无法解码该图片。");
                int[] raster = new int[width * height];
                if (!tiff.ReadRGBAImageOriented(width, height, raster, Orientation.TOPLEFT))
                    throw new InvalidDataException("无法解码该图片。");

                SKBitmap bitmap = CopyRaster(raster, width, height);
                if (!DecodeBudget.NeedsPreview(width, height)) return bitmap;
                int targetWidth;
                int targetHeight;
                PictureLoader.SelectTarget(width, height, out targetWidth, out targetHeight);
                return PictureLoader.Downscale(bitmap, targetWidth, targetHeight);
            }
        }

        static SKBitmap CopyRaster(int[] raster, int width, int height)
        {
            var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
            unsafe
            {
                byte* dest = (byte*)bitmap.GetPixels();
                int stride = bitmap.RowBytes;
                for (int y = 0; y < height; y++)
                {
                    byte* row = dest + y * stride;
                    int source = y * width;
                    for (int x = 0; x < width; x++)
                    {
                        int pixel = raster[source + x];
                        byte* sample = row + x * 4;
                        sample[0] = (byte)Tiff.GetB(pixel);
                        sample[1] = (byte)Tiff.GetG(pixel);
                        sample[2] = (byte)Tiff.GetR(pixel);
                        sample[3] = (byte)Tiff.GetA(pixel);
                    }
                }
            }
            return bitmap;
        }

        sealed class QuietHandler : TiffErrorHandler
        {
            public override void ErrorHandler(Tiff tif, string method, string format, params object[] args) { }
            public override void ErrorHandlerExt(Tiff tif, object clientData, string method, string format, params object[] args) { }
            public override void WarningHandler(Tiff tif, string method, string format, params object[] args) { }
            public override void WarningHandlerExt(Tiff tif, object clientData, string method, string format, params object[] args) { }
        }
    }
}
