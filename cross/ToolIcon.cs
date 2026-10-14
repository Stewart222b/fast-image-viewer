using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace FastImageViewer.Desktop
{
    public sealed class ToolIcon : Control
    {
        private readonly string kind;
        private readonly Pen pen;

        public ToolIcon(string kind)
        {
            this.kind = kind;
            Width = 16;
            Height = 16;
            IsHitTestVisible = false;
            pen = new Pen(new SolidColorBrush(Color.FromRgb(65, 65, 65)), 1.25, null, PenLineCap.Round, PenLineJoin.Round);
        }

        public override void Render(DrawingContext context)
        {
            switch (kind)
            {
                case "open":
                    Draw(context, "M1.5,12.5 L1.5,3.5 6,3.5 7.5,5 14.5,5 14.5,7 M1.5,12.5 L4,7 15,7 12.5,12.5 Z");
                    break;
                case "left": Draw(context, "M10,3 L5,8 10,13"); break;
                case "right": Draw(context, "M6,3 L11,8 6,13"); break;
                case "fit":
                    Draw(context, "M5,2 L2,2 2,5 M11,2 L14,2 14,5 M2,11 L2,14 5,14 M14,11 L14,14 11,14");
                    context.DrawRectangle(null, pen, new Rect(5, 5, 6, 6));
                    break;
                case "minus": Draw(context, "M3,8 L13,8"); break;
                case "plus": Draw(context, "M3,8 L13,8 M8,3 L8,13"); break;
                case "rotate":
                    Draw(context, "M12.5,6 A5.3,5.3 0 1 0 13,10 M12.5,2.5 L12.5,6.5 8.5,6.5");
                    break;
                case "info":
                    context.DrawEllipse(null, pen, new Point(8, 8), 6, 6);
                    Draw(context, "M8,7 L8,11 M7,11 L9,11");
                    context.DrawEllipse(pen.Brush, null, new Point(8, 4.5), 0.8, 0.8);
                    break;
                case "fullscreen": Draw(context, "M6,2 L2,2 2,6 M10,2 L14,2 14,6 M2,10 L2,14 6,14 M14,10 L14,14 10,14"); break;
                case "more":
                    for (int x = 3; x <= 13; x += 5) context.DrawEllipse(pen.Brush, null, new Point(x, 8), 1, 1);
                    break;
                case "close": Draw(context, "M4,4 L12,12 M12,4 L4,12"); break;
                case "down": Draw(context, "M4,6 L8,10 12,6"); break;
            }
        }

        private void Draw(DrawingContext context, string data)
        {
            context.DrawGeometry(null, pen, Geometry.Parse(data));
        }
    }
}
