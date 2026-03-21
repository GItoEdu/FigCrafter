using System;

namespace FigCrafterApp.Models
{
    /// <summary>
    /// パスの描画タイプ
    /// </summary>
    public enum PathNodeType
    {
        Move,   // パスの開始点
        Line,   // 直線
        Bezier  // 3次ベジェ曲線
    }

    /// <summary>
    /// パスを構成する個々の頂点（アンカーポイント）とハンドル（コントロールポイント）
    /// </summary>
    public class PathNode
    {
        public PathNodeType NodeType { get; set; }

        public float X { get; set; }
        public float Y { get; set; }
        public float Control1X { get; set; }
        public float Control1Y { get; set; }
        public float Control2X { get; set; }
        public float Control2Y { get; set; }
        public PathNode Clone()
        {
            return (PathNode)this.MemberwiseClone();
        }
    }
}