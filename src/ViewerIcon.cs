using System;
using System.Windows;
using System.Windows.Media;

namespace FastImageViewer
{
    // Small, consistent native vector icons; no image assets or icon-font dependency.
    internal sealed class ViewerIcon : FrameworkElement
    {
        private readonly string kind;
        private readonly Pen pen;

        public ViewerIcon(string kind)
        {
            this.kind = kind;
            Width = Height = 16;
            IsHitTestVisible = false;
            pen = new Pen(new SolidColorBrush(Color.FromRgb(65, 65, 65)), 1.25);
            pen.StartLineCap = pen.EndLineCap = PenLineCap.Round;
            pen.LineJoin = PenLineJoin.Round;
            pen.Freeze();
        }

        protected override void OnRender(DrawingContext dc)
        {
            switch (kind)
            {
                case "open":
                    Path(dc, "M1.5,12.5 L1.5,3.5 6,3.5 7.5,5 14.5,5 14.5,7 M1.5,12.5 L4,7 15,7 12.5,12.5 Z"); break;
                case "left": Path(dc, "M10,3 L5,8 10,13"); break;
                case "right": Path(dc, "M6,3 L11,8 6,13"); break;
                case "fit":
                    Path(dc, "M5,2 L2,2 2,5 M11,2 L14,2 14,5 M2,11 L2,14 5,14 M14,11 L14,14 11,14");
                    dc.DrawRectangle(null, pen, new Rect(5, 5, 6, 6)); break;
                case "minus": Path(dc, "M3,8 L13,8"); break;
                case "plus": Path(dc, "M3,8 L13,8 M8,3 L8,13"); break;
                case "rotate":
                    Path(dc, "M12.5,6 A5.3,5.3 0 1 0 13,10 M12.5,2.5 L12.5,6.5 8.5,6.5"); break;
                case "info":
                    dc.DrawEllipse(null, pen, new Point(8, 8), 6, 6);
                    Path(dc, "M8,7 L8,11 M7,11 L9,11");
                    dc.DrawEllipse(pen.Brush, null, new Point(8, 4.5), 0.8, 0.8); break;
                case "fullscreen": Path(dc, "M6,2 L2,2 2,6 M10,2 L14,2 14,6 M2,10 L2,14 6,14 M14,10 L14,14 10,14"); break;
                case "more":
                    for (int x = 3; x <= 13; x += 5) dc.DrawEllipse(pen.Brush, null, new Point(x, 8), 1, 1); break;
                case "close": Path(dc, "M4,4 L12,12 M12,4 L4,12"); break;
                case "down": Path(dc, "M4,6 L8,10 12,6"); break;
            }
        }

        private void Path(DrawingContext dc, string data) { dc.DrawGeometry(null, pen, Geometry.Parse(data)); }
    }
}
