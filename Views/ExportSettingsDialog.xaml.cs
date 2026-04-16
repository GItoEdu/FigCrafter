using System.Windows;
using System.Windows.Controls;

namespace FigCrafterApp.Views
{
    public partial class ExportSettingsDialog : Window
    {
        public string SelectedFormat { get; private set; } = ".png";
        public float SelectedDpi { get; private set; } = 96f;
        public bool IsTransparent { get; private set; } = false;

        public ExportSettingsDialog()
        {
            InitializeComponent();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (FormatComboBox.SelectedItem is ComboBoxItem formatItem && formatItem.Tag != null)
                SelectedFormat = formatItem.Tag.ToString() ?? ".png";

            if (DpiComboBox.SelectedItem is ComboBoxItem dpiItem && dpiItem.Tag != null)
            {
                if (float.TryParse(dpiItem.Tag.ToString(), out float dpi))
                    SelectedDpi = dpi;
            }

            IsTransparent = TransparentCheckBox.IsChecked ?? false;
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}