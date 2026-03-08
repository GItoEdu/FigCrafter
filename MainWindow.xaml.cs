using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace FigCrafterApp;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[]? files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && DataContext is ViewModels.MainViewModel vm)
            {
                _ = vm.ProcessDroppedFilesAsync(files);
            }
        }
    }

    private void FontSizeInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (sender is TextBox tb)
            {
                // フォーカスを外すことでバインディング（LostFocus）をトリガーする
                DependencyObject parent = VisualTreeHelper.GetParent(tb);
                while (parent != null && !(parent is FrameworkElement))
                {
                    parent = VisualTreeHelper.GetParent(parent);
                }
                (parent as FrameworkElement)?.Focus();
                e.Handled = true;
            }
        }
    }
}