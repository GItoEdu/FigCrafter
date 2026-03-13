using System;
using System.IO;
using System.Threading.Tasks;
using SkiaSharp;
using Windows.Storage;
using Windows.Data.Pdf;

namespace FigCrafterApp.Helpers
{
    public static class ImportHelper
    {
        /// <summary>
        /// 画像、Adobe Illustrator（*.ai; PDF互換）、PDF、EMFファイルを読み込み、SKBitmapとして返す
        /// </summary>
        public static async Task<SKBitmap?> ImportFileAsync(string filePath)
        {
            try
            {
                string ext = Path.GetExtension(filePath).ToLower();
                if (ext == ".ai" || ext == ".pdf")
                {
                    return await ImportPdfOrAiAsync(filePath);
                }
                else if (ext == ".emf" || ext == ".wmf")
                {
                    return ImportMetafile(filePath);
                }
                else if (ext == ".tif" || ext == ".tiff")
                {
                    return ImportTiffImage(filePath);
                }
                else
                {
                    // 標準の画像読込
                    using var data = SKData.Create(filePath);
                    if (data == null) return null;
                    return SKBitmap.Decode(data);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Import error: {ex.Message}");
                return null;
            }
        }

        private static async Task<SKBitmap?> ImportPdfOrAiAsync(string filePath)
        {
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(filePath));
            var pdfDoc = await PdfDocument.LoadFromFileAsync(file);
            if (pdfDoc.PageCount == 0) return null;

            using var page = pdfDoc.GetPage(0);
            
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var options = new PdfPageRenderOptions
            {
                // 高解像度でレンダリング (2倍スケール等)
                DestinationWidth = (uint)(page.Size.Width * 2), 
                BackgroundColor = Windows.UI.Color.FromArgb(0, 255, 255, 255) // 透明背景
            };
            
            await page.RenderToStreamAsync(stream, options);
            
            using var netStream = stream.AsStreamForRead();
            netStream.Position = 0;
            return SKBitmap.Decode(netStream);
        }

        private static SKBitmap? ImportMetafile(string filePath)
        {
#pragma warning disable CA1416 // プラットフォーム互換性の検証
            using var metafile = new System.Drawing.Imaging.Metafile(filePath);
            
            // 解像度やサイズを調整。一旦メタファイルの元サイズを利用
            int width = metafile.Width;
            int height = metafile.Height;

            // EMF/WMFによってはWidth/Heightが非常に小さい場合があるので最低限のサイズを確保
            if (width < 10) width = 800;
            if (height < 10) height = 600;

            using var bmp = new System.Drawing.Bitmap(width, height);
            using var g = System.Drawing.Graphics.FromImage(bmp);
            g.Clear(System.Drawing.Color.Transparent);
            g.DrawImage(metafile, 0, 0, width, height);

            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;
            return SKBitmap.Decode(ms);
#pragma warning restore CA1416
        }

        private static SKBitmap? ImportTiffImage(string filePath)
        {
            try
            {
                using var data = SKData.Create(filePath);
                if (data == null) throw new Exception("Failed to load TIFF data");
                var bitmap = SKBitmap.Decode(data);
                if (bitmap == null) throw new Exception("Failed to decode TIFF");
                return bitmap;
            }
            catch
            {
                // フォールバック: System.Drawing (ただし8bit化する)
#pragma warning disable CA1416
                using var bmp = new System.Drawing.Bitmap(filePath);
                using var ms = new MemoryStream();
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;
                return SKBitmap.Decode(ms);
#pragma warning restore CA1416
            }
        }

        /// <summary>
        /// 画像ファイルから水平・垂直解像度(DPI)を取得します。取得失敗時はデフォルトで (96f, 96f) を返します。
        /// </summary>
        public static (float dpiX, float dpiY) GetImageDpi(string filePath)
        {
            try
            {
                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                if (ext == ".ai" || ext == ".pdf")
                {
                    // PDF/AIの場合はレンダリング時に拡大しているため、高解像度扱いにするか標準を返す
                    return (96f, 96f);
                }

#pragma warning disable CA1416 // プラットフォーム互換性の検証
                using var bmp = new System.Drawing.Bitmap(filePath);
                float x = bmp.HorizontalResolution;
                float y = bmp.VerticalResolution;
                
                // あまりに異常な値(0以下など)の場合は96にフォールバック
                if (x <= 0) x = 96f;
                if (y <= 0) y = 96f;
                
                return (x, y);
#pragma warning restore CA1416
            }
            catch
            {
                return (96f, 96f);
            }
        }
    }
}
