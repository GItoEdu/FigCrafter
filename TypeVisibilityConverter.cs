namespace FigCrafterApp
{
    using System;
    using System.Windows.Data;
    using System.Globalization;
    using System.Windows;

    public class TypeVisibilityConverter : IValueConverter
    {
        public static readonly TypeVisibilityConverter Instance = new TypeVisibilityConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null) return Visibility.Collapsed;

            string targetTypeName = parameter.ToString() ?? "";
            if (value.GetType().Name == targetTypeName)
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
