using HandyControl.Controls;
using HandyControl.Data;
using Microsoft.Win32;
using Newtonsoft.Json;
using Quickenshtein;
using Solicen.JSON;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using UAssetAPI.UnrealTypes;
using TabItem = HandyControl.Controls.TabItem;

namespace KissEGUI
{
    public class DataItem : INotifyPropertyChanged
    {
        private string _fileName;
        public string FileName
        {
            get => _fileName;
            set
            {
                if (_fileName != value)
                {
                    _fileName = value;
                    OnPropertyChanged(nameof(FileName));
                }
            }
        }

        private bool _isEditing;
        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (_isEditing != value)
                {
                    _isEditing = value;
                    OnPropertyChanged(nameof(IsEditing));
                }
            }
        }
        public DataTable Values { get; set; }
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class SearchResult
    {
        public TabItem Tab { get; set; }
        public int RowIndex { get; set; }
        public int ColumnIndex { get; set; }
    }

    public class UEVersionItem
    {
        public string DisplayName { get; set; }
        public EngineVersion VersionEnum { get; set; }
    }

    public class TMSuggestion
    {
        public string Text { get; set; }
        public double Similarity { get; set; }
    }

    public class OnlineSuggestion
    {
        public string Text { get; set; }
        public string ServiceName { get; set; }
    }

    public class LanguageItem
    {
        public string Name { get; set; }
        public string IsoCode { get; set; }
    }

    public class AppSettings
    {
        public string SourceLanguage { get; set; } = "en"; // Английский по умолчанию
        public string TargetLanguage { get; set; } = "ru"; // Русский по умолчанию
    }


    public class WidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double width = (double)value;
            double margin = System.Convert.ToDouble(parameter, CultureInfo.InvariantCulture);
            return width > margin ? width - margin : double.PositiveInfinity;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : HandyControl.Controls.Window
    {
        GTranslate.Translators.YandexTranslator yTranslate = new GTranslate.Translators.YandexTranslator();
        GTranslate.Translators.GoogleTranslator gTranslate = new GTranslate.Translators.GoogleTranslator();
        GTranslate.Translators.GoogleTranslator2 gTranslateV2 = new GTranslate.Translators.GoogleTranslator2();



        public string DefaultFileName = string.Empty;
        public string FilePath = string.Empty;

        public ObservableCollection<DataItem> DataItems { get; set; }
        public ICollectionView ItemsView { get; private set; }
        public bool IsTextWrappingEnabled { get; set; } = true;
        public ObservableCollection<UEVersionItem> UEVersions { get; set; }
        public ObservableCollection<TMSuggestion> TranslationMemorySuggestions { get; set; }
        public ObservableCollection<OnlineSuggestion> OnlineSuggestions { get; set; }
        public EngineVersion SelectedUEVersion { get; set; }
        public ObservableCollection<LanguageItem> Languages { get; set; }
        public string SourceLanguage { get; set; }
        public string TargetLanguage { get; set; }
        public ICommand ShowSearchCommand { get; }

        private readonly List<SearchResult> _searchResults = new List<SearchResult>();
        private int _currentSearchIndex = -1;

        // Система памяти переводов
        private Dictionary<string, string> _translationMemory = new Dictionary<string, string>();
        private const string TranslationMemoryFileName = "memory.db";

        // Кэш онлайн-переводов
        private Dictionary<string, List<OnlineSuggestion>> _onlineTranslationCache = new Dictionary<string, List<OnlineSuggestion>>();
        private const string OnlineCacheFileName = "online_cache.db";
        private const string SettingsFileName = "settings.json";

        public MainWindow()
        {   
            InitializeComponent();
            ShowSearchCommand = new RelayCommand(_ => ShowSearchPanel());
            DataItems = new ObservableCollection<DataItem>
            {
            };
            ItemsView = CollectionViewSource.GetDefaultView(DataItems);
            ItemsView.Filter = FilterFiles;
            TranslationMemorySuggestions = new ObservableCollection<TMSuggestion>();
            OnlineSuggestions = new ObservableCollection<OnlineSuggestion>();
            Languages = new ObservableCollection<LanguageItem>();

            LoadUEVersions();
            LoadLanguages();
            LoadTranslationMemory();
            LoadOnlineTranslationCache();
            LoadSettings();

            this.DataContext = this;

            this.Closing += MainWindow_Closing;
            
        }
        /// <summary>
        /// Загружает все версии движка из UAssetAPI, форматирует их и устанавливает значение по умолчанию.
        /// </summary>
        private void LoadUEVersions()
        {
            UEVersions = new ObservableCollection<UEVersionItem>();
            foreach (EngineVersion version in Enum.GetValues(typeof(EngineVersion)))
            {
                // Пропускаем неизвестные или служебные значения
                if (version < 0) continue;
                if (version.ToString().Contains("AUTOMATIC") ||
                    version.ToString().Contains("OLDEST")) continue;

                var displayName = version.ToString().Replace("VER_", "");
                if (UEVersions.Any(x => x.DisplayName == displayName)) continue;

                UEVersions.Add(new UEVersionItem
                {
                    DisplayName = displayName,
                    VersionEnum = version
                });
            }

            // Устанавливаем последнюю версию как выбранную по умолчанию
            SelectedUEVersion = UEVersions.LastOrDefault()?.VersionEnum ?? EngineVersion.UNKNOWN;
        }

        private void LoadLanguages()
        {
            // Базовый список языков. Можно расширить при необходимости.
            Languages.Add(new LanguageItem { Name = "English", IsoCode = "en" });
            Languages.Add(new LanguageItem { Name = "Russian", IsoCode = "ru" });
            Languages.Add(new LanguageItem { Name = "German", IsoCode = "de" });
            Languages.Add(new LanguageItem { Name = "French", IsoCode = "fr" });
            Languages.Add(new LanguageItem { Name = "Spanish", IsoCode = "es" });
            Languages.Add(new LanguageItem { Name = "Italian", IsoCode = "it" });
            Languages.Add(new LanguageItem { Name = "Chinese", IsoCode = "zh" });
            Languages.Add(new LanguageItem { Name = "Japanese", IsoCode = "ja" });
            Languages.Add(new LanguageItem { Name = "Korean", IsoCode = "ko" });
            Languages.Add(new LanguageItem { Name = "Ukrainian", IsoCode = "uk" });
            Languages.Add(new LanguageItem { Name = "Polish", IsoCode = "pl" });
        }


        #region Translation Memory System

        private void LoadTranslationMemory()
        {
            if (File.Exists(TranslationMemoryFileName))
            {
                try
                {
                    var json = File.ReadAllText(TranslationMemoryFileName);
                    _translationMemory = JsonConvert.DeserializeObject<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                }
                catch (Exception ex)
                {
                    Growl.Error($"Failed to load translation memory: {ex.Message}");
                }
            }
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            var json = JsonConvert.SerializeObject(_translationMemory, Formatting.Indented);
            File.WriteAllText(TranslationMemoryFileName, json);

            var onlineCacheJson = JsonConvert.SerializeObject(_onlineTranslationCache, Formatting.Indented);
            File.WriteAllText(OnlineCacheFileName, onlineCacheJson);

            var settings = new AppSettings
            {
                SourceLanguage = this.SourceLanguage,
                TargetLanguage = this.TargetLanguage
            };
            File.WriteAllText(SettingsFileName, JsonConvert.SerializeObject(settings, Formatting.Indented));
        }

        private void LoadOnlineTranslationCache()
        {
            if (File.Exists(OnlineCacheFileName))
            {
                try
                {
                    var json = File.ReadAllText(OnlineCacheFileName);
                    _onlineTranslationCache = JsonConvert.DeserializeObject<Dictionary<string, List<OnlineSuggestion>>>(json) ?? new Dictionary<string, List<OnlineSuggestion>>();
                }
                catch (Exception ex)
                {
                    Growl.Error($"Failed to load online translation cache: {ex.Message}");
                }
            }
        }

        private void LoadSettings()
        {
            var settings = new AppSettings(); // Загружаем настройки по умолчанию
            if (File.Exists(SettingsFileName))
            {
                try
                {
                    var json = File.ReadAllText(SettingsFileName);
                    settings = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                }
                catch (Exception ex)
                {
                    Growl.Warning($"Could not load settings, using defaults. Error: {ex.Message}");
                }
            }
            SourceLanguage = settings.SourceLanguage;
            TargetLanguage = settings.TargetLanguage;
        }

        #endregion

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void NestedDataGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            e.Column.Width = new DataGridLength(1, DataGridLengthUnitType.Star);

            if (e.Column is DataGridTextColumn textColumn)
            {
                var wrapMode = IsTextWrappingEnabled ? TextWrapping.Wrap : TextWrapping.NoWrap;

                // Стиль для отображения (не в режиме редактирования)
                var displayStyle = new Style(typeof(TextBlock));
                displayStyle.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Top));
                displayStyle.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Left));
                displayStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, wrapMode));
                // displayStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, (Brush)(new BrushConverter().ConvertFrom("#cd917a"))));

                // Создаем триггер, который будет срабатывать, когда IsTextWrappingEnabled = false
                var trigger = new DataTrigger
                {
                    Binding = new Binding("IsTextWrappingEnabled") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(HandyControl.Controls.Window), 1) },
                    Value = false,
                };

                // Когда триггер активен, устанавливаем подсказку и обрезку текста
                var textBlockForToolTip = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap, // Включаем перенос текста
                };
                ToolTipService.SetInitialShowDelay(textBlockForToolTip, 3);
                // Привязываем текст этого TextBlock к тексту элемента, к которому прикреплена подсказка (т.е. к TextBlock в ячейке)
                textBlockForToolTip.SetBinding(TextBlock.TextProperty, new Binding("PlacementTarget.Text") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ToolTip), 1) });

                var toolTip = new ToolTip
                {
                    MaxWidth = 400, // Ограничиваем максимальную ширину
                    Content = textBlockForToolTip,
                    FontSize = 14,
                    HasDropShadow = true,
                };
                trigger.Setters.Add(new Setter(ToolTipService.ToolTipProperty, toolTip));
                trigger.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));

                displayStyle.Triggers.Add(trigger);
                textColumn.ElementStyle = displayStyle;


                // Стиль для редактирования, чтобы TextBox тоже поддерживал перенос строк
                var editStyle = new Style(typeof(System.Windows.Controls.TextBox));
                editStyle.Setters.Add(new Setter(System.Windows.Controls.TextBox.PaddingProperty, new Thickness(4)));
                editStyle.Setters.Add(new Setter(System.Windows.Controls.TextBox.MarginProperty, new Thickness(2)));
                editStyle.Setters.Add(new Setter(System.Windows.Controls.TextBox.TextWrappingProperty, wrapMode));
                editStyle.Setters.Add(new Setter(System.Windows.Controls.TextBox.AcceptsReturnProperty, true)); // Позволяет использовать Enter для новой строки
                editStyle.Setters.Add(new Setter(System.Windows.Controls.TextBox.VerticalContentAlignmentProperty, VerticalAlignment.Top));

                // Привязываем минимальную высоту TextBox к высоте родительской ячейки, чтобы он занимал все пространство
                var minHeightBinding = new Binding("ActualHeight") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGridCell), 1) };
                editStyle.Setters.Add(new Setter(System.Windows.Controls.TextBox.MinHeightProperty, minHeightBinding));

                textColumn.EditingElementStyle = editStyle;
            }
        }

        private double TextSize = 14;
        private void TextZoomIn_Click(object sender, RoutedEventArgs e)
        {
            TextSize = TextSize + 1;
            ChangeZoom(TextSize);
        }
        private void ChangeZoom(double textSize)
        {
            foreach (TabItem tab in MainTabControl.Items)
            {
                if (tab.Content is DataGrid grid)
                {
                    foreach (var column in grid.Columns.OfType<DataGridTextColumn>())
                    {
                        TextBlock.SetFontSize(column, (double)textSize);
                    }
                }
            }
        }
        private void TextZoomOut_Click(object sender, RoutedEventArgs e)
        {
            TextSize = TextSize - 1;
            ChangeZoom(TextSize);
        }

        private void TextWrapMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // IsChecked уже изменился благодаря двусторонней привязке,
            // нам нужно только применить это изменение ко всем загруженным DataGrid.
            var wrapMode = IsTextWrappingEnabled ? TextWrapping.Wrap : TextWrapping.NoWrap;

            foreach (TabItem tab in MainTabControl.Items)
            {
                if (tab.Content is DataGrid grid)
                {
                    foreach (var column in grid.Columns.OfType<DataGridTextColumn>())
                    {
                        // Обновляем стиль отображения
                        var newDisplayStyle = new Style(typeof(TextBlock), column.ElementStyle);
                        newDisplayStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, wrapMode));
                        column.ElementStyle = newDisplayStyle;

                        // Обновляем стиль редактирования
                        var newEditStyle = new Style(typeof(System.Windows.Controls.TextBox), column.EditingElementStyle);
                        newEditStyle.Setters.Add(new Setter(System.Windows.Controls.TextBox.TextWrappingProperty, wrapMode));
                        column.EditingElementStyle = newEditStyle;
                    }
                }
            }
        }


        #region Context Menu Handlers

        private void DataGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // Находим строку (DataGridRow), на которой был сделан клик
            var row = ItemsControl.ContainerFromElement((ItemsControl)sender, e.OriginalSource as DependencyObject) as DataGridRow;

            // Если клик был не на строке (например, на заголовке), ничего не делаем
            if (row == null)
            {
                e.Handled = true; // Предотвращаем открытие любого другого меню
                return;
            }

            // Создаем контекстное меню "на лету"
            var contextMenu = new ContextMenu();

            // Меню "Row"
            var rowMenu = new MenuItem { Header = "Row" }; // Убраны лишние отступы
            var insertBefore = new MenuItem { Header = "Insert Before" };
            insertBefore.Click += (s, a) => InsertRow(row, false);
            var insertAfter = new MenuItem { Header = "Insert After" };
            insertAfter.Click += (s, a) => InsertRow(row, true);
            var deleteRow = new MenuItem { Header = "Delete" };
            deleteRow.Click += (s, a) => { if (row.Item is DataRowView r) DeleteSelectedRow(r); };
            rowMenu.Items.Add(insertBefore);
            rowMenu.Items.Add(insertAfter);
            rowMenu.Items.Add(new Separator());
            rowMenu.Items.Add(deleteRow);

            var formatMenu = new MenuItem { Header = "Format", InputGestureText = "Ctrl+Q" };
            formatMenu.Click += (s, a) => { if (row.Item is DataRowView r) FormatSelectedRow(r); };

            // Меню "Copy"
            var copyMenu = new MenuItem { Header = "Copy" }; // Убраны лишние отступы
            var copyOriginal = new MenuItem { Header = "Original" };
            copyOriginal.Click += (s, a) => CopyCellContent(row, "Original");
            var copyNewValue = new MenuItem { Header = "NewValue" };
            copyNewValue.Click += (s, a) => CopyCellContent(row, "NewValue");
            copyMenu.Items.Add(copyOriginal);
            copyMenu.Items.Add(copyNewValue);

            // Меню "Paste"
            var pasteMenu = new MenuItem { Header = "Paste" }; // Убраны лишние отступы
            var pasteOriginal = new MenuItem { Header = "Original" };
            pasteOriginal.Click += (s, a) => PasteCellContent(row, "Original");
            var pasteNewValue = new MenuItem { Header = "NewValue" };
            pasteNewValue.Click += (s, a) => PasteCellContent(row, "NewValue");
            pasteMenu.Items.Add(pasteOriginal);
            pasteMenu.Items.Add(pasteNewValue);

            contextMenu.Items.Add(rowMenu);
            contextMenu.Items.Add(formatMenu);
            contextMenu.Items.Add(copyMenu);
            contextMenu.Items.Add(pasteMenu);

            // Присваиваем меню строке
            row.ContextMenu = contextMenu;
        }

        private void InsertRow(DataGridRow contextRow, bool after)
        {
            if (contextRow.Item is DataRowView rowView)
            {
                var table = rowView.Row.Table;
                var newRow = table.NewRow();
                int index = table.Rows.IndexOf(rowView.Row);
                table.Rows.InsertAt(newRow, after ? index + 1 : index);
            }
        }

        private void CopyCellContent(DataGridRow contextRow, string columnName)
        {
            if (contextRow.Item is DataRowView rowView && rowView.Row.Table.Columns.Contains(columnName))
            {
                Clipboard.SetText(rowView[columnName]?.ToString() ?? string.Empty);
            }
        }

        private void PasteCellContent(DataGridRow contextRow, string columnName)
        {
            if (contextRow.Item is DataRowView rowView && rowView.Row.Table.Columns.Contains(columnName))
            {
                if (Clipboard.ContainsText())
                {
                    rowView[columnName] = Clipboard.GetText();
                }
            }
        }

        #endregion
        private string _fileFilterText;
        private void FileFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            _fileFilterText = (sender as System.Windows.Controls.TextBox).Text;
            ItemsView.Refresh();
        }

        private bool FilterFiles(object item)
        {
            if (string.IsNullOrEmpty(_fileFilterText))
                return true;

            if (item is DataItem dataItem)
            {
                return dataItem.FileName.Contains(_fileFilterText, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }
        private void FileExplorer_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBox listBox && listBox.SelectedItem is DataItem selectedItem)
            {
                // Проверяем, не открыта ли уже такая вкладка
                var existingTab = MainTabControl.Items.OfType<TabItem>().FirstOrDefault(t => t.Tag == selectedItem);

                if (existingTab != null)
                {
                    // Если вкладка уже есть, просто делаем ее активной
                    MainTabControl.SelectedItem = existingTab;
                }
                else
                {
                    // Если вкладки нет, создаем новую
                    var newTab = new TabItem
                    {
                        Header = selectedItem.FileName,
                        Tag = selectedItem // Сохраняем DataItem в Tag для ленивой загрузки
                        
                    };
                    MainTabControl.Items.Add(newTab);
                    MainTabControl.SelectedItem = newTab;
                }
            }
        }

        #region File Explorer Context Menu Handlers

        private void RenameFile_Click(object sender, RoutedEventArgs e)
        {
            if (FileListBox.SelectedItem is DataItem selectedItem)
            {
                selectedItem.IsEditing = true;
            }
        }

        private void RenameTextBox_Loaded(object sender, RoutedEventArgs e)
        {
            var textBox = (System.Windows.Controls.TextBox)sender;
            textBox.Focus();
            textBox.SelectAll();
        }

        private void RenameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var textBox = (System.Windows.Controls.TextBox)sender;
            if (textBox.DataContext is DataItem item)
            {
                // Завершаем редактирование и обновляем значение
                item.FileName = textBox.Text;
                item.IsEditing = false;
            }
        }

        private void RenameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            var textBox = (System.Windows.Controls.TextBox)sender;
            if (textBox.DataContext is DataItem item)
            {
                if (e.Key == Key.Enter)
                {
                    // Применяем изменения и выходим из режима редактирования
                    item.FileName = textBox.Text;
                    item.IsEditing = false;
                }
                else if (e.Key == Key.Escape)
                {
                    // Отменяем изменения и выходим из режима редактирования
                    textBox.Text = item.FileName; // Возвращаем старое значение
                    item.IsEditing = false;
                }
            }
        }

        private string GetFileWithDialog(string filter = "All files (*.*)|*.*", string defaultExt = "*", string title = "")
        {
            var ofd = new OpenFileDialog
            {
                Filter = filter,
                DefaultExt = defaultExt,
                Title = title
            };

            if (ofd.ShowDialog() == true)
            {
                return ofd.FileName;
            }

            return string.Empty;
        }

        private void OpenTextureInjector(object sender, RoutedEventArgs e) => new TextureInjectorWindow().Show();
        private void AddRowToFile_Click(object sender, RoutedEventArgs e)
        {
            if (FileListBox.SelectedItem is DataItem selectedItem)
            {
                var table = selectedItem.Values;
                table.Rows.Add(table.NewRow());
                Growl.Success($"New row added to '{selectedItem.FileName}'");
            }
        }

        private void DeleteFile_Click(object sender, RoutedEventArgs e)
        {
            if (FileListBox.SelectedItem is DataItem selectedItem)
            {
                Growl.Ask("Are you sure you want to delete this file?", isConfirmed =>
                {
                    if (isConfirmed)
                    {
                        DataItems.Remove(selectedItem);
                        // Также закрываем соответствующую вкладку, если она открыта
                        var tabToRemove = MainTabControl.Items.OfType<TabItem>().FirstOrDefault(t => t.Tag == selectedItem);
                        if (tabToRemove != null)
                        {
                            MainTabControl.Items.Remove(tabToRemove);
                        }
                    }
                    return true;
                });
            }
        }

        #endregion

        #region Drag & Drop Handlers

        private void FileDrop_DragEnter(object sender, DragEventArgs e)
        {
            // Проверяем, что перетаскиваются именно файлы
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                var allowedExtensions = new[] { ".uasset", ".umap" };

                // Если хотя бы один файл имеет разрешенное расширение, показываем иконку копирования
                if (files.Any(f => allowedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())))
                {
                    e.Effects = DragDropEffects.Copy;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void FileDrop_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                var allowedExtensions = new[] { ".uasset", ".umap" };

                foreach (var file in files)
                {
                    // Добавляем только файлы с разрешенными расширениями
                    if (allowedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()))
                    {
                        Solicen.Kismet.AssetLoader.SetVersion(SelectedUEVersion);
                        var uber = Solicen.Kismet.BytecodeExtractor.ExtractValuesFromAssets(new [] { file });
                        if (uber.Any())
                        {
                            AddNewTable(UberToTable(uber)[0]);
                            Growl.Success($"Succesfull import: {Path.GetFileName(file)}");
                        }
                        else
                        {
                            if (Solicen.Kismet.AssetLoader.ExceptionMessage != string.Empty)
                            {
                                Growl.Error($"{Path.GetFileName(file)}:\n - UassetAPI: \"{Solicen.Kismet.AssetLoader.ExceptionMessage}\"");
                            }
                            else
                            {
                                Growl.Error($"Not find values in {Path.GetFileName(file)}");
                            }
                           
                        }
                    }
                }
            }
        }

        #endregion

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        public DataItem[] UberToTable(UberJSON[] Uber)
        {
            var dataItemCollection = new List<DataItem>();
            foreach (var file in Uber)
            {
                var table = new DataTable();
                table.Columns.Add("Original");
                table.Columns.Add("NewValue");
                table.TableName = file.FileName;
                foreach (var value in file.Values)
                {
                    table.Rows.Add($"{value.Original}", $"{value.NewValue}");
                }

                var newItem = new DataItem
                {
                    FileName = file.FileName,
                    Values = table
                };
                
                dataItemCollection.Add(newItem);
            }
            return dataItemCollection.ToArray();
        }
        public Solicen.JSON.UberJSON[] LoadUberJsonFromFileDialog()
        {
            var flDialog = new OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = "json"
            };

            if (flDialog.ShowDialog() == true)
            {
                return UberJSONProcessor.ReadFile(flDialog.FileName);
            }
            return Array.Empty<UberJSON>();
        }

        public void AddNewTable(DataItem item)
        {
            // Просто добавляем элемент в коллекцию, которая привязана к ListView
            DataItems.Add(item);
        }

        private UberJSON[] CreateUberJsonFromData()
        {
            var uberCollection = new List<UberJSON>();

            // 1. Проходим по всем элементам в основной коллекции данных
            foreach (var dataItem in DataItems)
            {
                {
                    var uberValues = new List<KismetString>();
                    var table = dataItem.Values;

                    // 3. Преобразуем каждую строку DataTable обратно в UberValue
                    foreach (DataRow row in table.Rows)
                    {
                        uberValues.Add(new KismetString
                        {
                            Original = row["Original"]?.ToString() ?? string.Empty,
                            NewValue = row["NewValue"]?.ToString() ?? string.Empty
                        });
                    }

                    uberCollection.Add(new UberJSON(dataItem.FileName)
                    {
                        Values = uberValues
                    });
                }
            }
            return uberCollection.ToArray();
        }
        private void SaveFile(object sender, RoutedEventArgs e)
        {
            var uberCollection = CreateUberJsonFromData();
            if (!uberCollection.Any()) return; // Нечего сохранять
            UberJSONProcessor.SaveFile(uberCollection.ToArray(), FilePath);
        }
        private void SaveAsFile(object sender, RoutedEventArgs e)
        { 
            var uberCollection = CreateUberJsonFromData();
            if (!uberCollection.Any()) return; // Нечего сохранять

            // 4. Открываем диалог сохранения файла
            var sfd = new SaveFileDialog
            {
                FileName = DefaultFileName,
                Filter = "JSON Files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = "json"
            };

            if (sfd.ShowDialog() == true)
            {
                UberJSONProcessor.SaveFile(uberCollection.ToArray(), sfd.FileName);
            }
        }
        private void OpenFile(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = "json"
            };

            if (ofd.ShowDialog() == true)
            {
                DefaultFileName = System.IO.Path.GetFileName(ofd.FileName);
                FilePath = ofd.FileName;

                var result = UberJSONProcessor.ReadFile(ofd.FileName);
                DataItems.Clear();
                MainTabControl.Items.Clear();
                var items = UberToTable(result);

                foreach (var item in items)
                {
                    AddNewTable(item);
                }
            }
        }

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Проверяем, что выбрана новая вкладка
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is HandyControl.Controls.TabItem selectedTab)
            {
                // Если контент еще не создан (т.е. это не DataGrid)
                if (!(selectedTab.Content is DataGrid))
                {
                    // И если в Tag есть наши данные
                    if (selectedTab.Tag is DataItem item)
                    {
                        // Создаем DataGrid только сейчас
                        var dataGrid = new DataGrid
                        {
                            ItemsSource = item.Values.DefaultView,
                            AutoGenerateColumns = true,
                            CanUserAddRows = false,
                            HeadersVisibility = DataGridHeadersVisibility.Column,
                            Background = (System.Windows.Media.Brush)FindResource("RegionBrush"),
                            BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrush"),
                            BorderThickness = new Thickness(0, 1, 0, 0),
                            RowHeight = double.NaN
                        };

                        
                        // Создаем стиль для строки, который уберет фон выделения.
                        // Мы наследуем его от стиля по умолчанию, чтобы не потерять остальное оформление HandyControl.
                        var rowStyle = new Style(typeof(DataGridRow), dataGrid.RowStyle);
                        rowStyle.Setters.Add(new Setter(BackgroundProperty, (Brush)(new BrushConverter().ConvertFrom("#1c1c1c"))));
                        rowStyle.Setters.Add(new Setter(ForegroundProperty, Brushes.White));

                        var mouseOverTrigger = new Trigger { Property = DataGridRow.IsMouseOverProperty, Value = true };
                        // Устанавливаем прозрачный фон для выделенной строки.
                        mouseOverTrigger.Setters.Add(new Setter(BackgroundProperty, (Brush)(new BrushConverter().ConvertFrom("#252526")))); 
                        mouseOverTrigger.Setters.Add(new Setter(BorderBrushProperty, (Brush)(new BrushConverter().ConvertFrom("#252526"))));
                        rowStyle.Triggers.Add(mouseOverTrigger);

                        // Применяем стиль к строкам DataGrid.
                        dataGrid.RowStyle = rowStyle;
                        

                        // Привязываем горячие клавиши к DataGrid
                        dataGrid.InputBindings.Add(new KeyBinding(new RelayCommand(_ => DeleteSelectedRow(dataGrid)), Key.Delete, ModifierKeys.None));
                        dataGrid.InputBindings.Add(new KeyBinding(new RelayCommand(_ => TranslateSelectedRow(dataGrid)), Key.I, ModifierKeys.Control));
                        dataGrid.InputBindings.Add(new KeyBinding(new RelayCommand(_ => FormatSelectedRow(dataGrid)), Key.Q, ModifierKeys.Control));
                        dataGrid.ContextMenuOpening += DataGrid_ContextMenuOpening;
                        // Подписываемся на событие для настройки колонок
                        dataGrid.AutoGeneratingColumn += NestedDataGrid_AutoGeneratingColumn;
                        // Подписываемся на события для работы с памятью переводов
                        dataGrid.CellEditEnding += DataGrid_CellEditEnding;
                        dataGrid.CurrentCellChanged += DataGrid_CurrentCellChanged;

                        // Устанавливаем созданный DataGrid как контент вкладки
                        selectedTab.Content = dataGrid;
                    }
                }
            }
        }

        private void DataGrid_CurrentCellChanged(object sender, EventArgs e)
        {
            TranslationMemorySuggestions.Clear();
            OnlineSuggestions.Clear(); // Очищаем и онлайн-подсказки
            if (sender is DataGrid grid && grid.CurrentItem is DataRowView rowView)
            {
                var originalText = rowView["Original"]?.ToString();
                if (string.IsNullOrWhiteSpace(originalText)) return;

                var suggestions = new List<TMSuggestion>();

                foreach (var entry in _translationMemory)
                {
                    double similarity = CalculateSimilarity(originalText, entry.Key);
                    if (similarity > 0.5) // Порог схожести 50%
                    {
                        suggestions.Add(new TMSuggestion { Text = entry.Value, Similarity = similarity * 100 });
                    }
                }

                // Сортируем по убыванию схожести и добавляем в коллекцию
                foreach (var suggestion in suggestions.OrderByDescending(s => s.Similarity))
                {
                    TranslationMemorySuggestions.Add(suggestion);
                }

                if (!string.IsNullOrWhiteSpace(originalText))
                {
                    // 1. Проверяем наличие перевода в кэше
                    if (_onlineTranslationCache.TryGetValue(originalText, out var cachedSuggestions))
                    {
                        // Если нашли в кэше, добавляем оттуда
                        foreach (var suggestion in cachedSuggestions)
                        {
                            OnlineSuggestions.Add(suggestion);
                        }
                    }
                    else
                    {
                        // 2. Если в кэше нет, делаем запрос к API
                        var yTranslated = yTranslate.TranslateAsync(originalText, TargetLanguage, SourceLanguage).Result.Translation;
                        var gTranslated = gTranslate.TranslateAsync(originalText, TargetLanguage, SourceLanguage).Result.Translation;
                        var gTranslatedV2 = gTranslateV2.TranslateAsync(originalText, TargetLanguage, SourceLanguage).Result.Translation;

                        var newSuggestions = new List<OnlineSuggestion>
                        {
                            new OnlineSuggestion { Text = gTranslated, ServiceName = "Google" },
                            new OnlineSuggestion { Text = gTranslatedV2, ServiceName = "Google v2" },
                            new OnlineSuggestion { Text = yTranslated, ServiceName = "Yandex" }
                        };

                        // 3. Добавляем новые переводы в UI и сохраняем их в кэш
                        _onlineTranslationCache[originalText] = newSuggestions;
                        foreach (var suggestion in newSuggestions)
                        {
                            OnlineSuggestions.Add(suggestion);
                        }
                    }
                }
            }
        }

        private void DataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Commit && e.Column.Header.ToString() == "NewValue")
            {
                if (e.Row.Item is DataRowView rowView)
                {
                    var originalText = rowView["Original"]?.ToString();
                    var newValue = (e.EditingElement as System.Windows.Controls.TextBox)?.Text;

                    if (!string.IsNullOrWhiteSpace(originalText) && !string.IsNullOrWhiteSpace(newValue))
                    {
                        // Добавляем или обновляем запись в памяти переводов
                        _translationMemory[originalText] = newValue;
                    }
                }
            }
        }

        // Простой алгоритм Левенштейна для определения схожести строк
        public static double CalculateSimilarity(string source, string target)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target)) return 0.0;
            if (source == target) return 1.0;

            int stepsToSame = Levenshtein.GetDistance(source, target);
            return (1.0 - ((double)stepsToSame / (double)Math.Max(source.Length, target.Length)));
        }


        #region KeyBinding Handlers

        private void DeleteSelectedRow(DataGrid dataGrid)
        {
            DeleteSelectedRow(dataGrid.SelectedItem as DataRowView);
        }

        private void TranslateSelectedRow(DataGrid dataGrid)
        {
            TranslateSelectedRow(dataGrid.SelectedItem as DataRowView);
        }

        private void FormatSelectedRow(DataGrid dataGrid)
        {
            FormatSelectedRow(dataGrid.SelectedItem as DataRowView);
        }

        // Универсальные методы, вызываемые и по горячим клавишам, и из меню
        private void DeleteSelectedRow(DataRowView rowView)
        {
            rowView?.Row.Delete();
        }
        private void TranslateSelectedRow(DataRowView rowView)
        {
            if (rowView != null)
            {
                var original = rowView["Original"]?.ToString();
                Growl.Info($"Placeholder for translating row with 'Original' value: {original}");
            }
        }
        private void FormatSelectedRow(DataRowView rowView)
        {
            if (rowView != null)
            {
                var original = rowView["Original"]?.ToString();
                var newValue = rowView["NewValue"]?.ToString();
                Growl.Info($"Placeholder for formatting row: '{original}' | '{newValue}'");
            }
        }
        #endregion

        private void NewImport(object sender, RoutedEventArgs e)
        {
            ImportFromJson(true);
        }
        private void AddImport(object sender, RoutedEventArgs e)
        {
            ImportFromJson(false);
        }

        private void ImportFromJson(bool ClearAllItems = false)
        {
            if (ClearAllItems)
            {
                DataItems.Clear();
                MainTabControl.Items.Clear();
            }


            var uber = LoadUberJsonFromFileDialog();
            var items = UberToTable(uber);

            foreach(var item in items)
            {
                AddNewTable(item);
            }
        }

        private void SetMappings_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Unreal Mapping File (*.usmap)|*.usmap|All files (*.*)|*.*",
                DefaultExt = "usmap"
            };

            if (ofd.ShowDialog() == true)
            {
                DefaultFileName = System.IO.Path.GetFileName(ofd.FileName);
                Solicen.Kismet.AssetLoader.MappingsPath = ofd.FileName;
            }



        }

        private void AddNewTable_Click(object sender, RoutedEventArgs e)
        {
            var newTable = new DataTable();
            newTable.Columns.Add("Original");
            newTable.Columns.Add("NewValue");

            var newItem = new DataItem
            {
                FileName = $"New File {DataItems.Count + 1}",
                Values = newTable
            };
            AddNewTable(newItem);
        }

        #region Search Logic
        bool searchIsVisible = false;
        private void ShowSearchPanel()
        {
            searchIsVisible = !searchIsVisible;
            ChangeVisibilitySearch(searchIsVisible);
            SearchTextBox.Focus();
        }

        void ChangeVisibilitySearch(bool isVisible)
        {
            if (isVisible)
                SearchPanel.Visibility = Visibility.Visible;
            else
                SearchPanel.Visibility = Visibility.Collapsed;
        }

        private void CloseSearch_Click(object sender, RoutedEventArgs e)
        {
            searchIsVisible = false;
            ChangeVisibilitySearch(searchIsVisible);

            _searchResults.Clear();
            _currentSearchIndex = -1;
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var searchTerm = SearchTextBox.Text;
            _searchResults.Clear();
            _currentSearchIndex = -1;

            if (string.IsNullOrWhiteSpace(searchTerm) || searchTerm.Length < 2)
            {
                return;
            }

            // Проходим по всем вкладкам, даже незагруженным
            foreach (TabItem tab in MainTabControl.Items)
            {
                if (tab.Tag is DataItem item)
                {
                    var table = item.Values;
                    for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
                    {
                        for (int colIndex = 0; colIndex < table.Columns.Count; colIndex++)
                        {
                            var cellValue = table.Rows[rowIndex][colIndex]?.ToString();
                            if (!string.IsNullOrEmpty(cellValue) && cellValue.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                            {
                                _searchResults.Add(new SearchResult { Tab = tab, RowIndex = rowIndex, ColumnIndex = colIndex });
                            }
                        }
                    }
                }
            }

            if (_searchResults.Any())
            {
                _currentSearchIndex = 0;
                NavigateToResult(_currentSearchIndex);
            }
        }

        private void NavigateToResult(int index)
        {
            if (index < 0 || index >= _searchResults.Count) return;

            var result = _searchResults[index];

            // Переключаемся на нужную вкладку. Это вызовет ее загрузку, если она еще не загружена.
            MainTabControl.SelectedItem = result.Tab;

            // Даем WPF время на отрисовку содержимого вкладки
            Dispatcher.Invoke(() =>
            {
                if (result.Tab.Content is DataGrid grid)
                {
                    // Добавляем проверку на случай, если ItemsSource еще не полностью загружен
                    if (grid.Items.Count > result.RowIndex)
                    {
                        var item = grid.Items[result.RowIndex];
                        grid.SelectedItem = item;
                        grid.ScrollIntoView(item);
                    }
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded); // Используем более высокий приоритет
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_searchResults.Any()) return;
            _currentSearchIndex = (_currentSearchIndex + 1) % _searchResults.Count;
            NavigateToResult(_currentSearchIndex);
        }

        private void PrevButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_searchResults.Any()) return;
            _currentSearchIndex = (_currentSearchIndex - 1 + _searchResults.Count) % _searchResults.Count;
            NavigateToResult(_currentSearchIndex);
        }

        #endregion

    }
}