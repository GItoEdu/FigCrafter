using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using FigCrafterApp.ViewModels;
using FigCrafterApp.Models;
using FigCrafterApp.Commands;

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
        private SKRect _originalResizeRect;
        private float _originalAspectRatio;
        
        // Undo用の一時保存
        private List<(GraphicObject Obj, float OldX, float OldY)> _preDragPositions = new();
        private (float X, float Y, float EndX, float EndY) _preDragLineEnd;

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
            
            // 状態リセット (タブ切り替え時に前タブの操作状態を引き継がないようにする)
            _selectedObject = null;
            _tempObject = null;
            _isDragging = false;
            _isResizing = false;
            _isRangeSelecting = false;
            _resizeHandleIndex = -1;

            if (e.NewValue is CanvasViewModel newVm)
            {
                newVm.InvalidateRequested += OnInvalidateRequested;
                SkiaElement.InvalidateVisual(); // DataContext変更時に再描画を強制する
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
                        _originalResizeRect = GetBoundingRect(_selectedObject);
                        _originalAspectRatio = _originalResizeRect.Width / _originalResizeRect.Height;
                        if (_selectedObject is LineObject line)
                        {
                            _preDragLineEnd = (line.X, line.Y, line.EndX, line.EndY);
                        }
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
                    _preDragPositions.Clear();
                    foreach (var obj in vm.SelectedObjects)
                    {
                        _preDragPositions.Add((obj, obj.X, obj.Y));
                        if (obj is LineObject lineObj)
                        {
                            // Lineの場合は始点も終点も動くので、ここでは始点をY/Xとして記録しているがLine全体の移動の際に工夫が必要
                            // 移動処理(_isDragging)自体はX,Y等の変異だけを記録すればMoveObjectsCommandで対応可能
                        }
                    }
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

            // リサイズ中でなければカーソル更新
            if (!_isResizing && !_isDragging && !_isRangeSelecting && _selectedObject != null)
            {
                int hoverHandle = GetHandleHitIndex(_selectedObject, currentPoint);
                if (_selectedObject is LineObject)
                {
                    SkiaElement.Cursor = hoverHandle >= 0 ? System.Windows.Input.Cursors.Cross : System.Windows.Input.Cursors.Arrow;
                }
                else
                {
                    SkiaElement.Cursor = hoverHandle switch
                    {
                        0 => System.Windows.Input.Cursors.SizeNWSE, // TopLeft
                        1 => System.Windows.Input.Cursors.SizeNESW, // TopRight
                        2 => System.Windows.Input.Cursors.SizeNWSE, // BottomRight
                        3 => System.Windows.Input.Cursors.SizeNESW, // BottomLeft
                        _ => System.Windows.Input.Cursors.Arrow
                    };
                }
            }

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
                    // 矩形・楽円・テキスト・画像などのリサイズ
                    var rect = GetBoundingRect(_selectedObject);
                    float left = rect.Left, top = rect.Top, right = rect.Right, bottom = rect.Bottom;

                    switch (_resizeHandleIndex)
                    {
                        case 0: left += dx; top += dy; break;      // TopLeft
                        case 1: right += dx; top += dy; break;     // TopRight
                        case 2: right += dx; bottom += dy; break;  // BottomRight
                        case 3: left += dx; bottom += dy; break;   // BottomLeft
                    }

                    // Shiftが押されている場合は縦横比を維持（後からShiftを押しても対応）
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _originalAspectRatio > 0)
                    {
                        float newWidth = Math.Abs(right - left);
                        float newHeight = Math.Abs(bottom - top);

                        // ドラッグ量の大きい方を基準に調整
                        if (Math.Abs(dx) >= Math.Abs(dy))
                        {
                            newHeight = newWidth / _originalAspectRatio;
                        }
                        else
                        {
                            newWidth = newHeight * _originalAspectRatio;
                        }

                        // ハンドルの位置に応じて座標を再計算
                        switch (_resizeHandleIndex)
                        {
                            case 0: left = right - newWidth; top = bottom - newHeight; break;
                            case 1: right = left + newWidth; top = bottom - newHeight; break;
                            case 2: right = left + newWidth; bottom = top + newHeight; break;
                            case 3: left = right - newWidth; bottom = top + newHeight; break;
                        }
                    }

                    // 幅・高さが負にならないよう調整
                    float newX = Math.Min(left, right);
                    float newY = Math.Min(top, bottom);
                    float newW = Math.Abs(right - left);
                    float newH = Math.Abs(bottom - top);

                    // GroupObject の場合は子オブジェクトも比例的にスケーリング
                    if (_selectedObject is GroupObject groupObj && rect.Width > 0 && rect.Height > 0)
                    {
                        float scaleX = newW / rect.Width;
                        float scaleY = newH / rect.Height;

                        foreach (var child in groupObj.Children)
                        {
                            // 元のバウンディングボックスに対する相対位置を計算して再配置
                            float relX = (child.X - rect.Left) / rect.Width;
                            float relY = (child.Y - rect.Top) / rect.Height;
                            child.X = newX + relX * newW;
                            child.Y = newY + relY * newH;
                            child.Width *= scaleX;
                            child.Height *= scaleY;

                            if (child is LineObject childLine)
                            {
                                float relEndX = (childLine.EndX - rect.Left) / rect.Width;
                                float relEndY = (childLine.EndY - rect.Top) / rect.Height;
                                childLine.EndX = newX + relEndX * newW;
                                childLine.EndY = newY + relEndY * newH;
                            }
                        }
                    }

                    _selectedObject.X = newX;
                    _selectedObject.Y = newY;
                    _selectedObject.Width = newW;
                    _selectedObject.Height = newH;
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
            if (DataContext is not CanvasViewModel vmObj) return;

            if (_isResizing)
            {
                _isResizing = false;
                
                if (_selectedObject != null)
                {
                    if (_selectedObject is LineObject lineObj)
                    {
                        var cmd = new MoveLineEndCommand(lineObj, _preDragLineEnd.X, _preDragLineEnd.Y, _preDragLineEnd.EndX, _preDragLineEnd.EndY, lineObj.X, lineObj.Y, lineObj.EndX, lineObj.EndY);
                        vmObj.ExecuteCommand(cmd);
                    }
                    else
                    {
                        var cmd = new ResizeCommand(_selectedObject, _originalResizeRect.Left, _originalResizeRect.Top, _originalResizeRect.Width, _originalResizeRect.Height, _selectedObject.X, _selectedObject.Y, _selectedObject.Width, _selectedObject.Height);
                        vmObj.ExecuteCommand(cmd);
                    }
                }

                _resizeHandleIndex = -1;
                SkiaElement.ReleaseMouseCapture();
                return;
            }

            if (_isDragging)
            {
                _isDragging = false;
                if (_selectedObject != null && _preDragPositions.Count > 0)
                {
                    // 実際に動いたか確認
                    bool hasMoved = _preDragPositions.Any(p => p.Obj.X != p.OldX || p.Obj.Y != p.OldY);
                    if (hasMoved)
                    {
                        var moves = new List<(GraphicObject Obj, float OldX, float OldY, float NewX, float NewY)>();
                        foreach (var (obj, oldX, oldY) in _preDragPositions)
                        {
                            moves.Add((obj, oldX, oldY, obj.X, obj.Y));
                        }
                        vmObj.ExecuteCommand(new MoveObjectsCommand(moves));
                    }
                }
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

            if (DataContext is CanvasViewModel vmAdd)
            {
                // 一時オブジェクトを本番の色に変更して追加
                if (_tempObject is RectangleObject) _tempObject.FillColor = SKColors.SkyBlue;
                if (_tempObject is EllipseObject) _tempObject.FillColor = SKColors.Salmon;
                if (_tempObject is LineObject) _tempObject.StrokeColor = SKColors.Black;
                
                vmAdd.ExecuteCommand(new AddObjectCommand(vmAdd.GraphicObjects, _tempObject));
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
                // クリップボードから画像をペースト（複数形式に対応）
                var skBitmap = TryGetImageFromClipboard();
                if (skBitmap != null)
                {
                    // 画像オブジェクト
                    var imageObj = new ImageObject
                    {
                        X = 10,
                        Y = 10,
                        ImageData = skBitmap
                    };
                    
                    // 枠線オブジェクト (透明背景、黒枠線)
                    var borderObj = new RectangleObject
                    {
                        X = 10,
                        Y = 10,
                        Width = imageObj.Width,
                        Height = imageObj.Height,
                        FillColor = SKColors.Transparent,
                        StrokeColor = SKColors.Black,
                        StrokeWidth = 2
                    };

                    // グループオブジェクト
                    var groupObj = new GroupObject
                    {
                        X = 10,
                        Y = 10,
                        Width = imageObj.Width,
                        Height = imageObj.Height
                    };
                    
                    // 子要素として追加
                    groupObj.Children.Add(imageObj);
                    groupObj.Children.Add(borderObj);

                    vm.GraphicObjects.Add(groupObj);
                    vm.SelectObject(groupObj);
                    _selectedObject = groupObj;
                    SkiaElement.InvalidateVisual();
                }
                else if (vm.PasteCommand.CanExecute(null))
                {
                    vm.PasteCommand.Execute(null);
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
        /// クリップボードから画像を取得（複数形式に対応）
        /// </summary>
        private SKBitmap? TryGetImageFromClipboard()
        {
            try
            {
                var dataObject = System.Windows.Clipboard.GetDataObject();
                if (dataObject == null) return null;

                // 1) PNG ストリーム直接デコード（最も確実・ImageJ対応）
                if (dataObject.GetDataPresent("PNG"))
                {
                    var pngData = dataObject.GetData("PNG") as System.IO.MemoryStream;
                    if (pngData != null)
                    {
                        pngData.Position = 0;
                        var result = SKBitmap.Decode(pngData);
                        if (result != null) return result;
                    }
                }

                // 2) WPF標準の画像形式（PngBitmapEncoder経由で変換）
                if (System.Windows.Clipboard.ContainsImage())
                {
                    var bitmapSource = System.Windows.Clipboard.GetImage();
                    if (bitmapSource != null)
                    {
                        var result = ConvertBitmapSourceToSKBitmap(bitmapSource);
                        if (result != null) return result;
                    }
                }

                // 3) DIB/Bitmap 形式
                if (dataObject.GetDataPresent(System.Windows.DataFormats.Bitmap))
                {
                    var data = dataObject.GetData(System.Windows.DataFormats.Bitmap);
                    if (data is System.Windows.Media.Imaging.BitmapSource bmpSrc)
                    {
                        var result = ConvertBitmapSourceToSKBitmap(bmpSrc);
                        if (result != null) return result;
                    }
                }

                // 4) ファイルドロップ形式
                if (dataObject.GetDataPresent(System.Windows.DataFormats.FileDrop))
                {
                    var files = dataObject.GetData(System.Windows.DataFormats.FileDrop) as string[];
                    if (files != null && files.Length > 0)
                    {
                        var ext = System.IO.Path.GetExtension(files[0]).ToLower();
                        if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff")
                        {
                            using var stream = System.IO.File.OpenRead(files[0]);
                            return SKBitmap.Decode(stream);
                        }
                    }
                }
            }
            catch
            {
                // クリップボードアクセスに失敗した場合は null
            }
            return null;
        }

        /// <summary>
        /// WPF の BitmapSource を SkiaSharp の SKBitmap に変換（PngBitmapEncoder 経由で全フォーマット対応）
        /// </summary>
        private SKBitmap? ConvertBitmapSourceToSKBitmap(System.Windows.Media.Imaging.BitmapSource bitmapSource)
        {
            try
            {
                // BitmapSource → PNG メモリストリーム → SKBitmap
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmapSource));
                using var ms = new System.IO.MemoryStream();
                encoder.Save(ms);
                ms.Position = 0;
                return SKBitmap.Decode(ms);
            }
            catch
            {
                return null;
            }
        }
    }
}
