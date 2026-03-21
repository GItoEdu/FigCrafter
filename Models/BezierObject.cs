using System.Collections.ObjectModel;
using System.Linq;
using SkiaSharp;

namespace FigCrafterApp.Models
{
    /// <summary>
    /// 頂点とハンドルを持っており、自由に変形・編集できるベジェ曲線オブジェクト
    /// </summary>
    public class BezierObject : GraphicObject
    {
        // パスを構成する頂点のリスト
        public ObservableCollection<PathNode> Nodes { get; set; } = new();

        // パスの始点と終点を閉じるかどうか
        public bool IsClosed { get; set; } = false;
        public SKPathFillType FillType { get; set; } = SKPathFillType.Winding;

        public override void Draw(SKCanvas canvas)
        {
            if (Nodes == null || Nodes.Count == 0) return;

            using var path = new SKPath();
            bool isFirst = true;

            // ノードを辿って SKPath を構築する
            foreach (var node in Nodes)
            {
                // Nodesの座標はPathObjectのX,Yを基準とした相対座標として扱う
                // ドラッグ移動でGraphicObject.X,Yが変わるだけで図形全体が移動する
                float absX = X + node.X;
                float absY = Y + node.Y;

                if (isFirst || node.NodeType == PathNodeType.Move)
                {
                    path.MoveTo(absX, absY);
                    isFirst = false;
                }
                else if (node.NodeType == PathNodeType.Line)
                {
                    path.LineTo(absX, absY);
                }
                else if (node.NodeType == PathNodeType.Bezier)
                {
                    path.CubicTo(
                        X + node.Control1X, Y + node.Control1Y,
                        X + node.Control2X, Y + node.Control2Y,
                        absX, absY
                    );
                }
            }

            if (IsClosed)
            {
                path.Close();
            }

            // 回転などのトランスフォームを適用
            canvas.Save();
            TransformCanvas(canvas);

            // 塗りつぶしの描画
            if (FillColor != SKColors.Transparent)
            {
                using var fillPaint = new SKPaint
                {
                    Color = FillColor,
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                };
                canvas.DrawPath(path, fillPaint);
            }

            // 線の描画
            if (StrokeWidth > 0 && StrokeColor != SKColors.Transparent)
            {
                using var strokePaint = new SKPaint
                {
                    Color = StrokeColor,
                    StrokeWidth = StrokeWidth / CurrentZoomLevel,
                    Style = SKPaintStyle.Stroke,
                    IsAntialias = true
                };
                canvas.DrawPath(path, strokePaint);
            }

            // 選択時のバウンディングボックス描画
            if (IsSelected)
            {
                var rect = path.Bounds;
                DrawSelectionBox(canvas, rect);
            }

            canvas.Restore();
        }

        public override bool HitTest(SKPoint point)
        {
            if (Nodes == null || Nodes.Count == 0) return false;

            // 回転を考慮したローカル座標への変換
            var localPoint = UntransformPoint(point);

            // バウンディングボックスで大まかに判定
            var rect = new SKRect (X, Y, X + Width, Y + Height);
            if (!rect.Contains(localPoint)) return false;

            // バウンディングボックス内なら、パスの内側・線の上か厳密に判定
            using var path = GetSKPath();

            // 塗りが透明でなければ、パスの内側を判定
            if (FillColor != SKColors.Transparent && path.Contains(localPoint.X, localPoint.Y))
            {
                return true;
            }

            // 塗りが透明でも線の上をクリックしたか判定
            if (StrokeWidth > 0 && StrokeColor != SKColors.Transparent)
            {
                float margin = System.Math.Max(StrokeWidth / 2f, 2.0f / CurrentZoomLevel);
                using var outlinePath = new SKPath();
                using var paint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = margin * 2
                };
                paint.GetFillPath(path, outlinePath);
                return outlinePath.Contains(localPoint.X, localPoint.Y);
            }

            return false;
        }

        /// <summary>
        /// 現在のノードの状態から SKPath を生成するヘルパー関数
        /// </summary>
        /// <returns></returns>
        private SKPath GetSKPath()
        {
            var path = new SKPath();
            path.FillType = this.FillType;
            if (Nodes == null || Nodes.Count == 0) return path;

            bool isFirst = true;
            foreach (var node in Nodes)
            {
                float absX = X + node.X;
                float absY = Y + node.Y;

                if (isFirst || node.NodeType == PathNodeType.Move)
                {
                    path.MoveTo(absX, absY);
                    isFirst = false;
                }
                else if (node.NodeType == PathNodeType.Line)
                {
                    path.LineTo(absX, absY);
                }
                else if (node.NodeType == PathNodeType.Bezier)
                {
                    path.CubicTo(
                        X + node.Control1X, Y + node.Control1Y,
                        X + node.Control2X, Y + node.Control2Y,
                        absX, absY
                    );
                }
            }
            if (IsClosed) path.Close();
            return path;
        }

        public override GraphicObject Clone()
        {
            var clone = new BezierObject();
            // プロパティをコピー
            CopyPropertiesTo(clone);

            clone.IsClosed = this.IsClosed;
            clone.FillType = this.FillType;
            
            // ノードのディープコピー
            clone.Nodes = new ObservableCollection<PathNode>(this.Nodes.Select(n => n.Clone()));
            return clone;
        }

        /// <summary>
        /// ノードの座標から Width と Height を再計算する
        /// </summary>
        public void RecalculateBounds()
        {
            if (Nodes == null || Nodes.Count == 0) return;

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            foreach (var node in Nodes)
            {
                if (node.X < minX) minX = node.X;
                if (node.Y < minY) minY = node.Y;
                if (node.X > maxX) maxX = node.X;
                if (node.Y > maxY) maxY = node.Y;
            }

            Width = maxX - minX;
            Height = maxY - minY;
        }
    }
}