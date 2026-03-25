using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using SkiaSharp;
using System.Text.RegularExpressions;
using System.Globalization;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Graphics.Operations;
using UglyToad.PdfPig.Graphics.Operations.PathConstruction;
using UglyToad.PdfPig.Graphics.Operations.PathPainting;
using UglyToad.PdfPig.Graphics.Colors;
using FigCrafterApp.Models;
using System.Drawing.Drawing2D;
using OpenTK.Graphics.GL;
using System.Security.RightsManagement;
using iText.Layout.Element;

namespace FigCrafterApp.Helpers
{
    public static class VectorFileParser
    {
        private const float PxToMm = 25.4f / 96f; // Inkscape (96DPI) -> FigCrafter (mm)
        private const float PtToMm = 25.4f / 72f; // PDF (72DPI/pt) -> FigCrafter (mm)
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

                // DEBUG
                // Debug.WriteLine($"Temp SVG: {tempSvg}");
                // string svgContent = File.ReadAllText(tempSvg);
                // Debug.WriteLine("=== SVG CONTENT START ===");
                // Debug.WriteLine(svgContent);
                // Debug.WriteLine("=== SVG CONTENT END ===");

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
                var styleDict = ParseStyles(root, ns);
                ProcessElement(root, SKMatrix.CreateScale(scale, scale), group, ns, scale, SvgStyle.Default, styleDict);

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

        /// <summary>
        /// PDF互換の.aiファイルまたは.pdfファイルを読み込み、GraphicObjectのリストに変換する
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public static List<GraphicObject> ParsePdfFile(string filePath)
        {
            var parsedObjects = new List<GraphicObject>();

            try
            {
                // PdfPigでドキュメントを開く
                using var document = PdfDocument.Open(filePath);
                if (document.NumberOfPages == 0) return parsedObjects;

                // 1ページ目のみを対象とする
                var page = document.GetPage(1);
                decimal pageHeight = (decimal)page.Height;

                // ラスタ画像読み込み
                foreach (var image in page.GetImages())
                {
                    var bitmap = ConvertPdfImageToSKBitmap(image);
                    if (bitmap != null)
                    {
                        var imgObj = new ImageObject
                        {
                            ImageData = bitmap,
                            X = (float)image.Bounds.Left * PtToMm,
                            Y = ((float)pageHeight - (float)image.Bounds.Top) * PtToMm,
                            Width = (float)image.Bounds.Width * PtToMm,
                            Height = (float)image.Bounds.Height * PtToMm,
                            Opacity = 1.0f,
                            StrokeWidth = 0f,
                            StrokeColor = SKColors.Transparent
                        };
                        parsedObjects.Add(imgObj);
                    }
                }

                // テキスト読み込み
                foreach (var word in page.GetWords())
                {
                    var firstLetter = word.Letters.FirstOrDefault();
                    var lastLetter = word.Letters.LastOrDefault();
                    if (firstLetter == null || lastLetter == null) continue;

                    // --- 回転角度と正確なサイズの計算 ---
                    double angleDx = firstLetter.EndBaseLine.X - firstLetter.StartBaseLine.X;
                    double angleDy = firstLetter.EndBaseLine.Y - firstLetter.StartBaseLine.Y;
                    double angleRad = Math.Atan2(angleDy, angleDx);
                    
                    float rotation = (float)(-angleRad * 180.0 / Math.PI);

                    // 単語全体の幅と高さを計算
                    double wordDx = lastLetter.EndBaseLine.X - firstLetter.StartBaseLine.X;
                    double wordDy = lastLetter.EndBaseLine.Y - firstLetter.StartBaseLine.Y;
                    float widthPt = (float)Math.Sqrt(wordDx * wordDx + wordDy * wordDy);
                    float heightPt = (float)firstLetter.PointSize;

                    // 回転を考慮した本来の左上座標(TopLeft)の計算
                    double upDx = -Math.Sin(angleRad) * heightPt;
                    double upDy = Math.Cos(angleRad) * heightPt;
                    double topLeftPdfX = firstLetter.StartBaseLine.X + upDx;
                    double topLeftPdfY = firstLetter.StartBaseLine.Y + upDy;

                    string rawFontName = firstLetter.FontName ?? "Arial";
                    
                    bool isBold = rawFontName.Contains("Bold", StringComparison.OrdinalIgnoreCase) 
                               || rawFontName.Contains("Black", StringComparison.OrdinalIgnoreCase);
                    bool isItalic = rawFontName.Contains("Italic", StringComparison.OrdinalIgnoreCase) 
                                 || rawFontName.Contains("Oblique", StringComparison.OrdinalIgnoreCase);

                    string cleanFontName = rawFontName;
                    int plusIndex = cleanFontName.IndexOf('+');
                    if (plusIndex >= 0 && plusIndex < cleanFontName.Length - 1) cleanFontName = cleanFontName.Substring(plusIndex + 1);
                    int hyphenIndex = cleanFontName.IndexOf('-');
                    if (hyphenIndex >= 0) cleanFontName = cleanFontName.Substring(0, hyphenIndex);
                    
                    if (cleanFontName.EndsWith("PSMT", StringComparison.OrdinalIgnoreCase)) cleanFontName = cleanFontName.Substring(0, cleanFontName.Length - 4);
                    else if (cleanFontName.EndsWith("MT", StringComparison.OrdinalIgnoreCase) || cleanFontName.EndsWith("PS", StringComparison.OrdinalIgnoreCase)) cleanFontName = cleanFontName.Substring(0, cleanFontName.Length - 2);

                    var textObj = new TextObject
                    {
                        Text = word.Text,
                        X = (float)topLeftPdfX * PtToMm,
                        Y = ((float)pageHeight - (float)topLeftPdfY) * PtToMm,
                        Width = widthPt * PtToMm,
                        Height = heightPt * PtToMm,
                        Rotation = rotation,
                        
                        FontSize = heightPt * PtToMm,
                        FontFamily = cleanFontName,
                        IsBold = isBold,
                        IsItalic = isItalic,
                        FillColor = SKColors.Black,
                        StrokeColor = SKColors.Transparent,
                        StrokeWidth = 0f,
                        Opacity = 1.0f
                    };

                    parsedObjects.Add(textObj);
                }

                // 現在構築中のパスデータ
                var currentNodes = new List<PathNode>();

                // 現在のグラフィックス状態
                SKColor currentFillColor = SKColors.Black;
                SKColor currentStrokeColor = SKColors.Black;
                float currentStrokeWidth = 0.5f * PtToMm;

                var stateStack = new Stack<GraphicsState>();
                var currentMatrix = SKMatrix.CreateIdentity();

                // ページ内の全描画オペレーションを順番に処理
                foreach (var operation in page.Operations)
                {
                    try
                    {
                        string opName = operation.GetType().Name; 
                        string opCode = operation.Operator; // 生コマンド
                        string opString = operation.ToString() ?? "";

                        // グラフィックスステートの管理
                        if (opCode == "q") // PushGraphicsState
                        {
                            stateStack.Push(new GraphicsState
                            {
                               Matrix = currentMatrix,
                               FillColor = currentFillColor,
                               StrokeColor = currentStrokeColor,
                               StrokeWidth = currentStrokeWidth 
                            });
                        }
                        else if (opCode == "Q") // PopGraphicsState
                        {
                            // スタックから復元
                            if (stateStack.Count > 0)
                            {
                                var state = stateStack.Pop();
                                currentMatrix = state.Matrix;
                                currentFillColor = state.FillColor;
                                currentStrokeColor = state.StrokeColor;
                                currentStrokeWidth = state.StrokeWidth;
                            }
                        }
                        else if (opName == "ModifyCurrentTransformationMatrix")
                        {
                            dynamic cm = operation;
                            float a = Convert.ToSingle(cm.Value[0]);
                            float b = Convert.ToSingle(cm.Value[1]);
                            float c = Convert.ToSingle(cm.Value[2]);
                            float d = Convert.ToSingle(cm.Value[3]);
                            float tx = Convert.ToSingle(cm.Value[4]);
                            float ty = Convert.ToSingle(cm.Value[5]);

                            var newMat = new SKMatrix(a, c, tx, b, d, ty, 0, 0, 1);
                            currentMatrix = SKMatrix.Concat(currentMatrix, newMat);
                        }

                        // パス構築オペレーション
                        // 四角形
                        else if (opName == "AppendRectangle")
                        {
                            var matches = Regex.Matches(opString, @"[-+]?[0-9]*\.?[0-9]+");
                            if (matches.Count >= 4)
                            {
                                float x = float.Parse(matches[0].Value, CultureInfo.InvariantCulture);
                                float y = float.Parse(matches[1].Value, CultureInfo.InvariantCulture);
                                float w = float.Parse(matches[2].Value, CultureInfo.InvariantCulture);
                                float h = float.Parse(matches[3].Value, CultureInfo.InvariantCulture);

                                var p1 = currentMatrix.MapPoint(new SKPoint(x, y));
                                var p2 = currentMatrix.MapPoint(new SKPoint(x + w, y));
                                var p3 = currentMatrix.MapPoint(new SKPoint(x + w, y + h));
                                var p4 = currentMatrix.MapPoint(new SKPoint(x, y + h));

                                currentNodes.Add(new PathNode
                                {
                                    NodeType = PathNodeType.Move,
                                    X = p1.X * PtToMm,
                                    Y = ((float)pageHeight - p1.Y) * PtToMm
                                });
                                currentNodes.Add(new PathNode
                                {
                                    NodeType = PathNodeType.Line,
                                    X = p2.X * PtToMm,
                                    Y = ((float)pageHeight - p2.Y) * PtToMm    
                                });
                                currentNodes.Add(new PathNode
                                {
                                    NodeType = PathNodeType.Line,
                                    X = p3.X * PtToMm,
                                    Y = ((float)pageHeight - p3.Y) * PtToMm    
                                });
                                currentNodes.Add(new PathNode
                                {
                                    NodeType = PathNodeType.Line,
                                    X = p4.X * PtToMm,
                                    Y = ((float)pageHeight - p4.Y) * PtToMm    
                                });
                                currentNodes.Add(new PathNode
                                {
                                    NodeType = PathNodeType.Line,
                                    X = p1.X * PtToMm,
                                    Y = ((float)pageHeight - p1.Y) * PtToMm    
                                });
                            }
                        }
                        // 移動
                        else if (operation is BeginNewSubpath move)
                        {
                            var p = currentMatrix.MapPoint(new SKPoint((float)move.X, (float)move.Y));
                            currentNodes.Add(new PathNode
                            {
                                NodeType = PathNodeType.Move,
                                X = p.X * PtToMm,
                                Y = ((float)pageHeight - p.Y) * PtToMm
                            });
                        }
                        // 直線
                        else if (operation is AppendStraightLineSegment line)
                        {
                            var p = currentMatrix.MapPoint(new SKPoint((float)line.X, (float)line.Y));
                            currentNodes.Add(new PathNode
                            {
                            NodeType = PathNodeType.Line,
                            X = p.X * PtToMm,
                            Y = ((float)pageHeight - p.Y) * PtToMm
                            });
                        }
                        // ベジェ曲線
                        else if (operation is AppendDualControlPointBezierCurve cubic)
                        {
                            var cp1 = currentMatrix.MapPoint(new SKPoint((float)cubic.X1, (float)cubic.Y1));
                            var cp2 = currentMatrix.MapPoint(new SKPoint((float)cubic.X2, (float)cubic.Y2));
                            var p3 = currentMatrix.MapPoint(new SKPoint((float)cubic.X3, (float)cubic.Y3));

                            currentNodes.Add(new PathNode
                            {
                                NodeType = PathNodeType.Bezier,
                                Control1X = cp1.X * PtToMm,
                                Control1Y = ((float)pageHeight - cp1.Y) * PtToMm,
                                Control2X = cp2.X * PtToMm,
                                Control2Y = ((float)pageHeight - cp2.Y) * PtToMm,
                                X = p3.X * PtToMm,
                                Y = ((float)pageHeight - p3.Y) * PtToMm
                            });
                        }
                        else if (operation is UglyToad.PdfPig.Graphics.Operations.PathConstruction.CloseSubpath)
                        {
                            // パスを閉じる命令（h）が来たら、後でIsClosedをtrueにするフラグとして扱う
                            // ここでは特別にノードとしては追加せず、描画コマンド実行時に判定します
                        }
                        // 色と線の設定オペレーション
                        else if (opName == "SetLineWidth")
                        {
                            var matches = Regex.Matches(opString, @"[-+]?[0-9]*\.?[0-9]+");
                            if (matches.Count >= 1)
                            {
                                currentStrokeWidth = float.Parse(matches[0].Value, CultureInfo.InvariantCulture) * PtToMm;
                            }
                        }
                        else if (opCode == "rg" || opCode == "RG" || opCode == "k" || opCode == "K" || opCode == "g" || opCode == "G" || opCode == "scn" || opCode == "SCN")
                        {
                            var matches = Regex.Matches(opString, @"[-+]?[0-9]*\.?[0-9]+");
                            if (matches.Count >= 1)
                            {
                                float r = 0, g = 0, b = 0;
                                if (matches.Count == 1) // グレースケール (g, G)
                                {
                                    r = g = b = float.Parse(matches[0].Value, CultureInfo.InvariantCulture);
                                }
                                else if (matches.Count >= 4) // CMYK (k, K) をRGBに簡易変換
                                {
                                    float c = float.Parse(matches[0].Value, CultureInfo.InvariantCulture);
                                    float m = float.Parse(matches[1].Value, CultureInfo.InvariantCulture);
                                    float y = float.Parse(matches[2].Value, CultureInfo.InvariantCulture);
                                    float k = float.Parse(matches[3].Value, CultureInfo.InvariantCulture);
                                    r = 1f - Math.Min(1f, c * (1f - k) + k);
                                    g = 1f - Math.Min(1f, m * (1f - k) + k);
                                    b = 1f - Math.Min(1f, y * (1f - k) + k);
                                }
                                else if (matches.Count >= 3) // RGB (rg, RG, scn, SCN)
                                {
                                    r = float.Parse(matches[0].Value, CultureInfo.InvariantCulture);
                                    g = float.Parse(matches[1].Value, CultureInfo.InvariantCulture);
                                    b = float.Parse(matches[2].Value, CultureInfo.InvariantCulture);
                                }

                                byte rB = (byte)Math.Clamp(r * 255f, 0, 255);
                                byte gB = (byte)Math.Clamp(g * 255f, 0, 255);
                                byte bB = (byte)Math.Clamp(b * 255f, 0, 255);
                                var color = new SKColor(rB, gB, bB);

                                // 最初の文字が小文字なら塗り(Fill)、大文字なら線(Stroke)
                                if (char.IsLower(opCode[0])) currentFillColor = color;
                                else currentStrokeColor = color;
                            }
                        }

                        // 描画オペレーション
                        else
                        {
                            bool isStroke = opName.Contains("Stroke");
                            bool isFill = opName.Contains("Fill");
                            bool isClose = opName.Contains("Close");
                            bool isEvenOdd = opName.Contains("EvenOdd");

                            if (isStroke || isFill)
                            {
                                if (currentNodes.Count > 0)
                                {
                                    var bezierObj = new BezierObject
                                    {
                                        Nodes = new ObservableCollection<PathNode>(currentNodes),
                                        StrokeWidth = currentStrokeWidth,
                                        FillColor = isFill ? currentFillColor : SKColors.Transparent,
                                        StrokeColor = isStroke ? currentStrokeColor : SKColors.Transparent,
                                        IsClosed = isClose,
                                        FillType = isEvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding
                                    };
                                    
                                    FinalizeBezierObject(bezierObj);
                                    parsedObjects.Add(bezierObj);                             
                                    currentNodes.Clear();
                                }
                            }
                            else if (opName == "EndPath" || opCode == "n")
                            {
                                currentNodes.Clear();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Operation Parse Error ({operation.GetType().Name}): {ex.Message}");
                    }
                }                
            }
            catch (Exception ex)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() => 
                {
                    System.Windows.MessageBox.Show($"PDF全体解析エラー: {ex.Message}", "エラー");
                });
            }

            return parsedObjects;
        }
       
        private static void FinalizeBezierObject(BezierObject obj)
        {
            if (obj.Nodes.Count == 0) return;

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            // バウンディングボックスの計算
            foreach (var node in obj.Nodes)
            {
                if (node.X < minX) minX = node.X;
                if (node.Y < minY) minY = node.Y;
                if (node.X > maxX) maxX = node.X;
                if (node.Y > maxY) maxY = node.Y;
            }

            obj.X = minX;
            obj.Y = minY;
            obj.Width = maxX - minX;
            obj.Height = maxY - minY;

            // 各ノードを相対座標に変換
            foreach (var node in obj.Nodes)
            {
                node.X -= minX;
                node.Y -= minY;

                if (node.NodeType == PathNodeType.Bezier)
                {
                    node.Control1X -= minX;
                    node.Control1Y -= minY;
                    node.Control2X -= minX;
                    node.Control2Y -= minY;
                }
            }
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
                        double horizontalThreshold = current.FontSize * 5.0;

                        if (Math.Abs(distY) < verticalThreshold && Math.Abs(distX) < horizontalThreshold)
                        {
                            using SKTypeface typeface = SKTypeface.FromFamilyName(target.FontFamily);
                            using SKFont font = new SKFont(typeface, target.FontSize);

                            double spaceThreshold = current.FontSize * 0.25;

                            if (distX > 0)
                            {
                                float targetWidth = font.MeasureText(target.Text);
                                double gap = distX - targetWidth;

                                if (gap > spaceThreshold && !target.Text.EndsWith(" ") && !current.Text.StartsWith(" "))
                                {
                                    target.Text += " ";
                                }
                                target.Text += current.Text;
                            }
                            else
                            {
                                float currentWidth = font.MeasureText(current.Text);
                                double gap = Math.Abs(distX) - currentWidth;

                                string prefix = "";
                                if (gap > spaceThreshold && !current.Text.EndsWith(" ") && !target.Text.StartsWith(" "))
                                {
                                    prefix = " ";
                                }
                                target.Text = current.Text + prefix + target.Text;
                                target.X = current.X;
                                target.Y = current.Y;
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

        private struct GraphicsState
        {
            public SKMatrix Matrix;
            public SKColor FillColor;
            public SKColor StrokeColor;
            public float StrokeWidth;
        }

        private static void ProcessElement(XElement element, SKMatrix parentMatrix, GroupObject targetGroup, XNamespace ns, float scale, SvgStyle parentStyle, Dictionary<string, string> styleDict)
        {
            // ローカルの transform を取得し、親の行列と結合
            string transformAttr = element.Attribute("transform")?.Value ?? "";
            SKMatrix localMatrix = string.IsNullOrEmpty(transformAttr) ? SKMatrix.CreateIdentity() : ParseTransform(transformAttr);
            SKMatrix currentMatrix = SKMatrix.Concat(parentMatrix, localMatrix);

            // スタイルの継承と解決
            var currentStyle = parentStyle;
            
            string fillStr = GetAttributeOrStyle(element, "fill", styleDict);
            if (!string.IsNullOrEmpty(fillStr)) currentStyle.Fill = ParseColor(fillStr);

            string strokeStr = GetAttributeOrStyle(element, "stroke", styleDict);
            if (!string.IsNullOrEmpty(strokeStr)) currentStyle.Stroke = ParseColor(strokeStr);

            string strokeWidthStr = GetAttributeOrStyle(element, "stroke-width", styleDict);
            if (!string.IsNullOrEmpty(strokeWidthStr)) currentStyle.StrokeWidth = ParseSvgToUserUnits(strokeWidthStr, scale);

            string opacityStr = GetAttributeOrStyle(element, "opacity", styleDict);
            if (!string.IsNullOrEmpty(opacityStr)) currentStyle.Opacity = ParseFloat(opacityStr);

            string d = "";
            if (element.Name == ns + "path") d = element.Attribute("d")?.Value ?? "";
            else if (element.Name == ns + "rect") d = ConvertRectToPath(element);
            else if (element.Name == ns + "circle" || element.Name == ns + "ellipse") d = ConvertEllipseToPath(element);
            else if (element.Name == ns + "line") d = ConvertLineToPath(element);
            else if (element.Name == ns + "polyline" || element.Name == ns + "polygon") d = ConvertPolyToPath(element);
            else if (element.Name == ns + "text" || element.Name == ns + "tspan")
            {
                ProcessTextElement(element, currentMatrix, targetGroup, currentStyle, scale, styleDict);
            }
            else if (element.Name == ns + "image")
            {
                ProcessImageElement(element, currentMatrix, targetGroup, currentStyle, styleDict);
            }

            if (!string.IsNullOrEmpty(d))
            {
                var pathObj = new PathObject
                {
                    StrokeColor = currentStyle.Stroke ?? SKColors.Transparent,
                    FillColor = currentStyle.Fill ?? SKColors.Transparent,
                    // インポートしたパスの線幅は0.5ptに統一する
                    StrokeWidth = 0.5f,
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
                ProcessElement(child, currentMatrix, targetGroup, ns, scale, currentStyle, styleDict);
            }
        }

        private static void ProcessTextElement(XElement el, SKMatrix matrix, GroupObject targetGroup, SvgStyle style, float scale, Dictionary<string, string> styleDict)
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
            float fontSize = ParseSvgToUserUnits(GetAttributeOrStyle(el, "font-size", styleDict), scale) ?? 12f;
            string fontFamily = GetAttributeOrStyle(el, "font-family", styleDict) ?? "Arial";
            string fontWeight = GetAttributeOrStyle(el, "font-weight", styleDict) ?? "";
            string fontStyleStr = GetAttributeOrStyle(el, "font-style", styleDict) ?? "";

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
                Rotation = rotation,
                StrokeWidth = 0,
                StrokeColor = SKColors.Transparent
            };

            // 行列変換前でY座標をベースラインから上端へシフトする
            float localY = y - (fontSize * 0.8f);
            var p = matrix.MapPoint(x, localY);

            textObj.X = p.X;
            textObj.Y = p.Y;

            targetGroup.Children.Add(textObj);
        }

        private static void ProcessImageElement(XElement el, SKMatrix matrix, GroupObject targetGroup, SvgStyle style, Dictionary<string, string> styleDict)
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

        private static string GetAttributeOrStyle(XElement element, string name, Dictionary<string, string> styleDict)
        {
            string? attr = element.Attribute(name)?.Value;
            if (attr != null) return attr;

            string? style = element.Attribute("style")?.Value;
            if (style != null)
            {
                var properties = style.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var prop in properties)
                {
                    var kv = prop.Split(new[] { ':' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (kv.Length == 2 && kv[0].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        return kv[1].Trim();
                    }
                }
            }

            string? cls = element.Attribute("class")?.Value;
            if (cls != null)
            {
                var classes = cls.Split(new[] { ' '}, StringSplitOptions.RemoveEmptyEntries);
                foreach (var c in classes)
                {
                    if (styleDict.TryGetValue(c, out var classStyle))
                    {
                        // プロパティ群をセミコロンで分割
                        var properties = classStyle.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var prop in properties)
                        {
                            var kv = prop.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
                            if (kv.Length == 2 && kv[0].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                            {
                                return kv[1].Trim();
                            }
                        }
                    }
                }
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

        // SVG内の<style>タグからCSSクラスの辞書を生成するメソッド
        private static Dictionary<string, string> ParseStyles(XElement root, XNamespace ns)
        {
            var dict = new Dictionary<string, string>();
            foreach (var styleNode in root.Descendants(ns + "style"))
            {
                var css = styleNode.Value;
                // セレクタとプロパティを抽出
                var matches = Regex.Matches(css, @"([^{]+)\s*\{\s*([^}]+)\s*\}");
                foreach (Match m in matches)
                {
                    string selectors = m.Groups[1].Value;
                    string properties = m.Groups[2].Value;
                    var classes = Regex.Matches(selectors, @"\.([a-zA-Z0-9_-]+)");
                    foreach (Match cm in classes)
                    {
                        dict[cm.Groups[1].Value] = properties;
                    }
                }
            }
            return dict;
        }

        /// <summary>
        /// PDF/AIファイルのオペレーションを解析し、テキストファイルにダンプ出力するデバッグ用メソッド
        /// </summary>
        public static void DumpPdfOperations(string filePath)
        {
            try
            {
                using var document = PdfDocument.Open(filePath);
                if (document.NumberOfPages == 0) return;

                var page = document.GetPage(1);

                // 出力先のテキストファイル（元のファイル名の末尾に _dump.txt を付ける）
                string dumpFilePath = filePath + "_dump.txt";
                using var writer = new System.IO.StreamWriter(dumpFilePath);

                writer.WriteLine($"=== Operations Dump for {System.IO.Path.GetFileName(filePath)} ===");
                writer.WriteLine($"Page Size: {page.Width} x {page.Height}");
                writer.WriteLine("====================================================================\n");

                foreach (var operation in page.Operations)
                {
                    // PDFの生コマンド(Operator)、PdfPigのクラス名、内容(ToString)を書き出す
                    string opCode = operation.Operator;
                    string className = operation.GetType().Name;
                    string? rawData = operation.ToString();

                    writer.WriteLine($"[{opCode}] {className}");
                    writer.WriteLine($"    Data: {rawData}");
                    
                    // 値の構造をより深く知るため、dynamicでプロパティや配列の型を調べる（オプション）
                    try
                    {
                        dynamic dynOp = operation;
                        if (opCode == "cm" || opCode == "w" || opCode.Contains("rg") || opCode.Contains("k"))
                        {
                            writer.WriteLine($"    Value Type: {dynOp.Value?.GetType().Name}");
                        }
                    }
                    catch { /* 読み取れないプロパティは無視 */ }

                    writer.WriteLine("--------------------------------------------------");
                }

                System.Diagnostics.Debug.WriteLine($"ダンプ出力完了: {dumpFilePath}");
                System.Windows.Application.Current.Dispatcher.Invoke(() => 
                {
                    System.Windows.MessageBox.Show($"解析完了！\n{dumpFilePath}\nに出力しました。", "ダンプ成功");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Dump Error: {ex.Message}");
            }
        }

        /// <summary>
        /// PDFファイル内の画像メタデータをダンプ出力するデバッグ用メソッド
        /// </summary>
        /// <param name="filePath">対象のPDFファイルパス</param>
        public static void DumpPdfImageMetadata(string filePath)
        {
            try
            {
                using var document = PdfDocument.Open(filePath);
                System.Diagnostics.Debug.WriteLine($"=== 画像メタデータダンプ開始: {System.IO.Path.GetFileName(filePath)} ===");

                foreach (var page in document.GetPages())
                {
                    System.Diagnostics.Debug.WriteLine($"--- Page {page.Number} ---");
                    
                    // ページ内の画像リソースを取得
                    var images = page.GetImages();
                    int imgIndex = 0;

                    foreach (var image in images)
                    {
                        imgIndex++;
                        System.Diagnostics.Debug.WriteLine($"  [Image {imgIndex}]");

                        // 画像の辞書（メタデータ）を取得
                        var dict = image.ImageDictionary;

                        // 幅と高さ
                        System.Diagnostics.Debug.WriteLine($"    Width: {image.WidthInSamples}, Height: {image.HeightInSamples}");

                        // カラースペース
                        if (dict.TryGet<UglyToad.PdfPig.Tokens.NameToken>(UglyToad.PdfPig.Tokens.NameToken.ColorSpace, out var colorSpaceName))
                        {
                            System.Diagnostics.Debug.WriteLine($"    ColorSpace: {colorSpaceName.Data}");
                        }
                        else if (dict.TryGet(UglyToad.PdfPig.Tokens.NameToken.ColorSpace, out var colorSpaceObj))
                        {
                            // 配列形式などで定義されている場合
                            System.Diagnostics.Debug.WriteLine($"    ColorSpace: {colorSpaceObj}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"    ColorSpace: (Not specified / Inherited)");
                        }

                        // ビット深度
                        if (dict.TryGet<UglyToad.PdfPig.Tokens.NumericToken>(UglyToad.PdfPig.Tokens.NameToken.BitsPerComponent, out var bpc))
                        {
                            System.Diagnostics.Debug.WriteLine($"    BitsPerComponent: {bpc.Int}");
                        }

                        // 圧縮フィルタ
                        if (dict.TryGet<UglyToad.PdfPig.Tokens.NameToken>(UglyToad.PdfPig.Tokens.NameToken.Filter, out var filterName))
                        {
                            System.Diagnostics.Debug.WriteLine($"    Filter: {filterName.Data}");
                        }
                        else if (dict.TryGet(UglyToad.PdfPig.Tokens.NameToken.Filter, out var filterArray))
                        {
                            System.Diagnostics.Debug.WriteLine($"    Filter: {filterArray}");
                        }

                        // マスク（透過情報）の有無
                        // SMask = Soft Mask (アルファチャンネル), Mask = Color Key Mask など
                        bool hasSMask = dict.ContainsKey(UglyToad.PdfPig.Tokens.NameToken.Smask);
                        bool hasMask = dict.ContainsKey(UglyToad.PdfPig.Tokens.NameToken.Mask);
                        System.Diagnostics.Debug.WriteLine($"    Has SMask (Alpha): {hasSMask}");
                        System.Diagnostics.Debug.WriteLine($"    Has Mask: {hasMask}");

                        // 生データのバイト数
                        System.Diagnostics.Debug.WriteLine($"    RawBytes Length: {image.RawBytes.Count} bytes");
                    }
                }
                System.Diagnostics.Debug.WriteLine($"=== 画像メタデータダンプ終了 ===");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DumpPdfImageMetadata Error: {ex.Message}");
            }
        }

        /// <summary>
        /// AI/PDFファイル内のすべてのラスター画像を抽出し、指定したフォルダに保存します。
        /// </summary>
        public static void ExportImagesFromPdf(string filePath, string outputDirectory)
        {
            try
            {
                if (!Directory.Exists(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                using var document = PdfDocument.Open(filePath);
                var page = document.GetPage(1);
                int imgIndex = 0;

                foreach (var image in page.GetImages())
                {
                    imgIndex++;
                    using var bitmap = ConvertPdfImageToSKBitmap(image);

                    if (bitmap != null)
                    {
                        string outPath = Path.Combine(outputDirectory, $"extracted_image_{imgIndex}.png");
                        using var imageStream = File.OpenWrite(outPath);
                        // PNG形式で保存
                        bitmap.Encode(imageStream, SKEncodedImageFormat.Png, 100);
                        System.Diagnostics.Debug.WriteLine($"画像を書き出しました：{outPath}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"画像 {imgIndex} のデコードに失敗しました。");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ExportImages Error：{ex.Message}");
            }
        }

        /// <summary>
        /// IPdfImageの生データをカラースペースに合わせてSKBitmapに変換するヘルパー
        /// </summary>
        /// <param name="image"></param>
        /// <returns></returns>
        public static SKBitmap? ConvertPdfImageToSKBitmap(UglyToad.PdfPig.Content.IPdfImage image)
        {
            // 圧縮解除済みの生ピクセルデータを取得
            if (!image.TryGetBytes(out var decodedBytes)) return null;

            int width = image.WidthInSamples;
            int height = image.HeightInSamples;

            // カラースペースの取得
            string colorSpace = "DeviceRBG"; // デフォルト
            if (image.ImageDictionary.TryGet<UglyToad.PdfPig.Tokens.NameToken>(UglyToad.PdfPig.Tokens.NameToken.ColorSpace, out var cs))
            {
                colorSpace = cs.Data;
            }

            var bitmap = new SKBitmap(width, height);
            var pixels = new SKColor[width * height];
            int byteIndex = 0;

            // ピクセルデータのマッピング
            for (int i = 0; i < pixels.Length; i++)
            {
                if (colorSpace == "DeviceGray" && byteIndex < decodedBytes.Count)
                {
                    // グレースケール：1バイト = 1ピクセル
                    byte g = decodedBytes[byteIndex++];
                    pixels[i] = new SKColor(g, g, g, 255);
                }
                else if (colorSpace == "DeviceCMYK" && byteIndex + 3 < decodedBytes.Count)
                {
                    // CMYK：4バイト = 1ピクセル -> 簡易的にRGBに変換
                    byte c = decodedBytes[byteIndex++];
                    byte m = decodedBytes[byteIndex++];
                    byte y = decodedBytes[byteIndex++];
                    byte k = decodedBytes[byteIndex++];

                    float cF = c / 255f;
                    float mF = m / 255f;
                    float yF = y / 255f;
                    float kF = k / 255f;

                    byte r = (byte)(255 * (1 - cF) * (1 - kF));
                    byte g = (byte)(255 * (1 - mF) * (1 - kF));
                    byte b = (byte)(255 * (1 - yF) * (1 - kF));

                    pixels[i] = new SKColor(r, g, b, 255);
                }
                else if (byteIndex + 2 < decodedBytes.Count)
                {
                    // RGB：3バイト = 1ピクセル
                    byte r = decodedBytes[byteIndex++];
                    byte g = decodedBytes[byteIndex++];
                    byte b = decodedBytes[byteIndex++];
                    pixels[i] = new SKColor(r, g, b, 255);
                }
                else
                {
                    // データ不足時は透明で埋める
                    pixels[i] = SKColors.Transparent;
                }
            }

            bitmap.Pixels = pixels;

            return bitmap;
        }

        /// <summary>
        /// 画像を上下反転させる処理
        /// </summary>
        /// <param name="original"></param>
        /// <returns></returns>
        private static SKBitmap FlipBitmapVertical(SKBitmap original)
        {
            var flipped = new SKBitmap(original.Width, original.Height);
            using (var canvas = new SKCanvas(flipped))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.Scale(1, -1, 0, original.Height / 2.0f);
                canvas.DrawBitmap(original, 0, 0);
            }
            return flipped;
        }
    }
}

