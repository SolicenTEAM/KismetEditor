using HandyControl.Controls;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using UAssetAPI.UnrealTypes;

namespace KissEGUI
{
    public class FileItem : INotifyPropertyChanged
    {
        public string FileName { get; set; }
        public string FilePath { get; set; }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    OnPropertyChanged(nameof(IsChecked));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public partial class TextureInjectorWindow : HandyControl.Controls.Window
    {
        public ObservableCollection<FileItem> AssetFiles { get; set; }
        public ObservableCollection<FileItem> TextureFiles { get; set; }
        public ObservableCollection<UEVersionItem> UEVersions { get; set; }
        public EngineVersion SelectedUEVersion { get; set; }


        public TextureInjectorWindow()
        {
            InitializeComponent();
            AssetFiles = new ObservableCollection<FileItem>();
            TextureFiles = new ObservableCollection<FileItem>();
            LoadUEVersions();
            AssetFiles.CollectionChanged += OnCollectionChanged;
            TextureFiles.CollectionChanged += OnCollectionChanged;
            this.DataContext = this;
        }

        private void LoadUEVersions()
        {
            UEVersions = new ObservableCollection<UEVersionItem>();
            foreach (EngineVersion version in Enum.GetValues(typeof(EngineVersion)))
            {
                if (version < 0 || version.ToString().Contains("AUTOMATIC") || version.ToString().Contains("OLDEST")) continue;
                var displayName = version.ToString().Replace("VER_", "");
                if (UEVersions.Any(x => x.DisplayName == displayName)) continue;
                UEVersions.Add(new UEVersionItem { DisplayName = displayName, VersionEnum = version });
            }
            SelectedUEVersion = UEVersions.LastOrDefault()?.VersionEnum ?? EngineVersion.UNKNOWN;
        }

        private void FileDrop_Handler(object sender, DragEventArgs e, ObservableCollection<FileItem> targetCollection, string[] allowedExtensions)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] droppedPaths = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var path in droppedPaths)
                {
                    // Проверяем, является ли перетаскиваемый элемент папкой
                    if (Directory.Exists(path))
                    {
                        // Если это папка, ищем все файлы рекурсивно
                        var filesInDir = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                                                  .Where(f => allowedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

                        foreach (var file in filesInDir)
                        {
                            if (!targetCollection.Any(f => f.FilePath.Equals(file, StringComparison.OrdinalIgnoreCase)))
                            {
                                targetCollection.Add(new FileItem { FilePath = file, FileName = Path.GetFileName(file), IsChecked = true });
                            }
                        }
                    }
                    // Если это файл
                    else if (File.Exists(path))
                    {
                        if (allowedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
                        {
                            if (!targetCollection.Any(f => f.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                            {
                                targetCollection.Add(new FileItem { FilePath = path, FileName = Path.GetFileName(path), IsChecked = true });
                            }
                        }
                    }
                }
            }
            e.Handled = true;
        }

        private void AssetList_Drop(object sender, DragEventArgs e)
        {
            FileDrop_Handler(sender, e, AssetFiles, new[] { ".uasset", ".umap" });
        }

        private void TextureList_Drop(object sender, DragEventArgs e)
        {
            FileDrop_Handler(sender, e, TextureFiles, new[] { ".png" });
        }

        private void DragEnter_Handler(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void BrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFolderDialog();
            if (ofd.ShowDialog() == true)
            {
                OutputDirectoryTextBox.Text = ofd.FolderName;
            }
        }

        private void Inject_Click(object sender, RoutedEventArgs e)
        {
            var checkedAssets = AssetFiles.Where(f => f.IsChecked).ToList();
            var checkedTextures = TextureFiles.Where(f => f.IsChecked).ToList();
            var pairs = new List<Tuple<FileItem, FileItem>>();

            foreach (var asset in checkedAssets)
            {
                var assetNameWithoutExt = Path.GetFileNameWithoutExtension(asset.FileName);
                var matchingTexture = checkedTextures.FirstOrDefault(tex =>
                    Path.GetFileNameWithoutExtension(tex.FileName).Equals(assetNameWithoutExt, StringComparison.OrdinalIgnoreCase));

                if (matchingTexture != null)
                {
                    pairs.Add(new Tuple<FileItem, FileItem>(asset, matchingTexture));
                }
            }

            if (pairs.Any())
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Found {pairs.Count} pairs to process:");
                foreach (var pair in pairs)
                {
                    Solicen.Kismet.AssetLoader.SetVersion(SelectedUEVersion);

                    bool hasUbulk = File.Exists(Path.ChangeExtension(pair.Item1.FilePath, ".ubulk"));
                    if (!hasUbulk && SkipNonTextureCheckBox.IsChecked.Value) continue;

                    var asset = Solicen.Kismet.AssetLoader.LoadAsset(pair.Item1.FilePath);
                    if (asset != null)
                    {
                        var textureData = Solicen.Kismet.AssetMod.GetTextureData(asset);
                        var texConv = new TexConv();
                        var assetFormat = texConv.TextureFormatFromUE(textureData);
                        var texture = Array.Empty<byte>();
                        var pngPath = pair.Item2.FilePath;
                        var filter = UseCubricCheckBox.IsChecked.Value ? "CUBIC" : "LINEAR";

                        var tex = texConv.ConvertPngToDds(pngPath, assetFormat, filter);
                        var dds = texConv.ParseDds(File.ReadAllBytes(tex));

                        // Если размер данных текстуры равен нулю (по умолчанию) выходим - ошибка
                        if (dds.Texture.Length == 0) return;

                        int size = dds.Texture.Length;
                        texture = new byte[size];
                        Array.Copy(dds.Texture, 0, texture, 0, size);

                        if (texture.Any())
                        {
                            var outputDir = OutputDirectoryTextBox.Text;
                            if (!Directory.Exists(outputDir))
                                Directory.CreateDirectory(outputDir);

                            var virtualPath = pair.Item1.FilePath.UE_FolderWithoutFileName();
                            var fullPath = $"{outputDir}\\{virtualPath}";
                            var fileName = Path.GetFileNameWithoutExtension(pair.Item1.FilePath);

                            Directory.CreateDirectory(fullPath);
                            File.WriteAllBytes($"{fullPath}\\{fileName}.ubulk", texture);
                        }
                    }
                    else
                    {
                        Growl.Error("Error loading the asset. Specify the correct version and try again.");
                        return;
                    }

                    sb.AppendLine($" - Asset: {pair.Item1.FileName}, Texture: {pair.Item2.FileName}");
                }
                Growl.Info(sb.ToString());
            }
            else
            {
                Growl.Warning("No matching pairs found between checked assets and textures.");
            }
        }

        private void ClearAssets_Click(object sender, RoutedEventArgs e)
        {
            AssetFiles.Clear();
        }

        private void ClearTextures_Click(object sender, RoutedEventArgs e)
        {
            TextureFiles.Clear();
        }

        private void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // Запускаем логику только если чекбокс активен
            if (UncheckNonPairedCheckBox?.IsChecked == true)
            {
                UpdatePairedFileChecks();
            }
        }

        private void UncheckNonPaired_Changed(object sender, RoutedEventArgs e)
        {
            // При изменении состояния чекбокса также применяем логику
            if (UncheckNonPairedCheckBox?.IsChecked == true)
            {
                UpdatePairedFileChecks();
            }
        }

        private void UpdatePairedFileChecks()
        {
            if (AssetFiles == null || TextureFiles == null) return;

            // Создаем множества имен файлов (без расширений) для быстрого поиска
            var assetNames = new HashSet<string>(AssetFiles.Select(f => Path.GetFileNameWithoutExtension(f.FileName)), StringComparer.OrdinalIgnoreCase);
            var textureNames = new HashSet<string>(TextureFiles.Select(f => Path.GetFileNameWithoutExtension(f.FileName)), StringComparer.OrdinalIgnoreCase);

            // Снимаем флажки с ассетов, у которых нет пары
            foreach (var asset in AssetFiles.Where(a => !textureNames.Contains(Path.GetFileNameWithoutExtension(a.FileName))))
            {
                asset.IsChecked = false;
            }

            // Снимаем флажки с текстур, у которых нет пары
            foreach (var texture in TextureFiles.Where(t => !assetNames.Contains(Path.GetFileNameWithoutExtension(t.FileName))))
            {
                texture.IsChecked = false;
            }

            // Ставим у флажки у ассетов, у которых есть пара
            foreach (var asset in AssetFiles.Where(a => textureNames.Contains(Path.GetFileNameWithoutExtension(a.FileName))))
            {
                asset.IsChecked = true;
            }

            // Ставим у флажки у текстур, у которых есть пара
            foreach (var texture in TextureFiles.Where(t => assetNames.Contains(Path.GetFileNameWithoutExtension(t.FileName))))
            {
                texture.IsChecked = true;
            }
        }
    }
}