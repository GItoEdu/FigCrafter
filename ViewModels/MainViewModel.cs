using System.Collections.ObjectModel;
using System.Windows.Input;

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

        public MainViewModel()
        {
            NewDocumentCommand = new RelayCommand(p => AddNewDocument());
            CloseDocumentCommand = new RelayCommand(p => CloseDocument(p as CanvasViewModel));
            FloatDocumentCommand = new RelayCommand(p => FloatDocument(p as CanvasViewModel));

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
