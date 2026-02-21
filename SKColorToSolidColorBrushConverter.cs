namespace FigCrafterApp
{
    using System;
    using System.Windows.Data;
    using System.Windows.Media;
    using SkiaSharp;
    using SkiaSharp.Views.WPF;
    using System.Globalization;

    public class SKColorToSolidColorBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SKColor skColor)
            {
                return new SolidColorBrush(Color.FromArgb(skColor.Alpha, skColor.Red, skColor.Green, skColor.Blue));
            }
            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SolidColorBrush solidColorBrush)
            {
                var c = solidColorBrush.Color;
                return new SKColor(c.R, c.G, c.B, c.A);
            }
            return SKColors.Transparent;
        }
    }
}
