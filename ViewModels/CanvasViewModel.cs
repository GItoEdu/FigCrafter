using System.Collections.ObjectModel;
using System.Windows.Input;
using SkiaSharp;
using FigCrafterApp.Models;
using FigCrafterApp.Commands;
using FigCrafterApp.Serialization;

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

        // Undo / Redo 用の履歴スタック
        private readonly Stack<IUndoableCommand> _undoStack = new();
        private readonly Stack<IUndoableCommand> _redoStack = new();

        // プロパティ変更検知用の直前値保持ディクショナリ
        private readonly Dictionary<string, object?> _propertyChangeOldValues = new();

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
        public ICommand UndoCommand { get; }
        public ICommand RedoCommand { get; }

        public void Invalidate() => InvalidateRequested?.Invoke(this, EventArgs.Empty);

        private bool _isExecutingCommand = false;

        public void ExecuteCommand(IUndoableCommand command)
        {
            _isExecutingCommand = true;
            try
            {
                command.Execute();
            }
            finally
            {
                _isExecutingCommand = false;
            }
            _undoStack.Push(command);
            _redoStack.Clear(); // 新しい操作が行われたらRedo履歴をクリア
            // コマンドの実行可否状態を通知
            (UndoCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RedoCommand as RelayCommand)?.RaiseCanExecuteChanged();
            Invalidate();
        }

        private void Undo()
        {
            if (_undoStack.Count > 0)
            {
                var command = _undoStack.Pop();
                
                _isExecutingCommand = true;
                try
                {
                    command.Undo();
                }
                finally
                {
                    _isExecutingCommand = false;
                }
                
                _redoStack.Push(command);
                (UndoCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (RedoCommand as RelayCommand)?.RaiseCanExecuteChanged();
                Invalidate();
            }
        }

        private void Redo()
        {
            if (_redoStack.Count > 0)
            {
                var command = _redoStack.Pop();
                
                _isExecutingCommand = true;
                try
                {
                    command.Execute();
                }
                finally
                {
                    _isExecutingCommand = false;
                }
                
                _undoStack.Push(command);
                (UndoCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (RedoCommand as RelayCommand)?.RaiseCanExecuteChanged();
                Invalidate();
            }
        }

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
                    _selectedObject.PropertyChanging -= OnSelectedObjectPropertyChanging;
                    _selectedObject.PropertyChanged -= OnSelectedObjectPropertyChanged;
                }
                
                if (SetProperty(ref _selectedObject, value))
                {
                    if (_selectedObject != null)
                    {
                        _selectedObject.PropertyChanging += OnSelectedObjectPropertyChanging;
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

        private void OnSelectedObjectPropertyChanging(object? sender, System.ComponentModel.PropertyChangingEventArgs e)
        {
            if (sender is not GraphicObject obj) return;
            if (e.PropertyName == null) return;

            // プロパティ変更前の値を記録
            var value = obj.GetType().GetProperty(e.PropertyName)?.GetValue(obj);
            _propertyChangeOldValues[e.PropertyName] = value;
        }

        private void OnSelectedObjectPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // 選択オブジェクトのプロパティ変更時は再描画を要求する
            Invalidate();

            if (sender is not GraphicObject obj) return;
            if (e.PropertyName == null) return;
            
            // X, Y の変更はドラッグ操作（CanvasView）から行われるため、Undo コマンドの二重登録を防ぐ
            // Width, Height なども Resize 操作から行われるため除外
            // 純粋なプロパティパネル（TextBox 等）からの変更を対象とするか、あるいは CanvasView からの変更時には Event を外す等の工夫が必要
            // ここでは簡易的に、一部のプロパティ（StrokeWidth, Text, FontFamily, FontSize, HasArrowStart, HasArrowEnd等）のみをUndo対象とする
            var targetedProperties = new HashSet<string>
            {
                nameof(GraphicObject.Rotation),
                nameof(GraphicObject.StrokeWidth),
                nameof(GraphicObject.Opacity),
                nameof(TextObject.Text),
                nameof(TextObject.FontFamily),
                nameof(TextObject.FontSize),
                nameof(LineObject.HasArrowStart),
                nameof(LineObject.HasArrowEnd),
                nameof(ImageObject.IsGrayscale)
            };

            if (targetedProperties.Contains(e.PropertyName) && _propertyChangeOldValues.TryGetValue(e.PropertyName, out var oldValue))
            {
                var newValue = obj.GetType().GetProperty(e.PropertyName)?.GetValue(obj);
                
                // 値が本当に変わっている場合のみコマンドを積む
                if (!Equals(oldValue, newValue))
                {
                    // 既に Undo 操作中かどうかのフラグが必要だが、ここでは簡易的に Execute 時にイベントリスナから一時的に外すなどの工夫が必要
                    // 一旦シンプルに実装（バインディング経由の変更を拾う）
                    // 念のため、現在Undoスタックの先頭が同じプロパティ変更の連続（文字入力中など）であれば、
                    // 古い値を引き継ぐ Composite 化を行うのがベストだが、ここではシンプルに積む
                    
                    // ※ 注意: このイベントハンドラ中での ExecuteCommand は再度 PropertyChanged を呼ぶ可能性はない（Executeしないため）。
                    // PropertyChangeCommand の Execute は既に UI 側でプロパティが Set されているため何もしない（あるいは再セットするだけ）ようにする方が良い。
                    
                    _undoStack.Push(new PropertyChangeCommand(obj, e.PropertyName, oldValue, newValue));
                    _redoStack.Clear();
                }
            }
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
            
            // Undo/Redoコマンド。CanExecute でスタック数をチェック
            UndoCommand = new RelayCommand(_ => Undo(), _ => _undoStack.Count > 0);
            RedoCommand = new RelayCommand(_ => Redo(), _ => _redoStack.Count > 0);
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
                ExecuteCommand(new ReorderObjectCommand(GraphicObjects, SelectedObject, index, GraphicObjects.Count - 1));
            }
        }

        private void SendToBack()
        {
            if (SelectedObject == null) return;
            int index = GraphicObjects.IndexOf(SelectedObject);
            if (index > 0)
            {
                ExecuteCommand(new ReorderObjectCommand(GraphicObjects, SelectedObject, index, 0));
            }
        }

        private void BringForward()
        {
            if (SelectedObject == null) return;
            int index = GraphicObjects.IndexOf(SelectedObject);
            if (index >= 0 && index < GraphicObjects.Count - 1)
            {
                ExecuteCommand(new ReorderObjectCommand(GraphicObjects, SelectedObject, index, index + 1));
            }
        }

        private void SendBackward()
        {
            if (SelectedObject == null) return;
            int index = GraphicObjects.IndexOf(SelectedObject);
            if (index > 0)
            {
                ExecuteCommand(new ReorderObjectCommand(GraphicObjects, SelectedObject, index, index - 1));
            }
        }

        // --- 削除 ---
        private void DeleteSelected()
        {
            if (_selectedObjects.Count == 0) return;
            
            // 複数選択対応: 選択中の全オブジェクトを削除するコマンドを実行
            var toRemove = _selectedObjects.ToList();
            var command = new RemoveObjectsCommand(GraphicObjects, toRemove);
            ExecuteCommand(command);

            // 選択状態の解除（ビューモデル側の状態）
            foreach (var obj in toRemove)
            {
                obj.IsSelected = false;
            }
            _selectedObjects.Clear();
            SelectedObject = null;
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

            // コマンド実行用
            var command = new AddObjectCommand(GraphicObjects, pasted);
            ExecuteCommand(command);

            // ペーストしたオブジェクトを選択状態にする
            if (SelectedObject != null) SelectedObject.IsSelected = false;
            pasted.IsSelected = true;
            SelectedObject = pasted;
            
            // 複数選択用のハック: 今追加したものを唯一の選択状態とする
            _selectedObjects.Clear();
            _selectedObjects.Add(pasted);

            // 次回ペースト時にさらにずれるようにクリップボードも更新
            _clipboard = pasted.Clone();
        }

        // --- 整列 ---
        private enum AlignDirection { Left, Right, Top, Bottom, CenterH, CenterV }

        /// <summary>
        /// オブジェクトの左端X座標を取得
        /// </summary>
        private float GetLeftEdge(GraphicObject obj)
        {
            if (obj is LineObject line) return Math.Min(line.X, line.EndX);
            if (obj is GroupObject group) { group.RecalculateBounds(); return group.X; }
            return obj.X;
        }

        /// <summary>
        /// オブジェクトの右端X座標を取得
        /// </summary>
        private float GetRightEdge(GraphicObject obj)
        {
            if (obj is LineObject line) return Math.Max(line.X, line.EndX);
            if (obj is GroupObject group) { group.RecalculateBounds(); return group.X + group.Width; }
            return obj.X + obj.Width;
        }

        /// <summary>
        /// オブジェクトの上端Y座標を取得
        /// </summary>
        private float GetTopEdge(GraphicObject obj)
        {
            if (obj is LineObject line) return Math.Min(line.Y, line.EndY);
            if (obj is GroupObject group) { group.RecalculateBounds(); return group.Y; }
            return obj.Y;
        }

        /// <summary>
        /// オブジェクトの下端Y座標を取得
        /// </summary>
        private float GetBottomEdge(GraphicObject obj)
        {
            if (obj is LineObject line) return Math.Max(line.Y, line.EndY);
            if (obj is GroupObject group) { group.RecalculateBounds(); return group.Y + group.Height; }
            return obj.Y + obj.Height;
        }

        /// <summary>
        /// オブジェクトを水平方向にオフセット移動（GroupObject は子も連動）し、古い状態と新しい状態を記録するためのタプルを返す
        /// </summary>
        private (GraphicObject Obj, float OldX, float OldY, float NewX, float NewY) MoveObjectXWithRecord(GraphicObject obj, float offsetX)
        {
            float oldX = obj.X;
            float oldY = obj.Y;
            obj.X += offsetX;

            if (obj is LineObject line) line.EndX += offsetX;
            else if (obj is GroupObject group)
            {
                foreach (var child in group.Children) MoveObjectXWithRecord(child, offsetX);
            }
            
            return (obj, oldX, oldY, obj.X, obj.Y);
        }

        /// <summary>
        /// オブジェクトを垂直方向にオフセット移動（GroupObject は子も連動）し、履歴用タプルを返す
        /// </summary>
        private (GraphicObject Obj, float OldX, float OldY, float NewX, float NewY) MoveObjectYWithRecord(GraphicObject obj, float offsetY)
        {
            float oldX = obj.X;
            float oldY = obj.Y;
            obj.Y += offsetY;

            if (obj is LineObject line) line.EndY += offsetY;
            else if (obj is GroupObject group)
            {
                foreach (var child in group.Children) MoveObjectYWithRecord(child, offsetY);
            }
            
            return (obj, oldX, oldY, obj.X, obj.Y);
        }

        private void AlignSelected(AlignDirection direction)
        {
            if (_selectedObjects.Count < 2) return; // 2つ以上選択されている場合のみ整列

            var objects = _selectedObjects.ToList();
            var moves = new List<(GraphicObject Obj, float OldX, float OldY, float NewX, float NewY)>();

            switch (direction)
            {
                case AlignDirection.Left:
                {
                    // 基準: 最も左端にあるオブジェクトのX
                    float targetX = objects.Min(o => GetLeftEdge(o));
                    foreach (var obj in objects)
                    {
                        float offsetX = targetX - GetLeftEdge(obj);
                        if (offsetX != 0) moves.Add(MoveObjectXWithRecord(obj, offsetX));
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
                        if (offsetX != 0) moves.Add(MoveObjectXWithRecord(obj, offsetX));
                    }
                    break;
                }

                case AlignDirection.Top:
                {
                    float targetY = objects.Min(o => GetTopEdge(o));
                    foreach (var obj in objects)
                    {
                        float offsetY = targetY - GetTopEdge(obj);
                        if (offsetY != 0) moves.Add(MoveObjectYWithRecord(obj, offsetY));
                    }
                    break;
                }

                case AlignDirection.Bottom:
                {
                    float targetY = objects.Max(o => GetBottomEdge(o));
                    foreach (var obj in objects)
                    {
                        float offsetY = targetY - GetBottomEdge(obj);
                        if (offsetY != 0) moves.Add(MoveObjectYWithRecord(obj, offsetY));
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
                        if (offsetX != 0) moves.Add(MoveObjectXWithRecord(obj, offsetX));
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
                        if (offsetY != 0) moves.Add(MoveObjectYWithRecord(obj, offsetY));
                    }
                    break;
                }
            }

            if (moves.Count > 0)
            {
                // これらは既に移動が完了しているので、元の状態と今の状態をコマンドとして記録する（Undoに備えるため）
                // ただし、MoveObjectsCommand は Execute で Set を行うため、再適用を防ぐか、そもそもここで MoveObjectXWithRecord等を行わずに差分だけ計算してコマンド内で実移動させるほうが本来は綺麗。
                // ここでは MoveObjectXWithRecord によって既に GraphicObjects の各プロパティが新しい値に変異しているので、
                // Undo が呼ばれたときに Old に戻ることを保証するコマンドとして登録する。
                ExecuteCommand(new MoveObjectsCommand(moves));
            }
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

            var commands = new List<IUndoableCommand>();

            // 子オブジェクトをグループに追加し、キャンバスからは削除するコマンドを作成
            foreach (var obj in objectsToGroup)
            {
                obj.IsSelected = false;
                group.Children.Add(obj);
            }
            commands.Add(new RemoveObjectsCommand(GraphicObjects, objectsToGroup));

            group.RecalculateBounds();

            // 元の重ね順位置にグループを挿入するコマンド
            int insertIndex = Math.Min(minIndex, GraphicObjects.Count);
            // 本来は指定インデックスにInsertするコマンドが必要だが AddObjectCommand のみ実装されているためリスト末尾以外への追加は順番を崩す可能性がある
            // （現状は AddObjectCommand をそのまま使うか、または単純に ExecuteCommand でグループ化操作自体をまとめるアプローチにする）
            // 一旦、専用のコマンド群ではなく ExecuteCommand() 内でこの複合コマンドを登録する

            var composite = new GroupingCommand(GraphicObjects, objectsToGroup, group, insertIndex);
            ExecuteCommand(composite);

            // グループを選択状態にする
            _selectedObjects.Clear();
            group.IsSelected = true;
            _selectedObjects.Add(group);
            SelectedObject = group;
        }

        private void UngroupSelected()
        {
            // 選択中の GroupObject を解除
            var groupsToUngroup = _selectedObjects.OfType<GroupObject>().ToList();
            if (groupsToUngroup.Count == 0) return;

            var composite = new UngroupingCommand(GraphicObjects, groupsToUngroup);
            ExecuteCommand(composite);

            ClearSelection();
            
            // Undo 実行後ではなく Execute 後の現在状態に対して選択を復元
            foreach (var group in groupsToUngroup)
            {
                foreach (var child in group.Children)
                {
                    child.IsSelected = true;
                    _selectedObjects.Add(child);
                }
            }

            SelectedObject = _selectedObjects.Count > 0 ? _selectedObjects[^1] : null;
        }

        // --- プロジェクトデータの変換 ---
        public ProjectData CreateProjectData()
        {
            return new ProjectData
            {
                Title = Title,
                WidthMm = WidthMm,
                HeightMm = HeightMm,
                GraphicObjects = new ObservableCollection<GraphicObject>(GraphicObjects.Select(x => x.Clone()))
            };
        }

        public void LoadFromProjectData(ProjectData data)
        {
            Title = data.Title;
            WidthMm = data.WidthMm;
            HeightMm = data.HeightMm;
            
            GraphicObjects.Clear();
            foreach (var obj in data.GraphicObjects)
            {
                GraphicObjects.Add(obj);
            }
            
            // 状態リセット
            ClearSelection();
            _undoStack.Clear();
            _redoStack.Clear();
            (UndoCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RedoCommand as RelayCommand)?.RaiseCanExecuteChanged();
            Invalidate();
        }
    }
}
