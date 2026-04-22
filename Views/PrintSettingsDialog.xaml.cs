using System.Windows;
using System.Windows.Controls;
using FigCrafterApp.ViewModels;

namespace FigCrafterApp.Views
{
    public partial class PrintSettingsDialog : Window
    {
        private readonly CanvasViewModel _viewModel;
        public double PrintScale { get; private set; } = 1.0;
        public bool AutoFit { get; private set; }
        private bool _isInitialized = false;

        public PrintSettingsDialog(CanvasViewModel viewModel)
        {
            _viewModel = viewModel;
            InitializeComponent();
            _isInitialized = true;
            UpdatePreview();
        }

        private void SettingsChanged(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            if (sender == ScaleSlider && ScaleTextBox != null)
                ScaleTextBox.Text = ((int)ScaleSlider.Value).ToString();

            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (ScaleTextBox == null || PreviewImage == null) return;

            if (double.TryParse(ScaleTextBox.Text, out double val))
            {
                PrintScale = val / 100.0;
                AutoFit = AutoFitCheckBox.IsChecked ?? false;
                PreviewImage.Source = _viewModel.GeneratePreviewImage(PrintScale, AutoFit);
            }
        }

        private void Print_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}