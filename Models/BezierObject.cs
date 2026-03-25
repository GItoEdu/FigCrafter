using System;
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

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            // X, Y を起点とした相対座標でパスを構築し、同時に元のサイズを計算
            foreach (var node in Nodes)
            {
                if (node.X < minX) minX = node.X;
                if (node.Y < minY) minY = node.Y;
                if (node.X > maxX) maxX = node.X;
                if (node.Y > maxY) maxY = node.Y;

                if (isFirst || node.NodeType == PathNodeType.Move)
                {
                    path.MoveTo(node.X, node.Y);
                    isFirst = false;
                }
                else if (node.NodeType == PathNodeType.Line)
                {
                    path.LineTo(node.X, node.Y);
                }
                else if (node.NodeType == PathNodeType.Bezier)
                {
                    path.CubicTo(node.Control1X, node.Control1Y, node.Control2X, node.Control2Y, node.X, node.Y);
                }
            }
            if (IsClosed) path.Close();

            // スケール比率の計算
            float nodeW = maxX - minX;
            float nodeH = maxY - minY;
            float scaleX = nodeW > 0 ? Width / nodeW : 1f;
            float scaleY = nodeH > 0 ? Height / nodeH : 1f;

            // スケールを適用した新しいパスを生成
            using var scaledPath = new SKPath();
            var matrix = SKMatrix.CreateScale(scaleX, scaleY);
            path.Transform(matrix, scaledPath);
            scaledPath.FillType = FillType;

            canvas.Save();
            TransformCanvas(canvas);
            canvas.Translate(X, Y); // 起点座標へ移動

            var fillWithOpacity = FillColor.WithAlpha((byte)(FillColor.Alpha * Opacity));
            var strokeWithOpacity = StrokeColor.WithAlpha((byte)(StrokeColor.Alpha * Opacity));

            using var paint = new SKPaint
            {
                Color = fillWithOpacity,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawPath(scaledPath, paint);

            paint.Color = strokeWithOpacity;
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = StrokeWidth; // 線の太さは不変（そのまま）
            canvas.DrawPath(scaledPath, paint);

            if (IsSelected)
            {
                var rect = scaledPath.Bounds;
                DrawSelectionBox(canvas, rect);
            }

            canvas.Restore();
        }

        public override bool HitTest(SKPoint point)
        {
            if (Nodes == null || Nodes.Count == 0) return false;

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (var node in Nodes)
            {
                if (node.X < minX) minX = node.X;
                if (node.Y < minY) minY = node.Y;
                if (node.X > maxX) maxX = node.X;
                if (node.Y > maxY) maxY = node.Y;
            }

            float nodeW = maxX - minX;
            float nodeH = maxY - minY;
            float scaleX = nodeW > 0 ? Width / nodeW : 1f;
            float scaleY = nodeH > 0 ? Height / nodeH : 1f;

            var p = UntransformPoint(point);
            p.X -= X;
            p.Y -= Y;

            // マウス座標をスケールの逆数で割って、元のパス空間に戻す
            p.X = scaleX > 0 ? p.X / scaleX : p.X;
            p.Y = scaleY > 0 ? p.Y / scaleY : p.Y;

            using var path = new SKPath();
            bool isFirst = true;
            foreach (var node in Nodes)
            {
                if (isFirst || node.NodeType == PathNodeType.Move)
                {
                    path.MoveTo(node.X, node.Y);
                    isFirst = false;
                }
                else if (node.NodeType == PathNodeType.Line)
                {
                    path.LineTo(node.X, node.Y);
                }
                else if (node.NodeType == PathNodeType.Bezier)
                {
                    path.CubicTo(node.Control1X, node.Control1Y, node.Control2X, node.Control2Y, node.X, node.Y);
                }
            }
            if (IsClosed) path.Close();
            path.FillType = FillType;

            if (path.Contains(p.X, p.Y)) return true;

            if (StrokeWidth > 0 && StrokeColor != SKColors.Transparent)
            {
                // ここでのマージンは元のパススケール基準になるため、スケールの逆数を掛けて補正する
                float avgScale = (scaleX + scaleY) / 2f;
                float margin = Math.Max(StrokeWidth / 2f, 2.0f / CurrentZoomLevel);
                if (avgScale > 0) margin /= avgScale;

                using var outlinePath = new SKPath();
                using var paint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = margin * 2 };
                paint.GetFillPath(path, outlinePath);
                return outlinePath.Contains(p.X, p.Y);
            }

            return false;
        }

        public override GraphicObject Clone()
        {
            var clone = new BezierObject();
            CopyPropertiesTo(clone);
            clone.IsClosed = this.IsClosed;
            clone.FillType = this.FillType;
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