using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace FigCrafterApp.Serialization
{
    /// <summary>
    /// SKColor を #AARRGGBB 形式の文字列としてシリアライズ・デシリアライズするコンバータ
    /// </summary>
    public class SKColorJsonConverter : JsonConverter<SKColor>
    {
        public override SKColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? colorString = reader.GetString();
            if (string.IsNullOrEmpty(colorString))
            {
                return SKColors.Transparent;
            }

            if (SKColor.TryParse(colorString, out SKColor color))
            {
                return color;
            }

            return SKColors.Transparent;
        }

        public override void Write(Utf8JsonWriter writer, SKColor value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
