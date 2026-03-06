using System.Windows;
using FigCrafterApp.Models;

namespace FigCrafterApp.Views
{
    public partial class ContrastDialog : Window
    {
        private float _originalMinimum;
        private float _originalMaximum;
        private readonly bool _originalIsGrayscale;

        public ContrastDialog(ImageObject image)
        {
            InitializeComponent();
            DataContext = image;
            _originalMinimum = image.Minimum;
            _originalMaximum = image.Maximum;
            _originalIsGrayscale = image.IsGrayscale;
            this.Owner = Application.Current.MainWindow;
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ImageObject image)
            {
                image.Minimum = _originalMinimum;
                image.Maximum = _originalMaximum;
                image.IsGrayscale = _originalIsGrayscale;
            }
            DialogResult = false;
            Close();
        }
    }
}
