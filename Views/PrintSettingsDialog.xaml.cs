using System.Windows;

namespace FigCrafterApp.Views
{
    public partial class PrintSettingsDialog : Window
    {
        public double PrintScale { get; private set; } = 1.0;
        public bool AutoFit { get; private set; }

        public PrintSettingsDialog()
        {
            InitializeComponent();
            ScaleSlider.ValueChanged += (s, e) => ScaleTextBox.Text = ((int)ScaleSlider.Value).ToString();
        }

        private void Print_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(ScaleTextBox.Text, out double val))
            {
                PrintScale = val / 100.0;
                AutoFit = AutoFitCheckBox.IsChecked ?? false;
                DialogResult = true;
            }
        }
    }
}