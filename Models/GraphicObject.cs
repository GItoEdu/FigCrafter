using SkiaSharp;

namespace FigCrafterApp.Models
{
    public abstract class GraphicObject
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public SKColor FillColor { get; set; } = SKColors.Blue;
        public SKColor StrokeColor { get; set; } = SKColors.Black;
        public float StrokeWidth { get; set; } = 1;

        public abstract void Draw(SKCanvas canvas);
    }

    public class RectangleObject : GraphicObject
    {
        public override void Draw(SKCanvas canvas)
        {
            using var paint = new SKPaint
            {
                Color = FillColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawRect(X, Y, Width, Height, paint);

            paint.Color = StrokeColor;
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = StrokeWidth;
            canvas.DrawRect(X, Y, Width, Height, paint);
        }
    }

    public class EllipseObject : GraphicObject
    {
        public override void Draw(SKCanvas canvas)
        {
            using var paint = new SKPaint
            {
                Color = FillColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawOval(new SKRect(X, Y, X + Width, Y + Height), paint);

            paint.Color = StrokeColor;
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = StrokeWidth;
            canvas.DrawOval(new SKRect(X, Y, X + Width, Y + Height), paint);
        }
    }

    public class LineObject : GraphicObject
    {
        public float EndX { get; set; }
        public float EndY { get; set; }

        public override void Draw(SKCanvas canvas)
        {
            using var paint = new SKPaint
            {
                Color = StrokeColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = StrokeWidth,
                IsAntialias = true
            };
            canvas.DrawLine(X, Y, EndX, EndY, paint);
        }
    }
}
