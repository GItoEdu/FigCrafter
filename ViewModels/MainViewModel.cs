using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Win32;
using SkiaSharp;
using FigCrafterApp.Models;
using FigCrafterApp.Serialization;
using System.Text.Json;
using System.IO;
using System.Linq;
using System.Numerics;

namespace FigCrafterApp.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private ObservableCollection<CanvasViewModel> _documents = new();
        private CanvasViewModel? _activeDocument;
        private float _currentStrokeWidth = 0.5f * (25.4f / 72.0f);

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

        // 全ドキュメントの未保存状態を判定
        public bool IsDirty => _documents.Any(doc => doc.IsDirty);

        // 線幅
        public float CurrentStrokeWidth
        {
            get => _currentStrokeWidth;
            set => SetProperty(ref _currentStrokeWidth, value);
        }

        public ICommand NewDocumentCommand { get; }
        public ICommand CloseDocumentCommand { get; }
        public ICommand FloatDocumentCommand { get; }

        public ICommand ChangeFillColorCommand { get; }
        public ICommand ChangeStrokeColorCommand { get; }

        public ICommand ImportFileCommand { get; }
        public ICommand OpenProjectCommand { get; }
        public ICommand SaveProjectCommand { get; }
        public ICommand SaveAsProjectCommand { get; }
        public ICommand ExportMixedCommand { get; }
        public ICommand ShowContrastDialogCommand { get; }

        public MainViewModel()
        {
            NewDocumentCommand = new RelayCommand(p => AddNewDocument());
            CloseDocumentCommand = new RelayCommand(p => CloseDocument(p as CanvasViewModel));
            FloatDocumentCommand = new RelayCommand(p => FloatDocument(p as CanvasViewModel));

            ChangeFillColorCommand = new RelayCommand(p => ChangeSelectedObjectColor(p?.ToString(), true));
            ChangeStrokeColorCommand = new RelayCommand(p => ChangeSelectedObjectColor(p?.ToString(), false));

            OpenProjectCommand = new RelayCommand(async p => await OpenFileAsync());
            SaveProjectCommand = new RelayCommand(p => SaveProject());
            SaveAsProjectCommand = new RelayCommand(p => SaveProjectAs());
            ImportFileCommand = new RelayCommand(async p => await OpenFileAsync());
            ExportMixedCommand = new RelayCommand(p => ExportMixed());
            ShowContrastDialogCommand = new RelayCommand(p => ShowContrastDialog());

            // 起動時は何も初期化しない (空の状態から開始)
            // AddNewDocument();
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

            // HEX入力 (#RRGGBB または #AARRGGBB) の処理
            if (colorName.StartsWith("#"))
            {
                string hex = colorName.TrimStart('#');
                if (hex.Length == 6)
                {
                    // #RRGGBB → 不透明色
                    if (byte.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out byte r) &&
                        byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out byte g) &&
                        byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out byte b))
                    {
                        color = new SKColor(r, g, b);
                    }
                }
                else if (hex.Length == 8)
                {
                    // #AARRGGBB
                    if (byte.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out byte a) &&
                        byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out byte r) &&
                        byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out byte g) &&
                        byte.TryParse(hex.Substring(6, 2), System.Globalization.NumberStyles.HexNumber, null, out byte b))
                    {
                        color = new SKColor(r, g, b, a);
                    }
                }
            }
            else
            {
                // 色名による指定
                switch (colorName.ToLower())
                {
                    // 基本12色
                    case "black": color = SKColors.Black; break;
                    case "white": color = SKColors.White; break;
                    case "red": color = SKColors.Red; break;
                    case "orange": color = new SKColor(255, 165, 0); break;
                    case "yellow": color = SKColors.Yellow; break;
                    case "green": color = new SKColor(0, 128, 0); break;
                    case "cyan": color = SKColors.Cyan; break;
                    case "blue": color = SKColors.Blue; break;
                    case "purple": color = new SKColor(128, 0, 128); break;
                    case "pink": color = new SKColor(255, 105, 180); break;
                    case "brown": color = new SKColor(139, 69, 19); break;
                    case "gray": color = SKColors.Gray; break;
                    // 明るいバリエーション
                    case "lightgray": color = SKColors.LightGray; break;
                    case "lightred": color = new SKColor(255, 102, 102); break;
                    case "lightorange": color = new SKColor(255, 200, 100); break;
                    case "lightyellow": color = new SKColor(255, 255, 153); break;
                    case "lightgreen": color = SKColors.LightGreen; break;
                    case "lightcyan": color = new SKColor(153, 255, 255); break;
                    case "lightblue": color = SKColors.LightBlue; break;
                    case "lightpurple": color = new SKColor(200, 153, 255); break;
                    case "lightpink": color = new SKColor(255, 182, 193); break;
                    case "lightbrown": color = new SKColor(210, 180, 140); break;
                    case "skyblue": color = SKColors.SkyBlue; break;
                    case "salmon": color = SKColors.Salmon; break;
                    // 暗いバリエーション
                    case "darkgray": color = SKColors.DarkGray; break;
                    case "darkred": color = SKColors.DarkRed; break;
                    case "darkorange": color = new SKColor(200, 120, 0); break;
                    case "darkyellow": color = new SKColor(200, 200, 0); break;
                    case "darkgreen": color = SKColors.DarkGreen; break;
                    case "darkcyan": color = SKColors.DarkCyan; break;
                    case "darkblue": color = SKColors.DarkBlue; break;
                    case "darkpurple": color = new SKColor(80, 0, 80); break;
                    case "darkpink": color = new SKColor(200, 50, 100); break;
                    case "darkbrown": color = new SKColor(101, 50, 10); break;
                    case "navy": color = SKColors.Navy; break;
                    case "maroon": color = SKColors.Maroon; break;
                    // 特殊
                    case "transparent": color = SKColors.Transparent; break;
                }
            }

            var obj = ActiveDocument.SelectedObject;
            if (isFill)
            {
                var cmd = new FigCrafterApp.Commands.PropertyChangeCommand(obj, nameof(GraphicObject.FillColor), obj.FillColor, color);
                ActiveDocument.ExecuteCommand(cmd);
            }
            else
            {
                var cmd = new FigCrafterApp.Commands.PropertyChangeCommand(obj, nameof(GraphicObject.StrokeColor), obj.StrokeColor, color);
                ActiveDocument.ExecuteCommand(cmd);
            }
            ActiveDocument.Invalidate();
        }

        private void ExportMixed()
        {
            if (ActiveDocument == null) return;

            var dialog = new SaveFileDialog
            {
                Filter = "PNG画像 (*.png)|*.png|PDFファイル (*.pdf)|*.pdf|TIFF画像 (*.tif;*.tiff)|*.tif;*.tiff",
                FileName = ActiveDocument.Title,
                Title = "エクスポート"
            };

            if (dialog.ShowDialog() == true)
            {
                string extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                try
                {
                    switch (extension)
                    {
                        case ".png":
                            bool transparent = dialog.FileName.Contains("_transparent", StringComparison.OrdinalIgnoreCase);
                            ActiveDocument.ExportPng(dialog.FileName, transparent);
                            break;
                        case ".pdf":
                            ActiveDocument.ExportPdf(dialog.FileName);
                            break;
                        case ".tif":
                        case ".tiff":
                            ActiveDocument.ExportTif(dialog.FileName);
                            break;
                        default:
                            System.Windows.MessageBox.Show("未対応の拡張子です。", "エラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                            return;
                    }
                    System.Windows.MessageBox.Show($"正常にエクスポートしました:\n{dialog.FileName}", "エクスポート完了", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"エクスポートに失敗しました:\n{ex.Message}", "エラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }

        private async System.Threading.Tasks.Task OpenFileAsync()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "FigCrafter プロジェクト (*.fcp)|*.fcp|画像・AI・EMFファイル|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.ai;*.pdf;*.emf;*.wmf|すべてのファイル (*.*)|*.*",
                Title = "プロジェクトを開く / ファイルをインポート"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    string extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                    if (extension == ".fcp")
                    {
                        // プロジェクトとして開く
                        string json = File.ReadAllText(dialog.FileName);
                        var projectData = JsonSerializer.Deserialize<ProjectData>(json, ProjectData.GetSerializerOptions());
                        
                        if (projectData != null)
                        {
                            var newDoc = new CanvasViewModel();
                            newDoc.LoadFromProjectData(projectData);
                            newDoc.FilePath = dialog.FileName;
                            Documents.Add(newDoc);
                            ActiveDocument = newDoc;
                        }
                    }
                    else
                    {
                        // キャンバスがなければ新規作成
                        if (ActiveDocument == null)
                        {
                            AddNewDocument();
                        }
                        
                        string ext = System.IO.Path.GetExtension(dialog.FileName).ToLower();
                        if (ext == ".emf" || ext == ".wmf" || ext == ".pdf")
                        {
                            var vectorGroup = FigCrafterApp.Helpers.VectorFileParser.ParseVectorFile(dialog.FileName);
                            if (vectorGroup != null)
                            {
                                if (ActiveDocument != null)
                                {
                                    // 初期配置位置を (10, 10) に調整
                                    float offsetX = 10 - vectorGroup.X;
                                    float offsetY = 10 - vectorGroup.Y;
                                    foreach (var child in vectorGroup.Children)
                                    {
                                        child.X += offsetX;
                                        child.Y += offsetY;
                                        if (child is LineObject line)
                                        {
                                            line.EndX += offsetX;
                                            line.EndY += offsetY;
                                        }
                                    }
                                    vectorGroup.RecalculateBounds();

                                    ActiveDocument.ImportGraphicObject(vectorGroup);
                                    return;
                                }
                            }
                            
                            // ベクター変換に失敗した場合、かつ .pdf の場合はビットマップとして読み込むフォールバックを行う
                            if (ext != ".pdf")
                            {
                                System.Windows.MessageBox.Show("ファイルの解析に失敗しました。", "エラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                                return;
                            }
                        }

                        // 画像等としてインポート。新規レイヤーを作成してそこに配置する
                        var bitmap = await FigCrafterApp.Helpers.ImportHelper.ImportFileAsync(dialog.FileName);
                        if (bitmap != null)
                        {
                            if (ActiveDocument != null)
                            {
                                var (dpiX, dpiY) = FigCrafterApp.Helpers.ImportHelper.GetImageDpi(dialog.FileName);
                                ActiveDocument.ImportImageAsGroup(bitmap, dpiX, dpiY);
                            }
                        }
                        else
                        {
                            System.Windows.MessageBox.Show("ファイルの読み込みに失敗しました。未対応の形式かデータが破損しています。", "エラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"ファイルの読み込みに失敗しました:\n{ex.Message}", "エラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }

        private void SaveProject()
        {
            if (ActiveDocument == null) return;

            // ファイルパスが既知の場合は上書き保存
            if (!string.IsNullOrEmpty(ActiveDocument.FilePath))
            {
                try
                {
                    var projectData = ActiveDocument.CreateProjectData();
                    string json = JsonSerializer.Serialize(projectData, ProjectData.GetSerializerOptions());
                    File.WriteAllText(ActiveDocument.FilePath, json);

                    // 保存成功時にフラグをリセット
                    ActiveDocument.MarkAsSaved();
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"プロジェクトの保存に失敗しました：\n{ex.Message}", "エラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
            else
            {
                // ファイルパスが未設定の場合は「名前を付けて保存」にフォールバック
                SaveProjectAs();
            }
        }

        private void SaveProjectAs()
        {
            if (ActiveDocument == null) return;

            var dialog = new SaveFileDialog
            {
                Filter = "FigCrafter プロジェクト (*.fcp)|*.fcp",
                FileName = $"{ActiveDocument.Title}.fcp",
                Title = "プロジェクトを保存"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var projectData = ActiveDocument.CreateProjectData();
                    string json = JsonSerializer.Serialize(projectData, ProjectData.GetSerializerOptions());
                    File.WriteAllText(dialog.FileName, json);
                    
                    // タイトルとファイルパスを更新
                    ActiveDocument.Title = Path.GetFileNameWithoutExtension(dialog.FileName);
                    ActiveDocument.FilePath = dialog.FileName;

                    // 保存成功時にフラグをリセット
                    ActiveDocument.MarkAsSaved();
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"プロジェクトの保存に失敗しました：\n{ex.Message}", "エラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }

        public async System.Threading.Tasks.Task ProcessDroppedFilesAsync(string[] filePaths)
        {
            if (filePaths == null || filePaths.Length == 0) return;

            foreach (var path in filePaths)
            {
                try
                {
                    string extension = Path.GetExtension(path).ToLowerInvariant();
                    if (extension == ".fcp")
                    {
                        // プロジェクトとして新しく開く
                        string json = File.ReadAllText(path);
                        var projectData = JsonSerializer.Deserialize<ProjectData>(json, ProjectData.GetSerializerOptions());
                        if (projectData != null)
                        {
                            var newDoc = new CanvasViewModel();
                            newDoc.LoadFromProjectData(projectData);
                            newDoc.FilePath = path;
                            Documents.Add(newDoc);
                            ActiveDocument = newDoc;
                        }
                    }
                    else
                    {
                        // アクティブなドキュメントがなければ新規作成
                        if (ActiveDocument == null)
                        {
                            AddNewDocument();
                        }

                        if (extension == ".emf" || extension == ".wmf" || extension == ".pdf" || extension == ".ai")
                        {
                            FigCrafterApp.Models.GroupObject? vectorGroup = null;

                            if (extension == ".ai")
                            {
                                // DEBUG：aiファイルのoperationを全てtxtファイルにダンプする
                                // FigCrafterApp.Helpers.VectorFileParser.DumpPdfOperations(path);
                                // return;

                                // DEBUG：aiファイルに格納されているラスタ画像のメタ情報をダンプする
                                // FigCrafterApp.Helpers.VectorFileParser.DumpPdfImageMetadata(path);
                                // return;

                                // DEBUG：aiファイルに格納されているラスタ画像をPNG形式で出力する
                                // FigCrafterApp.Helpers.VectorFileParser.ExportImagesFromPdf(path, @"C:\temp");
                                // return;

                                var parsedObjects = await System.Threading.Tasks.Task.Run(() =>
                                FigCrafterApp.Helpers.VectorFileParser.ParsePdfFile(path));

                                if (parsedObjects != null && parsedObjects.Count > 0)
                                {
                                    vectorGroup = new FigCrafterApp.Models.GroupObject();
                                    foreach (var obj in parsedObjects)
                                    {
                                        vectorGroup.Children.Add(obj);
                                    }

                                    // グループ全体のバウンディングボックスを計算
                                    float minX = float.MaxValue, minY = float.MaxValue;
                                    float maxX = float.MinValue, maxY = float.MinValue;
                                    foreach (var child in vectorGroup.Children)
                                    {
                                        if (child.X < minX) minX = child.X;
                                        if (child.Y < minY) minY = child.Y;
                                        if (child.X + child.Width > maxX) maxX = child.X + child.Width;
                                        if (child.Y + child.Height > maxY) maxY = child.Y + child.Height;
                                    }
                                    vectorGroup.X = minX;
                                    vectorGroup.Y = minY;
                                    vectorGroup.Width = Math.Max(0.1f, maxX - minX);
                                    vectorGroup.Height = Math.Max(0.1f, maxY - minY);
                                }
                            }
                            else
                            {
                                vectorGroup = FigCrafterApp.Helpers.VectorFileParser.ParseVectorFile(path);   
                            }

                            if (vectorGroup != null && ActiveDocument != null)
                            {
                                // 初期配置位置を調整
                                float offsetX = 10 - vectorGroup.X;
                                float offsetY = 10 - vectorGroup.Y;
                                foreach (var child in vectorGroup.Children)
                                {
                                    child.X += offsetX;
                                    child.Y += offsetY;
                                }
                                vectorGroup.X += offsetX;
                                vectorGroup.Y += offsetY;
                                
                                vectorGroup.RecalculateBounds();
                                ActiveDocument.ImportGraphicObject(vectorGroup);
                                continue;
                            }
                            
                            // PDF の場合はベクター変換に失敗してもビットマップとして読み込むフォールバックを行う
                            if (extension != ".pdf") continue;
                        }

                        // 画像としてインポート
                        var bitmap = await FigCrafterApp.Helpers.ImportHelper.ImportFileAsync(path);
                        if (bitmap != null && ActiveDocument != null)
                        {
                            var (dpiX, dpiY) = FigCrafterApp.Helpers.ImportHelper.GetImageDpi(path);
                            ActiveDocument.ImportImageAsGroup(bitmap, dpiX, dpiY);
                        }
                        else
                        {
                            System.Windows.MessageBox.Show($"ファイルの読み込みに失敗しました: {path}", "インポートエラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Drop Error ({path}): {ex.Message}");
                }
            }
        }

        private void ShowContrastDialog()
        {
            if (ActiveDocument?.SelectedObject is ImageObject image)
            {
                float originalMin = image.Minimum;
                float originalMax = image.Maximum;
                bool originalGrayscale = image.IsGrayscale;

                var dialog = new FigCrafterApp.Views.ContrastDialog(image);
                if (dialog.ShowDialog() == true)
                {
                    // 確定時は Undo 用に一括コマンドを発行
                    var commands = new List<FigCrafterApp.Commands.IUndoableCommand>();
                    
                    if (Math.Abs(image.Minimum - originalMin) > 0.001f)
                    {
                        commands.Add(new FigCrafterApp.Commands.PropertyChangeCommand(image, nameof(ImageObject.Minimum), originalMin, image.Minimum));
                    }

                    if (Math.Abs(image.Maximum - originalMax) > 0.001f)
                    {
                        commands.Add(new FigCrafterApp.Commands.PropertyChangeCommand(image, nameof(ImageObject.Maximum), originalMax, image.Maximum));
                    }

                    if (image.IsGrayscale != originalGrayscale)
                    {
                        commands.Add(new FigCrafterApp.Commands.PropertyChangeCommand(image, nameof(ImageObject.IsGrayscale), originalGrayscale, image.IsGrayscale));
                    }

                    if (commands.Count > 0)
                    {
                        // 1つのコマンドとして実行（Push）
                        if (commands.Count == 1)
                            ActiveDocument.ExecuteCommand(commands[0]);
                        else
                            ActiveDocument.ExecuteCommand(new FigCrafterApp.Commands.CompositeCommand(commands));
                    }
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

        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
