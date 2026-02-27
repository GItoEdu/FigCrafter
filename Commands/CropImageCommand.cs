using System;
using FigCrafterApp.Models;
using SkiaSharp;

namespace FigCrafterApp.Commands
{
    public class CropImageCommand : IUndoableCommand
    {
        private readonly ImageObject _image;
        private readonly string? _oldBase64;
        private readonly string? _newBase64;
        private readonly float _oldX, _oldY, _oldW, _oldH;
        private readonly float _newX, _newY, _newW, _newH;
        private readonly float _oldCropX, _oldCropY, _oldCropW, _oldCropH;

        public CropImageCommand(ImageObject image, 
            float oldX, float oldY, float oldW, float oldH, float newX, float newY, float newW, float newH,
            float oldCropX, float oldCropY, float oldCropW, float oldCropH, float newCropX, float newCropY, float newCropW, float newCropH)
        {
            _image = image;
            _oldX = oldX; _oldY = oldY; _oldW = oldW; _oldH = oldH;
            _newX = newX; _newY = newY; _newW = newW; _newH = newH;
            _oldCropX = oldCropX; _oldCropY = oldCropY; _oldCropW = oldCropW; _oldCropH = oldCropH;

            _oldBase64 = image.ImageBase64;

            // 新しいクロップ画像を生成してBase64で保持
            if (image.ImageData != null && newCropW > 0 && newCropH > 0)
            {
                int w = (int)Math.Max(1, Math.Round(newCropW));
                int h = (int)Math.Max(1, Math.Round(newCropH));
                using var croppedBitmap = new SKBitmap(w, h);
                using (var canvas = new SKCanvas(croppedBitmap))
                {
                    var srcLocalRect = new SKRect(newCropX, newCropY, newCropX + newCropW, newCropY + newCropH);
                    var destLocalRect = new SKRect(0, 0, w, h);
                    
                    using var paint = new SKPaint();
                    // 変形や不透明度などは適用せず、純粋に画像データを切り抜く
                    canvas.DrawBitmap(image.ImageData, srcLocalRect, destLocalRect, paint);
                }
                
                using var skImage = SKImage.FromBitmap(croppedBitmap);
                using var data = skImage.Encode(SKEncodedImageFormat.Png, 100);
                _newBase64 = Convert.ToBase64String(data.ToArray());
            }
        }

        public void Execute()
        {
            _image.ImageBase64 = _newBase64;
            _image.X = _newX; 
            _image.Y = _newY; 
            _image.Width = _newW; 
            _image.Height = _newH;
            
            // 物理的に切り抜かれたため、クロップ枠をリセット
            if (_image.ImageData != null)
            {
                _image.CropX = 0;
                _image.CropY = 0;
                _image.CropWidth = _image.ImageData.Width;
                _image.CropHeight = _image.ImageData.Height;
            }
        }

        public void Undo()
        {
            _image.ImageBase64 = _oldBase64;
            _image.X = _oldX; 
            _image.Y = _oldY; 
            _image.Width = _oldW; 
            _image.Height = _oldH;
            
            _image.CropX = _oldCropX; 
            _image.CropY = _oldCropY; 
            _image.CropWidth = _oldCropW; 
            _image.CropHeight = _oldCropH;
        }
    }
}

