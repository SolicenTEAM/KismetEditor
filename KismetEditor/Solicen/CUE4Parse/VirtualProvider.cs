/*
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UAssetAPI;
using UAssetAPI.UnrealTypes;
using static CUE4Parse.UE4.Objects.StructUtils.FInstancedPropertyBag;

namespace Solicen.CUE4Parse
{
    internal class VirtualProvider
    {
        public DefaultFileProvider provider;
        EngineVersion engineVersion;

        public VirtualProvider(string fromFolder, string stringVersion)
        {
            // Преобразуем версию для CUE4Parse
            EGame cue4ParseVersion = EGame.GAME_UE4_18;
            Enum.TryParse($"GAME_{stringVersion.Replace(".", "_")}", out cue4ParseVersion);

            // Преобразуем и сохраняем версию для UAssetAPI
            string uassetApiVersionString = $"VER_{stringVersion.Replace(".", "_")}";
            if (!Enum.TryParse(uassetApiVersionString, out this.engineVersion))
            {
                // Устанавливаем версию по умолчанию, если не удалось распознать
                this.engineVersion = EngineVersion.UNKNOWN;
                CLI.Console.WriteLine($"[Warning] Unknown engine version '{stringVersion}'. UAssetAPI will use a default value.");
            }

            this.provider = new DefaultFileProvider(fromFolder, SearchOption.AllDirectories, true, new VersionContainer(cue4ParseVersion));
            provider.Initialize();
            provider.LoadVirtualPaths();
            provider.Mount();
            provider.PostMount();
        }
        public string[] GetAssets()
        {
            if (provider != null)
            {
                var validExtensions = new[] { ".uasset", ".umap" };
                var assets = this.provider.Files.Keys.Where(x => validExtensions
                .Any(ext => x.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                .Where(x => !x.Contains("Engine/")) // Очищение: работаем только с файлами не из Engine папки.
                .ToList();
                return assets.ToArray();
            }
            return new string[] {};
        }

        public UAsset FromGameFile(string assetPath)
        {
            if (provider == null) return null;

            // 1. CUE4Parse извлекает данные ассета в виде байтовых массивов
            if (provider.TrySavePackage(assetPath, out IReadOnlyDictionary<string, byte[]> data))
            {
                var uassetData = data.FirstOrDefault(x => x.Key.EndsWith(".uasset") || x.Key.EndsWith(".umap")).Value;
                var uexpData = data.FirstOrDefault(x => x.Key.EndsWith(".uexp")).Value;
                // 2. Создаем UAsset, передавая данные через MemoryStream
                var asset = new UAsset(this.engineVersion);

                // 3. UAssetAPI требует, чтобы мы читали данные через его собственный ридер
                using (var uassetStream = new MemoryStream(uassetData))
                using (var uexpStream = uexpData != null ? new MemoryStream(uexpData) : null)
                {
                    asset.Read(new AssetBinaryReader(uassetStream, false, asset));
                    asset.Read(new AssetBinaryReader(uexpStream, true, asset));
                }

                Solicen.CLI.Console.WriteLine($"[Success] Ассет '{assetPath}' успешно преобразован в UAsset.");
                return asset;
            }
            else
            {
                Solicen.CLI.Console.WriteLine($"[Error] Не удалось найти или загрузить ассет '{assetPath}' с помощью CUE4Parse.");
                return null;
            }

            try
            {
                
            }
            catch (Exception ex)
            {
                Solicen.CLI.Console.WriteLine($"[Error] Произошла ошибка при преобразовании: {ex.Message}");
                return null;
            }
        }
    }
}
*/
