using System.Windows.Data;
using System.Globalization;

namespace FigCrafterApp
{
    public class EnumToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value?.Equals(parameter) ?? false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value?.Equals(true) == true ? parameter : Binding.DoNothing;
        }
    }

    /// <summary>
    /// ミリメートル(mm) と ポイント(pt) の変換を行うコンバーター
    /// </summary>
    public class FontSizeConverter : IValueConverter
    {
        private const double MmToPt = 72.0 / 25.4;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is float mm)
            {
                return (double)(mm * MmToPt);
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string str && double.TryParse(str, out double pt))
            {
                return (float)(pt / MmToPt);
            }
            if (value is double d)
            {
                return (float)(d / MmToPt);
            }
            return value;
        }
    }
}
