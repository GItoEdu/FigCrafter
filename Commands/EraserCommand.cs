using System;
using SkiaSharp;
using FigCrafterApp.Models;

namespace FigCrafterApp.Commands
{
    public class EraserCommand : IUndoableCommand
    {
        private readonly ImageObject _imageObject;
        private readonly SKBitmap? _oldMask;
        private readonly SKBitmap? _newMask;

        public EraserCommand(ImageObject imageObject, SKBitmap? oldMask, SKBitmap? newMask)
        {
            _imageObject = imageObject;
            
            // SKBitmapはミュータブルなので、状態を保持するためにコピー(Clone)を作成して保持する
            _oldMask = oldMask?.Copy();
            _newMask = newMask?.Copy();
        }

        public void Execute()
        {
            _imageObject.EraserMask = _newMask?.Copy();
        }

        public void Undo()
        {
            _imageObject.EraserMask = _oldMask?.Copy();
        }
    }
}
