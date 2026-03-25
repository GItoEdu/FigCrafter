using System;
using System.Globalization;
using System.Windows.Data;

namespace FigCrafterApp.Converters
{
    /// <summary>
    /// ミリメートル（mm）とポイント（pt）を相互変換するコンバーター
    /// Model（mm）<-> UI（pt）
    /// </summary>
    public class MmToPtConverter : IValueConverter
    {
        private const double PtToMm = 25.4 / 72.0;
        
        // Model（内部データ：mm）-> UI（表示：pt）への変換
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double mmDouble)
            {
                return Math.Round(mmDouble / PtToMm, 2);
            }
            if (value is float mmFloat)
            {
                return Math.Round(mmFloat / PtToMm, 2);
            }
            return value;
        }

        // UI（入力：pt）-> Model（内部データ：mm）への変換
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double ptValue = 0;

            if (value is double d) ptValue = d;
            else if (value is float f) ptValue = f;
            else if (value is string s && double.TryParse(s, out double parsed)) ptValue = parsed;
            else return value;

            // GraphicObjectのStrokeWidthがfloat型なのでfloatで返す
            return (float)(ptValue * PtToMm);
        }
    }
}