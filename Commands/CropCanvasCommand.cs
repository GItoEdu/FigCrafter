using System.Collections.Generic;
using FigCrafterApp.Models;
using FigCrafterApp.ViewModels;

namespace FigCrafterApp.Commands
{
    public class CropCanvasCommand : IUndoableCommand
    {
        private readonly CanvasViewModel _canvasVM;
        private readonly double _oldWidthMm;
        private readonly double _oldHeightMm;
        private readonly double _newWidthMm;
        private readonly double _newHeightMm;
        private readonly float _offsetX;
        private readonly float _offsetY;

        public CropCanvasCommand(CanvasViewModel canvasVM, double oldW, double oldH, double newW, double newH, float offsetX, float offsetY)
        {
            _canvasVM = canvasVM;
            _oldWidthMm = oldW;
            _oldHeightMm = oldH;
            _newWidthMm = newW;
            _newHeightMm = newH;
            _offsetX = offsetX; // 切り取る左上のX座標
            _offsetY = offsetY; // 切り取る左上のY座標
        }

        public void Execute()
        {
            _canvasVM.WidthMm = _newWidthMm;
            _canvasVM.HeightMm = _newHeightMm;
            // 原点が切り取った左上に移動するため、オブジェクトはマイナス方向にシフトする
            ShiftObjects(-_offsetX, -_offsetY);
        }

        public void Undo()
        {
            _canvasVM.WidthMm = _oldWidthMm;
            _canvasVM.HeightMm = _oldHeightMm;
            // 元に戻すときはプラス方向にシフトする
            ShiftObjects(_offsetX, _offsetY);
        }

        private void ShiftObjects(float dx, float dy)
        {
            foreach (var layer in _canvasVM.Layers)
            {
                foreach (var obj in layer.GraphicObjects)
                {
                    ShiftObject(obj, dx, dy);
                }
            }
        }

        private void ShiftObject(GraphicObject obj, float dx, float dy)
        {
            obj.X += dx;
            obj.Y += dy;
            if (obj is LineObject line)
            {
                line.EndX += dx;
                line.EndY += dy;
            }
            else if (obj is GroupObject group)
            {
                foreach (var child in group.Children)
                {
                    ShiftObject(child, dx, dy);
                }
            }
        }
    }
}