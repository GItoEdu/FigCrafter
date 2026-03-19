using System.Linq;
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
        public CanvasViewModel ViewModel => (CanvasViewModel)DataContext;
        private GraphicObject? _tempObject;
        private SKPoint _startPoint;
        private GraphicObject? _selectedObject; // 現在選択中のオブジェクト

        // ドラッグ・変形の状態管理用
        private bool _isDragging = false;
        private bool _isResizing = false;
        private bool _isRotating = false; // 回転中フラグ
        private bool _isSelecting = false; // 範囲選択中フラグ
        private bool _isCropping = false; // トリミング中フラグ
        private SKRect _selectionRect; // 範囲選択の矩形
        private int _resizeHandleIndex = -1;
        private int _cropHandleIndex = -1;
        private SKPoint _lastMousePos;
        private SKRect _originalResizeRect;
        private SKRect _originalCropRect;
        private float _originalAspectRatio;
        private float _originalRotation; // 回転開始時の角度を保持
        private float _startRotationMouseAngle; // 回転開始時のマウス角度を保持
        
        // Undo用の一時保存
        private List<(GraphicObject Obj, float OldX, float OldY, float? OldEndX, float? OldEndY)> _preDragPositions = new();
        private (float X, float Y, float EndX, float EndY) _preDragLineEnd;

        // スナップ機能用
        private float? _snapGuideX = null;
        private float? _snapGuideY = null;
        private SKRect _originalDragRect;
        private SKPoint[] _originalDragCorners = Array.Empty<SKPoint>(); // ドラッグ開始時の回転後頂点
        private GraphicObject? _snapXTarget = null; // X軸スナップ先のオブジェクト
        private GraphicObject? _snapYTarget = null; // Y軸スナップ先のオブジェクト

        // 半透明クロップガイド用のペイントキャッシュ
        private SKPaint? _cachedCropGuidePaintNormal;
        private SKPaint? _cachedCropGuidePaintGray;

        // 消しゴム用
        private bool _isErasing = false;
        private ImageObject? _eraserTarget = null;
        private SKRect _eraserRect; // 消しゴムをかける矩形領域
        private SKBitmap? _preEraserMask = null; // Undo用：消しゴム適用前のマスク

        // インライン編集用
        private TextObject? _editingTextObject;
        private string _editingOriginalText = "";

        private SKPaint GetCachedCropGuidePaint(bool isGrayscale)
        {
            if (isGrayscale)
            {
                if (_cachedCropGuidePaintGray == null)
                {
                    _cachedCropGuidePaintGray = new SKPaint();
                    var matrix = new float[] {
                        0.299f, 0.587f, 0.114f, 0, 0,
                        0.299f, 0.587f, 0.114f, 0, 0,
                        0.299f, 0.587f, 0.114f, 0, 0,
                        0,      0,      0,      0.3f, 0
                    };
                    _cachedCropGuidePaintGray.ColorFilter = SKColorFilter.CreateColorMatrix(matrix);
                }
                return _cachedCropGuidePaintGray;
            }
            else
            {
                if (_cachedCropGuidePaintNormal == null)
                {
                    _cachedCropGuidePaintNormal = new SKPaint();
                    var matrix = new float[] {
                        1, 0, 0, 0,    0,
                        0, 1, 0, 0,    0,
                        0, 0, 1, 0,    0,
                        0, 0, 0, 0.3f, 0
                    };
                    _cachedCropGuidePaintNormal.ColorFilter = SKColorFilter.CreateColorMatrix(matrix);
                }
                return _cachedCropGuidePaintNormal;
            }
        }

        // 描画間引き用（約60FPS制限）
        private DateTime _lastInvalidateTime = DateTime.MinValue;

        private void ThrottledInvalidateVisual()
        {
            var now = DateTime.Now;
            if ((now - _lastInvalidateTime).TotalMilliseconds > 16) // 1000ms / 60fps ≒ 16.6ms
            {
                SkiaElement.InvalidateVisual();
                _lastInvalidateTime = now;
            }
        }

        public CanvasView()
        {
            InitializeComponent();
            // マウスクリック時にキーボードフォーカスを取得
            this.MouseDown += (s, e) =>
            {
                if (_editingTextObject == null)
                {
                    Keyboard.Focus(this);
                }
            };    
            // SkiaElement上のダブルクリックでテキスト編集を開始
            // SKElementはFrameworkElement派生のためMouseDoubleClickイベントがない
            // PreviewMouseLeftButtonDownでClickCount==2を検出する
            SkiaElement.PreviewMouseLeftButtonDown += SkiaElement_PreviewDoubleClick;
            // ビューポート（表示領域）のサイズ変更を検知
            CanvasScrollViewer.SizeChanged += CanvasScrollViewer_SizeChanged;
        }

        private void CanvasScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (DataContext is CanvasViewModel vm)
            {
                vm.ViewportWidth = e.NewSize.Width;
                vm.ViewportHeight = e.NewSize.Height;

                if (vm.ShouldZoomToFitOnSizeChange)
                {
                    vm.ZoomToFit(vm.ZoomTargetObject);
                    vm.ShouldZoomToFitOnSizeChange = false;
                    vm.ZoomTargetObject = null;
                }
            }
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
            _isCropping = false;
            _isErasing = false;
            _eraserTarget = null;
            _resizeHandleIndex = -1;
            _cropHandleIndex = -1;

            if (e.NewValue is CanvasViewModel newVm)
            {
                newVm.InvalidateRequested += OnInvalidateRequested;
                
                // 初回のビューポートサイズを同期
                newVm.ViewportWidth = CanvasScrollViewer.ActualWidth;
                newVm.ViewportHeight = CanvasScrollViewer.ActualHeight;

                if (newVm.ShouldZoomToFitOnSizeChange)
                {
                    newVm.ZoomToFit(newVm.ZoomTargetObject);
                    newVm.ShouldZoomToFitOnSizeChange = false;
                    newVm.ZoomTargetObject = null;
                }

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
                // UI上の表示倍率（ズーム）に合わせてスケール適用。座標系をmmにするため、96DPI(WPF)ベースの係数を掛ける
                float mmToPx = 96.0f / 25.4f;
                float totalScale = (float)vm.ZoomLevel * mmToPx;
                canvas.Scale(totalScale);

                // レイヤーのリストはUI（ListBox）上ではインデックス0が「一番上（手前）」に表示されるため、
                // 描画はインデックスが後ろ（奥）のものから順に行う
                for (int i = vm.Layers.Count - 1; i >= 0; i--)
                {
                    var layer = vm.Layers[i];
                    if (!layer.IsVisible) continue;
                    foreach (var obj in layer.GraphicObjects)
                    {
                        obj.CurrentZoomLevel = totalScale;
                        obj.Draw(canvas);
                    }
                }
            }

            if (_tempObject != null && DataContext is CanvasViewModel vm2)
            {
                float mmToPx = 96.0f / 25.4f;
                float totalScale = (float)vm2.ZoomLevel * mmToPx;
                _tempObject.CurrentZoomLevel = totalScale;
                _tempObject.Draw(canvas);
            }

            float currentZoom = 1.0f;
            if (DataContext is CanvasViewModel vmZoom)
            {
                currentZoom = (float)vmZoom.ZoomLevel;
            }
            if (currentZoom == 0) currentZoom = 1.0f;

            // 消しゴム矩形の描画
            if (_isErasing)
            {
                using var fillPaint = new SKPaint
                {
                    Color = new SKColor(255, 0, 0, 40), // 半透明の赤
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                };
                using var strokePaint = new SKPaint
                {
                    Color = new SKColor(255, 0, 0, 180),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 0.25f / currentZoom, // 0.5 -> 0.25
                    PathEffect = SKPathEffect.CreateDash(new float[] { 2 / currentZoom, 2 / currentZoom }, 0), // 4 -> 2
                    IsAntialias = true
                };
                canvas.DrawRect(_eraserRect, fillPaint);
                canvas.DrawRect(_eraserRect, strokePaint);
            }

            // トリミングハンドルの描画と、元画像の半透明表示
            if (DataContext is CanvasViewModel vmCrop && vmCrop.IsCropMode && _selectedObject is ImageObject imgObj && imgObj.ImageData != null)
            {
                // 現在の表示領域とクロップ領域
                var destRect = GetBoundingRect(imgObj);

                // クロップ領域と表示領域の比率から、元画像全体の表示先矩形を逆算する
                float scaleX = destRect.Width / imgObj.CropWidth;
                float scaleY = destRect.Height / imgObj.CropHeight;

                float fullLeft = destRect.Left - (imgObj.CropX * scaleX);
                float fullTop = destRect.Top - (imgObj.CropY * scaleY);
                float fullWidth = imgObj.ImageData.Width * scaleX;
                float fullHeight = imgObj.ImageData.Height * scaleY;

                var fullDestRect = new SKRect(fullLeft, fullTop, fullLeft + fullWidth, fullTop + fullHeight);

                // 全体画像を半透明で描画
                var paint = GetCachedCropGuidePaint(imgObj.IsGrayscale);
                
                canvas.Save();
                imgObj.TransformCanvas(canvas);
                canvas.DrawBitmap(imgObj.ImageData, fullDestRect, paint);
                canvas.Restore();

                // トリミングハンドルの描画
                using var handlePaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
                using var handleStrokePaint = new SKPaint { Color = SKColors.Orange, Style = SKPaintStyle.Stroke, StrokeWidth = 1 / currentZoom, IsAntialias = true };
                float hs = 5 / currentZoom;
                var points = new[]
                {
                    new SKPoint(destRect.Left, destRect.Top),
                    new SKPoint(destRect.Right, destRect.Top),
                    new SKPoint(destRect.Right, destRect.Bottom),
                    new SKPoint(destRect.Left, destRect.Bottom)
                };
                foreach (var pt in points)
                {
                    var hr = new SKRect(pt.X - hs / 2, pt.Y - hs / 2, pt.X + hs / 2, pt.Y + hs / 2);
                    canvas.DrawRect(hr, handlePaint);
                    canvas.DrawRect(hr, handleStrokePaint);
                }
            }

            // 範囲選択矩形の描画
            if (_isSelecting)
            {
                using var fillPaint = new SKPaint
                {
                    Color = new SKColor(0, 122, 204, 30), // 半透明の青
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                };
                using var strokePaint = new SKPaint
                {
                    Color = new SKColor(0, 122, 204, 200),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 0.5f / currentZoom, // 1.0 -> 0.5
                    PathEffect = SKPathEffect.CreateDash(new float[] { 2.5f / currentZoom, 2.5f / currentZoom }, 0), // 4 -> 2.5
                    IsAntialias = true
                };
                canvas.DrawRect(_selectionRect, fillPaint);
                canvas.DrawRect(_selectionRect, strokePaint);
            }

            // スナップガイド線の描画
            if (_isDragging && (_snapGuideX.HasValue || _snapGuideY.HasValue))
            {
                using var snapGuidePaint = new SKPaint
                {
                    Color = new SKColor(255, 50, 50, 200),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 0.5f / currentZoom,
                    PathEffect = SKPathEffect.CreateDash(new float[] { 4 / currentZoom, 4 / currentZoom }, 0),
                    IsAntialias = true
                };

                // X軸ガイド
                if (_snapGuideX.HasValue)
                {
                    float gx = _snapGuideX.Value;
                    canvas.DrawLine(gx, -10000, gx, 10000, snapGuidePaint);
                }

                // Y軸ガイド
                if (_snapGuideY.HasValue)
                {
                    float gy = _snapGuideY.Value;
                    canvas.DrawLine(-10000, gy, 10000, gy, snapGuidePaint);
                }

                // スナップ先オブジェクトの強調表示
                using var highlightPaint = new SKPaint
                {
                    Color = new SKColor(255, 140, 0, 150), // 半透明オレンジ
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1.5f / currentZoom,
                    IsAntialias = true
                };

                // X軸スナップ先の強調
                if (_snapXTarget != null)
                {
                    DrawSnapTargetHighlight(canvas, _snapXTarget, highlightPaint);
                }

                // Y軸スナップ先の強調（X軸と同じオブジェクトの場合は二重描画を避ける）
                if (_snapYTarget != null && _snapYTarget != _snapXTarget)
                {
                    DrawSnapTargetHighlight(canvas, _snapYTarget, highlightPaint);
                }
            }
        }

        private void SkiaElement_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_editingTextObject != null)
            {
                EndInlineEditing();
            }

            if (DataContext is not CanvasViewModel vm) return;

            var pRaw = e.GetPosition(SkiaElement);
            _startPoint = new SKPoint((float)(pRaw.X / vm.ZoomLevel * (25.4 / 96.0)), (float)(pRaw.Y / vm.ZoomLevel * (25.4 / 96.0)));
            _lastMousePos = _startPoint;

            if (vm.CurrentTool == DrawingTool.Eraser)
            {
                ImageObject? targetImg = null;

                // 1. まず現在選択中のオブジェクトがImageObject（またはその中の画像）であれば、それを優先ターゲットとする
                if (vm.SelectedObject is ImageObject selectedImg)
                {
                    targetImg = selectedImg;
                }
                else if (vm.SelectedObject is GroupObject selectedGrp)
                {
                    targetImg = FindImageInGroup(selectedGrp, _startPoint) ?? selectedGrp.Children.OfType<ImageObject>().FirstOrDefault();
                }

                // 2. 選択中の画像がない場合は、クリック位置でヒットテストを行う（アクティブレイヤーを優先）
                if (targetImg == null && vm.ActiveLayer != null && vm.ActiveLayer.IsVisible && !vm.ActiveLayer.IsLocked)
                {
                    for (int i = vm.ActiveLayer.GraphicObjects.Count - 1; i >= 0; i--)
                    {
                        var obj = vm.ActiveLayer.GraphicObjects[i];
                        if (obj is ImageObject img && img.HitTest(_startPoint))
                        {
                            targetImg = img;
                            break;
                        }
                        // GroupObject内のImageObjectも検索
                        if (obj is GroupObject group && obj.HitTest(_startPoint))
                        {
                            targetImg = FindImageInGroup(group, _startPoint);
                            if (targetImg != null) break;
                        }
                    }
                }

                if (targetImg != null)
                {
                    System.IO.File.AppendAllText("eraser_debug.log", $"MouseDown: Eraser target selected={targetImg.GetHashCode()}, rect={GetBoundingRect(targetImg)}\n");
                }
                else
                {
                    System.IO.File.AppendAllText("eraser_debug.log", $"MouseDown: No target selected\n");
                }

                _isErasing = true;
                _eraserTarget = targetImg; // ここでnullでも、MouseUp時のフォールバックに任せる
                _eraserRect = new SKRect(_startPoint.X, _startPoint.Y, _startPoint.X, _startPoint.Y);
                
                if (_eraserTarget != null)
                {
                    _eraserTarget.EnsureEraserMask();
                    _preEraserMask = _eraserTarget.EraserMask?.Copy();
                }
                else
                {
                    _preEraserMask = null;
                }

                SkiaElement.CaptureMouse();
                return;
            }

            if (vm.CurrentTool == DrawingTool.Select || vm.CurrentTool == DrawingTool.Eraser)
            {
                // 既に選択されているオブジェクトがあればハンドルのヒットテストを優先
                if (_selectedObject != null && vm.CurrentTool == DrawingTool.Select)
                {
                    int handleIdx = GetHandleHitIndex(_selectedObject, _startPoint);
                    if (handleIdx >= 0)
                    {
                        if (vm.IsCropMode && _selectedObject is ImageObject imgObj)
                        {
                            _isCropping = true;
                            _cropHandleIndex = handleIdx;
                            _originalResizeRect = GetBoundingRect(imgObj);
                            _originalCropRect = new SKRect(imgObj.CropX, imgObj.CropY, imgObj.CropX + imgObj.CropWidth, imgObj.CropY + imgObj.CropHeight);
                            SkiaElement.CaptureMouse();
                            return;
                        }
                        if (handleIdx == 4)
                        {
                            _isRotating = true;
                            _originalRotation = _selectedObject.Rotation; // 回転開始時の角度を記録
                            
                            var pivot = _selectedObject.GetRotationCenter();
                            float angleRad = (float)Math.Atan2(_startPoint.Y - pivot.Y, _startPoint.X - pivot.X);
                            _startRotationMouseAngle = angleRad * 180.0f / (float)Math.PI;

                            _originalResizeRect = GetBoundingRect(_selectedObject);
                            SkiaElement.CaptureMouse();
                            return;
                        }

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

                // 選択ツール: 前面(index 0)から背面へHitTest
                GraphicObject? hitObject = null;
                // 上にあるレイヤーから順にヒットテストを試行
                for (int layerIndex = 0; layerIndex < vm.Layers.Count; layerIndex++)
                {
                    var layer = vm.Layers[layerIndex];
                    if (!layer.IsVisible || layer.IsLocked) continue;

                    for (int i = layer.GraphicObjects.Count - 1; i >= 0; i--)
                    {
                        if (layer.GraphicObjects[i].HitTest(_startPoint))
                        {
                            hitObject = layer.GraphicObjects[i];
                            break;
                        }
                    }
                    if (hitObject != null) break;
                }

                bool isShiftHeld = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

                if (hitObject != null)
                {
                    if (vm.CurrentTool == DrawingTool.Select)
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
                            _selectedObject = vm.SelectedObject;
                        }
                    }
                }
                else
                {
                    if (!isShiftHeld && vm.CurrentTool == DrawingTool.Select)
                    {
                        // 何もない場所をクリック: 全選択解除
                        vm.ClearSelection();
                        _selectedObject = null;
                        
                        // 範囲選択開始
                        _isSelecting = true;
                        _selectionRect = new SKRect(_startPoint.X, _startPoint.Y, _startPoint.X, _startPoint.Y);
                        SkiaElement.CaptureMouse();
                        SkiaElement.InvalidateVisual();
                    }
                }
                
                if (_selectedObject != null && !isShiftHeld && vm.CurrentTool == DrawingTool.Select)
                {
                    _isDragging = true;
                    _originalDragRect = GetBoundingRect(_selectedObject);
                    _originalDragCorners = _selectedObject.GetTransformedCorners();
                    _preDragPositions.Clear();
                    foreach (var obj in vm.SelectedObjects)
                    {
                        if (obj is LineObject lineObj)
                        {
                            _preDragPositions.Add((obj, obj.X, obj.Y, lineObj.EndX, lineObj.EndY));
                        }
                        else
                        {
                            _preDragPositions.Add((obj, obj.X, obj.Y, null, null));   
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
                    FillColor = SKColors.Black,
                    StrokeWidth = 0,
                    StrokeColor = SKColors.Transparent
                };
                
                if (vm.ActiveLayer != null)
                {
                    vm.ExecuteCommand(new AddObjectCommand(vm.ActiveLayer.GraphicObjects, textObj));
                }
                vm.SelectObject(textObj);
                _selectedObject = vm.SelectedObject;
                
                vm.CurrentTool = DrawingTool.Select;
                SkiaElement.InvalidateVisual();
                
                StartInlineEditing(textObj, vm);
                e.Handled = true;
                return;
            }

            float strokeWidthToApply = 0.5f;
            if (System.Windows.Application.Current?.MainWindow?.DataContext is MainViewModel mainVM)
            {
                strokeWidthToApply = mainVM.CurrentStrokeWidth;
            }
            else if (this.DataContext is MainViewModel thisVM)
            {
                strokeWidthToApply = thisVM.CurrentStrokeWidth;
            }
            
            switch (vm.CurrentTool)
            {
               
                case DrawingTool.Rectangle:
                    _tempObject = new RectangleObject
                    {
                        X = _startPoint.X,
                        Y = _startPoint.Y,
                        Width = 0,
                        Height = 0,
                        FillColor = SKColors.SkyBlue.WithAlpha(128),
                        StrokeWidth = strokeWidthToApply
                    };
                    break;
                case DrawingTool.Ellipse:
                    _tempObject = new EllipseObject
                    {
                        X = _startPoint.X,
                        Y = _startPoint.Y,
                        Width = 0,
                        Height = 0,
                        FillColor = SKColors.Salmon.WithAlpha(128),
                        StrokeWidth = strokeWidthToApply,
                    };
                    break;
                case DrawingTool.Line:
                    _tempObject = new LineObject
                    {
                        X = _startPoint.X,
                        Y = _startPoint.Y,
                        EndX = _startPoint.X,
                        EndY = _startPoint.Y,
                        StrokeColor = SKColors.Black.WithAlpha(128),
                        StrokeWidth = strokeWidthToApply
                    };
                    break;
            }

            SkiaElement.CaptureMouse();
        }

        private void SkiaElement_MouseMove(object sender, MouseEventArgs e)
        {
            var vm = DataContext as CanvasViewModel;
            double zoom = vm?.ZoomLevel ?? 1.0;
            var pRaw = e.GetPosition(SkiaElement);
            var currentPoint = new SKPoint((float)(pRaw.X / zoom * (25.4 / 96.0)), (float)(pRaw.Y / zoom * (25.4 / 96.0)));
            var dx = currentPoint.X - _lastMousePos.X;
            var dy = currentPoint.Y - _lastMousePos.Y;
            _lastMousePos = currentPoint;

            // 消しゴムドラッグ中
            if (_isErasing && _eraserTarget != null)
            {
                _eraserRect = new SKRect(
                    Math.Min(_startPoint.X, currentPoint.X),
                    Math.Min(_startPoint.Y, currentPoint.Y),
                    Math.Max(_startPoint.X, currentPoint.X),
                    Math.Max(_startPoint.Y, currentPoint.Y)
                );
                ThrottledInvalidateVisual();
                return;
            }

            // 範囲選択ドラッグ中
            if (_isSelecting)
            {
                _selectionRect = new SKRect(
                    Math.Min(_startPoint.X, currentPoint.X),
                    Math.Min(_startPoint.Y, currentPoint.Y),
                    Math.Max(_startPoint.X, currentPoint.X),
                    Math.Max(_startPoint.Y, currentPoint.Y)
                );
                ThrottledInvalidateVisual();
                return;
            }

            // リサイズ中でなければカーソル更新
            if (!_isResizing && !_isDragging && !_isCropping && _selectedObject != null)
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
                        4 => System.Windows.Input.Cursors.Hand,     // Rotation
                        _ => System.Windows.Input.Cursors.Arrow
                    };
                }
            }

            if (_isRotating && _selectedObject != null)
            {
                var pivot = _selectedObject.GetRotationCenter();

                // 中心点から現在のマウス位置への角度を計算
                float angleRad = (float)Math.Atan2(currentPoint.Y - pivot.Y, currentPoint.X - pivot.X);
                float currentMouseAngle = angleRad * 180.0f / (float)Math.PI;

                // 開始時のマウス角度と現在の角度の差を、開始時のオブジェクト角度に加える
                float rotation = _originalRotation + (currentMouseAngle - _startRotationMouseAngle);

                // Shiftキー押下で15度刻みスナップ
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    rotation = (float)Math.Round(rotation / 15.0f) * 15.0f;
                }

                // 360度の範囲に収める (オプション)
                rotation = (rotation % 360 + 360) % 360;

                _selectedObject.Rotation = rotation;
                ThrottledInvalidateVisual();
                return;
            }

            if (_isCropping && _selectedObject is ImageObject cropImgObj)
            {
                var rect = GetBoundingRect(cropImgObj);
                // 元の描画範囲と元の切り抜き範囲の比率
                float scaleX = _originalCropRect.Width > 0 ? _originalResizeRect.Width / _originalCropRect.Width : 1;
                float scaleY = _originalCropRect.Height > 0 ? _originalResizeRect.Height / _originalCropRect.Height : 1;

                // dx, dy はキャンバス上の増分移動量だが、_originalからの累積計算のために totalDx, totalDy を使用
                float totalDx = currentPoint.X - _startPoint.X;
                float totalDy = currentPoint.Y - _startPoint.Y;

                // これを元の画像サイズ(ピクセル)上の移動量に変換
                float cropDx = totalDx / scaleX;
                float cropDy = totalDy / scaleY;

                float newCropLeft = _originalCropRect.Left;
                float newCropTop = _originalCropRect.Top;
                float newCropRight = _originalCropRect.Right;
                float newCropBottom = _originalCropRect.Bottom;

                float newXLeft = _originalResizeRect.Left;
                float newXRight = _originalResizeRect.Right;
                float newYTop = _originalResizeRect.Top;
                float newYBottom = _originalResizeRect.Bottom;

                switch (_cropHandleIndex)
                {
                    case 0: // TopLeft
                        newCropLeft += cropDx; newCropTop += cropDy;
                        newXLeft += totalDx; newYTop += totalDy;
                        break;
                    case 1: // TopRight
                        newCropRight += cropDx; newCropTop += cropDy;
                        newXRight += totalDx; newYTop += totalDy;
                        break;
                    case 2: // BottomRight
                        newCropRight += cropDx; newCropBottom += cropDy;
                        newXRight += totalDx; newYBottom += totalDy;
                        break;
                    case 3: // BottomLeft
                        newCropLeft += cropDx; newCropBottom += cropDy;
                        newXLeft += totalDx; newYBottom += totalDy;
                        break;
                }

                // 反転（負の幅・高さ）を防ぐ、または反転した場合は最小値最大値で整理する
                float cX = Math.Min(newCropLeft, newCropRight);
                float cY = Math.Min(newCropTop, newCropBottom);
                float cW = Math.Abs(newCropRight - newCropLeft);
                float cH = Math.Abs(newCropBottom - newCropTop);

                float dX = Math.Min(newXLeft, newXRight);
                float dY = Math.Min(newYTop, newYBottom);
                float dW = Math.Abs(newXRight - newXLeft);
                float dH = Math.Abs(newYBottom - newYTop);

                // 画像の元のピクセル幅/高さを超えないように制限を入れることも可能だが、一旦は自由枠とする
                cropImgObj.CropX = cX;
                cropImgObj.CropY = cY;
                cropImgObj.CropWidth = cW;
                cropImgObj.CropHeight = cH;

                cropImgObj.X = dX;
                cropImgObj.Y = dY;
                cropImgObj.Width = dW;
                cropImgObj.Height = dH;

                ThrottledInvalidateVisual();
                return;
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
                    // キャンバス上の増分移動量を、オブジェクトのローカル回転に応じて変換
                    float localDx = dx;
                    float localDy = dy;
                    if (_selectedObject.Rotation != 0)
                    {
                        float rad = -_selectedObject.Rotation * (float)Math.PI / 180.0f;
                        float cos = (float)Math.Cos(rad);
                        float sin = (float)Math.Sin(rad);
                        localDx = dx * cos - dy * sin;
                        localDy = dx * sin + dy * cos;
                    }

                    // 矩形・楕円・テキスト・画像などのリサイズ
                    var rect = GetBoundingRect(_selectedObject);
                    float left = rect.Left, top = rect.Top, right = rect.Right, bottom = rect.Bottom;

                    switch (_resizeHandleIndex)
                    {
                        case 0: left += localDx; top += localDy; break;      // TopLeft
                        case 1: right += localDx; top += localDy; break;     // TopRight
                        case 2: right += localDx; bottom += localDy; break;  // BottomRight
                        case 3: left += localDx; bottom += localDy; break;   // BottomLeft
                    }

                    // Shiftが押されている場合は縦横比を維持
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && _originalAspectRatio > 0)
                    {
                        float newWidth = Math.Abs(right - left);
                        float newHeight = Math.Abs(bottom - top);

                        if (Math.Abs(localDx) >= Math.Abs(localDy)) newHeight = newWidth / _originalAspectRatio;
                        else newWidth = newHeight * _originalAspectRatio;

                        switch (_resizeHandleIndex)
                        {
                            case 0: left = right - newWidth; top = bottom - newHeight; break;
                            case 1: right = left + newWidth; top = bottom - newHeight; break;
                            case 2: right = left + newWidth; bottom = top + newHeight; break;
                            case 3: left = right - newWidth; bottom = top + newHeight; break;
                        }
                    }

                    // 幅・高さが負にならないよう調整
                    _selectedObject.X = Math.Min(left, right);
                    _selectedObject.Y = Math.Min(top, bottom);
                    _selectedObject.Width = Math.Max(0.1f, Math.Abs(right - left));
                    _selectedObject.Height = Math.Max(0.1f, Math.Abs(bottom - top));

                    // GroupObject の場合は子オブジェクトも比例的にスケーリング
                    if (_selectedObject is GroupObject groupObj && rect.Width > 0 && rect.Height > 0)
                    {
                        float scaleW = _selectedObject.Width / rect.Width;
                        float scaleH = _selectedObject.Height / rect.Height;

                        foreach (var child in groupObj.Children)
                        {
                            float relX = (child.X - rect.Left) / rect.Width;
                            float relY = (child.Y - rect.Top) / rect.Height;
                            child.X = _selectedObject.X + relX * _selectedObject.Width;
                            child.Y = _selectedObject.Y + relY * _selectedObject.Height;
                            child.Width *= scaleW;
                            child.Height *= scaleH;

                            if (child is LineObject childLine)
                            {
                                float relEndX = (childLine.EndX - rect.Left) / rect.Width;
                                float relEndY = (childLine.EndY - rect.Top) / rect.Height;
                                childLine.EndX = _selectedObject.X + relEndX * _selectedObject.Width;
                                childLine.EndY = _selectedObject.Y + relEndY * _selectedObject.Height;
                            }
                        }
                    }
                }
                
                ThrottledInvalidateVisual();
                return;
            }

            if (_isDragging && _selectedObject != null)
            {
                // ドラッグ開始時からの総移動量
                float totalDx = currentPoint.X - _startPoint.X;
                float totalDy = currentPoint.Y - _startPoint.Y;

                // スナップガイドの初期化
                _snapGuideX = null;
                _snapGuideY = null;
                _snapXTarget = null;
                _snapYTarget = null;

                // 選択中オブジェクトの元の矩形と予定される矩形
                var targetRect = _originalDragRect;

                // 回転後の頂点座標をtotalDx/totalDyで移動した位置を計算
                var draggedXSet = new HashSet<float>();
                var draggedYSet = new HashSet<float>();
                foreach (var c in _originalDragCorners)
                {
                    draggedXSet.Add((float)Math.Round(c.X + totalDx, 1));
                    draggedYSet.Add((float)Math.Round(c.Y + totalDy, 1));
                }
                // 中心座標も追加
                float draggedCenterX = _originalDragCorners.Average(c => c.X) + totalDx;
                float draggedCenterY = _originalDragCorners.Average(c => c.Y) + totalDy;
                draggedXSet.Add((float)Math.Round(draggedCenterX, 1));
                draggedYSet.Add((float)Math.Round(draggedCenterY, 1));

                float snapThreshold = (vm != null && vm.ZoomLevel != 0) ? 10.0f / (float)vm.ZoomLevel : 10.0f;

                float closestDistX = float.MaxValue;
                float closestDistY = float.MaxValue;
                float snapOffsetX = 0;
                float snapOffsetY = 0;

                // 他のオブジェクトに対するスナップ判定（スナップ有効かつShiftキーが押されていない場合のみ）
                if (vm != null && vm.IsSnapEnabled && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && vm.ActiveLayer != null)
                {
                    float[] targetXLines = draggedXSet.ToArray();
                    float[] targetYLines = draggedYSet.ToArray();

                    foreach (var layer in vm.Layers)
                    {
                        if (!layer.IsVisible || layer.IsLocked) continue;

                        foreach (var obj in layer.GraphicObjects)
                        {
                            if (vm.SelectedObjects.Contains(obj)) continue;

                            // 回転後の頂点座標を取得
                            var corners = obj.GetTransformedCorners();

                            // 頂点から各軸座標を収集（重複を避けるためHashSetを使用）
                            var otherXSet = new HashSet<float>();
                            var otherYSet = new HashSet<float>();
                            foreach (var c in corners)
                            {
                                otherXSet.Add((float)Math.Round(c.X, 1));
                                otherYSet.Add((float)Math.Round(c.Y, 1));
                            }

                            // 中心座標も追加
                            float centerX = corners.Average(c => c.X);
                            float centerY = corners.Average(c => c.Y);
                            otherXSet.Add((float)Math.Round(centerX, 1));
                            otherYSet.Add((float)Math.Round(centerY, 1));

                            // X軸スナップ
                            foreach (var tx in targetXLines)
                            {
                                foreach (var ox in otherXSet)
                                {
                                    float dist = Math.Abs(tx - ox);
                                    if (dist < snapThreshold && dist < closestDistX)
                                    {
                                        closestDistX = dist;
                                        snapOffsetX = ox - tx;
                                        _snapGuideX = ox;
                                        _snapXTarget = obj;
                                    }
                                }
                            }

                            // Y軸スナップ
                            foreach (var ty in targetYLines)
                            {
                                foreach (var oy in otherYSet)
                                {
                                    float dist = Math.Abs(ty - oy);
                                    if (dist < snapThreshold && dist < closestDistY)
                                    {
                                        closestDistY = dist;
                                        snapOffsetY = oy - ty;
                                        _snapGuideY = oy;
                                        _snapYTarget = obj;
                                    }
                                }
                            }
                        }
                    }
                }

                // スナップを適用した最終的な総移動量
                totalDx += snapOffsetX;
                totalDy += snapOffsetY;

                if (vm != null && _preDragPositions.Count > 0)
                {
                    foreach (var (obj, oldX, oldY, oldEndX, oldEndY) in _preDragPositions)
                    {
                        // GroupObjectなどの再帰移動用に、今回のフレームでの「実際の差分」を計算しておく
                        float actualDx = (oldX + totalDx) - obj.X;
                        float actualDy = (oldY + totalDy) - obj.Y;

                        // 記憶している初期座標に総移動量を足して直接設定する
                        obj.X = oldX + totalDx;
                        obj.Y = oldX + totalDy;
                    
                        if (obj is LineObject lineObj && oldEndX.HasValue && oldEndY.HasValue)
                        {                           
                            lineObj.EndX = oldEndX.Value + totalDx;
                            lineObj.EndY = oldEndY.Value + totalDy;
                        }
                    }
                }

                ThrottledInvalidateVisual();
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

            ThrottledInvalidateVisual();
        }

        private void SkiaElement_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not CanvasViewModel vmObj) return;

            // 消しゴム操作終了
            if (_isErasing)
            {
                if (_eraserRect.Width > 0 && _eraserRect.Height > 0)
                {
                    ImageObject? targetImg = _eraserTarget;

                    if (targetImg == null)
                    {
                        // ドラッグ開始時に画像がなかった場合、まずは選択中の画像を優先する
                        if (vmObj.SelectedObject is ImageObject selectedImg && _eraserRect.IntersectsWith(GetBoundingRect(selectedImg)))
                        {
                            targetImg = selectedImg;
                        }
                        else if (vmObj.SelectedObject is GroupObject selectedGrp)
                        {
                            targetImg = selectedGrp.Children.OfType<ImageObject>().FirstOrDefault(img => _eraserRect.IntersectsWith(GetBoundingRect(img)));
                        }

                        // それでも見つからない場合は、矩形に交差するImageObjectを探す（アクティブレイヤー内を優先）
                        if (targetImg == null && vmObj.ActiveLayer != null)
                        {
                            for (int i = vmObj.ActiveLayer.GraphicObjects.Count - 1; i >= 0; i--)
                            {
                                var obj = vmObj.ActiveLayer.GraphicObjects[i];
                                if (obj is ImageObject img && _eraserRect.IntersectsWith(GetBoundingRect(img)))
                                {
                                    targetImg = img;
                                    break;
                                }
                                if (obj is GroupObject group)
                                {
                                    targetImg = group.Children.OfType<ImageObject>().FirstOrDefault(img => _eraserRect.IntersectsWith(GetBoundingRect(img)));
                                    if (targetImg != null) break;
                                }
                            }
                        }
                    }

                    if (targetImg != null)
                    {
                        System.IO.File.AppendAllText("eraser_debug.log", $"MouseUp: Applying eraser to {targetImg.GetHashCode()}, rect={GetBoundingRect(targetImg)}\n");
                        
                        // Maskが未作成の場合のため
                        targetImg.EnsureEraserMask();
                        var oldMask = _preEraserMask; 
                        
                        ApplyEraserRectToImage(targetImg, _eraserRect);
                        
                        // 新しいMaskと古いMaskの差分をコマンドとして登録する
                        var newMask = targetImg.EraserMask;
                        vmObj.ExecuteCommand(new EraserCommand(targetImg, oldMask, newMask));
                    }
                    else
                    {
                        System.IO.File.AppendAllText("eraser_debug.log", $"MouseUp: No target found for eraser\n");
                    }
                }
                _isErasing = false;
                _eraserTarget = null;
                _preEraserMask = null;
                SkiaElement.ReleaseMouseCapture();
                SkiaElement.InvalidateVisual();
                return;
            }

            if (_isSelecting)
            {
                _isSelecting = false;
                if (_selectionRect.Width > 0.1f || _selectionRect.Height > 0.1f)
                {
                    // 矩形内のオブジェクトを選択
                    bool anyHit = false;
                    foreach (var layer in vmObj.Layers)
                    {
                        if (!layer.IsVisible || layer.IsLocked) continue;
                        foreach (var obj in layer.GraphicObjects)
                        {
                            if (_selectionRect.Contains(GetBoundingRect(obj)))
                            {
                                vmObj.ToggleSelectObject(obj);
                                anyHit = true;
                            }
                        }
                    }
                    if (anyHit)
                    {
                        _selectedObject = vmObj.SelectedObject;
                    }
                }
                SkiaElement.ReleaseMouseCapture();
                SkiaElement.InvalidateVisual();
                return;
            }

            if (_isCropping)
            {
                _isCropping = false;
                if (_selectedObject is ImageObject imgObj)
                {
                    var cmd = new CropImageCommand(imgObj,
                        _originalResizeRect.Left, _originalResizeRect.Top, _originalResizeRect.Width, _originalResizeRect.Height,
                        imgObj.X, imgObj.Y, imgObj.Width, imgObj.Height,
                        _originalCropRect.Left, _originalCropRect.Top, _originalCropRect.Width, _originalCropRect.Height,
                        imgObj.CropX, imgObj.CropY, imgObj.CropWidth, imgObj.CropHeight);
                    vmObj.ExecuteCommand(cmd);
                }
                _cropHandleIndex = -1;
                SkiaElement.ReleaseMouseCapture();
                return;
            }

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

            if (_isRotating)
            {
                _isRotating = false;
                if (_selectedObject != null && _selectedObject.Rotation != _originalRotation)
                {
                    vmObj.ExecuteCommand(new RotateCommand(_selectedObject, _originalRotation, _selectedObject.Rotation));
                }
                SkiaElement.ReleaseMouseCapture();
                return;
            }

            if (_isDragging)
            {
                _isDragging = false;

                // スナップガイドのクリア
                _snapGuideX = null;
                _snapGuideY = null;
                _snapXTarget = null;
                _snapYTarget = null;

                if (_selectedObject != null && _preDragPositions.Count > 0)
                {
                    // 実際に動いたか確認
                    bool hasMoved = _preDragPositions.Any(p => p.Obj.X != p.OldX || p.Obj.Y != p.OldY);
                    if (hasMoved)
                    {
                        var moves = new List<(GraphicObject Obj, float OldX, float OldY, float NewX, float NewY)>();
                        foreach (var (obj, oldX, oldY, oldEndX, oldEndY) in _preDragPositions)
                        {
                            moves.Add((obj, oldX, oldY, obj.X, obj.Y));
                        }
                        vmObj.ExecuteCommand(new MoveObjectsCommand(moves));
                    }
                }
                SkiaElement.ReleaseMouseCapture();
                SkiaElement.InvalidateVisual();
                return;
            }

            if (_tempObject == null) return;

            if (DataContext is CanvasViewModel vmAdd && vmAdd.ActiveLayer != null)
            {
                // 一時オブジェクトを本番の色に変更して追加
                if (_tempObject is RectangleObject) _tempObject.FillColor = SKColors.SkyBlue;
                if (_tempObject is EllipseObject) _tempObject.FillColor = SKColors.Salmon;
                if (_tempObject is LineObject) _tempObject.StrokeColor = SKColors.Black;
                
                vmAdd.ExecuteCommand(new AddObjectCommand(vmAdd.ActiveLayer.GraphicObjects, _tempObject));
            }

            _tempObject = null;
            SkiaElement.ReleaseMouseCapture();
            SkiaElement.InvalidateVisual();
        }

        /// <summary>
        /// スナップ先オブジェクトの外枠を強調描画するヘルパー
        /// </summary>
        private void DrawSnapTargetHighlight(SKCanvas canvas, GraphicObject target, SKPaint paint)
        {
            var corners = target.GetTransformedCorners();
            if (corners.Length < 2) return;

            using var path = new SKPath();
            path.MoveTo(corners[0]);
            for (int i = 1; i < corners.Length; i++)
            {
                path.LineTo(corners[i]);
            }
            path.Close();
            canvas.DrawPath(path, paint);
        }

        private int GetHandleHitIndex(GraphicObject obj, SKPoint hitPoint)
        {
            float zoom = (float)(ViewModel?.ZoomLevel ?? 1.0f);
            float handleRadius = 6.0f / (float)zoom * (96.0f / 25.4f) / 2.0f; // 約6px程度のヒット判定
            if (handleRadius < 4.0f / (float)zoom) handleRadius = 4.0f / (float)zoom; // 最小ヒット半径
            
            // 図形の回転を加味するため、マウス座標を図形のローカル座標系に逆変換する
            var localHitPoint = obj.UntransformPoint(hitPoint);

            if (obj is LineObject lineObj)
            {
                var p0 = new SKPoint(lineObj.X, lineObj.Y);
                var p1 = new SKPoint(lineObj.EndX, lineObj.EndY);
                
                if (HitTestHandle(p0, localHitPoint, handleRadius)) return 0;
                if (HitTestHandle(p1, localHitPoint, handleRadius)) return 1;
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
                if (HitTestHandle(points[i], localHitPoint, handleRadius)) return i;
            }

            // 回転ハンドル (インデックス 4) の判定
            float rotationHandleOffset = obj.SelectionBoxRotationHandleOffset;
            float midX = (rect.Left + rect.Right) / 2;
            var rotationHandlePos = new SKPoint(midX, rect.Top - rotationHandleOffset);
            if (HitTestHandle(rotationHandlePos, localHitPoint, handleRadius)) return 4;

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
                if (string.IsNullOrEmpty(textObj.Text))
                    return new SKRect(obj.X, obj.Y, obj.X + 10, obj.Y + textObj.FontSize);

                using var typeface = SKTypeface.FromFamilyName(textObj.FontFamily,
                    textObj.IsBold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                    SKFontStyleWidth.Normal,
                    textObj.IsItalic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
                using var font = new SKFont(typeface, textObj.FontSize);

                var lines = textObj.Text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                float spacing = font.Spacing;
                float maxWidth = 0;

                foreach (var line in lines)
                {
                    maxWidth = Math.Max(maxWidth, font.MeasureText(line));
                }

                float totalHeight = (lines.Length - 1) * spacing + textObj.FontSize;
                float left = obj.X;
                if (textObj.HorizontalAlignment == SKTextAlign.Center) left -= maxWidth / 2;
                else if (textObj.HorizontalAlignment == SKTextAlign.Right) left -= maxWidth;

                return new SKRect(left, obj.Y, left + maxWidth, obj.Y + totalHeight);
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
            else if (e.Key == Key.X && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // Ctrl+X: 切り取り
                if (vm.CutCommand.CanExecute(null))
                {
                    vm.CutCommand.Execute(null);
                    _selectedObject = null;
                }
                e.Handled = true;
            }
            else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // Ctrl+A: 全て選択
                vm.SelectAllCommand.Execute(null);
                _selectedObject = vm.SelectedObject;
                SkiaElement.InvalidateVisual();
                e.Handled = true;
            }
            else if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // クリップボードから画像をペースト（複数形式に対応）
                var (skBitmap, dpiX, dpiY) = TryGetImageFromClipboard();
                if (skBitmap != null)
                {
                    vm.ImportImageAsGroup(skBitmap, dpiX, dpiY);
                }
                else if (vm.PasteCommand.CanExecute(null))
                {
                    vm.PasteCommand.Execute(null);
                    if (vm.SelectedObject != null)
                    {
                        vm.SelectObject(vm.SelectedObject); // レイヤー同期のため呼び出し
                        _selectedObject = vm.SelectedObject;
                    }
                }
                e.Handled = true;
            }
            else if (e.Key == Key.G && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // Ctrl+G: グループ化
                if (vm.GroupCommand.CanExecute(null))
                {
                    vm.GroupCommand.Execute(null);
                    if (vm.SelectedObject != null)
                    {
                        vm.SelectObject(vm.SelectedObject);
                        _selectedObject = vm.SelectedObject;
                    }
                }
                e.Handled = true;
            }
            else if (e.Key == Key.G && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                // Ctrl+Shift+G: グループ解除
                if (vm.UngroupCommand.CanExecute(null))
                {
                    vm.UngroupCommand.Execute(null);
                    if (vm.SelectedObject != null)
                    {
                        vm.SelectObject(vm.SelectedObject);
                        _selectedObject = vm.SelectedObject;
                    }
                }
                e.Handled = true;
            }
            else if ((e.Key == Key.OemPlus || e.Key == Key.Add) && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                vm.ZoomInCommand.Execute(null);
                e.Handled = true;
            }
            else if ((e.Key == Key.OemMinus || e.Key == Key.Subtract) && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                vm.ZoomOutCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.D0 && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                vm.ResetZoomCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.U && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // Ctrl+U: スナップON/OFF切替
                vm.IsSnapEnabled = !vm.IsSnapEnabled;
                SkiaElement.InvalidateVisual();
                e.Handled = true;
            }
        }

        /// <summary>
        /// クリップボードから画像を取得（複数形式に対応）
        /// </summary>
        private (SKBitmap? bitmap, float dpiX, float dpiY) TryGetImageFromClipboard()
        {
            try
            {
                var dataObject = System.Windows.Clipboard.GetDataObject();
                if (dataObject == null) return (null, 96f, 96f);

                // 1) PNG ストリーム直接デコード（最も確実・ImageJ対応）
                if (dataObject.GetDataPresent("PNG"))
                {
                    var pngData = dataObject.GetData("PNG") as System.IO.MemoryStream;
                    if (pngData != null)
                    {
                        pngData.Position = 0;
                        var result = SKBitmap.Decode(pngData);
                        if (result != null) return (result, 96f, 96f);
                    }
                }

                // 2) WPF標準の画像形式（PngBitmapEncoder経由で変換）
                if (System.Windows.Clipboard.ContainsImage())
                {
                    var bitmapSource = System.Windows.Clipboard.GetImage();
                    if (bitmapSource != null)
                    {
                        var result = ConvertBitmapSourceToSKBitmap(bitmapSource);
                        if (result != null) return (result, (float)bitmapSource.DpiX, (float)bitmapSource.DpiY);
                    }
                }

                // 3) DIB/Bitmap 形式
                if (dataObject.GetDataPresent(System.Windows.DataFormats.Bitmap))
                {
                    var data = dataObject.GetData(System.Windows.DataFormats.Bitmap);
                    if (data is System.Windows.Media.Imaging.BitmapSource bmpSrc)
                    {
                        var result = ConvertBitmapSourceToSKBitmap(bmpSrc);
                        if (result != null) return (result, (float)bmpSrc.DpiX, (float)bmpSrc.DpiY);
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
                            var dpi = FigCrafterApp.Helpers.ImportHelper.GetImageDpi(files[0]);
                            using var stream = System.IO.File.OpenRead(files[0]);
                            return (SKBitmap.Decode(stream), dpi.dpiX, dpi.dpiY);
                        }
                    }
                }
            }
            catch
            {
                // クリップボードアクセスに失敗した場合は null
            }
            return (null, 96f, 96f);
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

        /// <summary>
        /// 消しゴム操作：キャンバス座標の矩形を画像ピクセル座標の矩形に変換してApplyEraserRectを呼び出す
        /// </summary>
        private void ApplyEraserRectToImage(ImageObject imgObj, SKRect canvasRect)
        {
            if (imgObj.ImageData == null) return;

            // 画面座標の四隅を画像のローカル（回転前）座標に変換
            var pTL = imgObj.UntransformPoint(new SKPoint(canvasRect.Left, canvasRect.Top));
            var pTR = imgObj.UntransformPoint(new SKPoint(canvasRect.Right, canvasRect.Top));
            var pBL = imgObj.UntransformPoint(new SKPoint(canvasRect.Left, canvasRect.Bottom));
            var pBR = imgObj.UntransformPoint(new SKPoint(canvasRect.Right, canvasRect.Bottom));

            // ローカル座標 -> 画像ピクセル座標に変換する共通スケール
            float scaleX = imgObj.CropWidth / imgObj.Width;
            float scaleY = imgObj.CropHeight / imgObj.Height;
            
            SKPoint ToPixel(SKPoint localP)
            {
                return new SKPoint(
                    imgObj.CropX + (localP.X - imgObj.X) * scaleX,
                    imgObj.CropY + (localP.Y - imgObj.Y) * scaleY
                );
            }

            var pxTL = ToPixel(pTL);
            var pxTR = ToPixel(pTR);
            var pxBL = ToPixel(pBL);
            var pxBR = ToPixel(pBR);

            float minX = Math.Min(Math.Min(pxTL.X, pxTR.X), Math.Min(pxBL.X, pxBR.X));
            float minY = Math.Min(Math.Min(pxTL.Y, pxTR.Y), Math.Min(pxBL.Y, pxBR.Y));
            float maxX = Math.Max(Math.Max(pxTL.X, pxTR.X), Math.Max(pxBL.X, pxBR.X));
            float maxY = Math.Max(Math.Max(pxTL.Y, pxTR.Y), Math.Max(pxBL.Y, pxBR.Y));

            var pixelRect = new SKRect(minX, minY, maxX, maxY);

            imgObj.ApplyEraserRect(pixelRect);
        }

        /// <summary>
        /// GroupObject内のImageObjectを再帰的に検索する
        /// </summary>
        private ImageObject? FindImageInGroup(GroupObject group, SKPoint point)
        {
            for (int i = group.Children.Count - 1; i >= 0; i--)
            {
                var child = group.Children[i];
                if (child is ImageObject img && img.HitTest(point))
                    return img;
                if (child is GroupObject nestedGroup && child.HitTest(point))
                {
                    var found = FindImageInGroup(nestedGroup, point);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private void CanvasScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && DataContext is CanvasViewModel vm)
            {
                e.Handled = true; // デフォルトのスクロール動作を無効化
                
                // 現在のキャンバス左上を基準にしたマウス座標と、実際に見えている領域のマウス座標
                Point mousePos = e.GetPosition(CanvasScrollViewer);
                double posXInCanvas = CanvasScrollViewer.HorizontalOffset + mousePos.X;
                double posYInCanvas = CanvasScrollViewer.VerticalOffset + mousePos.Y;
                
                // ズーム変更前の論理座標
                double logX = posXInCanvas / vm.ZoomLevel;
                double logY = posYInCanvas / vm.ZoomLevel;

                // ズーム処理
                if (e.Delta > 0) vm.ZoomInCommand.Execute(null);
                else vm.ZoomOutCommand.Execute(null);

                // ズーム変更後のピクセル座標
                double newPosXInCanvas = logX * vm.ZoomLevel;
                double newPosYInCanvas = logY * vm.ZoomLevel;

                // スクロール位置の強制更新
                CanvasScrollViewer.UpdateLayout();
                CanvasScrollViewer.ScrollToHorizontalOffset(newPosXInCanvas - mousePos.X);
                CanvasScrollViewer.ScrollToVerticalOffset(newPosYInCanvas - mousePos.Y);

                // ズーム時にインライン編集を終了させる
                if (_editingTextObject != null) EndInlineEditing();
            }
        }

        private void StartInlineEditing(TextObject textObj, CanvasViewModel vm)
        {
            _editingTextObject = textObj;
            _editingOriginalText = textObj.Text;

            // 編集中は元のテキスト描画を非表示にする
            // この変更はUndoに記録しない
            vm.IsUndoSuppressed = true;
            try
            {
                textObj.Text = "";
            }
            finally
            {
                vm.IsUndoSuppressed = false;
            }
            vm.Invalidate();

            // 単位変換係数 (mm -> px)
            const double mmToPx = 96.0 / 25.4;

            InlineEditingTextBox.Text = _editingOriginalText;
            InlineEditingTextBox.FontFamily = new System.Windows.Media.FontFamily(textObj.FontFamily);
            // FontSize を mm から px に変換し、ズームを適用
            InlineEditingTextBox.FontSize = textObj.FontSize * mmToPx * vm.ZoomLevel;
            InlineEditingTextBox.FontWeight = textObj.IsBold ? FontWeights.Bold : FontWeights.Normal;
            InlineEditingTextBox.FontStyle = textObj.IsItalic ? FontStyles.Italic : FontStyles.Normal;

            // TextBoxの配置を設定
            InlineEditingTextBox.TextAlignment = textObj.HorizontalAlignment switch
            {
                SKTextAlign.Center => TextAlignment.Center,
                SKTextAlign.Right => TextAlignment.Right,
                _ => TextAlignment.Left
            };

            // TextBoxの位置（Margin）を計算。アンカー点 X から、配置に応じたオフセットを適用
            using var typefaceForMeasure = SKTypeface.FromFamilyName(textObj.FontFamily);
            using var fontForMeasure = new SKFont(typefaceForMeasure, textObj.FontSize);

            float maxWidth = 0;
            var lines = _editingOriginalText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            foreach (var line in lines) maxWidth = Math.Max(maxWidth, fontForMeasure.MeasureText(line));
            
            float visualLeft = textObj.X;
            if (textObj.HorizontalAlignment == SKTextAlign.Center) visualLeft -= maxWidth / 2;
            else if (textObj.HorizontalAlignment == SKTextAlign.Right) visualLeft -= maxWidth;

            double offsetX = visualLeft * mmToPx * vm.ZoomLevel - 1;
            double offsetY = textObj.Y * mmToPx * vm.ZoomLevel - 1;
            InlineEditingTextBox.Margin = new Thickness(offsetX, offsetY, 0, 0);
            
            InlineEditingTextBox.TextWrapping = TextWrapping.NoWrap;
            InlineEditingTextBox.AcceptsReturn = true; // 明示的に改行した場合には改行する

            InlineEditingTextBox.MinWidth = maxWidth * mmToPx * vm.ZoomLevel + 8;
            InlineEditingTextBox.Width = double.NaN; // Width="Auto"に相当
            InlineEditingTextBox.MinHeight = (textObj.FontSize * mmToPx * vm.ZoomLevel) + 4;
            
            // 回転の適用
            if (Math.Abs(textObj.Rotation) > 0.01)
            {
                // SkiaSharpのアンカーポイントとTextBoxの左端との差分をスケール変換
                double anchorLocalX = (textObj.X - visualLeft) * mmToPx * vm.ZoomLevel;
                double anchorLocalY = 0;

                InlineEditingTextBox.RenderTransformOrigin = new Point(0, 0);
                InlineEditingTextBox.RenderTransform = new System.Windows.Media.RotateTransform(textObj.Rotation, anchorLocalX, anchorLocalY);
            }
            else
            {
                InlineEditingTextBox.RenderTransform = System.Windows.Media.Transform.Identity;
            }

            vm.ClearSelection();
            _selectedObject = null;
            InlineEditingTextBox.Visibility = Visibility.Visible;

            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                InlineEditingTextBox.Focus();
                InlineEditingTextBox.SelectAll();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void EndInlineEditing(bool isCancel = false)
        {
            if (_editingTextObject == null) return;
            var vm = DataContext as CanvasViewModel;
            if (vm == null) return;
            
            // 編集結果を取得（キャンセル時は元のテキストを使用）
            string newText = isCancel ? _editingOriginalText : InlineEditingTextBox.Text;
            if (string.IsNullOrEmpty(newText)) newText = _editingOriginalText;

            // プロパティ変更通知が走らないように抑制
            vm.IsUndoSuppressed = true;
            try
            {
                // 一旦、表示用に隠していたテキストを元の値に戻す
                _editingTextObject.Text = _editingOriginalText;
            }
            finally
            {
                vm.IsUndoSuppressed = false;
            }

            // キャンセルでない、かつ変更がある場合のみコマンド登録
            if (!isCancel && newText != _editingOriginalText)
            {
                vm.ExecuteCommand(new PropertyChangeCommand(_editingTextObject, nameof(TextObject.Text), _editingOriginalText, newText));
            }
            else
            {
                vm.Invalidate();
            }
            
            vm.SelectObject(_editingTextObject);
            _selectedObject = vm.SelectedObject;

            // UI終了処理
            InlineEditingTextBox.Visibility = Visibility.Hidden;
            _editingTextObject = null;
            _editingOriginalText = "";
        }

        private void InlineEditingTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            EndInlineEditing();
        }

        private void InlineEditingTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                // 編集中の Undo は「編集のキャンセル」として扱う
                EndInlineEditing(true);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    // Ctrl+Enter: 改行を挿入
                    var tb = (System.Windows.Controls.TextBox)sender;
                    int caretIndex = tb.CaretIndex;
                    tb.Text = tb.Text.Insert(caretIndex, "\n");
                    tb.CaretIndex = caretIndex + 1;
                    e.Handled = true;
                }
                else
                {
                    // Enter: 編集確定
                    EndInlineEditing();
                    e.Handled = true;
                    SkiaElement.Focus();
                }
            }
            else if (e.Key == Key.Escape)
            {
                // キャンセル: 元のテキストを復元
                if (_editingTextObject != null)
                {
                    _editingTextObject.Text = _editingOriginalText;
                    (DataContext as CanvasViewModel)?.Invalidate();
                }
                InlineEditingTextBox.Visibility = Visibility.Hidden;
                _editingTextObject = null;
                _editingOriginalText = "";
                SkiaElement.Focus();
                e.Handled = true;
            }
        }

        private void SkiaElement_PreviewDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2) return; // ダブルクリックのみ処理
            if (DataContext is not CanvasViewModel vm) return;
            if (vm.CurrentTool != DrawingTool.Select) return;

            var pRaw = e.GetPosition(SkiaElement);
            var point = new SKPoint((float)(pRaw.X / vm.ZoomLevel * (25.4 / 96.0)), (float)(pRaw.Y / vm.ZoomLevel * (25.4 / 96.0)));

            // ヒットテストでTextObjectを探す
            for (int layerIndex = 0; layerIndex < vm.Layers.Count; layerIndex++)
            {
                var layer = vm.Layers[layerIndex];
                if (!layer.IsVisible || layer.IsLocked) continue;

                for (int i = layer.GraphicObjects.Count - 1; i >= 0; i--)
                {
                    if (layer.GraphicObjects[i] is TextObject textObj && textObj.HitTest(point))
                    {
                        // ドラッグ操作を即座にキャンセル
                        _isDragging = false;
                        if (SkiaElement.IsMouseCaptured)
                            SkiaElement.ReleaseMouseCapture();

                        StartInlineEditing(textObj, vm);
                        e.Handled = true;
                        return;
                    }
                }
            }
        }
    }
}
