using System.Collections.ObjectModel;
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

        public event EventHandler? InvalidateRequested;

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
        }

        public CanvasViewModel(string title)
        {
            Title = title;
        }
    }
}
