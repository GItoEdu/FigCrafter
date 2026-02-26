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

        // ドラッグ・変形の状態管理用
        private bool _isDragging = false;
        private bool _isResizing = false;
        private bool _isRangeSelecting = false; // 範囲選択中フラグ
        private SKRect _selectionRect; // 範囲選択の矩形
        private int _resizeHandleIndex = -1;
        private SKPoint _lastMousePos;

        public CanvasView()
        {
            InitializeComponent();
            // マウスクリック時にキーボードフォーカスを取得
            this.MouseDown += (s, e) => Keyboard.Focus(this);
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

            // 範囲選択矩形の描画
            if (_isRangeSelecting)
            {
                using var fillPaint = new SKPaint
                {
                    Color = new SKColor(0, 122, 204, 40), // 半透明の青
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                };
                using var strokePaint = new SKPaint
                {
                    Color = new SKColor(0, 122, 204, 180),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1,
                    PathEffect = SKPathEffect.CreateDash(new float[] { 4, 4 }, 0),
                    IsAntialias = true
                };
                canvas.DrawRect(_selectionRect, fillPaint);
                canvas.DrawRect(_selectionRect, strokePaint);
            }
        }

        private void SkiaElement_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not CanvasViewModel vm) return;

            var p = e.GetPosition(SkiaElement);
            _startPoint = new SKPoint((float)p.X, (float)p.Y);
            _lastMousePos = _startPoint;

            if (vm.CurrentTool == DrawingTool.Select)
            {
                // 既に選択されているオブジェクトがあればハンドルのヒットテストを優先
                if (_selectedObject != null)
                {
                    int handleIdx = GetHandleHitIndex(_selectedObject, _startPoint);
                    if (handleIdx >= 0)
                    {
                        _isResizing = true;
                        _resizeHandleIndex = handleIdx;
                        SkiaElement.CaptureMouse();
                        return;
                    }
                }

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

                bool isShiftHeld = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

                if (hitObject != null)
                {
                    if (isShiftHeld)
                    {
                        // Shift+クリック: トグル選択
                        vm.ToggleSelectObject(hitObject);
                        _selectedObject = vm.SelectedObject;
                    }
                    else
                    {
                        // 通常クリック: 単一選択
                        vm.SelectObject(hitObject);
                        _selectedObject = hitObject;
                    }
                }
                else
                {
                    if (!isShiftHeld)
                    {
                        // 何もない場所をクリック: 全選択解除し、範囲選択を開始
                        vm.ClearSelection();
                        _selectedObject = null;
                        _isRangeSelecting = true;
                        _selectionRect = new SKRect(_startPoint.X, _startPoint.Y, _startPoint.X, _startPoint.Y);
                        SkiaElement.CaptureMouse();
                    }
                }

                SkiaElement.InvalidateVisual();
                
                if (_selectedObject != null && !isShiftHeld)
                {
                    _isDragging = true;
                    SkiaElement.CaptureMouse();
                }
                
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
                
                vm.GraphicObjects.Add(textObj);
                vm.SelectObject(textObj);
                _selectedObject = textObj;
                
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
            var p = e.GetPosition(SkiaElement);
            var currentPoint = new SKPoint((float)p.X, (float)p.Y);
            var dx = currentPoint.X - _lastMousePos.X;
            var dy = currentPoint.Y - _lastMousePos.Y;
            _lastMousePos = currentPoint;

            if (_isResizing && _selectedObject != null)
            {
                if (_selectedObject is LineObject lineObj)
                {
                    // Line のリサイズ（端点の移動）
                    if (_resizeHandleIndex == 0) // Start point
                    {
                        lineObj.X += dx;
                        lineObj.Y += dy;
                    }
                    else if (_resizeHandleIndex == 1) // End point
                    {
                        lineObj.EndX += dx;
                        lineObj.EndY += dy;
                    }
                }
                else
                {
                    // 矩形・楕円・テキスト（必要に応じて）などのリサイズ
                    var rect = GetBoundingRect(_selectedObject);
                    float left = rect.Left, top = rect.Top, right = rect.Right, bottom = rect.Bottom;

                    switch (_resizeHandleIndex)
                    {
                        case 0: left += dx; top += dy; break;      // TopLeft
                        case 1: right += dx; top += dy; break;     // TopRight
                        case 2: right += dx; bottom += dy; break;  // BottomRight
                        case 3: left += dx; bottom += dy; break;   // BottomLeft
                    }

                    // 幅・高さが負にならないよう調整
                    _selectedObject.X = Math.Min(left, right);
                    _selectedObject.Y = Math.Min(top, bottom);
                    _selectedObject.Width = Math.Abs(right - left);
                    _selectedObject.Height = Math.Abs(bottom - top);
                }
                
                SkiaElement.InvalidateVisual();
                return;
            }

            if (_isDragging && _selectedObject != null)
            {
                // 移動処理
                _selectedObject.X += dx;
                _selectedObject.Y += dy;

                if (_selectedObject is LineObject lineObj)
                {
                    lineObj.EndX += dx;
                    lineObj.EndY += dy;
                }
                else if (_selectedObject is GroupObject groupObj)
                {
                    // グループ内の子オブジェクトも連動して移動
                    MoveChildrenRecursive(groupObj, dx, dy);
                }

                SkiaElement.InvalidateVisual();
                return;
            }

            // 範囲選択中
            if (_isRangeSelecting)
            {
                _selectionRect = new SKRect(
                    Math.Min(_startPoint.X, currentPoint.X),
                    Math.Min(_startPoint.Y, currentPoint.Y),
                    Math.Max(_startPoint.X, currentPoint.X),
                    Math.Max(_startPoint.Y, currentPoint.Y)
                );
                SkiaElement.InvalidateVisual();
                return;
            }

            // 新規図形の描画中
            if (_tempObject == null) return;

            var endPoint = currentPoint;

            if (_tempObject is LineObject tempLine)
            {
                tempLine.EndX = endPoint.X;
                tempLine.EndY = endPoint.Y;
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
            if (_isResizing)
            {
                _isResizing = false;
                _resizeHandleIndex = -1;
                SkiaElement.ReleaseMouseCapture();
                return;
            }

            if (_isDragging)
            {
                _isDragging = false;
                SkiaElement.ReleaseMouseCapture();
                return;
            }

            // 範囲選択の確定
            if (_isRangeSelecting)
            {
                _isRangeSelecting = false;
                SkiaElement.ReleaseMouseCapture();

                if (DataContext is CanvasViewModel vmSelect)
                {
                    vmSelect.ClearSelection();
                    // 範囲に完全に含まれるオブジェクトを選択
                    foreach (var obj in vmSelect.GraphicObjects)
                    {
                        if (IsObjectFullyContained(obj, _selectionRect))
                        {
                            obj.IsSelected = true;
                            vmSelect.SelectedObjects.Add(obj);
                        }
                    }
                    // SelectedObject を最後の選択オブジェクトに設定
                    if (vmSelect.SelectedObjects.Count > 0)
                    {
                        vmSelect.SelectedObject = vmSelect.SelectedObjects[^1];
                        _selectedObject = vmSelect.SelectedObject;
                    }
                }

                SkiaElement.InvalidateVisual();
                return;
            }

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

        private int GetHandleHitIndex(GraphicObject obj, SKPoint hitPoint)
        {
            float handleRadius = 6.0f; // Clickable area
            if (obj is LineObject lineObj)
            {
                var p0 = new SKPoint(lineObj.X, lineObj.Y);
                var p1 = new SKPoint(lineObj.EndX, lineObj.EndY);
                
                if (HitTestHandle(p0, hitPoint, handleRadius)) return 0;
                if (HitTestHandle(p1, hitPoint, handleRadius)) return 1;
                return -1;
            }

            var rect = GetBoundingRect(obj);
            var points = new[]
            {
                new SKPoint(rect.Left, rect.Top),
                new SKPoint(rect.Right, rect.Top),
                new SKPoint(rect.Right, rect.Bottom),
                new SKPoint(rect.Left, rect.Bottom)
            };

            for (int i = 0; i < points.Length; i++)
            {
                if (HitTestHandle(points[i], hitPoint, handleRadius)) return i;
            }
            return -1;
        }

        private bool HitTestHandle(SKPoint handleCenter, SKPoint hitPoint, float radius)
        {
            float dx = handleCenter.X - hitPoint.X;
            float dy = handleCenter.Y - hitPoint.Y;
            return dx * dx + dy * dy <= radius * radius;
        }

        private SKRect GetBoundingRect(GraphicObject obj)
        {
            if (obj is LineObject lineObj)
            {
                return new SKRect(
                    Math.Min(lineObj.X, lineObj.EndX),
                    Math.Min(lineObj.Y, lineObj.EndY),
                    Math.Max(lineObj.X, lineObj.EndX),
                    Math.Max(lineObj.Y, lineObj.EndY)
                );
            }
            if (obj is TextObject textObj)
            {
                using var paint = new SKPaint
                {
                    Typeface = SKTypeface.FromFamilyName(textObj.FontFamily),
                    TextSize = textObj.FontSize
                };
                var bounds = new SKRect();
                paint.MeasureText(textObj.Text, ref bounds);
                return new SKRect(obj.X, obj.Y, obj.X + bounds.Width, obj.Y + bounds.Height);
            }
            return new SKRect(obj.X, obj.Y, obj.X + obj.Width, obj.Y + obj.Height);
        }

        /// <summary>
        /// オブジェクトが選択矩形に完全に含まれるか判定
        /// </summary>
        private bool IsObjectFullyContained(GraphicObject obj, SKRect selectionRect)
        {
            var objRect = GetBoundingRect(obj);
            return selectionRect.Contains(objRect);
        }

        /// <summary>
        /// グループ内の子オブジェクトを再帰的に移動
        /// </summary>
        private void MoveChildrenRecursive(GroupObject group, float dx, float dy)
        {
            foreach (var child in group.Children)
            {
                child.X += dx;
                child.Y += dy;
                if (child is LineObject line)
                {
                    line.EndX += dx;
                    line.EndY += dy;
                }
                else if (child is GroupObject nestedGroup)
                {
                    MoveChildrenRecursive(nestedGroup, dx, dy);
                }
            }
        }

        private void CanvasView_KeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not CanvasViewModel vm) return;

            if (e.Key == Key.Delete)
            {
                if (vm.DeleteSelectedCommand.CanExecute(null))
                {
                    vm.DeleteSelectedCommand.Execute(null);
                    // CanvasView側の選択状態もクリア
                    _selectedObject = null;
                }
                e.Handled = true;
            }
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (vm.CopyCommand.CanExecute(null))
                    vm.CopyCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // クリップボードに画像があれば画像をペースト
                if (System.Windows.Clipboard.ContainsImage())
                {
                    var bitmapSource = System.Windows.Clipboard.GetImage();
                    if (bitmapSource != null)
                    {
                        var skBitmap = ConvertBitmapSourceToSKBitmap(bitmapSource);
                        if (skBitmap != null)
                        {
                            var imageObj = new ImageObject
                            {
                                X = 10,
                                Y = 10,
                                ImageData = skBitmap
                            };
                            vm.GraphicObjects.Add(imageObj);
                            vm.SelectObject(imageObj);
                            _selectedObject = imageObj;
                            SkiaElement.InvalidateVisual();
                        }
                    }
                }
                else if (vm.PasteCommand.CanExecute(null))
                {
                    vm.PasteCommand.Execute(null);
                    // ペースト後の選択状態をCanvasView側にも反映
                    _selectedObject = vm.SelectedObject;
                }
                e.Handled = true;
            }
            else if (e.Key == Key.G && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // Ctrl+G: グループ化
                if (vm.GroupCommand.CanExecute(null))
                {
                    vm.GroupCommand.Execute(null);
                    _selectedObject = vm.SelectedObject;
                }
                e.Handled = true;
            }
            else if (e.Key == Key.G && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                // Ctrl+Shift+G: グループ解除
                if (vm.UngroupCommand.CanExecute(null))
                {
                    vm.UngroupCommand.Execute(null);
                    _selectedObject = vm.SelectedObject;
                }
                e.Handled = true;
            }
        }

        /// <summary>
        /// WPF の BitmapSource を SkiaSharp の SKBitmap に変換
        /// </summary>
        private SKBitmap? ConvertBitmapSourceToSKBitmap(System.Windows.Media.Imaging.BitmapSource bitmapSource)
        {
            try
            {
                // Bgra32 に変換
                var formatted = new System.Windows.Media.Imaging.FormatConvertedBitmap(
                    bitmapSource,
                    System.Windows.Media.PixelFormats.Bgra32,
                    null, 0);

                int width = formatted.PixelWidth;
                int height = formatted.PixelHeight;
                int stride = width * 4;
                byte[] pixels = new byte[stride * height];
                formatted.CopyPixels(pixels, stride, 0);

                var skBitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
                try
                {
                    skBitmap.InstallPixels(
                        new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul),
                        handle.AddrOfPinnedObject(),
                        stride);
                    // InstallPixels は参照を共有するのでコピーを作成
                    return skBitmap.Copy();
                }
                finally
                {
                    handle.Free();
                    skBitmap.Dispose();
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
