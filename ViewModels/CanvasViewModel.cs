using System.Collections.ObjectModel;
using System.Windows.Input;
using SkiaSharp;
using FigCrafterApp.Models;

namespace FigCrafterApp.ViewModels
{
    public enum DrawingTool
    {
        Select,
        Rectangle,
        Ellipse,
        Line,
        Text
    }

    public class CanvasViewModel : ViewModelBase
    {
        private const double Dpi = 96.0;
        private const double MmPerInch = 25.4;

        private string _title = "名称未設定";
        private double _widthMm = 210; // A4幅
        private double _heightMm = 297; // A4高さ
        private DrawingTool _currentTool = DrawingTool.Select;
        private ObservableCollection<GraphicObject> _graphicObjects = new();
        private GraphicObject? _selectedObject;
        private ObservableCollection<GraphicObject> _selectedObjects = new(); // 複数選択
        private GraphicObject? _clipboard; // コピー用クリップボード

        public event EventHandler? InvalidateRequested;

        public ICommand BringToFrontCommand { get; }
        public ICommand SendToBackCommand { get; }
        public ICommand BringForwardCommand { get; }
        public ICommand SendBackwardCommand { get; }
        public ICommand DeleteSelectedCommand { get; }
        public ICommand CopyCommand { get; }
        public ICommand PasteCommand { get; }
        public ICommand AlignLeftCommand { get; }
        public ICommand AlignRightCommand { get; }
        public ICommand AlignTopCommand { get; }
        public ICommand AlignBottomCommand { get; }
        public ICommand AlignCenterHCommand { get; }
        public ICommand AlignCenterVCommand { get; }
        public ICommand GroupCommand { get; }
        public ICommand UngroupCommand { get; }

        public void Invalidate() => InvalidateRequested?.Invoke(this, EventArgs.Empty);

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public DrawingTool CurrentTool
        {
            get => _currentTool;
            set => SetProperty(ref _currentTool, value);
        }

        public ObservableCollection<GraphicObject> GraphicObjects
        {
            get => _graphicObjects;
            set => SetProperty(ref _graphicObjects, value);
        }

        /// <summary>
        /// プロパティパネル用の単一選択オブジェクト（最後に選択されたもの）
        /// </summary>
        public GraphicObject? SelectedObject
        {
            get => _selectedObject;
            set
            {
                if (_selectedObject != null)
                {
                    _selectedObject.PropertyChanged -= OnSelectedObjectPropertyChanged;
                }
                
                if (SetProperty(ref _selectedObject, value))
                {
                    if (_selectedObject != null)
                    {
                        _selectedObject.PropertyChanged += OnSelectedObjectPropertyChanged;
                    }
                }
            }
        }

        /// <summary>
        /// 複数選択されたオブジェクトのコレクション
        /// </summary>
        public ObservableCollection<GraphicObject> SelectedObjects
        {
            get => _selectedObjects;
            set => SetProperty(ref _selectedObjects, value);
        }

        private void OnSelectedObjectPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // 選択オブジェクトのプロパティ変更時は再描画を要求する
            Invalidate();
        }

        // --- 選択操作の公開メソッド ---

        /// <summary>
        /// 単一選択（既存の選択をクリアして1つだけ選択）
        /// </summary>
        public void SelectObject(GraphicObject? obj)
        {
            ClearSelection();
            if (obj != null)
            {
                obj.IsSelected = true;
                _selectedObjects.Add(obj);
                SelectedObject = obj;
            }
            else
            {
                SelectedObject = null;
            }
            Invalidate();
        }

        /// <summary>
        /// トグル選択（Shift+クリック用：追加 or 解除）
        /// </summary>
        public void ToggleSelectObject(GraphicObject obj)
        {
            if (_selectedObjects.Contains(obj))
            {
                // 選択解除
                obj.IsSelected = false;
                _selectedObjects.Remove(obj);
                // SelectedObject を更新（残っていれば最後の要素）
                SelectedObject = _selectedObjects.Count > 0 ? _selectedObjects[^1] : null;
            }
            else
            {
                // 追加選択
                obj.IsSelected = true;
                _selectedObjects.Add(obj);
                SelectedObject = obj;
            }
            Invalidate();
        }

        /// <summary>
        /// 全選択解除
        /// </summary>
        public void ClearSelection()
        {
            foreach (var obj in _selectedObjects)
            {
                obj.IsSelected = false;
            }
            _selectedObjects.Clear();
            SelectedObject = null;
        }

        public double WidthMm
        {
            get => _widthMm;
            set
            {
                var clampedValue = Math.Max(1.0, Math.Min(value, 4000.0));
                if (SetProperty(ref _widthMm, clampedValue))
                {
                    OnPropertyChanged(nameof(WidthPx));
                }
            }
        }

        public double HeightMm
        {
            get => _heightMm;
            set
            {
                var clampedValue = Math.Max(1.0, Math.Min(value, 4000.0));
                if (SetProperty(ref _heightMm, clampedValue))
                {
                    OnPropertyChanged(nameof(HeightPx));
                }
            }
        }

        public double WidthPx => WidthMm * Dpi / MmPerInch;
        public double HeightPx => HeightMm * Dpi / MmPerInch;

        public CanvasViewModel()
        {
            BringToFrontCommand = new RelayCommand(_ => BringToFront());
            SendToBackCommand = new RelayCommand(_ => SendToBack());
            BringForwardCommand = new RelayCommand(_ => BringForward());
            SendBackwardCommand = new RelayCommand(_ => SendBackward());
            DeleteSelectedCommand = new RelayCommand(_ => DeleteSelected());
            CopyCommand = new RelayCommand(_ => CopySelected());
            PasteCommand = new RelayCommand(_ => Paste());
            AlignLeftCommand = new RelayCommand(_ => AlignSelected(AlignDirection.Left));
            AlignRightCommand = new RelayCommand(_ => AlignSelected(AlignDirection.Right));
            AlignTopCommand = new RelayCommand(_ => AlignSelected(AlignDirection.Top));
            AlignBottomCommand = new RelayCommand(_ => AlignSelected(AlignDirection.Bottom));
            AlignCenterHCommand = new RelayCommand(_ => AlignSelected(AlignDirection.CenterH));
            AlignCenterVCommand = new RelayCommand(_ => AlignSelected(AlignDirection.CenterV));
            GroupCommand = new RelayCommand(_ => GroupSelected());
            UngroupCommand = new RelayCommand(_ => UngroupSelected());
        }

        public CanvasViewModel(string title) : this()
        {
            Title = title;
        }

        private void BringToFront()
        {
            if (SelectedObject == null) return;
            int index = GraphicObjects.IndexOf(SelectedObject);
            if (index >= 0 && index < GraphicObjects.Count - 1)
            {
                GraphicObjects.Move(index, GraphicObjects.Count - 1);
                Invalidate();
            }
        }

        private void SendToBack()
        {
            if (SelectedObject == null) return;
            int index = GraphicObjects.IndexOf(SelectedObject);
            if (index > 0)
            {
                GraphicObjects.Move(index, 0);
                Invalidate();
            }
        }

        private void BringForward()
        {
            if (SelectedObject == null) return;
            int index = GraphicObjects.IndexOf(SelectedObject);
            if (index >= 0 && index < GraphicObjects.Count - 1)
            {
                GraphicObjects.Move(index, index + 1);
                Invalidate();
            }
        }

        private void SendBackward()
        {
            if (SelectedObject == null) return;
            int index = GraphicObjects.IndexOf(SelectedObject);
            if (index > 0)
            {
                GraphicObjects.Move(index, index - 1);
                Invalidate();
            }
        }

        // --- 削除 ---
        private void DeleteSelected()
        {
            if (_selectedObjects.Count == 0) return;
            // 複数選択対応: 選択中の全オブジェクトを削除
            var toRemove = _selectedObjects.ToList();
            foreach (var obj in toRemove)
            {
                obj.IsSelected = false;
                GraphicObjects.Remove(obj);
            }
            _selectedObjects.Clear();
            SelectedObject = null;
            Invalidate();
        }

        // --- コピー＆ペースト ---
        private void CopySelected()
        {
            if (SelectedObject == null) return;
            _clipboard = SelectedObject.Clone();
        }

        private void Paste()
        {
            if (_clipboard == null) return;
            var pasted = _clipboard.Clone();
            // ペースト位置を少しずらす
            pasted.X += 10;
            pasted.Y += 10;
            if (pasted is LineObject lineObj)
            {
                lineObj.EndX += 10;
                lineObj.EndY += 10;
            }
            pasted.IsSelected = false;
            GraphicObjects.Add(pasted);
            // ペーストしたオブジェクトを選択状態にする
            if (SelectedObject != null) SelectedObject.IsSelected = false;
            pasted.IsSelected = true;
            SelectedObject = pasted;
            // 次回ペースト時にさらにずれるようにクリップボードも更新
            _clipboard = pasted.Clone();
            Invalidate();
        }

        // --- 整列 ---
        private enum AlignDirection { Left, Right, Top, Bottom, CenterH, CenterV }

        /// <summary>
        /// オブジェクトの左端X座標を取得
        /// </summary>
        private float GetLeftEdge(GraphicObject obj)
        {
            if (obj is LineObject line) return Math.Min(line.X, line.EndX);
            return obj.X;
        }

        /// <summary>
        /// オブジェクトの右端X座標を取得
        /// </summary>
        private float GetRightEdge(GraphicObject obj)
        {
            if (obj is LineObject line) return Math.Max(line.X, line.EndX);
            return obj.X + obj.Width;
        }

        /// <summary>
        /// オブジェクトの上端Y座標を取得
        /// </summary>
        private float GetTopEdge(GraphicObject obj)
        {
            if (obj is LineObject line) return Math.Min(line.Y, line.EndY);
            return obj.Y;
        }

        /// <summary>
        /// オブジェクトの下端Y座標を取得
        /// </summary>
        private float GetBottomEdge(GraphicObject obj)
        {
            if (obj is LineObject line) return Math.Max(line.Y, line.EndY);
            return obj.Y + obj.Height;
        }

        /// <summary>
        /// オブジェクトを水平方向にオフセット移動
        /// </summary>
        private void MoveObjectX(GraphicObject obj, float offsetX)
        {
            obj.X += offsetX;
            if (obj is LineObject line) line.EndX += offsetX;
        }

        /// <summary>
        /// オブジェクトを垂直方向にオフセット移動
        /// </summary>
        private void MoveObjectY(GraphicObject obj, float offsetY)
        {
            obj.Y += offsetY;
            if (obj is LineObject line) line.EndY += offsetY;
        }

        private void AlignSelected(AlignDirection direction)
        {
            if (_selectedObjects.Count < 2) return; // 2つ以上選択されている場合のみ整列

            var objects = _selectedObjects.ToList();

            switch (direction)
            {
                case AlignDirection.Left:
                {
                    // 基準: 最も左端にあるオブジェクトのX
                    float targetX = objects.Min(o => GetLeftEdge(o));
                    foreach (var obj in objects)
                    {
                        float offsetX = targetX - GetLeftEdge(obj);
                        MoveObjectX(obj, offsetX);
                    }
                    break;
                }

                case AlignDirection.Right:
                {
                    // 基準: 最も右端にあるオブジェクトのX
                    float targetX = objects.Max(o => GetRightEdge(o));
                    foreach (var obj in objects)
                    {
                        float offsetX = targetX - GetRightEdge(obj);
                        MoveObjectX(obj, offsetX);
                    }
                    break;
                }

                case AlignDirection.Top:
                {
                    float targetY = objects.Min(o => GetTopEdge(o));
                    foreach (var obj in objects)
                    {
                        float offsetY = targetY - GetTopEdge(obj);
                        MoveObjectY(obj, offsetY);
                    }
                    break;
                }

                case AlignDirection.Bottom:
                {
                    float targetY = objects.Max(o => GetBottomEdge(o));
                    foreach (var obj in objects)
                    {
                        float offsetY = targetY - GetBottomEdge(obj);
                        MoveObjectY(obj, offsetY);
                    }
                    break;
                }

                case AlignDirection.CenterH:
                {
                    // 基準: 全オブジェクトの水平中心の平均
                    float avgCenterX = objects.Average(o => (GetLeftEdge(o) + GetRightEdge(o)) / 2);
                    foreach (var obj in objects)
                    {
                        float objCenterX = (GetLeftEdge(obj) + GetRightEdge(obj)) / 2;
                        float offsetX = avgCenterX - objCenterX;
                        MoveObjectX(obj, offsetX);
                    }
                    break;
                }

                case AlignDirection.CenterV:
                {
                    float avgCenterY = objects.Average(o => (GetTopEdge(o) + GetBottomEdge(o)) / 2);
                    foreach (var obj in objects)
                    {
                        float objCenterY = (GetTopEdge(obj) + GetBottomEdge(obj)) / 2;
                        float offsetY = avgCenterY - objCenterY;
                        MoveObjectY(obj, offsetY);
                    }
                    break;
                }
            }

            Invalidate();
        }

        // --- PNG書き出し ---
        /// <summary>
        /// キャンバスの内容をPNGファイルとして書き出す
        /// </summary>
        public void ExportPng(string filePath, bool transparentBackground)
        {
            int width = (int)Math.Ceiling(WidthPx);
            int height = (int)Math.Ceiling(HeightPx);

            using var bitmap = new SKBitmap(width, height);
            using var canvas = new SKCanvas(bitmap);

            // 背景
            if (transparentBackground)
            {
                canvas.Clear(SKColors.Transparent);
            }
            else
            {
                canvas.Clear(SKColors.White);
            }

            // 選択ハイライトを一時的に解除して描画
            var selectedStates = new List<(GraphicObject obj, bool wasSelected)>();
            foreach (var obj in GraphicObjects)
            {
                selectedStates.Add((obj, obj.IsSelected));
                obj.IsSelected = false;
            }

            foreach (var obj in GraphicObjects)
            {
                obj.Draw(canvas);
            }

            // 選択状態を復元
            foreach (var (obj, wasSelected) in selectedStates)
            {
                obj.IsSelected = wasSelected;
            }

            // PNG保存
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = System.IO.File.OpenWrite(filePath);
            data.SaveTo(stream);
        }

        // --- グループ化 ---
        private void GroupSelected()
        {
            if (_selectedObjects.Count < 2) return;

            var group = new GroupObject();
            var objectsToGroup = _selectedObjects.ToList();

            // 元の重ね順で最も下にあるオブジェクトの位置を取得
            int minIndex = objectsToGroup.Min(o => GraphicObjects.IndexOf(o));

            // 子オブジェクトをグループに追加し、キャンバスから除去
            foreach (var obj in objectsToGroup)
            {
                obj.IsSelected = false;
                group.Children.Add(obj);
                GraphicObjects.Remove(obj);
            }

            group.RecalculateBounds();

            // 元の重ね順位置にグループを挿入
            int insertIndex = Math.Min(minIndex, GraphicObjects.Count);
            GraphicObjects.Insert(insertIndex, group);

            // グループを選択状態にする
            _selectedObjects.Clear();
            group.IsSelected = true;
            _selectedObjects.Add(group);
            SelectedObject = group;
            Invalidate();
        }

        private void UngroupSelected()
        {
            // 選択中の GroupObject を解除
            var groupsToUngroup = _selectedObjects.OfType<GroupObject>().ToList();
            if (groupsToUngroup.Count == 0) return;

            ClearSelection();

            foreach (var group in groupsToUngroup)
            {
                int index = GraphicObjects.IndexOf(group);
                if (index < 0) continue;

                GraphicObjects.Remove(group);

                // 子オブジェクトを元の位置に挿入
                int insertAt = Math.Min(index, GraphicObjects.Count);
                foreach (var child in group.Children)
                {
                    child.IsSelected = true;
                    GraphicObjects.Insert(insertAt, child);
                    _selectedObjects.Add(child);
                    insertAt++;
                }
            }

            SelectedObject = _selectedObjects.Count > 0 ? _selectedObjects[^1] : null;
            Invalidate();
        }
    }
}
