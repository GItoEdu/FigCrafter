using System.ComponentModel;
using System.Runtime.CompilerServices;
using SkiaSharp;

namespace FigCrafterApp.Models
{
    public abstract class GraphicObject : INotifyPropertyChanged
    {
        private float _x;
        private float _y;
        private float _width;
        private float _height;
        private SKColor _fillColor = SKColors.Blue;
        private SKColor _strokeColor = SKColors.Black;
        private float _strokeWidth = 1;
        private bool _isSelected = false;

        public float X { get => _x; set => SetProperty(ref _x, value); }
        public float Y { get => _y; set => SetProperty(ref _y, value); }
        public float Width { get => _width; set => SetProperty(ref _width, value); }
        public float Height { get => _height; set => SetProperty(ref _height, value); }
        public SKColor FillColor { get => _fillColor; set => SetProperty(ref _fillColor, value); }
        public SKColor StrokeColor { get => _strokeColor; set => SetProperty(ref _strokeColor, value); }
        public float StrokeWidth { get => _strokeWidth; set => SetProperty(ref _strokeWidth, value); }
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public abstract void Draw(SKCanvas canvas);
        public abstract bool HitTest(SKPoint point);
        public abstract GraphicObject Clone();

        /// <summary>
        /// 共通プロパティを対象オブジェクトにコピーするヘルパー
        /// </summary>
        protected void CopyPropertiesTo(GraphicObject target)
        {
            target.X = X;
            target.Y = Y;
            target.Width = Width;
            target.Height = Height;
            target.FillColor = FillColor;
            target.StrokeColor = StrokeColor;
            target.StrokeWidth = StrokeWidth;
        }
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

        public override GraphicObject Clone()
        {
            var clone = new RectangleObject();
            CopyPropertiesTo(clone);
            return clone;
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

        public override GraphicObject Clone()
        {
            var clone = new EllipseObject();
            CopyPropertiesTo(clone);
            return clone;
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
        private float _endX;
        private float _endY;

        public float EndX { get => _endX; set => SetProperty(ref _endX, value); }
        public float EndY { get => _endY; set => SetProperty(ref _endY, value); }

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

        public override GraphicObject Clone()
        {
            var clone = new LineObject();
            CopyPropertiesTo(clone);
            clone.EndX = EndX;
            clone.EndY = EndY;
            return clone;
        }
    }

    public class TextObject : GraphicObject
    {
        private string _text = "Text";
        private string _fontFamily = "Arial";
        private float _fontSize = 24;

        public string Text { get => _text; set => SetProperty(ref _text, value); }
        public string FontFamily { get => _fontFamily; set => SetProperty(ref _fontFamily, value); }
        public float FontSize { get => _fontSize; set => SetProperty(ref _fontSize, value); }

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

        public override GraphicObject Clone()
        {
            var clone = new TextObject();
            CopyPropertiesTo(clone);
            clone.Text = Text;
            clone.FontFamily = FontFamily;
            clone.FontSize = FontSize;
            return clone;
        }
    }

    public class GroupObject : GraphicObject
    {
        private List<GraphicObject> _children = new();

        public List<GraphicObject> Children
        {
            get => _children;
            set => _children = value;
        }

        /// <summary>
        /// 子オブジェクトのバウンディングボックスからグループの座標・サイズを再計算
        /// </summary>
        public void RecalculateBounds()
        {
            if (_children.Count == 0) return;

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            foreach (var child in _children)
            {
                if (child is LineObject line)
                {
                    minX = Math.Min(minX, Math.Min(line.X, line.EndX));
                    minY = Math.Min(minY, Math.Min(line.Y, line.EndY));
                    maxX = Math.Max(maxX, Math.Max(line.X, line.EndX));
                    maxY = Math.Max(maxY, Math.Max(line.Y, line.EndY));
                }
                else
                {
                    minX = Math.Min(minX, child.X);
                    minY = Math.Min(minY, child.Y);
                    maxX = Math.Max(maxX, child.X + child.Width);
                    maxY = Math.Max(maxY, child.Y + child.Height);
                }
            }

            X = minX;
            Y = minY;
            Width = maxX - minX;
            Height = maxY - minY;
        }

        public override void Draw(SKCanvas canvas)
        {
            // 子オブジェクトを描画（絶対座標で保持）
            foreach (var child in _children)
            {
                child.Draw(canvas);
            }

            if (IsSelected)
            {
                RecalculateBounds();
                // グループのバウンディングボックスを描画
                using var paint = new SKPaint
                {
                    Color = SKColors.LimeGreen,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1,
                    PathEffect = SKPathEffect.CreateDash(new float[] { 6, 3 }, 0),
                    IsAntialias = true
                };
                var rect = new SKRect(X, Y, X + Width, Y + Height);
                canvas.DrawRect(rect, paint);

                // 四隅のハンドル
                using var handlePaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
                using var handleStrokePaint = new SKPaint { Color = SKColors.LimeGreen, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
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

        public override bool HitTest(SKPoint point)
        {
            // 子オブジェクトのいずれかにヒットすればグループにヒット
            foreach (var child in _children)
            {
                if (child.HitTest(point)) return true;
            }
            return false;
        }

        public override GraphicObject Clone()
        {
            var clone = new GroupObject();
            CopyPropertiesTo(clone);
            foreach (var child in _children)
            {
                clone._children.Add(child.Clone());
            }
            return clone;
        }
    }

    public class ImageObject : GraphicObject, IDisposable
    {
        private SKBitmap? _imageData;

        public SKBitmap? ImageData
        {
            get => _imageData;
            set
            {
                _imageData = value;
                if (_imageData != null)
                {
                    Width = _imageData.Width;
                    Height = _imageData.Height;
                }
            }
        }

        public override void Draw(SKCanvas canvas)
        {
            if (_imageData == null) return;

            var destRect = new SKRect(X, Y, X + Width, Y + Height);
            canvas.DrawBitmap(_imageData, destRect);

            if (IsSelected)
            {
                // 選択枠
                using var paint = new SKPaint
                {
                    Color = SKColors.DodgerBlue,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1,
                    PathEffect = SKPathEffect.CreateDash(new float[] { 4, 4 }, 0),
                    IsAntialias = true
                };
                canvas.DrawRect(destRect, paint);

                // ハンドル
                using var handlePaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
                using var handleStrokePaint = new SKPaint { Color = SKColors.DodgerBlue, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
                float hs = 6;
                var points = new[]
                {
                    new SKPoint(destRect.Left, destRect.Top),
                    new SKPoint(destRect.Right, destRect.Top),
                    new SKPoint(destRect.Right, destRect.Bottom),
                    new SKPoint(destRect.Left, destRect.Bottom)
                };
                foreach (var pt in points)
                {
                    var hr = new SKRect(pt.X - hs / 2, pt.Y - hs / 2, pt.X + hs / 2, pt.Y + hs / 2);
                    canvas.DrawRect(hr, handlePaint);
                    canvas.DrawRect(hr, handleStrokePaint);
                }
            }
        }

        public override bool HitTest(SKPoint point)
        {
            var rect = new SKRect(X, Y, X + Width, Y + Height);
            return rect.Contains(point.X, point.Y);
        }

        public override GraphicObject Clone()
        {
            var clone = new ImageObject();
            CopyPropertiesTo(clone);
            if (_imageData != null)
            {
                clone._imageData = _imageData.Copy();
            }
            return clone;
        }

        public void Dispose()
        {
            _imageData?.Dispose();
            _imageData = null;
        }
    }
}
