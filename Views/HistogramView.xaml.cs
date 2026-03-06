using System.Windows;
using System.Windows.Controls;
using SkiaSharp;
using FigCrafterApp.Models;

namespace FigCrafterApp.Views
{
    public partial class HistogramView : UserControl
    {
        public static readonly DependencyProperty HistogramDataProperty =
            DependencyProperty.Register("HistogramData", typeof(int[]), typeof(HistogramView), 
                new PropertyMetadata(null, OnHistogramDataChanged));

        public int[]? HistogramData
        {
            get => (int[]?)GetValue(HistogramDataProperty);
            set => SetValue(HistogramDataProperty, value);
        }

        public HistogramView()
        {
            InitializeComponent();
        }

        private static void OnHistogramDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HistogramView view)
            {
                view.SkiaElement.InvalidateVisual();
            }
        }

        private void SkiaElement_PaintSurface(object sender, SkiaSharp.Views.Desktop.SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            var histogram = HistogramData;
            if (histogram == null || histogram.Length < 256) return;

            float width = (float)e.Info.Width;
            float height = (float)e.Info.Height;

            int max = 0;
            foreach (var val in histogram) if (val > max) max = val;
            if (max == 0) return;

            using var paint = new SKPaint
            {
                Color = SKColors.LightGray,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };

            float barWidth = width / 256f;
            for (int i = 0; i < 256; i++)
            {
                float barHeight = (float)histogram[i] / max * height;
                canvas.DrawRect(i * barWidth, height - barHeight, barWidth, barHeight, paint);
            }
        }
    }
}
