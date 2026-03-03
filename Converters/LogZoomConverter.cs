using System;
using System.Globalization;
using System.Windows.Data;

namespace FigCrafterApp
{
    /// <summary>
    /// Sliderの値 (-1.0 ～ 1.0) を ZoomLevel (0.1(10%) ～ 10.0(1000%)) に相互変換するコンバーター。
    /// これによりSliderの中央(0.0)が等倍(100%)になります。
    /// </summary>
    public class LogZoomConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double zoomLevel)
            {
                // ZoomLevel -> Slider Value
                // 10^x = zoomLevel  =>  x = log10(zoomLevel)
                return Math.Log10(zoomLevel);
            }
            return 0.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double sliderValue)
            {
                // Slider Value -> ZoomLevel
                // zoomLevel = 10^sliderValue
                return Math.Pow(10, sliderValue);
            }
            return 1.0;
        }
    }
}
