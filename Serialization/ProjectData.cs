using System.Collections.ObjectModel;
using System.Text.Json;
using FigCrafterApp.Models;

namespace FigCrafterApp.Serialization
{
    public class ProjectData
    {
        public string Title { get; set; } = "名称未設定";
        public double WidthMm { get; set; } = 210;
        public double HeightMm { get; set; } = 297;
        // 後方互換性のために残す（v1データ読み込み用）
        public ObservableCollection<GraphicObject>? GraphicObjects { get; set; }

        // 新しいレイヤーベースのデータ構造
        public ObservableCollection<Layer> Layers { get; set; } = new();

        /// <summary>
        /// デシリアライズ直後に呼ばれ、古いv1形式のデータをレイヤー形式に変換します。
        /// </summary>
        public void EnsureLayerCompatibility()
        {
            if (Layers == null || Layers.Count == 0)
            {
                Layers = new ObservableCollection<Layer>();
                var defaultLayer = new Layer { Name = "レイヤー 1" };
                
                if (GraphicObjects != null)
                {
                    foreach (var obj in GraphicObjects)
                    {
                        defaultLayer.GraphicObjects.Add(obj);
                    }
                }
                Layers.Add(defaultLayer);
            }
            
            // 変換後は古いリストをクリアしておく（次回の保存時には無視されるようにしてもよい）
            GraphicObjects = null;
        }

        public static JsonSerializerOptions GetSerializerOptions()
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true,
                Converters =
                {
                    new SKColorJsonConverter()
                }
            };
            return options;
        }
    }
}
