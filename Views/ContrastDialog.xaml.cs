using System.Windows;
using FigCrafterApp.Models;

namespace FigCrafterApp.Views
{
    public partial class ContrastDialog : Window
    {
        private readonly ImageObject _image;
        private readonly float _originalContrast;

        public ContrastDialog(ImageObject image)
        {
            InitializeComponent();
            _image = image;
            _originalContrast = image.Contrast;
            DataContext = image;
            this.Owner = Application.Current.MainWindow;
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _image.Contrast = _originalContrast;
            DialogResult = false;
            Close();
        }
    }
}
