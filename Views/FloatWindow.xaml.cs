using System.Windows;
using FigCrafterApp.ViewModels;

namespace FigCrafterApp.Views
{
    public partial class FloatWindow : Window
    {
        public FloatWindow()
        {
            InitializeComponent();
        }

        public FloatWindow(CanvasViewModel viewModel) : this()
        {
            DataContext = viewModel;
        }
    }
}
