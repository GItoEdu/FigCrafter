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
                // Inkscape CLI で SVG に変換（テキストは保持）
                var psi = new ProcessStartInfo
                {
                    FileName = inkscapePath,
                    Arguments = $"--export-type=svg --export-filename=\"{tempSvg}\" \"{filePath}\"",
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

                var doc = XDocument.Load(tempSvg, LoadOptions.PreserveWhitespace);
                XNamespace ns = "http://www.w3.org/2000/svg";
                var root = doc.Root;
                if (root == null) return null;

                // 単位と viewBox から正確なスケールを計算
                // widthMm は文字通りミリメートル単位である必要がある
                float widthMm = ParseSvgToMm(root.Attribute("width")?.Value) ?? 0;
                var viewBoxAttr = root.Attribute("viewBox")?.Value;
                float scale = PxToMm; // デフォルト

                if (!string.IsNullOrEmpty(viewBoxAttr))
                {
                    var vb = viewBoxAttr.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                                       .Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                    if (vb.Length == 4 && vb[2] > 0 && widthMm > 0)
                    {
                        scale = widthMm / vb[2];
                    }
                }
                else if (widthMm > 0)
                {
                    // viewBox がなく width がある場合（Inkscape 以外）
                    // width が px 指定なら PxToMm になるはず
                }

                var group = new GroupObject();
                
                // 再帰的に要素を処理し、座標変換を継承させる
                ProcessElement(root, SKMatrix.CreateScale(scale, scale), group, ns, scale, SvgStyle.Default);

                if (group.Children.Count > 0)
                {
                    MergeTextObjects(group);
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

        private static void MergeTextObjects(GroupObject group)
        {
            if (group.Children.Count < 2) return;

            var texts = group.Children.OfType<TextObject>().ToList();
            if (texts.Count < 2) return;

            // 回転、フォント、サイズ、色、垂直方向の近接度でグループ化
            // 垂直方向の近接度は FontSize の 0.5倍程度を許容
            var remainingTexts = new List<TextObject>(texts);
            var mergedList = new List<TextObject>();
            // 単語の途中で結合が途切れるのを防ぐため、直前に結合した文字の座標を保持
            var lastCoords = new Dictionary<TextObject, (double X, double Y)>();

            while (remainingTexts.Count > 0)
            {
                var current = remainingTexts[0];
                remainingTexts.RemoveAt(0);

                bool merged = false;
                for (int i = 0; i < mergedList.Count; i++)
                {
                    var target = mergedList[i];

                    // 属性の一致確認
                    if (Math.Abs(current.Rotation - target.Rotation) < 0.1 &&
                        current.FontFamily == target.FontFamily &&
                        Math.Abs(current.FontSize - target.FontSize) < 0.1 &&
                        current.FillColor == target.FillColor &&
                        current.IsBold == target.IsBold &&
                        current.IsItalic == target.IsItalic)
                    {
                        // 回転角度を考慮したベクトル計算
                        double angleRad = current.Rotation * Math.PI / 180.0;
                        double dirX = Math.Cos(angleRad);
                        double dirY = Math.Sin(angleRad);

                        double dx = current.X - target.X;
                        double dy = current.Y - target.Y;

                        // 進行方向の距離（内積）
                        double distX = dx * dirX + dy * dirY;
                        // 垂直方向の距離（外積的なベースラインのずれ）
                        double distY = -dx * dirY + dy * dirX;

                        // 許容値の定義
                        double verticalThreshold = current.FontSize * 0.5;
                        double horizontalThreshold = current.FontSize * 2.0;

                        if (Math.Abs(distY) < verticalThreshold && Math.Abs(distX) < horizontalThreshold)
                        {
                            if (distX > 0)
                            {
                                // currentがtargetの進行方向にある（後ろ）
                                target.Text += current.Text;
                                lastCoords[target] = (current.X, current.Y);
                            }
                            else
                            {
                                // currentがtargetの逆方向にある（前）
                                target.Text = current.Text + target.Text;
                                target.X = current.X;
                                target.Y = current.Y;
                                lastCoords[target] = (current.X, current.Y);
                            }
                            merged = true;
                            break;
                        }
                    }
                }

                if (!merged)
                {
                    mergedList.Add(current);
                }
            }

            // 元のリストから TextObject を取り除き、統合されたものを追加
            foreach (var t in texts) group.Children.Remove(t);
            foreach (var t in mergedList) group.Children.Add(t);

            // 再帰的に子グループも処理
            foreach (var child in group.Children.OfType<GroupObject>())
            {
                MergeTextObjects(child);
            }
        }

        private struct SvgStyle
        {
            public SKColor? Fill;
            public SKColor? Stroke;
            public float? StrokeWidth; // SVGユーザーユニット (px)
            public float? Opacity;

            public static SvgStyle Default => new SvgStyle
            {
                Fill = SKColors.Black, // SVG default fill is black
                Stroke = SKColors.Transparent,
                StrokeWidth = 1.0f,
                Opacity = 1.0f
            };

            public SvgStyle Clone() => (SvgStyle)this.MemberwiseClone();
        }

        private static void ProcessElement(XElement element, SKMatrix parentMatrix, GroupObject targetGroup, XNamespace ns, float scale, SvgStyle parentStyle)
        {
            // ローカルの transform を取得し、親の行列と結合
            string transformAttr = element.Attribute("transform")?.Value ?? "";
            SKMatrix localMatrix = string.IsNullOrEmpty(transformAttr) ? SKMatrix.CreateIdentity() : ParseTransform(transformAttr);
            SKMatrix currentMatrix = SKMatrix.Concat(parentMatrix, localMatrix);

            // スタイルの継承と解決
            var currentStyle = parentStyle;
            
            string fillStr = GetAttributeOrStyle(element, "fill");
            if (!string.IsNullOrEmpty(fillStr)) currentStyle.Fill = ParseColor(fillStr);

            string strokeStr = GetAttributeOrStyle(element, "stroke");
            if (!string.IsNullOrEmpty(strokeStr)) currentStyle.Stroke = ParseColor(strokeStr);

            string strokeWidthStr = GetAttributeOrStyle(element, "stroke-width");
            if (!string.IsNullOrEmpty(strokeWidthStr)) currentStyle.StrokeWidth = ParseSvgToUserUnits(strokeWidthStr, scale);

            string opacityStr = GetAttributeOrStyle(element, "opacity");
            if (!string.IsNullOrEmpty(opacityStr)) currentStyle.Opacity = ParseFloat(opacityStr);

            string d = "";
            if (element.Name == ns + "path") d = element.Attribute("d")?.Value ?? "";
            else if (element.Name == ns + "rect") d = ConvertRectToPath(element);
            else if (element.Name == ns + "circle" || element.Name == ns + "ellipse") d = ConvertEllipseToPath(element);
            else if (element.Name == ns + "line") d = ConvertLineToPath(element);
            else if (element.Name == ns + "polyline" || element.Name == ns + "polygon") d = ConvertPolyToPath(element);
            else if (element.Name == ns + "text" || element.Name == ns + "tspan")
            {
                ProcessTextElement(element, currentMatrix, targetGroup, currentStyle, scale);
            }
            else if (element.Name == ns + "image")
            {
                ProcessImageElement(element, currentMatrix, targetGroup, currentStyle);
            }

            if (!string.IsNullOrEmpty(d))
            {
                var pathObj = new PathObject
                {
                    StrokeColor = currentStyle.Stroke ?? SKColors.Transparent,
                    FillColor = currentStyle.Fill ?? SKColors.Transparent,
                    StrokeWidth = (currentStyle.StrokeWidth ?? 1.0f) * GetMatrixScale(currentMatrix),
                    Opacity = currentStyle.Opacity ?? 1.0f
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

                        // パスデータを X, Y を起点とした相対座標に変換して保存
                        path.Offset(-bounds.Left, -bounds.Top);
                        pathObj.PathData = path.ToSvgPathData();
                        
                        targetGroup.Children.Add(pathObj);
                    }
                }
            }

            // 子要素を再帰的に処理
            foreach (var child in element.Elements())
            {
                // text 要素の子要素としての tspan は ProcessTextElement 内で個別に処理する場合もあるが、
                // ここでは単純な再帰で処理し、親子関係を継承する
                ProcessElement(child, currentMatrix, targetGroup, ns, scale, currentStyle);
            }
        }

        private static void ProcessTextElement(XElement el, SKMatrix matrix, GroupObject targetGroup, SvgStyle style, float scale)
        {
            XNamespace ns = el.Name.Namespace;
            // <text> 要素に <tspan> 子要素がある場合、<text> 自体としてのテキスト処理はスキップして
            // 子要素（tspan）に任せる（重複防止と各行の座標精度向上のため）
            if (el.Name == ns + "text" && el.Elements(ns + "tspan").Any())
            {
                return;
            }

            string text = el.Value; 
            if (string.IsNullOrEmpty(text)) return;

            // 座標 (x, y)
            float x = ParseFloat(el.Attribute("x")?.Value) ?? 0;
            float y = ParseFloat(el.Attribute("y")?.Value) ?? 0;

            // 文字サイズ
            // scale は mm/unit。fontSize (unit) * scale (mm/unit) で mm に変換される。
            float fontSize = ParseSvgToUserUnits(GetAttributeOrStyle(el, "font-size"), scale) ?? 12f;
            string fontFamily = GetAttributeOrStyle(el, "font-family") ?? "Arial";
            string fontWeight = GetAttributeOrStyle(el, "font-weight") ?? "";
            string fontStyleStr = GetAttributeOrStyle(el, "font-style") ?? "";

            // 行列からスケールと回転を抽出
            float matrixScale = (float)Math.Sqrt(matrix.ScaleX * matrix.ScaleX + matrix.SkewY * matrix.SkewY);
            float rotation = (float)(Math.Atan2(matrix.SkewY, matrix.ScaleX) * 180.0 / Math.PI);

            var textObj = new TextObject
            {
                Text = text,
                FontSize = fontSize * matrixScale, // フォントサイズ自体はSVGの単位系なので行列のスケールを乗算
                FontFamily = fontFamily,
                IsBold = fontWeight.Contains("bold") || fontWeight == "700" || fontWeight == "800" || fontWeight == "900",
                IsItalic = fontStyleStr.Contains("italic"),
                FillColor = style.Fill ?? SKColors.Black,
                Opacity = style.Opacity ?? 1.0f,
                Rotation = rotation
            };

            // 行列変換前でY座標をベースラインから上端へシフトする
            float localY = y - (fontSize * 0.8f);
            var p = matrix.MapPoint(x, localY);

            textObj.X = p.X;
            textObj.Y = p.Y;

            targetGroup.Children.Add(textObj);
        }

        private static void ProcessImageElement(XElement el, SKMatrix matrix, GroupObject targetGroup, SvgStyle style)
        {
            string href = el.Attribute("{http://www.w3.org/1999/xlink}href")?.Value 
                       ?? el.Attribute("href")?.Value ?? "";

            if (string.IsNullOrEmpty(href)) return;

            SKBitmap? bitmap = null;

            if (href.StartsWith("data:image/"))
            {
                // Base64 データのデコード
                var match = Regex.Match(href, @"data:image/(?<type>[^;]+);base64,(?<data>.+)");
                if (match.Success)
                {
                    try
                    {
                        byte[] bytes = Convert.FromBase64String(match.Groups["data"].Value);
                        bitmap = SKBitmap.Decode(bytes);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Image Decode Error: {ex.Message}");
                    }
                }
            }

            if (bitmap != null)
            {
                float x = ParseFloat(el.Attribute("x")?.Value) ?? 0;
                float y = ParseFloat(el.Attribute("y")?.Value) ?? 0;
                float w = ParseFloat(el.Attribute("width")?.Value) ?? bitmap.Width;
                float h = ParseFloat(el.Attribute("height")?.Value) ?? bitmap.Height;

                var imgObj = new ImageObject
                {
                    ImageData = bitmap,
                    Opacity = style.Opacity ?? 1.0f
                };

                // 行列を適用して座標とサイズを決定
                var p = matrix.MapPoint(x, y);
                float s = GetMatrixScale(matrix);
                imgObj.X = p.X;
                imgObj.Y = p.Y;
                imgObj.Width = w * s;
                imgObj.Height = h * s;

                targetGroup.Children.Add(imgObj);
            }
        }

        private static float GetMatrixScale(SKMatrix matrix)
        {
            // 行列の平均スケールを計算 (sqrt((m11^2 + m21^2 + m12^2 + m22^2)/2))
            return (float)Math.Sqrt((matrix.ScaleX * matrix.ScaleX + matrix.SkewY * matrix.SkewY + 
                                     matrix.SkewX * matrix.SkewX + matrix.ScaleY * matrix.ScaleY) / 2.0);
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
            if (el.Name.LocalName == "polygon") d += " Z";
            return d;
        }

        private static SKColor? ParseColor(string? colorStr)
        {
            if (string.IsNullOrEmpty(colorStr) || colorStr == "none") return SKColors.Transparent;
            if (colorStr.StartsWith("rgb"))
            {
                var match = Regex.Match(colorStr, @"rgb\(\s*(\d+),\s*(\d+),\s*(\d+)\s*\)");
                if (match.Success)
                    return new SKColor(byte.Parse(match.Groups[1].Value), byte.Parse(match.Groups[2].Value), byte.Parse(match.Groups[3].Value));
            }
            if (SKColor.TryParse(colorStr, out var color)) return color;
            return null;
        }

        /// <summary>
        /// SVGの値を常にミリメートル(mm)に換算して返すユーティリティ
        /// </summary>
        private static float? ParseSvgToMm(string? val)
        {
            if (string.IsNullOrEmpty(val)) return null;
            
            float factorToMm = 1.0f;
            // 単位があるか確認 (px, mm, cm, in, pt, pc)
            // 単位なしは px (SVGの標準として 96DPI 相当) とみなす
            if (val.EndsWith("mm")) factorToMm = 1.0f;
            else if (val.EndsWith("cm")) factorToMm = 10.0f;
            else if (val.EndsWith("in")) factorToMm = 25.4f;
            else if (val.EndsWith("pt")) factorToMm = 25.4f / 72.0f;
            else if (val.EndsWith("pc")) factorToMm = 25.4f / 6.0f;
            else if (val.EndsWith("px")) factorToMm = 25.4f / 96.0f;
            else factorToMm = 25.4f / 96.0f; // 単位なし = ユーザー単位が 96DPI であると仮定

            string clean = Regex.Replace(val, @"[^\d.-]+", "");
            if (float.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
            {
                return f * factorToMm;
            }
            return null;
        }

        /// <summary>
        /// SVGの単位付き文字列をユーザー単位（内部座標系）に変換する。
        /// mmPerUserUnit: 1ユーザー単位が何ミリメートルに相当するか（スケール）
        /// </summary>
        private static float? ParseSvgToUserUnits(string? val, float mmPerUserUnit)
        {
            if (string.IsNullOrEmpty(val)) return null;
            
            // 単位があるか確認
            bool hasUnit = Regex.IsMatch(val, @"[a-zA-Z]+$");
            
            if (hasUnit)
            {
                // 単位がある場合、一旦ミリメートルに変換し、それを現在のスケールで割ってユーザー単位にする
                float? mm = ParseSvgToMm(val);
                if (mm == null) return null;
                return mm.Value / mmPerUserUnit;
            }
            else
            {
                // 単位がない場合は既にユーザー単位（unit）であるとみなす
                if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return f;
            }
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

