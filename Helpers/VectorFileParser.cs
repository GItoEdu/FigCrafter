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
    public static class VectorFileParser
    {
        private const float PxToMm = 25.4f / 96f; // Inkscape (96DPI) -> FigCrafter (mm)
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

        public static GroupObject? ParseVectorFile(string filePath)
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

                var doc = XDocument.Load(tempSvg);
                XNamespace ns = "http://www.w3.org/2000/svg";
                var root = doc.Root;
                if (root == null) return null;

                var group = new GroupObject();
                
                // 再帰的に要素を処理し、座標変換を継承させる
                ProcessElement(root, SKMatrix.CreateScale(PxToMm, PxToMm), group, ns);

                if (group.Children.Count > 0)
                {
                    group.RecalculateBounds();
                    return group;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Vector File Conversion Error: {ex.Message}");
            }
            finally
            {
                if (File.Exists(tempSvg)) File.Delete(tempSvg);
            }

            return null;
        }

        private static void ProcessElement(XElement element, SKMatrix parentMatrix, GroupObject targetGroup, XNamespace ns)
        {
            // ローカルの transform を取得し、親の行列と結合
            string transformAttr = element.Attribute("transform")?.Value ?? "";
            SKMatrix localMatrix = string.IsNullOrEmpty(transformAttr) ? SKMatrix.CreateIdentity() : ParseTransform(transformAttr);
            SKMatrix currentMatrix = SKMatrix.Concat(parentMatrix, localMatrix);

            string d = "";
            if (element.Name == ns + "path") d = element.Attribute("d")?.Value ?? "";
            else if (element.Name == ns + "rect") d = ConvertRectToPath(element);
            else if (element.Name == ns + "circle" || element.Name == ns + "ellipse") d = ConvertEllipseToPath(element);
            else if (element.Name == ns + "line") d = ConvertLineToPath(element);
            else if (element.Name == ns + "polyline" || element.Name == ns + "polygon") d = ConvertPolyToPath(element);

            if (!string.IsNullOrEmpty(d))
            {
                var pathObj = new PathObject
                {
                    StrokeColor = ParseColor(GetAttributeOrStyle(element, "stroke")) ?? SKColors.Transparent,
                    FillColor = ParseColor(GetAttributeOrStyle(element, "fill")) ?? SKColors.Black,
                    StrokeWidth = (ParseFloat(GetAttributeOrStyle(element, "stroke-width")) ?? 1.0f) * PxToMm,
                    Opacity = ParseFloat(GetAttributeOrStyle(element, "opacity")) ?? 1.0f
                };

                // 行列を適用
                using (var path = SKPath.ParseSvgPathData(d))
                {
                    if (path != null)
                    {
                        path.Transform(currentMatrix);
                        
                        var bounds = path.Bounds;
                        pathObj.X = bounds.Left;
                        pathObj.Y = bounds.Top;
                        pathObj.Width = bounds.Width;
                        pathObj.Height = bounds.Height;

                        // パスデータ自体は原点 (0,0) をバウンディングボックスの左上として保存する
                        // これを行わないと、GraphicObject の X/Y と重複してオフセットされる
                        path.Offset(-bounds.Left, -bounds.Top);
                        pathObj.PathData = path.ToSvgPathData();
                        
                        targetGroup.Children.Add(pathObj);
                    }
                }
            }

            // 子要素を再帰的に処理
            foreach (var child in element.Elements())
            {
                ProcessElement(child, currentMatrix, targetGroup, ns);
            }
        }

        private static SKMatrix ParseTransform(string transform)
        {
            var matrix = SKMatrix.CreateIdentity();
            
            // translate, matrix, scale, rotate などの複数の関数が並んでいる可能性がある
            var matches = Regex.Matches(transform, @"(\w+)\s*\(([^)]+)\)");
            foreach (Match match in matches)
            {
                string func = match.Groups[1].Value;
                string argsStr = match.Groups[2].Value;
                var args = Regex.Split(argsStr, @"[\s,]+").Where(s => !string.IsNullOrEmpty(s))
                                .Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();

                if (func == "translate")
                {
                    float dx = args[0];
                    float dy = args.Length > 1 ? args[1] : 0;
                    matrix = SKMatrix.Concat(matrix, SKMatrix.CreateTranslation(dx, dy));
                }
                else if (func == "matrix" && args.Length == 6)
                {
                    var m = new SKMatrix
                    {
                        ScaleX = args[0],
                        SkewY = args[1],
                        SkewX = args[2],
                        ScaleY = args[3],
                        TransX = args[4],
                        TransY = args[5],
                        Persp0 = 0, Persp1 = 0, Persp2 = 1
                    };
                    matrix = SKMatrix.Concat(matrix, m);
                }
                else if (func == "scale")
                {
                    float sx = args[0];
                    float sy = args.Length > 1 ? args[1] : sx;
                    matrix = SKMatrix.Concat(matrix, SKMatrix.CreateScale(sx, sy));
                }
                else if (func == "rotate")
                {
                    float angle = args[0];
                    if (args.Length == 1)
                        matrix = SKMatrix.Concat(matrix, SKMatrix.CreateRotationDegrees(angle));
                    else if (args.Length == 3)
                        matrix = SKMatrix.Concat(matrix, SKMatrix.CreateRotationDegrees(angle, args[1], args[2]));
                }
            }
            return matrix;
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

