using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace FigCrafterApp.Models
{
    [JsonDerivedType(typeof(RectangleObject), typeDiscriminator: "Rectangle")]
    [JsonDerivedType(typeof(EllipseObject), typeDiscriminator: "Ellipse")]
    [JsonDerivedType(typeof(LineObject), typeDiscriminator: "Line")]
    [JsonDerivedType(typeof(TextObject), typeDiscriminator: "Text")]
    [JsonDerivedType(typeof(PathObject), typeDiscriminator: "Path")]
    [JsonDerivedType(typeof(GroupObject), typeDiscriminator: "Group")]
    [JsonDerivedType(typeof(ImageObject), typeDiscriminator: "Image")]
    public abstract class GraphicObject : INotifyPropertyChanged, INotifyPropertyChanging
    {
        private float _x;
        private float _y;
        private float _width;
        private float _height;
        private float _rotation; // 回転角 (度)
        private SKColor _fillColor = SKColors.Blue;
        private SKColor _strokeColor = SKColors.Black;
        private float _strokeWidth = 0.5f;
        private float _opacity = 1.0f; // 1.0 = 不透明, 0.0 = 透明
        private bool _isSelected = false;

        public float X { get => _x; set => SetProperty(ref _x, value); }
        public float Y { get => _y; set => SetProperty(ref _y, value); }
        public float Width { get => _width; set => SetProperty(ref _width, value); }
        public float Height { get => _height; set => SetProperty(ref _height, value); }
        public float Rotation { get => _rotation; set => SetProperty(ref _rotation, value); }
        public SKColor FillColor { get => _fillColor; set => SetProperty(ref _fillColor, value); }
        public SKColor StrokeColor { get => _strokeColor; set => SetProperty(ref _strokeColor, value); }
        public float StrokeWidth { get => _strokeWidth; set => SetProperty(ref _strokeWidth, value); }
        public float Opacity { get => _opacity; set => SetProperty(ref _opacity, value); }
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
        
        private float _currentZoomLevel = 1.0f;

        // 描画時のスケール補正用（CanvasView.xaml.csから描画直前に渡される）
        [JsonIgnore]
        public virtual float CurrentZoomLevel
        {
            get => _currentZoomLevel;
            set => _currentZoomLevel = value;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public event PropertyChangingEventHandler? PropertyChanging;

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            OnPropertyChanging(propertyName);
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected virtual void OnPropertyChanging([CallerMemberName] string? propertyName = null)
        {
            PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName));
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
            target.Rotation = Rotation;
            target.FillColor = FillColor;
            target.StrokeColor = StrokeColor;
            target.StrokeWidth = StrokeWidth;
            target.Opacity = Opacity;
        }

        /// <summary>
        /// オブジェクトの中心を原点としてキャンバスに回転を適用する
        /// 呼び出し元で必ず canvas.Save() / canvas.Restore() を行うこと
        /// </summary>
        public void TransformCanvas(SKCanvas canvas)
        {
            if (Rotation != 0)
            {
                float cx = X + Width / 2;
                float cy = Y + Height / 2;
                canvas.Translate(cx, cy);
                canvas.RotateDegrees(Rotation);
                canvas.Translate(-cx, -cy);
            }
        }

        /// <summary>
        /// スクリーン座標系の点をオブジェクトのローカル（回転前）座標系に逆変換する
        /// HitTest 等で使用する
        /// </summary>
        public SKPoint UntransformPoint(SKPoint point)
        {
            if (Rotation == 0) return point;

            float cx = X + Width / 2;
            float cy = Y + Height / 2;

            // 中心を原点に移動
            float dx = point.X - cx;
            float dy = point.Y - cy;

            // 逆回転
            float rad = -Rotation * (float)Math.PI / 180.0f;
            float cos = (float)Math.Cos(rad);
            float sin = (float)Math.Sin(rad);

            float nx = dx * cos - dy * sin;
            float ny = dx * sin + dy * cos;

            // 元の位置に戻す
            return new SKPoint(nx + cx, ny + cy);
        }

        /// <summary>
        /// オブジェクトのローカル座標系の点をスクリーン座標（回転後）に変換する
        /// </summary>
        public SKPoint TransformPoint(SKPoint point)
        {
            if (Rotation == 0) return point;

            float cx = X + Width / 2;
            float cy = Y + Height / 2;

            float dx = point.X - cx;
            float dy = point.Y - cy;

            float rad = Rotation * (float)Math.PI / 180.0f;
            float cos = (float)Math.Cos(rad);
            float sin = (float)Math.Sin(rad);

            float nx = dx * cos - dy * sin;
            float ny = dx * sin + dy * cos;

            return new SKPoint(nx + cx, ny + cy);
        }

        /// <summary>
        /// 回転を適用した後のオブジェクトの4つの角座標を返す
        /// </summary>
        public virtual SKPoint[] GetTransformedCorners()
        {
            var corners = new[]
            {
                new SKPoint(X, Y),
                new SKPoint(X + Width, Y),
                new SKPoint(X + Width, Y + Height),
                new SKPoint(X, Y + Height)
            };

            if (Rotation == 0) return corners;

            for (int i = 0; i < corners.Length; i++)
            {
                corners[i] = TransformPoint(corners[i]);
            }
            return corners;
        }

        protected void DrawSelectionBox(SKCanvas canvas, SKRect rect)
        {
            using var paint = new SKPaint
            {
                Color = SKColors.DeepSkyBlue,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 0.5f / CurrentZoomLevel, // ズーム補正
                PathEffect = SKPathEffect.CreateDash(new float[] { 3 / CurrentZoomLevel, 3 / CurrentZoomLevel }, 0), // 破線も補正
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
                StrokeWidth = 0.5f / CurrentZoomLevel, // ズーム補正
                IsAntialias = true
            };
            
            float handleSize = 6 / CurrentZoomLevel; // ズーム補正
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

            // 回転ハンドルを描画 (上部中央から上に伸ばした位置)
            float rotationHandleOffset = 20 / CurrentZoomLevel;
            float midX = (rect.Left + rect.Right) / 2;
            var rotationHandlePos = new SKPoint(midX, rect.Top - rotationHandleOffset);

            // 繋ぐ線
            canvas.DrawLine(midX, rect.Top, midX, rotationHandlePos.Y, handleStrokePaint);

            // ハンドル本体
            var rotateRect = new SKRect(rotationHandlePos.X - handleSize / 2, rotationHandlePos.Y - handleSize / 2, rotationHandlePos.X + handleSize / 2, rotationHandlePos.Y + handleSize / 2);
            canvas.DrawRect(rotateRect, handlePaint);
            canvas.DrawRect(rotateRect, handleStrokePaint);
        }
    }

    public class RectangleObject : GraphicObject
    {
        public override void Draw(SKCanvas canvas)
        {
            canvas.Save();
            TransformCanvas(canvas);

            var fillWithOpacity = FillColor.WithAlpha((byte)(FillColor.Alpha * Opacity));
            var strokeWithOpacity = StrokeColor.WithAlpha((byte)(StrokeColor.Alpha * Opacity));

            using var paint = new SKPaint
            {
                Color = fillWithOpacity,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawRect(X, Y, Width, Height, paint);

            paint.Color = strokeWithOpacity;
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = StrokeWidth; // ズーム補正を削除（物理サイズ維持）
            canvas.DrawRect(X, Y, Width, Height, paint);
            
            if (IsSelected)
            {
                DrawSelectionBox(canvas, new SKRect(X, Y, X + Width, Y + Height));
            }

            canvas.Restore();
        }

        public override bool HitTest(SKPoint point)
        {
            var p = UntransformPoint(point);
            var rect = new SKRect(X, Y, X + Width, Y + Height);
            return rect.Contains(p.X, p.Y);
        }

        public override GraphicObject Clone()
        {
            var clone = new RectangleObject();
            CopyPropertiesTo(clone);
            return clone;
        }
    }

    public class EllipseObject : GraphicObject
    {
        public override void Draw(SKCanvas canvas)
        {
            canvas.Save();
            TransformCanvas(canvas);

            var fillWithOpacity = FillColor.WithAlpha((byte)(FillColor.Alpha * Opacity));
            var strokeWithOpacity = StrokeColor.WithAlpha((byte)(StrokeColor.Alpha * Opacity));

            using var paint = new SKPaint
            {
                Color = fillWithOpacity,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawOval(new SKRect(X, Y, X + Width, Y + Height), paint);

            paint.Color = strokeWithOpacity;
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = StrokeWidth; // ズーム補正を削除
            canvas.DrawOval(new SKRect(X, Y, X + Width, Y + Height), paint);

            if (IsSelected)
            {
                DrawSelectionBox(canvas, new SKRect(X, Y, X + Width, Y + Height));
            }

            canvas.Restore();
        }

        public override bool HitTest(SKPoint point)
        {
            var p = UntransformPoint(point);
            float cx = X + Width / 2;
            float cy = Y + Height / 2;
            float rx = Width / 2;
            float ry = Height / 2;

            if (rx <= 0 || ry <= 0) return false;

            // 楕円方程式: (x - cx)^2 / rx^2 + (y - cy)^2 / ry^2 <= 1
            float dx = p.X - cx;
            float dy = p.Y - cy;
            return (dx * dx) / (rx * rx) + (dy * dy) / (ry * ry) <= 1.0f;
        }

        public override GraphicObject Clone()
        {
            var clone = new EllipseObject();
            CopyPropertiesTo(clone);
            return clone;
        }
    }

    public class LineObject : GraphicObject
    {
        private float _endX;
        private float _endY;
        private bool _hasArrowStart;
        private bool _hasArrowEnd;

        public float EndX { get => _endX; set => SetProperty(ref _endX, value); }
        public float EndY { get => _endY; set => SetProperty(ref _endY, value); }
        public bool HasArrowStart { get => _hasArrowStart; set => SetProperty(ref _hasArrowStart, value); }
        public bool HasArrowEnd { get => _hasArrowEnd; set => SetProperty(ref _hasArrowEnd, value); }

        public override void Draw(SKCanvas canvas)
        {
            canvas.Save();
            TransformCanvas(canvas);

            var strokeWithOpacity = StrokeColor.WithAlpha((byte)(StrokeColor.Alpha * Opacity));

            using var paint = new SKPaint
            {
                Color = strokeWithOpacity,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = StrokeWidth, // ズーム補正を削除
                IsAntialias = true
            };
            canvas.DrawLine(X, Y, EndX, EndY, paint);

            // 矢印の描画
            if (HasArrowStart || HasArrowEnd)
            {
                float dx = EndX - X;
                float dy = EndY - Y;
                float angle = (float)Math.Atan2(dy, dx);
                float arrowLength = 15f + StrokeWidth; // ズーム補正を削除
                float arrowAngle = (float)(Math.PI / 6); // 30度

                using var arrowPaint = new SKPaint
                {
                    Color = strokeWithOpacity,
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                };

                if (HasArrowEnd)
                {
                    DrawArrowHead(canvas, arrowPaint, EndX, EndY, angle + (float)Math.PI, arrowLength, arrowAngle);
                }
                if (HasArrowStart)
                {
                    DrawArrowHead(canvas, arrowPaint, X, Y, angle, arrowLength, arrowAngle);
                }
            }

            if (IsSelected)
            {
                // 両端のハンドル
                using var handlePaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
                using var handleStrokePaint = new SKPaint { Color = SKColors.DeepSkyBlue, Style = SKPaintStyle.Stroke, StrokeWidth = 1 / CurrentZoomLevel, IsAntialias = true };
                float handleSize = 6 / CurrentZoomLevel;
                var points = new[] { new SKPoint(X, Y), new SKPoint(EndX, EndY) };
                foreach (var pt in points)
                {
                    var handleRect = new SKRect(pt.X - handleSize / 2, pt.Y - handleSize / 2, pt.X + handleSize / 2, pt.Y + handleSize / 2);
                    canvas.DrawRect(handleRect, handlePaint);
                    canvas.DrawRect(handleRect, handleStrokePaint);
                }
            }

            canvas.Restore();
        }

        private void DrawArrowHead(SKCanvas canvas, SKPaint paint, float x, float y, float angle, float length, float arrowAngle)
        {
            float x1 = x + length * (float)Math.Cos(angle + arrowAngle);
            float y1 = y + length * (float)Math.Sin(angle + arrowAngle);
            float x2 = x + length * (float)Math.Cos(angle - arrowAngle);
            float y2 = y + length * (float)Math.Sin(angle - arrowAngle);

            using var path = new SKPath();
            path.MoveTo(x, y);
            path.LineTo(x1, y1);
            path.LineTo(x2, y2);
            path.Close();

            canvas.DrawPath(path, paint);
        }

        public override bool HitTest(SKPoint point)
        {
            var p = UntransformPoint(point);
            // 点と線分の距離
            float lenSq = (EndX - X) * (EndX - X) + (EndY - Y) * (EndY - Y);
            if (lenSq == 0) return Math.Abs(p.X - X) < 5 && Math.Abs(p.Y - Y) < 5;

            float t = Math.Max(0, Math.Min(1, ((p.X - X) * (EndX - X) + (p.Y - Y) * (EndY - Y)) / lenSq));
            float projX = X + t * (EndX - X);
            float projY = Y + t * (EndY - Y);

            float distSq = (p.X - projX) * (p.X - projX) + (p.Y - projY) * (p.Y - projY);
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
        private bool _isBold = false;
        private bool _isItalic = false;
        private SKTextAlign _horizontalAlignment = SKTextAlign.Left;

        public string Text { get => _text; set => SetProperty(ref _text, value); }
        public string FontFamily { get => _fontFamily; set => SetProperty(ref _fontFamily, value); }
        public float FontSize { get => _fontSize; set => SetProperty(ref _fontSize, value); }
        public bool IsBold { get => _isBold; set => SetProperty(ref _isBold, value); }
        public bool IsItalic { get => _isItalic; set => SetProperty(ref _isItalic, value); }
        public SKTextAlign HorizontalAlignment { get => _horizontalAlignment; set => SetProperty(ref _horizontalAlignment, value); }

        public override void Draw(SKCanvas canvas)
        {
            if (string.IsNullOrEmpty(Text)) return;

            canvas.Save();
            TransformCanvas(canvas);

            var fillWithOpacity = FillColor.WithAlpha((byte)(FillColor.Alpha * Opacity));

            using var paint = new SKPaint
            {
                Color = fillWithOpacity,
                IsAntialias = true,
                TextAlign = HorizontalAlignment
            };

            using var typeface = SKTypeface.FromFamilyName(FontFamily, 
                IsBold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, 
                SKFontStyleWidth.Normal, 
                IsItalic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
            using var font = new SKFont(typeface, FontSize);

            var lines = Text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            float spacing = font.Spacing;
            
            // 全体の幅を計算（配置用・ローカル変数を使用）
            float maxWidth = 0;
            foreach (var line in lines)
            {
                maxWidth = Math.Max(maxWidth, font.MeasureText(line));
            }

            font.GetFontMetrics(out var metrics);

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                
                float xAnchor = X;
                if (HorizontalAlignment == SKTextAlign.Center) xAnchor = X + maxWidth / 2;
                else if (HorizontalAlignment == SKTextAlign.Right) xAnchor = X + maxWidth;

                // ベースライン計算: Y座標は上端を基準にするため、-metrics.Ascent (正の値) を加算
                float lineY = Y - metrics.Ascent + (i * spacing);
                canvas.DrawText(line, xAnchor, lineY, font, paint);
            }

            if (IsSelected)
            {
                float totalHeight = (lines.Length - 1) * spacing + FontSize;
                var rect = new SKRect(X, Y, X + maxWidth, Y + totalHeight);
                DrawSelectionBox(canvas, rect);
            }

            canvas.Restore();
        }

        public override bool HitTest(SKPoint point)
        {
            if (string.IsNullOrEmpty(Text)) return false;

            var p = UntransformPoint(point);

            using var typeface = SKTypeface.FromFamilyName(FontFamily, 
                IsBold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, 
                SKFontStyleWidth.Normal, 
                IsItalic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
            using var font = new SKFont(typeface, FontSize);

            var lines = Text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            float spacing = font.Spacing;
            float maxWidth = 0;
            
            foreach (var line in lines)
            {
                maxWidth = Math.Max(maxWidth, font.MeasureText(line));
            }

            float totalHeight = (lines.Length - 1) * spacing + FontSize;
            var rect = new SKRect(X, Y, X + maxWidth, Y + totalHeight);
            return rect.Contains(p.X, p.Y);
        }

        public override GraphicObject Clone()
        {
            var clone = new TextObject();
            CopyPropertiesTo(clone);
            clone.Text = Text;
            clone.FontFamily = FontFamily;
            clone.FontSize = FontSize;
            clone.IsBold = IsBold;
            clone.IsItalic = IsItalic;
            clone.HorizontalAlignment = HorizontalAlignment;
            return clone;
        }
    }

    public class GroupObject : GraphicObject
    {
        private List<GraphicObject> _children = new();
        private bool _isGrayscale = false;

        public override float CurrentZoomLevel
        {
            get => base.CurrentZoomLevel;
            set
            {
                base.CurrentZoomLevel = value;
                foreach (var child in _children)
                {
                    child.CurrentZoomLevel = value;
                }
            }
        }

        /// <summary>
        /// グループ内の画像オブジェクトのグレースケール状態を一括制御するプロパティ。
        /// 変更時に子の ImageObject すべてに伝播する。
        /// </summary>
        public bool IsGrayscale
        {
            get => _isGrayscale;
            set
            {
                if (SetProperty(ref _isGrayscale, value))
                {
                    // 子の ImageObject に伝播
                    PropagateGrayscale(this, value);
                }
            }
        }

        /// <summary>
        /// 再帰的に子の ImageObject に IsGrayscale を伝播する
        /// </summary>
        private static void PropagateGrayscale(GroupObject group, bool isGrayscale)
        {
            foreach (var child in group.Children)
            {
                if (child is ImageObject img)
                {
                    img.IsGrayscale = isGrayscale;
                }
                else if (child is GroupObject nested)
                {
                    nested.IsGrayscale = isGrayscale;
                }
            }
        }

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
            canvas.Save();
            TransformCanvas(canvas);

            // グループの不透明度を canvas.SaveLayer で適用
            // （子オブジェクトの Opacity プロパティを直接変更しないため PropertyChanged イベントが発火しない）
            if (Opacity < 1.0f)
            {
                using var layerPaint = new SKPaint { Color = SKColors.White.WithAlpha((byte)(255 * Opacity)) };
                canvas.SaveLayer(layerPaint);
            }

            // 子オブジェクトを描画（絶対座標で保持）
            foreach (var child in _children)
            {
                child.Draw(canvas);
            }

            // SaveLayer を使った場合はそのレイヤーを復元
            if (Opacity < 1.0f)
            {
                canvas.Restore();
            }

            if (IsSelected)
            {
                RecalculateBounds();
                var rect = new SKRect(X, Y, X + Width, Y + Height);
                DrawSelectionBox(canvas, rect);
            }

            canvas.Restore();
        }

        public override bool HitTest(SKPoint point)
        {
            var p = UntransformPoint(point);
            // 子オブジェクトのいずれかにヒットすればグループにヒット
            foreach (var child in _children)
            {
                if (child.HitTest(p)) return true;
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
        private SKBitmap? _eraserMask; // 消しゴム用アルファマスク（白=不透明, 黒=透過）
        private bool _isGrayscale = false;
        private float _minimum = 0.0f;
        private float _maximum = 1.0f;
        private int[]? _intensityHistogram;

        /// <summary>
        /// 消しゴム用アルファマスク。nullの場合はマスクなし。
        /// 白(255)=表示、黒(0)=透過。
        /// </summary>
        [JsonIgnore]
        public SKBitmap? EraserMask
        {
            get => _eraserMask;
            set => _eraserMask = value;
        }



        /// <summary>
        /// 消しゴムマスクを初期化（全面白=不透明）
        /// </summary>
        public void EnsureEraserMask()
        {
            try
            {
                if (_eraserMask != null) return;
                if (_imageData == null) return;
                // DstIn合成で確実に適用されるよう、Bgra8888カラーフォーマットを使用する
                _eraserMask = new SKBitmap(_imageData.Width, _imageData.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var canvas = new SKCanvas(_eraserMask);
                canvas.Clear(SKColors.White); // 白＝アルファ255なので後々のDstInで元画像がそのまま残る
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EnsureEraserMask Error: {ex}");
            }
        }

        /// <summary>
        /// 消しゴム操作：指定したピクセル矩形領域に透過を書き込む
        /// </summary>
        public void ApplyEraserRect(SKRect pixelRect)
        {
            try
            {
                EnsureEraserMask();
                if (_eraserMask == null) return;
                using var canvas = new SKCanvas(_eraserMask);
                using var paint = new SKPaint
                {
                    Color = new SKColor(0, 0, 0, 0), // 透明を書き込み
                    IsAntialias = false, // 矩形なのでアンチエイリアス不要
                    Style = SKPaintStyle.Fill,
                    BlendMode = SKBlendMode.Src // 既存値を上書き
                };
                canvas.DrawRect(pixelRect, paint);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ApplyEraserRect Error: {ex}");
            }
        }

        public bool IsGrayscale
        {
            get => _isGrayscale;
            set => SetProperty(ref _isGrayscale, value);
        }

        public float Minimum
        {
            get => _minimum;
            set
            {
                if (SetProperty(ref _minimum, value))
                {
                    OnPropertyChanged(nameof(Contrast));
                    OnPropertyChanged(nameof(Brightness));
                }
            }
        }

        public float Maximum
        {
            get => _maximum;
            set
            {
                if (SetProperty(ref _maximum, value))
                {
                    OnPropertyChanged(nameof(Contrast));
                    OnPropertyChanged(nameof(Brightness));
                }
            }
        }

        [JsonIgnore]
        public float Contrast
        {
            get { return 1.0f / (Maximum - Minimum + 0.0001f); }
            set
            {
                float center = (Maximum + Minimum) / 2f;
                float halfRange = 1.0f / (value * 2f);
                Minimum = Math.Max(0, center - halfRange);
                Maximum = Math.Min(1, center + halfRange);
            }
        }

        [JsonIgnore]
        public float Brightness
        {
            get { return (Maximum + Minimum) / 2f - 0.5f; }
            set
            {
                float range = Maximum - Minimum;
                float newCenter = value + 0.5f;
                Minimum = Math.Max(0, newCenter - range / 2f);
                Maximum = Math.Min(1, newCenter + range / 2f);
            }
        }

        [JsonIgnore]
        public int[]? IntensityHistogram
        {
            get => _intensityHistogram;
            private set => SetProperty(ref _intensityHistogram, value);
        }

        private float _cropX = 0;
        private float _cropY = 0;
        private float _cropWidth = -1;
        private float _cropHeight = -1;

        public float CropX
        {
            get => _cropX;
            set => SetProperty(ref _cropX, value);
        }

        public float CropY
        {
            get => _cropY;
            set => SetProperty(ref _cropY, value);
        }

        public float CropWidth
        {
            get => _cropWidth;
            set => SetProperty(ref _cropWidth, value);
        }

        public float CropHeight
        {
            get => _cropHeight;
            set => SetProperty(ref _cropHeight, value);
        }

        [JsonIgnore]
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
                    if (_cropWidth < 0) CropWidth = _imageData.Width;
                    if (_cropHeight < 0) CropHeight = _imageData.Height;
                    UpdateIntensityHistogram();
                }
            }
        }

        /// <summary>
        /// 画像の輝度ヒストグラムを計算します。
        /// </summary>
        public void UpdateIntensityHistogram()
        {
            if (_imageData == null)
            {
                IntensityHistogram = null;
                return;
            }

            int[] histogram = new int[256];
            unsafe
            {
                IntPtr pixels = _imageData.GetPixels();
                int pixelCount = _imageData.Width * _imageData.Height;
                byte* p = (byte*)pixels.ToPointer();

                for (int i = 0; i < pixelCount; i++)
                {
                    // Bgra8888 
                    byte b = *p++;
                    byte g = *p++;
                    byte r = *p++;
                    byte a = *p++;

                    // 輝度 Y = 0.299R + 0.587G + 0.114B
                    int y = (int)(0.299f * r + 0.587f * g + 0.114f * b);
                    if (y > 255) y = 255;
                    histogram[y]++;
                }
            }
            IntensityHistogram = histogram;
        }

        /// <summary>
        /// JSONシリアライズ/デシリアライズ用。画像のBase64エンコード文字列を取得・設定します。
        /// 設定されるとSKBitmapとしてImageDataに展開されます。
        /// </summary>
        public string? ImageBase64
        {
            get
            {
                if (_imageData == null) return null;
                using var image = SKImage.FromBitmap(_imageData);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                return Convert.ToBase64String(data.ToArray());
            }
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    ImageData = null;
                }
                else
                {
                    try
                    {
                        byte[] bytes = Convert.FromBase64String(value);
                        ImageData = SKBitmap.Decode(bytes);
                    }
                    catch
                    {
                        ImageData = null;
                    }
                }
            }
        }

        /// <summary>
        /// JSONシリアライズ/デシリアライズ用。消しゴムマスクのBase64エンコード文字列を取得・設定します。
        /// </summary>
        public string? EraserBase64
        {
            get
            {
                if (_eraserMask == null) return null;
                using var image = SKImage.FromBitmap(_eraserMask);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                return Convert.ToBase64String(data.ToArray());
            }
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    _eraserMask = null;
                }
                else
                {
                    try
                    {
                        byte[] bytes = Convert.FromBase64String(value);
                        _eraserMask = SKBitmap.Decode(bytes);
                    }
                    catch
                    {
                        _eraserMask = null;
                    }
                }
            }
        }

        private SKPaint? _cachedPaint;
        private float _lastOpacity = -1f;
        private bool _lastIsGrayscale = false;
        private float _lastContrast = -1f;
        private float _lastBrightness = -1f;

        private void UpdateCachedPaint()
        {
            if (_cachedPaint == null || _lastOpacity != Opacity || _lastIsGrayscale != IsGrayscale || _lastContrast != Contrast || _lastBrightness != Brightness)
            {
                _cachedPaint?.Dispose();
                _cachedPaint = new SKPaint();

                // コントラスト調整行列:
                // T = (1 - C) / 2
                // [ C 0 0 0 T+B ]
                // [ 0 C 0 0 T+B ]
                // [ 0 0 C 0 T+B ]
                // [ 0 0 0 1 0   ]
                // ImageJ 方式の調整行列 (Min/Max):
                // out = (in - min) / (max - min)
                // out = in * scale + offset
                // scale = 1.0 / (max - min)
                // offset = -min * scale
                float min = Minimum;
                float max = Maximum;
                float scale = 1.0f / (max - min + 0.0001f);
                float offset = -min * scale;

                float[] matrix;
                if (IsGrayscale)
                {
                    // グレースケール適用後に Min/Max 調整
                    matrix = new float[] {
                        0.299f * scale, 0.587f * scale, 0.114f * scale, 0, offset,
                        0.299f * scale, 0.587f * scale, 0.114f * scale, 0, offset,
                        0.299f * scale, 0.587f * scale, 0.114f * scale, 0, offset,
                        0,              0,              0,              Opacity, 0
                    };
                }
                else
                {
                    matrix = new float[] {
                        scale, 0,     0,     0, offset,
                        0,     scale, 0,     0, offset,
                        0,     0,     scale, 0, offset,
                        0,     0,     0,     Opacity, 0
                    };
                }

                _cachedPaint.ColorFilter = SKColorFilter.CreateColorMatrix(matrix);
                _lastOpacity = Opacity;
                _lastIsGrayscale = IsGrayscale;
                _lastContrast = Contrast;
                _lastBrightness = Brightness;
            }
        }

        public override void Draw(SKCanvas canvas)
        {
            if (_imageData == null) return;

            canvas.Save();
            TransformCanvas(canvas);

            var destRect = new SKRect(X, Y, X + Width, Y + Height);
            var srcRect = new SKRect(CropX, CropY, CropX + CropWidth, CropY + CropHeight);

            UpdateCachedPaint();

            // 消しゴムマスクがある場合はマスク適用描画
            if (_eraserMask != null)
            {
                try
                {
                    // オフスクリーンレイヤーに画像を描画（範囲を絞ってハングアップを防ぐ）
                    using var layerPaint = new SKPaint();
                    canvas.SaveLayer(destRect, layerPaint);
                    canvas.DrawBitmap(_imageData, srcRect, destRect, _cachedPaint);

                    // マスクをDstIn合成モードで適用（マスクの透明部分が画像を透過にする）
                    using var maskPaint = new SKPaint
                    {
                        BlendMode = SKBlendMode.DstIn, // dst のアルファをマスクで制限
                        IsAntialias = true
                    };
                    // マスクのソース矩形（クロップ対応）
                    var maskSrcRect = new SKRect(CropX, CropY, CropX + CropWidth, CropY + CropHeight);
                    canvas.DrawBitmap(_eraserMask, maskSrcRect, destRect, maskPaint);

                    canvas.Restore(); // SaveLayerの復元
                }
                catch (Exception ex)
                {
                    using var errPaint = new SKPaint { Color = SKColors.Red, TextSize = 16 };
                    canvas.DrawText("Eraser Err: " + ex.Message, X, Y - 10, errPaint);
                    canvas.DrawBitmap(_imageData, srcRect, destRect, _cachedPaint);
                }
            }
            else
            {
                canvas.DrawBitmap(_imageData, srcRect, destRect, _cachedPaint);
            }

            // 枠線の描画 (ImageObject 自体で描画)
            if (StrokeWidth > 0 && StrokeColor != SKColors.Transparent)
            {
                using var strokePaint = new SKPaint
                {
                    Color = StrokeColor.WithAlpha((byte)(StrokeColor.Alpha * Opacity)),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = StrokeWidth,
                    IsAntialias = true
                };
                canvas.DrawRect(destRect, strokePaint);
            }

            if (IsSelected)
            {
                DrawSelectionBox(canvas, destRect);
            }

            canvas.Restore();
        }

        public override bool HitTest(SKPoint point)
        {
            var p = UntransformPoint(point);
            var rect = new SKRect(X, Y, X + Width, Y + Height);
            return rect.Contains(p.X, p.Y);
        }

        public override GraphicObject Clone()
        {
            var clone = new ImageObject();
            CopyPropertiesTo(clone);
            clone.IsGrayscale = IsGrayscale;
            clone.Contrast = Contrast;
            clone.Brightness = Brightness;
            clone.CropX = CropX;
            clone.CropY = CropY;
            clone.CropWidth = CropWidth;
            clone.CropHeight = CropHeight;
            if (_imageData != null)
            {
                clone._imageData = _imageData.Copy();
            }
            if (_eraserMask != null)
            {
                clone._eraserMask = _eraserMask.Copy();
            }
            return clone;
        }

        public void Dispose()
        {
            _imageData?.Dispose();
            _imageData = null;
            _eraserMask?.Dispose();
            _eraserMask = null;
        }
    }

    public class PathObject : GraphicObject
    {
        private string _pathData = "";
        private SKPath? _cachedPath;

        public string PathData
        {
            get => _pathData;
            set
            {
                if (SetProperty(ref _pathData, value))
                {
                    _cachedPath?.Dispose();
                    _cachedPath = null;
                }
            }
        }

        private SKPath GetPath()
        {
            if (_cachedPath == null)
            {
                _cachedPath = SKPath.ParseSvgPathData(PathData) ?? new SKPath();
            }
            return _cachedPath;
        }

        public override void Draw(SKCanvas canvas)
        {
            var path = GetPath();
            if (path == null) return;

            canvas.Save();
            TransformCanvas(canvas);
            canvas.Translate(X, Y); // 起点を相対座標へ

            var fillWithOpacity = FillColor.WithAlpha((byte)(FillColor.Alpha * Opacity));
            var strokeWithOpacity = StrokeColor.WithAlpha((byte)(StrokeColor.Alpha * Opacity));

            using var paint = new SKPaint
            {
                Color = fillWithOpacity,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawPath(path, paint);

            paint.Color = strokeWithOpacity;
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = StrokeWidth; // ズーム補正を削除
            canvas.DrawPath(path, paint);

            if (IsSelected)
            {
                var rect = path.Bounds;
                DrawSelectionBox(canvas, rect);
            }

            canvas.Restore();
        }

        public override bool HitTest(SKPoint point)
        {
            var path = GetPath();
            if (path == null) return false;

            var p = UntransformPoint(point);
            // オブジェクトの基準点 (X, Y) からの相対座標に変換
            p.X -= X;
            p.Y -= Y;

            // 塗りつぶし部分にヒットするか
            if (path.Contains(p.X, p.Y)) return true;

            // 線上のヒットテスト
            if (StrokeWidth > 0 && StrokeColor != SKColors.Transparent)
            {
                // 判定用のマージン（線の太さの半分 + 最小クリック領域）
                float margin = Math.Max(StrokeWidth / 2f, 2.0f);
                using var outlinePath = new SKPath();
                using var paint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = margin * 2 };
                paint.GetFillPath(path, outlinePath);
                return outlinePath.Contains(p.X, p.Y);
            }

            return false;
        }

        public override GraphicObject Clone()
        {
            var clone = new PathObject();
            CopyPropertiesTo(clone);
            clone.PathData = PathData;
            return clone;
        }
    }
}
