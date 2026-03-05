using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FigCrafterApp.Models;
using SkiaSharp;
using System.Linq;

namespace FigCrafterApp.Helpers
{
    public static class EmfParser
    {
        // EMF Record Types
        private const int EMR_POLYLINE = 4;
        private const int EMR_POLYGON = 3;
        private const int EMR_RECTANGLE = 43;
        private const int EMR_ELLIPSE = 42;
        private const int EMR_POLYPOLYGON = 8;
        private const int EMR_EXTTEXTOUTW = 84;
        private const int EMR_CREATEPEN = 38;
        private const int EMR_CREATEBRUSHINDIRECT = 39;
        private const int EMR_SELECTOBJECT = 37;

        private class GdiObject
        {
            public SKColor Color { get; set; } = SKColors.Black;
            public float Width { get; set; } = 1.0f;
            public bool IsSolid { get; set; } = true;
        }

        public static GroupObject? ParseEmf(string filePath)
        {
            try
            {
                using var metafile = new Metafile(filePath);
                var header = metafile.GetMetafileHeader();
                
                // EMFの論理単位からミリメートルへの変換係数を計算
                // Boundsはデバイス単位（通常ピクセル）でのサイズ
                // 1/100mm 単位のサイズもヘッダーから取得可能
                float emfWidthMm = header.Bounds.Width * (25.4f / header.DpiX);
                float emfHeightMm = header.Bounds.Height * (25.4f / header.DpiY);

                var group = new GroupObject
                {
                    X = 0, Y = 0,
                    Width = emfWidthMm, Height = emfHeightMm
                };

                var gdiObjects = new Dictionary<int, GdiObject>();
                SKColor currentPenColor = SKColors.Black;
                float currentPenWidth = 1.0f;
                SKColor currentBrushColor = SKColors.Transparent;

                using (var bmp = new Bitmap(1, 1))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.EnumerateMetafile(metafile, new PointF(0, 0), (recordType, flags, dataSize, data, callbackData) =>
                    {
                        byte[] recordData = new byte[dataSize];
                        if (dataSize > 0) Marshal.Copy(data, recordData, 0, dataSize);

                        int type = (int)recordType;

                        switch (type)
                        {
                            case EMR_CREATEPEN:
                                {
                                    int index = flags & 0xFFFF; // 実際にはインデックスは別の場所だが簡易化
                                    // 構造体: Style(4), Width(8: X,Y), Color(4)
                                    if (dataSize >= 16)
                                    {
                                        int colorRef = BitConverter.ToInt32(recordData, 12);
                                        var color = ColorTranslator.FromWin32(colorRef);
                                        int penIdx = BitConverter.ToInt32(recordData, 0); // 実際にはSelectで指定されるID
                                        // 簡易的な実装: 直近の作成を保持
                                        currentPenColor = new SKColor(color.R, color.G, color.B, color.A);
                                        currentPenWidth = BitConverter.ToInt32(recordData, 4);
                                    }
                                }
                                break;
                            case EMR_CREATEBRUSHINDIRECT:
                                {
                                    if (dataSize >= 12)
                                    {
                                        int colorRef = BitConverter.ToInt32(recordData, 4);
                                        var color = ColorTranslator.FromWin32(colorRef);
                                        currentBrushColor = new SKColor(color.R, color.G, color.B, color.A);
                                    }
                                }
                                break;
                            case EMR_RECTANGLE:
                                if (dataSize >= 16)
                                {
                                    var rect = ParseRect(recordData, 0);
                                    var obj = new RectangleObject
                                    {
                                        X = rect.Left * (emfWidthMm / header.Bounds.Width),
                                        Y = rect.Top * (emfHeightMm / header.Bounds.Height),
                                        Width = rect.Width * (emfWidthMm / header.Bounds.Width),
                                        Height = rect.Height * (emfHeightMm / header.Bounds.Height),
                                        FillColor = currentBrushColor,
                                        StrokeColor = currentPenColor,
                                        StrokeWidth = Math.Max(0.5f, currentPenWidth * (emfWidthMm / header.Bounds.Width))
                                    };
                                    group.Children.Add(obj);
                                }
                                break;
                            case EMR_ELLIPSE:
                                if (dataSize >= 16)
                                {
                                    var rect = ParseRect(recordData, 0);
                                    var obj = new EllipseObject
                                    {
                                        X = rect.Left * (emfWidthMm / header.Bounds.Width),
                                        Y = rect.Top * (emfHeightMm / header.Bounds.Height),
                                        Width = rect.Width * (emfWidthMm / header.Bounds.Width),
                                        Height = rect.Height * (emfHeightMm / header.Bounds.Height),
                                        FillColor = currentBrushColor,
                                        StrokeColor = currentPenColor,
                                        StrokeWidth = Math.Max(0.5f, currentPenWidth * (emfWidthMm / header.Bounds.Width))
                                    };
                                    group.Children.Add(obj);
                                }
                                break;
                            case EMR_POLYLINE:
                            case EMR_POLYGON:
                                if (dataSize >= 24)
                                {
                                    // Bounds(16), Count(4), Points(Count * 8)
                                    int count = BitConverter.ToInt32(recordData, 16);
                                    var points = new List<SKPoint>();
                                    for (int i = 0; i < count; i++)
                                    {
                                        float px = BitConverter.ToInt32(recordData, 20 + i * 8);
                                        float py = BitConverter.ToInt32(recordData, 24 + i * 8);
                                        points.Add(new SKPoint(
                                            px * (emfWidthMm / header.Bounds.Width),
                                            py * (emfHeightMm / header.Bounds.Height)
                                        ));
                                    }

                                    if (points.Count >= 2)
                                    {
                                        if (type == EMR_POLYLINE && points.Count == 2)
                                        {
                                            var line = new LineObject
                                            {
                                                X = points[0].X, Y = points[0].Y,
                                                EndX = points[1].X, EndY = points[1].Y,
                                                StrokeColor = currentPenColor,
                                                StrokeWidth = Math.Max(0.5f, currentPenWidth * (emfWidthMm / header.Bounds.Width))
                                            };
                                            group.Children.Add(line);
                                        }
                                        else
                                        {
                                            // PathObject を利用
                                            var path = new SKPath();
                                            path.MoveTo(points[0]);
                                            for (int i = 1; i < points.Count; i++) path.LineTo(points[i]);
                                            if (type == EMR_POLYGON) path.Close();

                                            var pathObj = new PathObject
                                            {
                                                PathData = path.ToSvgPathData(),
                                                FillColor = type == EMR_POLYGON ? currentBrushColor : SKColors.Transparent,
                                                StrokeColor = currentPenColor,
                                                StrokeWidth = Math.Max(0.5f, currentPenWidth * (emfWidthMm / header.Bounds.Width))
                                            };
                                            // バウンディングボックスを設定
                                            var tightBounds = path.Bounds;
                                            pathObj.X = tightBounds.Left;
                                            pathObj.Y = tightBounds.Top;
                                            pathObj.Width = tightBounds.Width;
                                            pathObj.Height = tightBounds.Height;
                                            
                                            group.Children.Add(pathObj);
                                        }
                                    }
                                }
                                break;
                        }
                        return true;
                    });
                }

                if (group.Children.Count > 0)
                {
                    group.RecalculateBounds();
                    return group;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EMF Parse Error: {ex.Message}");
            }

            return null;
        }

        private static Rectangle ParseRect(byte[] data, int offset)
        {
            int left = BitConverter.ToInt32(data, offset);
            int top = BitConverter.ToInt32(data, offset + 4);
            int right = BitConverter.ToInt32(data, offset + 8);
            int bottom = BitConverter.ToInt32(data, offset + 12);
            return new Rectangle(left, top, right - left, bottom - top);
        }
    }
}
