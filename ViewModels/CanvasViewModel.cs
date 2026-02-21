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

        public event EventHandler? InvalidateRequested;

        public ICommand BringToFrontCommand { get; }
        public ICommand SendToBackCommand { get; }
        public ICommand BringForwardCommand { get; }
        public ICommand SendBackwardCommand { get; }

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

        private void OnSelectedObjectPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // 選択オブジェクトのプロパティ変更時は再描画を要求する
            Invalidate();
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
    }
}
