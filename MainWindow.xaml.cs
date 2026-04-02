using System.Text;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using FigCrafterApp.Models;
using Windows.System.Profile;
using System.Linq;

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

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        // DataContextからViewModelを取得
        if (DataContext is ViewModels.MainViewModel vm)
        {
            // 未保存のドキュメントをリストアップ
            var dirtyDocs = vm.Documents.Where(d => d.IsDirty).ToList();

            foreach (var doc in dirtyDocs)
            {
                vm.ActiveDocument = doc;

                var result = MessageBox.Show(
                    $"ドキュメント '{doc.Title}' に未保存の変更があります。保存して終了しますか？",
                        "終了の確認",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Warning);
                
                if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
                else if (result == MessageBoxResult.Yes)
                {
                    if (vm.SaveProjectCommand != null && vm.SaveProjectCommand.CanExecute(null))
                    {
                        vm.SaveProjectCommand.Execute(null);

                        // 保存が未完了の場合には終了を中断
                        if (doc.IsDirty)
                        {
                            e.Cancel = true;
                            return;
                        }
                    }
                }
            }
        }
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

    private void StrokeWidthTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            ApplyStrokeWidthFromTextBox(sender as System.Windows.Controls.TextBox);
            
            // Enterを押したらフォーカスを外して入力を確定させる
            System.Windows.Input.Keyboard.ClearFocus(); 
        }
    }

    private void StrokeWidthTextBox_LostFocus(object sender, System.Windows.RoutedEventArgs e)
    {
        ApplyStrokeWidthFromTextBox(sender as System.Windows.Controls.TextBox);
    }

    private void ApplyStrokeWidthFromTextBox(System.Windows.Controls.TextBox? textBox)
    {
        if (textBox != null && float.TryParse(textBox.Text, out float pt))
        {
            if (DataContext is FigCrafterApp.ViewModels.MainViewModel vm)
            {
                if (vm.SetStrokeWidthCommand != null && vm.SetStrokeWidthCommand.CanExecute(pt))
                {
                    vm.SetStrokeWidthCommand.Execute(pt);
                }
            }
        }
    }

    // 上下ボタン共通の処理（どんな型や場所にあっても確実に値を変更する）
    private void ChangeStrokeWidth(object sender, double amount)
    {
        // 1. 押されたボタンとその裏側のデータ (DataContext) を取得
        if (sender is System.Windows.FrameworkElement button && button.DataContext != null)
        {
            var context = button.DataContext;
            
            // 2. データの中から "StrokeWidth" か "CurrentStrokeWidth" というプロパティを探す
            var property = context.GetType().GetProperty("StrokeWidth") 
                        ?? context.GetType().GetProperty("CurrentStrokeWidth");

            if (property != null)
            {
                // 3. 現在の値を読み取って増減させる
                double currentValue = Convert.ToDouble(property.GetValue(context));
                double newValue = Math.Round(currentValue + amount, 1);

                // 線幅が0以下にならないように制限
                if (newValue > 0)
                {
                    // 4. 元の型 (float, double等) に変換して安全にデータを上書きする
                    object convertedValue = Convert.ChangeType(newValue, property.PropertyType);
                    property.SetValue(context, convertedValue);
                }
            }
        }
    }
}