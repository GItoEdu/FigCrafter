namespace FigCrafterApp
{
    using System;
    using System.Linq;
    using System.Windows.Data;
    using System.Globalization;
    using System.Windows;
    using FigCrafterApp.Models;

    public class TypeVisibilityConverter : IValueConverter
    {
        public static readonly TypeVisibilityConverter Instance = new TypeVisibilityConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null) return Visibility.Collapsed;

            string targetTypeName = parameter.ToString() ?? "";

            Type type = value.GetType();

            // 直接の型一致
            if (type.Name == targetTypeName)
            {
                return Visibility.Visible;
            }

            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
