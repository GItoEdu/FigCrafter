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
        Text,
        Eraser
    }

    public class CanvasViewModel : ViewModelBase
    {
        private const double Dpi = 96.0;
        private const double MmPerInch = 25.4;

        private string _title = "名称未設定";
        private double _widthMm = 210; // A4幅
        private double _heightMm = 297; // A4高さ
        private double _zoomLevel = 1.0; // ズーム倍率
        private DrawingTool _currentTool = DrawingTool.Select;
        private ObservableCollection<Layer> _layers = new();
        private Layer? _activeLayer;
        private GraphicObject? _selectedObject;
        private ObservableCollection<GraphicObject> _selectedObjects = new(); // 複数選択
        private GraphicObject? _clipboard; // コピー用クリップボード
        private bool _isSnapEnabled = true; // スナップ機能のON/OFF
        private string? _filePath; // 保存先ファイルパス
        private bool _isUndoSuppressed = false; // Undo記録を抑制するかどうか
        private double _viewportWidth = 800; // ビューポートの幅
        private double _viewportHeight = 600; // ビューポートの高さ
        private bool _shouldZoomToFitOnSizeChange = false; // 次回のサイズ変更時にズームフィットさせるか
        private GraphicObject? _zoomTargetObject = null; // ズーム対象のオブジェクト

        // Undo / Redo 用の履歴スタック
        private readonly Stack<IUndoableCommand> _undoStack = new();
        private readonly Stack<IUndoableCommand> _redoStack = new();
        // 保存状態の追跡用フィールドとプロパティ
        private IUndoableCommand? _savedCommand = null;

        ///<summary>
        ///未保存の変更があるかどうか（Undoスタックの最上位と保存時の状態を比較）
        ///</summary>
        public bool IsDirty => (_undoStack.Count > 0 ? _undoStack.Peek() : null) != _savedCommand;

        /// <summary>
        /// 現在の状態を「保存済み」としてマーキングする
        /// </summary>
        public void MarkAsSaved()
        {
            _savedCommand = _undoStack.Count > 0 ? _undoStack.Peek() : null;
            OnPropertyChanged(nameof(IsDirty));
        }

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
        public ICommand ToggleCropModeCommand { get; }
        public ICommand CutCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand MoveObjectToLayerCommand { get; }
        public ICommand ResetImageAdjustmentCommand { get; }

        public ICommand IncreaseFontSizeCommand { get; }
        public ICommand DecreaseFontSizeCommand { get; }
        public ICommand InsertSpecialCharCommand { get; }

        public ICommand ZoomInCommand { get; }
        public ICommand ZoomOutCommand { get; }
        public ICommand ResetZoomCommand { get; }
        public ICommand ZoomToFitCommand { get; }

        public void Invalidate() => InvalidateRequested?.Invoke(this, EventArgs.Empty);

        internal bool _isExecutingCommand = false;

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

            // 状態変更を通知
            OnPropertyChanged(nameof(IsDirty));
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

                // 状態変更を通知
                OnPropertyChanged(nameof(IsDirty));
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

                // 状態変更を通知
                OnPropertyChanged(nameof(IsDirty));
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
            set
            {
                if (SetProperty(ref _currentTool, value))
                {
                    if (_currentTool != DrawingTool.Select)
                    {
                        IsCropMode = false;
                    }
                }
            }
        }

        private bool _isCropMode = false;
        public bool IsCropMode
        {
            get => _isCropMode;
            set
            {
                if (SetProperty(ref _isCropMode, value))
                {
                    Invalidate();
                }
            }
        }

        public bool IsSnapEnabled
        {
            get => _isSnapEnabled;
            set => SetProperty(ref _isSnapEnabled, value);
        }

        public string? FilePath
        {
            get => _filePath;
            set => SetProperty(ref _filePath, value);
        }

        public bool IsUndoSuppressed
        {
            get => _isUndoSuppressed;
            set => SetProperty(ref _isUndoSuppressed, value);
        }

        public double ViewportWidth
        {
            get => _viewportWidth;
            set => SetProperty(ref _viewportWidth, value);
        }

        public double ViewportHeight
        {
            get => _viewportHeight;
            set => SetProperty(ref _viewportHeight, value);
        }

        public bool ShouldZoomToFitOnSizeChange
        {
            get => _shouldZoomToFitOnSizeChange;
            set => SetProperty(ref _shouldZoomToFitOnSizeChange, value);
        }

        public GraphicObject? ZoomTargetObject
        {
            get => _zoomTargetObject;
            set => SetProperty(ref _zoomTargetObject, value);
        }

        public ObservableCollection<Layer> Layers
        {
            get => _layers;
            set
            {
                // 旧コレクションのイベント解除
                if (_layers != null)
                {
                    _layers.CollectionChanged -= OnLayersCollectionChanged;
                    foreach (var layer in _layers)
                    {
                        if (layer != null)
                            layer.PropertyChanged -= OnLayerPropertyChanged;
                    }
                }

                if (SetProperty(ref _layers!, value))
                {
                    if (_layers != null && _layers.Count > 0)
                    {
                        ActiveLayer = _layers[0];
                    }
                    else
                    {
                        ActiveLayer = null!;
                    }
                }

                // 新コレクションのイベント登録
                if (_layers != null)
                {
                    _layers.CollectionChanged += OnLayersCollectionChanged;
                    foreach (var layer in _layers)
                    {
                        if (layer != null)
                        layer.PropertyChanged += OnLayerPropertyChanged;
                    }
                }
            }
        }

        private void OnLayersCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // 追加されたレイヤーのイベント登録
            if (e.NewItems != null)
            {
                foreach (Layer layer in e.NewItems)
                    layer.PropertyChanged += OnLayerPropertyChanged;
            }
            // 削除されたレイヤーのイベント解除
            if (e.OldItems != null)
            {
                foreach (Layer layer in e.OldItems)
                    layer.PropertyChanged -= OnLayerPropertyChanged;
            }
            Invalidate();
        }

        private void OnLayerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // レイヤーのIsVisible/IsLocked/Opacity等の変更時に再描画を要求
            Invalidate();
        }

        private bool _isSyncingLayer = false;

        public Layer? ActiveLayer
        {
            get => _activeLayer;
            set
            {
                if (SetProperty(ref _activeLayer, value))
                {
                    if (!_isSyncingLayer && _activeLayer != null)
                    {
                        ClearSelection();
                        foreach (var obj in _activeLayer.GraphicObjects)
                        {
                            obj.IsSelected = true;
                            _selectedObjects.Add(obj);
                        }
                        SelectedObject = _selectedObjects.LastOrDefault();
                    }

                    Invalidate(); 
                    UpdateLayerCommands();
                }
            }
        }

        public Layer? FindLayer(GraphicObject obj)
        {
            return Layers.FirstOrDefault(l => l.GraphicObjects.Contains(obj));
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

            // Undo/Redo実行中（プログラム側からのプロパティ変更）は履歴に積まない
            if (_isExecutingCommand) return;

            if (IsUndoSuppressed) return; // Undo抑制中は記録しない

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
                nameof(GraphicObject.FillColor),
                nameof(GraphicObject.StrokeColor),
                nameof(TextObject.Text),
                nameof(TextObject.FontFamily),
                nameof(TextObject.FontSize),
                nameof(LineObject.HasArrowStart),
                nameof(LineObject.HasArrowEnd),
                nameof(ImageObject.CropX),
                nameof(ImageObject.CropY),
                nameof(ImageObject.CropWidth),
                nameof(ImageObject.CropHeight)
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
                    
                    System.Diagnostics.Debug.WriteLine($"Undo PUSH (Auto): {e.PropertyName}");
                    _undoStack.Push(new PropertyChangeCommand(obj, e.PropertyName, oldValue, newValue));
                    _redoStack.Clear();
                    
                    // コマンド状態が変わったことをUI(メニュー等)に通知
                    (UndoCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (RedoCommand as RelayCommand)?.RaiseCanExecuteChanged();
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

                var layer = FindLayer(obj);
                if (layer != null && ActiveLayer != layer)
                {
                    _isSyncingLayer = true;
                    ActiveLayer = layer;
                    _isSyncingLayer = false;
                }
            }
            else
            {
                SelectedObject = null;
            }
            if (obj is not ImageObject)
            {
                IsCropMode = false;
            }
            Invalidate();
        }

        /// <summary>
        /// トジー選択（Shift+クリック用：追加 or 解除）
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

                var layer = FindLayer(obj);
                if (layer != null && ActiveLayer != layer)
                {
                    _isSyncingLayer = true;
                    ActiveLayer = layer;
                    _isSyncingLayer = false;
                }
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
                // The instruction removed clamping, so I'm following that.
                if (SetProperty(ref _heightMm, value))
                {
                    OnPropertyChanged(nameof(HeightPx));
                    OnPropertyChanged(nameof(ZoomedWidthPx));
                    OnPropertyChanged(nameof(ZoomedHeightPx));
                    Invalidate();
                }
            }
        }

        public double WidthPx => _widthMm / MmPerInch * Dpi;
        public double HeightPx => _heightMm / MmPerInch * Dpi;

        public double ZoomLevel
        {
            get => _zoomLevel;
            set
            {
                // 値を制限 (10% ~ 1000%)
                double snappedZoom = Math.Round(value * 10) / 10.0;
                double newZoom = Math.Max(0.1, Math.Min(10.0, snappedZoom));
                if (SetProperty(ref _zoomLevel, newZoom))
                {
                    OnPropertyChanged(nameof(ZoomedWidthPx));
                    OnPropertyChanged(nameof(ZoomedHeightPx));
                    Invalidate();
                }
            }
        }

        public double ZoomedWidthPx => WidthPx * ZoomLevel;
        public double ZoomedHeightPx => HeightPx * ZoomLevel;

        // レイヤー用コマンド
        public ICommand AddLayerCommand { get; }
        public ICommand RemoveLayerCommand { get; }
        public ICommand MoveLayerUpCommand { get; }
        public ICommand MoveLayerDownCommand { get; }



        public CanvasViewModel()
        {
            // デフォルトコレクションのイベント登録
            _layers.CollectionChanged += OnLayersCollectionChanged;

            var defaultLayer = new Layer { Name = "レイヤー 1" };
            Layers.Add(defaultLayer);
            ActiveLayer = defaultLayer;

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
            UngroupCommand = new RelayCommand(_ => UngroupSelected(), p => _selectedObject is GroupObject);
            UndoCommand = new RelayCommand(_ => Undo(), _ => _undoStack.Count > 0);
            RedoCommand = new RelayCommand(_ => Redo(), _ => _redoStack.Count > 0);
            ToggleCropModeCommand = new RelayCommand(_ => { IsCropMode = !IsCropMode; }, p => _selectedObject is ImageObject);
            MoveObjectToLayerCommand = new RelayCommand(p => MoveObjectToLayer(p as Layer), p => p is Layer && _selectedObject != null);
            ResetImageAdjustmentCommand = new RelayCommand(_ => ResetImageAdjustment(), _ => _selectedObject is ImageObject);
            CutCommand = new RelayCommand(_ => CutSelected(), _ => _selectedObject != null);
            SelectAllCommand = new RelayCommand(_ => SelectAll());

            IncreaseFontSizeCommand = new RelayCommand(_ => 
            { 
                if (SelectedObject is TextObject text) 
                {
                    const float ptToMm = (float)(25.4 / 72.0);
                    float currentPt = text.FontSize / ptToMm;
                    // 次の 0.5 刻みにスナップ
                    float nextPt = (float)(Math.Floor(currentPt * 2.0 + 0.01) + 1.0) / 2.0f;
                    text.FontSize = nextPt * ptToMm;
                }
            });
            DecreaseFontSizeCommand = new RelayCommand(_ => 
            { 
                if (SelectedObject is TextObject text) 
                {
                    const float ptToMm = (float)(25.4 / 72.0);
                    float currentPt = text.FontSize / ptToMm;
                    // 前の 0.5 刻みにスナップ
                    float nextPt = (float)(Math.Ceiling(currentPt * 2.0 - 0.01) - 1.0) / 2.0f;
                    if (nextPt > 0) text.FontSize = nextPt * ptToMm;
                }
            });
            InsertSpecialCharCommand = new RelayCommand(p => { if (SelectedObject is TextObject text && p is string charStr) text.Text += charStr; });

            ZoomInCommand = new RelayCommand(p => ZoomLevel += 0.1);
            ZoomOutCommand = new RelayCommand(p => ZoomLevel -= 0.1);
            ResetZoomCommand = new RelayCommand(p => ZoomLevel = 1.0);
            ZoomToFitCommand = new RelayCommand(p => ZoomToFit(SelectedObject));

            AddLayerCommand = new RelayCommand(_ => AddLayer());
            RemoveLayerCommand = new RelayCommand(_ => RemoveLayer(), _ => Layers.Count > 1 && ActiveLayer != null);
            MoveLayerUpCommand = new RelayCommand(_ => MoveLayerUp(), _ => ActiveLayer != null && Layers.IndexOf(ActiveLayer) > 0);
            MoveLayerDownCommand = new RelayCommand(_ => MoveLayerDown(), _ => ActiveLayer != null && Layers.IndexOf(ActiveLayer) < Layers.Count - 1);
        }

        public CanvasViewModel(string title) : this()
        {
            Title = title;
        }

        // --- レイヤー操作 ---
        public void AddLayer()
        {
            var newLayer = new Layer { Name = $"レイヤー {Layers.Count + 1}" };
            Layers.Insert(0, newLayer); // リストの上に追加
            ActiveLayer = newLayer;
            UpdateLayerCommands();
        }

        private void RemoveLayer()
        {
            if (ActiveLayer != null && Layers.Count > 1)
            {
                int index = Layers.IndexOf(ActiveLayer);
                Layers.Remove(ActiveLayer);
                ActiveLayer = index < Layers.Count ? Layers[index] : Layers[Layers.Count - 1];
                UpdateLayerCommands();
            }
        }

        private void MoveLayerUp()
        {
            if (ActiveLayer != null)
            {
                int index = Layers.IndexOf(ActiveLayer);
                if (index > 0)
                {
                    Layers.Move(index, index - 1);
                    UpdateLayerCommands();
                }
            }
        }

        private void MoveLayerDown()
        {
            if (ActiveLayer != null)
            {
                int index = Layers.IndexOf(ActiveLayer);
                if (index < Layers.Count - 1)
                {
                    Layers.Move(index, index + 1);
                    UpdateLayerCommands();
                }
            }
        }

        public void UpdateLayerCommands()
        {
            (AddLayerCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RemoveLayerCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (MoveLayerUpCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (MoveLayerDownCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void BringToFront()
        {
            if (SelectedObject == null) return;
            var layer = FindLayer(SelectedObject);
            if (layer == null) return;
            var list = layer.GraphicObjects;
            
            int index = list.IndexOf(SelectedObject);
            if (index >= 0 && index < list.Count - 1)
            {
                ExecuteCommand(new ReorderObjectCommand(list, SelectedObject, index, list.Count - 1));
            }
        }

        private void SendToBack()
        {
            if (SelectedObject == null) return;
            var layer = FindLayer(SelectedObject);
            if (layer == null) return;
            var list = layer.GraphicObjects;
            
            int index = list.IndexOf(SelectedObject);
            if (index > 0)
            {
                ExecuteCommand(new ReorderObjectCommand(list, SelectedObject, index, 0));
            }
        }

        private void BringForward()
        {
            if (SelectedObject == null) return;
            var layer = FindLayer(SelectedObject);
            if (layer == null) return;
            var list = layer.GraphicObjects;
            
            int index = list.IndexOf(SelectedObject);
            if (index >= 0 && index < list.Count - 1)
            {
                ExecuteCommand(new ReorderObjectCommand(list, SelectedObject, index, index + 1));
            }
        }

        private void SendBackward()
        {
            if (SelectedObject == null) return;
            var layer = FindLayer(SelectedObject);
            if (layer == null) return;
            var list = layer.GraphicObjects;
            
            int index = list.IndexOf(SelectedObject);
            if (index > 0)
            {
                ExecuteCommand(new ReorderObjectCommand(list, SelectedObject, index, index - 1));
            }
        }

        // --- 削除 ---
        private void DeleteSelected()
        {
            if (_selectedObjects.Count == 0) return;
            
            var toRemove = _selectedObjects.ToList();
            var commands = new List<IUndoableCommand>();
            foreach (var grouping in toRemove.GroupBy(FindLayer))
            {
                if (grouping.Key == null) continue;
                commands.Add(new RemoveObjectsCommand(grouping.Key.GraphicObjects, grouping.ToList()));
            }

            if (commands.Count == 1)
            {
                ExecuteCommand(commands[0]);
            }
            else if (commands.Count > 1)
            {
                ExecuteCommand(new CompositeCommand(commands));
            }

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

        /// <summary>
        /// 切り取り（コピー＋削除）
        /// </summary>
        private void CutSelected()
        {
            CopySelected();
            DeleteSelected();
        }

        /// <summary>
        /// アクティブレイヤーの全オブジェクトを選択
        /// </summary>
        private void SelectAll()
        {
            if (ActiveLayer == null) return;

            _selectedObjects.Clear();
            foreach (var obj in ActiveLayer.GraphicObjects)
            {
                obj.IsSelected = true;
                _selectedObjects.Add(obj);
            }
            SelectedObject = _selectedObjects.FirstOrDefault();
            Invalidate();
        }

        /// <summary>
        /// 選択中のオブジェクトを指定レイヤーに移動
        /// </summary>
        private void MoveObjectToLayer(Layer? targetLayer)
        {
            if (targetLayer == null || _selectedObjects.Count == 0) return;

            var objectsToMove = _selectedObjects.ToList();
            var commands = new List<IUndoableCommand>();

            foreach (var obj in objectsToMove)
            {
                var sourceLayer = FindLayer(obj);
                if (sourceLayer == null || sourceLayer == targetLayer) continue;

                // 元レイヤーから削除して対象レイヤーに追加
                commands.Add(new RemoveObjectsCommand(sourceLayer.GraphicObjects, new List<GraphicObject> { obj }));
                commands.Add(new AddObjectCommand(targetLayer.GraphicObjects, obj));
            }

            if (commands.Count > 0)
            {
                ExecuteCommand(new CompositeCommand(commands));
                ActiveLayer = targetLayer;
                Invalidate();
            }
        }

        private void Paste()
        {
            if (_clipboard == null || ActiveLayer == null) return;
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
            var command = new AddObjectCommand(ActiveLayer.GraphicObjects, pasted);
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
        /// オブジェクトの左端X座標を取得（回転後の頂点を考慮）
        /// </summary>
        private float GetLeftEdge(GraphicObject obj)
        {
            var corners = obj.GetTransformedCorners();
            return corners.Min(c => c.X);
        }

        /// <summary>
        /// オブジェクトの右端X座標を取得（回転後の頂点を考慮）
        /// </summary>
        private float GetRightEdge(GraphicObject obj)
        {
            var corners = obj.GetTransformedCorners();
            return corners.Max(c => c.X);
        }

        /// <summary>
        /// オブジェクトの上端Y座標を取得（回転後の頂点を考慮）
        /// </summary>
        private float GetTopEdge(GraphicObject obj)
        {
            var corners = obj.GetTransformedCorners();
            return corners.Min(c => c.Y);
        }

        /// <summary>
        /// オブジェクトの下端Y座標を取得（回転後の頂点を考慮）
        /// </summary>
        private float GetBottomEdge(GraphicObject obj)
        {
            var corners = obj.GetTransformedCorners();
            return corners.Max(c => c.Y);
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

        /// <summary>
        /// 選択されているオブジェクトを相対移動させます（カーソルキー用）
        /// </summary>
        /// <param name="dx"></param>
        /// <param name="dy"></param>
        public void MoveSelectedObjects(float dx, float dy)
        {
            if (_selectedObjects.Count == 0) return;
            var moves = new List<(GraphicObject Obj, float OldX, float OldY, float NewX, float NewY)>();

            foreach (var obj in _selectedObjects)
            {
                float oldX = obj.X;
                float oldY = obj.Y;

                if (dx != 0) MoveObjectXWithRecord(obj, dx);
                if (dy != 0) MoveObjectYWithRecord(obj, dy);

                moves.Add((obj, oldX, oldY, obj.X, obj.Y));
            }
            // Undo履歴に登録して再描画
            ExecuteCommand(new FigCrafterApp.Commands.MoveObjectsCommand(moves));
        }

        // --- 画像インポート ---
        public void ImportImageAsGroup(SKBitmap bitmap, float dpiX = 96f, float dpiY = 96f)
        {
            // DPIからミリメートルサイズを計算 (1インチ = 25.4mm)
            float widthMm = bitmap.Width * (25.4f / dpiX);
            float heightMm = bitmap.Height * (25.4f / dpiY);

            var imageObj = new ImageObject 
            { 
                X = 10, Y = 10, 
                ImageData = bitmap,
                Width = widthMm, Height = heightMm,
                StrokeWidth = 0 // 初期状態は枠なし
            };

            var commands = new List<IUndoableCommand>();
            var newLayer = new Layer { Name = $"レイヤー {Layers.Count + 1}" };
            commands.Add(new AddLayerCommand(this, newLayer));
            commands.Add(new AddObjectCommand(newLayer.GraphicObjects, imageObj));

            ExecuteCommand(new CompositeCommand(commands));
            
            ActiveLayer = newLayer;
            SelectObject(imageObj);
            
            // ビューポートサイズが確定していれば即座に適用、そうでなければフラグを立てる
            if (ViewportWidth > 0 && ViewportHeight > 0)
            {
                ZoomToFit(imageObj);
            }
            else
            {
                ZoomTargetObject = imageObj;
                ShouldZoomToFitOnSizeChange = true;
            }

            Invalidate();
        }

        /// <summary>
        /// キャンバス全体または指定したオブジェクトが現在のウィンドウ（ビューポート）に収まるようにズーム調整する
        /// </summary>
        public void ZoomToFit(GraphicObject? target = null)
        {
            if (ViewportWidth <= 0 || ViewportHeight <= 0) return;

            // マージン分（40px * 2 = 80px）を考慮
            double availableWidth = ViewportWidth - 80;
            double availableHeight = ViewportHeight - 80;

            if (availableWidth <= 0 || availableHeight <= 0) return;

            double targetWidthPx;
            double targetHeightPx;

            if (target != null)
            {
                // GraphicObject の Width/Height は mm 単位なのでピクセルに変換
                targetWidthPx = target.Width / MmPerInch * Dpi;
                targetHeightPx = target.Height / MmPerInch * Dpi;
            }
            else
            {
                targetWidthPx = WidthPx;
                targetHeightPx = HeightPx;
            }

            if (targetWidthPx <= 0 || targetHeightPx <= 0) return;

            double zoomX = availableWidth / targetWidthPx;
            double zoomY = availableHeight / targetHeightPx;

            // 小さい方の倍率を採用し、さらに少し余裕(0.95)を持たせる
            ZoomLevel = Math.Min(zoomX, zoomY) * 0.95;
        }

        /// <summary>
        /// 解析済みのベクトルオブジェクト等を新規レイヤーにインポートする
        /// </summary>
        public void ImportGraphicObject(GraphicObject obj)
        {
            var commands = new List<IUndoableCommand>();
            var newLayer = new Layer { Name = $"インポート {Layers.Count + 1}" };
            commands.Add(new AddLayerCommand(this, newLayer));
            commands.Add(new AddObjectCommand(newLayer.GraphicObjects, obj));
            ExecuteCommand(new CompositeCommand(commands));
            ActiveLayer = newLayer;
            SelectObject(obj);

            if (ViewportWidth > 0 && ViewportHeight > 0)
            {
                ZoomToFit(obj);
            }
            else
            {
                ZoomTargetObject = obj;
                ShouldZoomToFitOnSizeChange = true;
            }
        }

        // --- PNG書き出し ---
        /// <summary>
        /// キャンバスの内容をPNGファイルとして書き出す
        /// </summary>
        public void ExportPng(string filePath, bool transparentBackground, float dpi = 300f)
        {
            float mmToPx = dpi / 25.4f;
            int width = (int)Math.Ceiling(WidthMm * mmToPx);
            int height = (int)Math.Ceiling(HeightMm * mmToPx);

            using var bitmap = new SKBitmap(width, height);
            using var canvas = new SKCanvas(bitmap);

            canvas.Scale(mmToPx);

            // 背景
            if (transparentBackground)
            {
                canvas.Clear(SKColors.Transparent);
            }
            else
            {
                canvas.Clear(SKColors.White);
            }

            var selectedStates = new List<(GraphicObject obj, bool wasSelected)>();
            foreach (var layer in Layers)
            {
                if (!layer.IsVisible) continue;
                foreach (var obj in layer.GraphicObjects)
                {
                    selectedStates.Add((obj, obj.IsSelected));
                    obj.IsSelected = false;
                }
            }

            for (int i = Layers.Count - 1; i >= 0; i--)
            {
                var layer = Layers[i];
                if (!layer.IsVisible) continue;
                foreach (var obj in layer.GraphicObjects)
                {
                    obj.Draw(canvas);
                }
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

        // --- PDF書き出し ---
        public void ExportPdf(string filePath, float dpi = 300f)
        {
            float mmToPx = dpi / 25.4f;
            int width = (int)Math.Ceiling(WidthMm * mmToPx);
            int height = (int)Math.Ceiling(HeightMm * mmToPx);

            using var stream = System.IO.File.OpenWrite(filePath);
            using var document = SKDocument.CreatePdf(stream, dpi);
            using var canvas = document.BeginPage(width, height);

            canvas.Scale(mmToPx);

            var selectedStates = new List<(GraphicObject obj, bool wasSelected)>();
            foreach (var layer in Layers)
            {
                if (!layer.IsVisible) continue;
                foreach (var obj in layer.GraphicObjects)
                {
                    selectedStates.Add((obj, obj.IsSelected));
                    obj.IsSelected = false;
                }
            }

            for (int i = Layers.Count - 1; i >= 0; i--)
            {
                var layer = Layers[i];
                if (!layer.IsVisible) continue;
                foreach (var obj in layer.GraphicObjects)
                {
                    obj.Draw(canvas);
                }
            }

            // 選択状態を復元
            foreach (var (obj, wasSelected) in selectedStates)
            {
                obj.IsSelected = wasSelected;
            }

            document.EndPage();
            document.Close();
        }

        // --- TIF書き出し ---
        public void ExportTif(string filePath, float dpi = 300f)
        {
            float mmToPx = dpi / 25.4f;
            int width = (int)Math.Ceiling(WidthMm * mmToPx);
            int height = (int)Math.Ceiling(HeightMm * mmToPx);

            using var bitmap = new SKBitmap(width, height);
            using var canvas = new SKCanvas(bitmap);

            canvas.Scale(mmToPx);

            canvas.Clear(SKColors.White);

            var selectedStates = new List<(GraphicObject obj, bool wasSelected)>();
            foreach (var layer in Layers)
            {
                if (!layer.IsVisible) continue;
                foreach (var obj in layer.GraphicObjects)
                {
                    selectedStates.Add((obj, obj.IsSelected));
                    obj.IsSelected = false;
                }
            }

            for (int i = Layers.Count - 1; i >= 0; i--)
            {
                var layer = Layers[i];
                if (!layer.IsVisible) continue;
                foreach (var obj in layer.GraphicObjects)
                {
                    obj.Draw(canvas);
                }
            }

            // 選択状態を復元
            foreach (var (obj, wasSelected) in selectedStates)
            {
                obj.IsSelected = wasSelected;
            }

            // TIF保存 (PNGとして一旦エンコードし、WPFのTiffBitmapEncoderで変換)
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var ms = new System.IO.MemoryStream();
            data.SaveTo(ms);
            ms.Position = 0;
            
            var decoder = new System.Windows.Media.Imaging.PngBitmapDecoder(ms, System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, System.Windows.Media.Imaging.BitmapCacheOption.Default);
            var encoder = new System.Windows.Media.Imaging.TiffBitmapEncoder();
            encoder.Frames.Add(decoder.Frames[0]);
            
            using var stream = System.IO.File.OpenWrite(filePath);
            encoder.Save(stream);
        }

        private void GroupSelected()
        {
            if (_selectedObjects.Count < 2) return;

            var group = new GroupObject();
            var objectsToGroup = _selectedObjects.ToList();
            var targetLayer = FindLayer(objectsToGroup.First());
            if (targetLayer == null) return;
            var targetList = targetLayer.GraphicObjects;

            // 元の重ね順で最も下にあるオブジェクトの位置を取得
            int minIndex = objectsToGroup.Where(o => targetList.Contains(o)).Min(o => targetList.IndexOf(o));

            var commands = new List<IUndoableCommand>();

            // 子オブジェクトをグループに追加し、キャンバスからは削除するコマンドを作成
            foreach (var obj in objectsToGroup)
            {
                obj.IsSelected = false;
                group.Children.Add(obj);
            }
            // FIXME: 異なるレイヤーにまたがるグループ化は非対応とし、最初のオブジェクトのレイヤーに集約する前提とするか、または別レイヤーの削除対応が必要
            commands.Add(new RemoveObjectsCommand(targetList, objectsToGroup.Where(o => targetList.Contains(o)).ToList()));

            group.RecalculateBounds();

            // 元の重ね順位置にグループを挿入するコマンド
            int insertIndex = Math.Min(minIndex, targetList.Count);

            var composite = new GroupingCommand(targetList, objectsToGroup, group, insertIndex);
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

            var compositeCommands = new List<IUndoableCommand>();
            foreach (var group in groupsToUngroup)
            {
                var targetLayer = FindLayer(group);
                if (targetLayer == null) continue;
                compositeCommands.Add(new UngroupingCommand(targetLayer.GraphicObjects, new List<GroupObject> { group }));
            }
            
            if (compositeCommands.Count > 0)
            {
                ExecuteCommand(new CompositeCommand(compositeCommands));
            }

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
            // 保存時は深いコピーを行う
            var layersCopy = new ObservableCollection<Layer>();
            foreach (var layer in Layers)
            {
                var newLayer = new Layer
                {
                    Name = layer.Name,
                    IsVisible = layer.IsVisible,
                    IsLocked = layer.IsLocked,
                    Opacity = layer.Opacity,
                    GraphicObjects = new ObservableCollection<GraphicObject>(layer.GraphicObjects.Select(x => x.Clone()))
                };
                layersCopy.Add(newLayer);
            }

            return new ProjectData
            {
                Title = Title,
                WidthMm = WidthMm,
                HeightMm = HeightMm,
                Layers = layersCopy
            };
        }

        private void ResetImageAdjustment()
        {
            if (_selectedObject is ImageObject img)
            {
                img.Contrast = 1.0f;
                img.Brightness = 0.0f;
                Invalidate();
            }
        }

        public void LoadFromProjectData(ProjectData data)
        {
            Title = data.Title;
            WidthMm = data.WidthMm;
            HeightMm = data.HeightMm;
            
            data.EnsureLayerCompatibility(); // 古いデータの互換処理

            Layers.Clear();
            foreach (var layer in data.Layers)
            {
                Layers.Add(layer);
            }

            ActiveLayer = Layers.Count > 0 ? Layers[0] : null;
            
            // 状態リセット
            ClearSelection();
            _undoStack.Clear();
            _redoStack.Clear();
            (UndoCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RedoCommand as RelayCommand)?.RaiseCanExecuteChanged();
            Invalidate();

            // ロード直後は保存済み状態とする
            MarkAsSaved();
        }
    }
}
