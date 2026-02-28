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
        private float _strokeWidth = 1;
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
            paint.StrokeWidth = StrokeWidth;
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
            paint.StrokeWidth = StrokeWidth;
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
                StrokeWidth = StrokeWidth,
                IsAntialias = true
            };
            canvas.DrawLine(X, Y, EndX, EndY, paint);

            // 矢印の描画
            if (HasArrowStart || HasArrowEnd)
            {
                float dx = EndX - X;
                float dy = EndY - Y;
                float angle = (float)Math.Atan2(dy, dx);
                float arrowLength = 15f + StrokeWidth;
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

        public string Text { get => _text; set => SetProperty(ref _text, value); }
        public string FontFamily { get => _fontFamily; set => SetProperty(ref _fontFamily, value); }
        public float FontSize { get => _fontSize; set => SetProperty(ref _fontSize, value); }

        public override void Draw(SKCanvas canvas)
        {
            canvas.Save();
            TransformCanvas(canvas);

            var fillWithOpacity = FillColor.WithAlpha((byte)(FillColor.Alpha * Opacity));

            using var paint = new SKPaint
            {
                Color = fillWithOpacity,
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

            canvas.Restore();
        }

        public override bool HitTest(SKPoint point)
        {
            var p = UntransformPoint(point);

            using var paint = new SKPaint
            {
                Typeface = SKTypeface.FromFamilyName(FontFamily),
                TextSize = FontSize
            };
            var bounds = new SKRect();
            paint.MeasureText(Text, ref bounds);

            var rect = new SKRect(X, Y, X + bounds.Width, Y + bounds.Height);
            return rect.Contains(p.X, p.Y);
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
        private bool _isGrayscale = false;

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
        private float _eraserSize = 20f; // 消しゴムブラシサイズ

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

        public float EraserSize
        {
            get => _eraserSize;
            set => SetProperty(ref _eraserSize, value);
        }

        /// <summary>
        /// 消しゴムマスクを初期化（全面白=不透明）
        /// </summary>
        public void EnsureEraserMask()
        {
            if (_eraserMask != null) return;
            if (_imageData == null) return;
            // DstIn合成で確実に適用されるよう、Bgra8888カラーフォーマットを使用する
            _eraserMask = new SKBitmap(_imageData.Width, _imageData.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(_eraserMask);
            canvas.Clear(SKColors.White); // 白＝アルファ255なので後々のDstInで元画像がそのまま残る
        }

        /// <summary>
        /// 消しゴム操作：指定ピクセル座標を中心に半径radiusの円で透過を書き込む
        /// </summary>
        public void ApplyEraser(float pixelX, float pixelY, float radius)
        {
            EnsureEraserMask();
            if (_eraserMask == null) return;
            using var canvas = new SKCanvas(_eraserMask);
            using var paint = new SKPaint
            {
                Color = new SKColor(0, 0, 0, 0), // 透明を書き込み
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                BlendMode = SKBlendMode.Src // 既存値を上書き
            };
            canvas.DrawCircle(pixelX, pixelY, radius, paint);
        }

        public bool IsGrayscale
        {
            get => _isGrayscale;
            set => SetProperty(ref _isGrayscale, value);
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
                }
            }
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

        private SKPaint? _cachedPaint;
        private float _lastOpacity = -1f;
        private bool _lastIsGrayscale = false;

        private void UpdateCachedPaint()
        {
            if (_cachedPaint == null || _lastOpacity != Opacity || _lastIsGrayscale != IsGrayscale)
            {
                _cachedPaint?.Dispose();
                _cachedPaint = new SKPaint();
                
                var matrix = IsGrayscale ? new float[] {
                    0.299f, 0.587f, 0.114f, 0, 0,
                    0.299f, 0.587f, 0.114f, 0, 0,
                    0.299f, 0.587f, 0.114f, 0, 0,
                    0,      0,      0,      Opacity, 0
                } : new float[] {
                    1, 0, 0, 0,       0,
                    0, 1, 0, 0,       0,
                    0, 0, 1, 0,       0,
                    0, 0, 0, Opacity, 0
                };

                _cachedPaint.ColorFilter = SKColorFilter.CreateColorMatrix(matrix);
                _lastOpacity = Opacity;
                _lastIsGrayscale = IsGrayscale;
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
            else
            {
                canvas.DrawBitmap(_imageData, srcRect, destRect, _cachedPaint);
            }

            if (IsSelected)
            {
                // 選択枠
                using var borderPaint = new SKPaint
                {
                    Color = SKColors.DodgerBlue,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1,
                    PathEffect = SKPathEffect.CreateDash(new float[] { 4, 4 }, 0),
                    IsAntialias = true
                };
                canvas.DrawRect(destRect, borderPaint);

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
            clone.CropX = CropX;
            clone.CropY = CropY;
            clone.CropWidth = CropWidth;
            clone.CropHeight = CropHeight;
            clone.EraserSize = EraserSize;
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
}
