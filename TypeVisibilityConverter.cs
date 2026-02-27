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

            // 直接の型一致
            if (value.GetType().Name == targetTypeName)
            {
                return Visibility.Visible;
            }

            // GroupObject の子に対象の型が含まれていれば表示
            // （例: 画像ペースト時に ImageObject が GroupObject の子として存在する）
            if (value is GroupObject group)
            {
                if (ContainsType(group, targetTypeName))
                {
                    return Visibility.Visible;
                }
            }

            return Visibility.Collapsed;
        }

        /// <summary>
        /// GroupObject の子要素を再帰的に探索し、指定の型名が含まれるか判定
        /// </summary>
        private bool ContainsType(GroupObject group, string typeName)
        {
            foreach (var child in group.Children)
            {
                if (child.GetType().Name == typeName) return true;
                if (child is GroupObject nestedGroup && ContainsType(nestedGroup, typeName)) return true;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
