using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using FigCrafterApp.ViewModels;
using FigCrafterApp.Models;

namespace FigCrafterApp.Views
{
    public partial class CanvasView : UserControl
    {
        private GraphicObject? _tempObject;
        private SKPoint _startPoint;
        private GraphicObject? _selectedObject; // 現在選択中のオブジェクト

        public CanvasView()
        {
            InitializeComponent();
        }

        private void SkiaElement_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is CanvasViewModel oldVm)
            {
                oldVm.InvalidateRequested -= OnInvalidateRequested;
            }
            if (e.NewValue is CanvasViewModel newVm)
            {
                newVm.InvalidateRequested += OnInvalidateRequested;
            }
        }

        private void OnInvalidateRequested(object? sender, EventArgs e)
        {
            SkiaElement.InvalidateVisual();
        }

        private void SkiaElement_PaintSurface(object sender, SkiaSharp.Views.Desktop.SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            canvas.Clear(SKColors.White);

            if (DataContext is CanvasViewModel vm)
            {
                foreach (var obj in vm.GraphicObjects)
                {
                    obj.Draw(canvas);
                }
            }

            _tempObject?.Draw(canvas);
        }

        private void SkiaElement_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not CanvasViewModel vm) return;

            var p = e.GetPosition(SkiaElement);
            _startPoint = new SKPoint((float)p.X, (float)p.Y);

            if (vm.CurrentTool == DrawingTool.Select)
            {
                // 選択ツール: 逆順（前面から背面）でHitTest
                GraphicObject? hitObject = null;
                for (int i = vm.GraphicObjects.Count - 1; i >= 0; i--)
                {
                    if (vm.GraphicObjects[i].HitTest(_startPoint))
                    {
                        hitObject = vm.GraphicObjects[i];
                        break;
                    }
                }

                // 選択状態の更新
                if (_selectedObject != hitObject)
                {
                    if (_selectedObject != null) _selectedObject.IsSelected = false;
                    _selectedObject = hitObject;
                    if (_selectedObject != null) _selectedObject.IsSelected = true;
                    SkiaElement.InvalidateVisual();
                }
                
                // ※移動処理の起点は別タスクで実装するため、ここでは選択のみ
                return;
            }

            if (vm.CurrentTool == DrawingTool.Text)
            {
                // テキストツール: クリック位置にテキストを配置
                var textObj = new TextObject 
                { 
                    X = _startPoint.X, 
                    Y = _startPoint.Y, 
                    FillColor = SKColors.Black 
                };
                
                // 一旦固定文字列。後ほどインプレース編集など拡張可能
                vm.GraphicObjects.Add(textObj);
                
                // 配置後に選択状態にする
                if (_selectedObject != null) _selectedObject.IsSelected = false;
                _selectedObject = textObj;
                _selectedObject.IsSelected = true;
                
                // ツールを選択に戻す
                vm.CurrentTool = DrawingTool.Select;
                SkiaElement.InvalidateVisual();
                return;
            }

            switch (vm.CurrentTool)
            {
                case DrawingTool.Rectangle:
                    _tempObject = new RectangleObject { X = _startPoint.X, Y = _startPoint.Y, FillColor = SKColors.SkyBlue.WithAlpha(128) };
                    break;
                case DrawingTool.Ellipse:
                    _tempObject = new EllipseObject { X = _startPoint.X, Y = _startPoint.Y, FillColor = SKColors.Salmon.WithAlpha(128) };
                    break;
                case DrawingTool.Line:
                    _tempObject = new LineObject { X = _startPoint.X, Y = _startPoint.Y, EndX = _startPoint.X, EndY = _startPoint.Y, StrokeColor = SKColors.Black.WithAlpha(128), StrokeWidth = 2 };
                    break;
            }

            SkiaElement.CaptureMouse();
        }

        private void SkiaElement_MouseMove(object sender, MouseEventArgs e)
        {
            if (_tempObject == null) return;

            var p = e.GetPosition(SkiaElement);
            var endPoint = new SKPoint((float)p.X, (float)p.Y);

            if (_tempObject is LineObject lineObj)
            {
                lineObj.EndX = endPoint.X;
                lineObj.EndY = endPoint.Y;
            }
            else
            {
                _tempObject.X = Math.Min(_startPoint.X, endPoint.X);
                _tempObject.Y = Math.Min(_startPoint.Y, endPoint.Y);
                _tempObject.Width = Math.Abs(_startPoint.X - endPoint.X);
                _tempObject.Height = Math.Abs(_startPoint.Y - endPoint.Y);
            }

            SkiaElement.InvalidateVisual();
        }

        private void SkiaElement_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_tempObject == null) return;

            if (DataContext is CanvasViewModel vm)
            {
                // 一時オブジェクトを本番の色に変更して追加
                if (_tempObject is RectangleObject) _tempObject.FillColor = SKColors.SkyBlue;
                if (_tempObject is EllipseObject) _tempObject.FillColor = SKColors.Salmon;
                if (_tempObject is LineObject) _tempObject.StrokeColor = SKColors.Black;
                
                vm.GraphicObjects.Add(_tempObject);
            }

            _tempObject = null;
            SkiaElement.ReleaseMouseCapture();
            SkiaElement.InvalidateVisual();
        }
    }
}
