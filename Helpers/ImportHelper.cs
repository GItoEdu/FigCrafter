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
        /// 画像、AI(PDF互換)、PDF、EMFファイルを読み込み、SKBitmapとして返す
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
                else
                {
                    // 標準の画像読込
                    using var stream = File.OpenRead(filePath);
                    return SKBitmap.Decode(stream);
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
    }
}
