using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FigCrafterApp.Models;
using SkiaSharp;
using System.Text.RegularExpressions;
using System.Globalization;

namespace FigCrafterApp.Helpers
{
    public static class EmfParser
    {
        private static readonly string[] InkscapePaths = new[]
        {
            @"D:\Inkscape\bin\inkscape.exe",
            @"C:\Program Files\Inkscape\bin\inkscape.exe",
            @"C:\Program Files (x86)\Inkscape\bin\inkscape.exe",
            @"\usr\bin\inkscape"
        };

        private static string? GetInkscapePath()
        {
            foreach (var path in InkscapePaths)
            {
                if (File.Exists(path)) return path;
            }
            return null;
        }

        public static GroupObject? ParseEmf(string filePath)
        {
            string inkscapePath = GetInkscapePath() ?? "";
            if (string.IsNullOrEmpty(inkscapePath))
            {
                System.Windows.MessageBox.Show("Inkscape が見つかりませんでした。\nC:\\Program Files\\Inkscape\\bin\\inkscape.exe にインストールされているか、パスを確認してください。", "エラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return null;
            }

            string tempSvg = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(filePath) + "_" + Guid.NewGuid().ToString().Substring(0, 8) + ".svg");

            try
            {
                // Inkscape CLI で SVG に変換（文字をパス化）
                var psi = new ProcessStartInfo
                {
                    FileName = inkscapePath,
                    Arguments = $"--export-type=svg --export-text-to-path --export-filename=\"{tempSvg}\" \"{filePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                using (var process = Process.Start(psi))
                {
                    process?.WaitForExit();
                }

                if (!File.Exists(tempSvg)) return null;

                // SVG を読み込み、パスを抽出
                var doc = XDocument.Load(tempSvg);
                XNamespace ns = "http://www.w3.org/2000/svg";

                var group = new GroupObject();

                // すべての <path> と <rect>, <ellipse>, <line>, <polyline>, <polygon> をパスとして抽出
                // Inkscape の --export-text-to-path はテキストも <path> に変換してくれる
                foreach (var element in doc.Descendants())
                {
                    string d = "";
                    if (element.Name == ns + "path") d = element.Attribute("d")?.Value ?? "";
                    else if (element.Name == ns + "rect") d = ConvertRectToPath(element);
                    else if (element.Name == ns + "circle" || element.Name == ns + "ellipse") d = ConvertEllipseToPath(element);
                    else if (element.Name == ns + "line") d = ConvertLineToPath(element);
                    else if (element.Name == ns + "polyline" || element.Name == ns + "polygon") d = ConvertPolyToPath(element);

                    if (string.IsNullOrEmpty(d)) continue;

                    var pathObj = new PathObject
                    {
                        PathData = d,
                        StrokeColor = ParseColor(GetAttributeOrStyle(element, "stroke")) ?? SKColors.Transparent,
                        FillColor = ParseColor(GetAttributeOrStyle(element, "fill")) ?? SKColors.Black,
                        StrokeWidth = ParseFloat(GetAttributeOrStyle(element, "stroke-width")) ?? 0.5f,
                        Opacity = ParseFloat(GetAttributeOrStyle(element, "opacity")) ?? 1.0f
                    };

                    // transform 属性の処理 (簡易的に translate のみ)
                    string transform = element.Attribute("transform")?.Value ?? "";
                    if (!string.IsNullOrEmpty(transform))
                    {
                        ApplyTransformToPath(pathObj, transform);
                    }

                    // バウンディングボックスの設定 (GroupObject の RecalculateBounds で使用するため)
                    using (var path = SKPath.ParseSvgPathData(pathObj.PathData))
                    {
                        if (path != null)
                        {
                            var bounds = path.Bounds;
                            pathObj.X = bounds.Left;
                            pathObj.Y = bounds.Top;
                            pathObj.Width = bounds.Width;
                            pathObj.Height = bounds.Height;
                        }
                    }

                    group.Children.Add(pathObj);
                }

                if (group.Children.Count > 0)
                {
                    group.RecalculateBounds();
                    return group;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EMF/SVG Conversion Error: {ex.Message}");
            }
            finally
            {
                if (File.Exists(tempSvg)) File.Delete(tempSvg);
            }

            return null;
        }

        private static string GetAttributeOrStyle(XElement element, string name)
        {
            string? attr = element.Attribute(name)?.Value;
            if (attr != null) return attr;

            string? style = element.Attribute("style")?.Value;
            if (style != null)
            {
                var match = Regex.Match(style, $@"{name}:\s*([^;]+)");
                if (match.Success) return match.Groups[1].Value.Trim();
            }
            return "";
        }

        private static void ApplyTransformToPath(PathObject pathObj, string transform)
        {
            using var path = SKPath.ParseSvgPathData(pathObj.PathData);
            if (path == null) return;

            var matrix = SKMatrix.CreateIdentity();
            
            // translate(x, y) または translate(x)
            var translateMatch = Regex.Match(transform, @"translate\(([-\d.]+),?\s*([-\d.]*)\)");
            if (translateMatch.Success)
            {
                float dx = float.Parse(translateMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                float dy = string.IsNullOrEmpty(translateMatch.Groups[2].Value) ? 0 : float.Parse(translateMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                matrix = SKMatrix.Concat(matrix, SKMatrix.CreateTranslation(dx, dy));
            }

            // matrix(a, b, c, d, e, f)
            var matrixMatch = Regex.Match(transform, @"matrix\(([-\d.e]+)\s+([-\d.e]+)\s+([-\d.e]+)\s+([-\d.e]+)\s+([-\d.e]+)\s+([-\d.e]+)\)");
            if (matrixMatch.Success)
            {
                var m = new SKMatrix
                {
                    ScaleX = float.Parse(matrixMatch.Groups[1].Value, CultureInfo.InvariantCulture),
                    SkewY = float.Parse(matrixMatch.Groups[2].Value, CultureInfo.InvariantCulture),
                    SkewX = float.Parse(matrixMatch.Groups[3].Value, CultureInfo.InvariantCulture),
                    ScaleY = float.Parse(matrixMatch.Groups[4].Value, CultureInfo.InvariantCulture),
                    TransX = float.Parse(matrixMatch.Groups[5].Value, CultureInfo.InvariantCulture),
                    TransY = float.Parse(matrixMatch.Groups[6].Value, CultureInfo.InvariantCulture)
                };
                matrix = SKMatrix.Concat(matrix, m);
            }

            path.Transform(matrix);
            pathObj.PathData = path.ToSvgPathData();
        }

        private static string ConvertRectToPath(XElement el)
        {
            float x = ParseFloat(el.Attribute("x")?.Value) ?? 0;
            float y = ParseFloat(el.Attribute("y")?.Value) ?? 0;
            float w = ParseFloat(el.Attribute("width")?.Value) ?? 0;
            float h = ParseFloat(el.Attribute("height")?.Value) ?? 0;
            return $"M {x} {y} L {x + w} {y} L {x + w} {y + h} L {x} {y + h} Z";
        }

        private static string ConvertEllipseToPath(XElement el)
        {
            float cx = ParseFloat(el.Attribute("cx")?.Value) ?? 0;
            float cy = ParseFloat(el.Attribute("cy")?.Value) ?? 0;
            float rx = ParseFloat(el.Attribute("rx")?.Value) ?? ParseFloat(el.Attribute("r")?.Value) ?? 0;
            float ry = ParseFloat(el.Attribute("ry")?.Value) ?? ParseFloat(el.Attribute("r")?.Value) ?? 0;
            
            using var path = new SKPath();
            path.AddOval(new SKRect(cx - rx, cy - ry, cx + rx, cy + ry));
            return path.ToSvgPathData();
        }

        private static string ConvertLineToPath(XElement el)
        {
            float x1 = ParseFloat(el.Attribute("x1")?.Value) ?? 0;
            float y1 = ParseFloat(el.Attribute("y1")?.Value) ?? 0;
            float x2 = ParseFloat(el.Attribute("x2")?.Value) ?? 0;
            float y2 = ParseFloat(el.Attribute("y2")?.Value) ?? 0;
            return $"M {x1} {y1} L {x2} {y2}";
        }

        private static string ConvertPolyToPath(XElement el)
        {
            string points = el.Attribute("points")?.Value ?? "";
            if (string.IsNullOrEmpty(points)) return "";
            var pts = points.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (pts.Length < 2) return "";
            
            string d = $"M {pts[0]} {pts[1]}";
            for (int i = 2; i < pts.Length; i += 2)
            {
                if (i + 1 < pts.Length) d += $" L {pts[i]} {pts[i+1]}";
            }
            if (el.Name == "polygon") d += " Z";
            return d;
        }

        private static SKColor? ParseColor(string? colorStr)
        {
            if (string.IsNullOrEmpty(colorStr) || colorStr == "none") return null;
            if (colorStr.StartsWith("rgb"))
            {
                var match = Regex.Match(colorStr, @"rgb\(\s*(\d+),\s*(\d+),\s*(\d+)\s*\)");
                if (match.Success)
                    return new SKColor(byte.Parse(match.Groups[1].Value), byte.Parse(match.Groups[2].Value), byte.Parse(match.Groups[3].Value));
            }
            if (SKColor.TryParse(colorStr, out var color)) return color;
            return null;
        }

        private static float? ParseFloat(string? val)
        {
            if (string.IsNullOrEmpty(val)) return null;
            string clean = Regex.Replace(val, @"[^\d.-]+", "");
            if (float.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return f;
            return null;
        }
    }
}

