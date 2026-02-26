using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Win32;
using SkiaSharp;
using FigCrafterApp.Models;

namespace FigCrafterApp.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private ObservableCollection<CanvasViewModel> _documents = new();
        private CanvasViewModel? _activeDocument;

        public ObservableCollection<CanvasViewModel> Documents
        {
            get => _documents;
            set => SetProperty(ref _documents, value);
        }

        public CanvasViewModel? ActiveDocument
        {
            get => _activeDocument;
            set => SetProperty(ref _activeDocument, value);
        }

        public ICommand NewDocumentCommand { get; }
        public ICommand CloseDocumentCommand { get; }
        public ICommand FloatDocumentCommand { get; }

        public ICommand ChangeFillColorCommand { get; }
        public ICommand ChangeStrokeColorCommand { get; }
        public ICommand ExportPngCommand { get; }

        public MainViewModel()
        {
            NewDocumentCommand = new RelayCommand(p => AddNewDocument());
            CloseDocumentCommand = new RelayCommand(p => CloseDocument(p as CanvasViewModel));
            FloatDocumentCommand = new RelayCommand(p => FloatDocument(p as CanvasViewModel));

            ChangeFillColorCommand = new RelayCommand(p => ChangeSelectedObjectColor(p?.ToString(), true));
            ChangeStrokeColorCommand = new RelayCommand(p => ChangeSelectedObjectColor(p?.ToString(), false));
            ExportPngCommand = new RelayCommand(p => ExportPng());

            // 初期ドキュメントを追加
            AddNewDocument();
        }

        private void AddNewDocument()
        {
            var newDoc = new CanvasViewModel($"名称未設定 {Documents.Count + 1}");
            Documents.Add(newDoc);
            ActiveDocument = newDoc;
        }

        private void CloseDocument(CanvasViewModel? doc)
        {
            if (doc != null && Documents.Contains(doc))
            {
                Documents.Remove(doc);
                if (ActiveDocument == doc)
                {
                    ActiveDocument = Documents.Count > 0 ? Documents[0] : null;
                }
            }
        }

        private void FloatDocument(CanvasViewModel? doc)
        {
            if (doc != null && Documents.Contains(doc))
            {
                Documents.Remove(doc);
                if (ActiveDocument == doc)
                {
                    ActiveDocument = Documents.Count > 0 ? Documents[0] : null;
                }

                var floatWindow = new FigCrafterApp.Views.FloatWindow(doc);
                floatWindow.Show();
            }
        }

        private void ChangeSelectedObjectColor(string? colorName, bool isFill)
        {
            if (string.IsNullOrEmpty(colorName) || ActiveDocument?.SelectedObject == null) return;

            SKColor color = SKColors.Transparent;
            switch (colorName.ToLower())
            {
                case "skyblue": color = SKColors.SkyBlue; break;
                case "salmon": color = SKColors.Salmon; break;
                case "lightgreen": color = SKColors.LightGreen; break;
                case "black": color = SKColors.Black; break;
                case "red": color = SKColors.Red; break;
                case "blue": color = SKColors.Blue; break;
                case "transparent": color = SKColors.Transparent; break;
            }

            if (isFill)
            {
                ActiveDocument.SelectedObject.FillColor = color;
            }
            else
            {
                ActiveDocument.SelectedObject.StrokeColor = color;
            }
            ActiveDocument.Invalidate();
        }

        private void ExportPng()
        {
            if (ActiveDocument == null) return;

            var dialog = new SaveFileDialog
            {
                Filter = "PNGファイル (*.png)|*.png",
                FileName = $"{ActiveDocument.Title}.png",
                Title = "PNG書き出し"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // ファイル名に"_transparent"が含まれていれば透過背景（将来的にはUIでチェックボックス化）
                    bool transparent = dialog.FileName.Contains("_transparent", StringComparison.OrdinalIgnoreCase);
                    ActiveDocument.ExportPng(dialog.FileName, transparent);
                    System.Windows.MessageBox.Show($"PNGを保存しました:\n{dialog.FileName}", "PNG書き出し", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"PNGの保存に失敗しました:\n{ex.Message}", "エラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }
    }

    // 簡易的なRelayCommandの実装
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object? parameter) => _execute(parameter);
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
