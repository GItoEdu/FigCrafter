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
        public ObservableCollection<GraphicObject> GraphicObjects { get; set; } = new();

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
