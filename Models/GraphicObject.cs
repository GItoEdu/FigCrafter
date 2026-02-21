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
        public bool IsSelected { get; set; } = false;

        public abstract void Draw(SKCanvas canvas);
        public abstract bool HitTest(SKPoint point);
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
            
            if (IsSelected)
            {
                DrawSelectionBox(canvas, new SKRect(X, Y, X + Width, Y + Height));
            }
        }

        public override bool HitTest(SKPoint point)
        {
            var rect = new SKRect(X, Y, X + Width, Y + Height);
            return rect.Contains(point.X, point.Y);
        }

        protected void DrawSelectionBox(SKCanvas canvas, SKRect rect)
        {
            using var paint = new SKPaint
            {
                Color = SKColors.DeepSkyBlue,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1,
                PathEffect = SKPathEffect.CreateDash(new float[] { 5, 5 }, 0),
                IsAntialias = true
            };
            canvas.DrawRect(rect, paint);
            
            // 四隅のハンドルを描画
            using var handlePaint = new SKPaint
            {
                Color = SKColors.White,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            using var handleStrokePaint = new SKPaint
            {
                Color = SKColors.DeepSkyBlue,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1,
                IsAntialias = true
            };
            
            float handleSize = 6;
            var points = new[]
            {
                new SKPoint(rect.Left, rect.Top),
                new SKPoint(rect.Right, rect.Top),
                new SKPoint(rect.Right, rect.Bottom),
                new SKPoint(rect.Left, rect.Bottom)
            };
            
            foreach (var pt in points)
            {
                var handleRect = new SKRect(pt.X - handleSize / 2, pt.Y - handleSize / 2, pt.X + handleSize / 2, pt.Y + handleSize / 2);
                canvas.DrawRect(handleRect, handlePaint);
                canvas.DrawRect(handleRect, handleStrokePaint);
            }
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

            if (IsSelected)
            {
                DrawSelectionBox(canvas, new SKRect(X, Y, X + Width, Y + Height));
            }
        }

        public override bool HitTest(SKPoint point)
        {
            float cx = X + Width / 2;
            float cy = Y + Height / 2;
            float rx = Width / 2;
            float ry = Height / 2;

            if (rx <= 0 || ry <= 0) return false;

            // 楕円方程式: (x - cx)^2 / rx^2 + (y - cy)^2 / ry^2 <= 1
            float dx = point.X - cx;
            float dy = point.Y - cy;
            return (dx * dx) / (rx * rx) + (dy * dy) / (ry * ry) <= 1.0f;
        }

        protected void DrawSelectionBox(SKCanvas canvas, SKRect rect)
        {
            using var paint = new SKPaint
            {
                Color = SKColors.DeepSkyBlue,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1,
                PathEffect = SKPathEffect.CreateDash(new float[] { 5, 5 }, 0),
                IsAntialias = true
            };
            canvas.DrawRect(rect, paint);
            
            // 四隅のハンドル
            using var handlePaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
            using var handleStrokePaint = new SKPaint { Color = SKColors.DeepSkyBlue, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
            float handleSize = 6;
            var points = new[] { new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Right, rect.Top), new SKPoint(rect.Right, rect.Bottom), new SKPoint(rect.Left, rect.Bottom) };
            foreach (var pt in points)
            {
                var handleRect = new SKRect(pt.X - handleSize / 2, pt.Y - handleSize / 2, pt.X + handleSize / 2, pt.Y + handleSize / 2);
                canvas.DrawRect(handleRect, handlePaint);
                canvas.DrawRect(handleRect, handleStrokePaint);
            }
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

            if (IsSelected)
            {
                using var highlightPaint = new SKPaint
                {
                    Color = SKColors.DeepSkyBlue,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1,
                    PathEffect = SKPathEffect.CreateDash(new float[] { 5, 5 }, 0),
                    IsAntialias = true
                };
                // 直線全体を含むバウンディングボックス
                var minX = Math.Min(X, EndX);
                var minY = Math.Min(Y, EndY);
                var maxX = Math.Max(X, EndX);
                var maxY = Math.Max(Y, EndY);
                canvas.DrawRect(new SKRect(minX, minY, maxX, maxY), highlightPaint);

                // 両端のハンドル
                using var handlePaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
                using var handleStrokePaint = new SKPaint { Color = SKColors.DeepSkyBlue, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
                float handleSize = 6;
                var points = new[] { new SKPoint(X, Y), new SKPoint(EndX, EndY) };
                foreach (var pt in points)
                {
                    var handleRect = new SKRect(pt.X - handleSize / 2, pt.Y - handleSize / 2, pt.X + handleSize / 2, pt.Y + handleSize / 2);
                    canvas.DrawRect(handleRect, handlePaint);
                    canvas.DrawRect(handleRect, handleStrokePaint);
                }
            }
        }

        public override bool HitTest(SKPoint point)
        {
            // 点と線分の距離
            float lenSq = (EndX - X) * (EndX - X) + (EndY - Y) * (EndY - Y);
            if (lenSq == 0) return Math.Abs(point.X - X) < 5 && Math.Abs(point.Y - Y) < 5;

            float t = Math.Max(0, Math.Min(1, ((point.X - X) * (EndX - X) + (point.Y - Y) * (EndY - Y)) / lenSq));
            float projX = X + t * (EndX - X);
            float projY = Y + t * (EndY - Y);

            float distSq = (point.X - projX) * (point.X - projX) + (point.Y - projY) * (point.Y - projY);
            // 許容幅 5px
            float threshold = Math.Max(5.0f, StrokeWidth / 2 + 2);
            return distSq <= threshold * threshold;
        }
    }

    public class TextObject : GraphicObject
    {
        public string Text { get; set; } = "Text";
        public string FontFamily { get; set; } = "Arial";
        public float FontSize { get; set; } = 24;

        public override void Draw(SKCanvas canvas)
        {
            using var paint = new SKPaint
            {
                Color = FillColor,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName(FontFamily),
                TextSize = FontSize
            };

            // テキストのバウンディングボックス計算 (描画用)
            var bounds = new SKRect();
            paint.MeasureText(Text, ref bounds);

            // X, Y を左上の基準として描画
            canvas.DrawText(Text, X, Y - bounds.Top, paint);

            if (IsSelected)
            {
                using var highlightPaint = new SKPaint
                {
                    Color = SKColors.DeepSkyBlue,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1,
                    PathEffect = SKPathEffect.CreateDash(new float[] { 5, 5 }, 0),
                    IsAntialias = true
                };
                
                var rect = new SKRect(X, Y, X + bounds.Width, Y + bounds.Height);
                canvas.DrawRect(rect, highlightPaint);
            }
        }

        public override bool HitTest(SKPoint point)
        {
            using var paint = new SKPaint
            {
                Typeface = SKTypeface.FromFamilyName(FontFamily),
                TextSize = FontSize
            };
            var bounds = new SKRect();
            paint.MeasureText(Text, ref bounds);

            var rect = new SKRect(X, Y, X + bounds.Width, Y + bounds.Height);
            return rect.Contains(point.X, point.Y);
        }
    }
}
