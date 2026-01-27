using Newtonsoft.Json;
using Solicen.CLI;
using Solicen.JSON;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UAssetAPI;
using UAssetAPI.FieldTypes;
using UAssetAPI.Unversioned;

namespace Solicen.Kismet
{
    internal static class BytecodeExtractor
    {
        public static bool AllowTableExtract = false;
        public static UAsset Asset;

        public static void ExtractAllAndWriteUberJSON(string asset, bool allowUnderscore = false, bool allowLocalized = false, string uberName = "UberJSON") => ExtractAllAndWriteUberJSON(new string[] { asset }, uberName: uberName);
        public static void ExtractAllAndWriteUberJSON(string[] assets, bool allowUnderscore = false, bool allowLocalized = false, string uberName = "UberJSON")
        {
            MapParser.AllowLocalizedSource = allowLocalized;
            MapParser.AllowUnderscore = allowUnderscore;
            var UberJSONName = assets.Length == 1 ? Path.GetFileNameWithoutExtension(assets[0]) : uberName;
            var JsonFilePath = $"{EnvironmentHelper.AssemblyDirectory}\\{UberJSONName}.json";
            var uberJSONCollection = new List<Solicen.JSON.UberJSON>();
            foreach (var asset in assets)
            {
                var FileName = Path.GetFileName(asset);
                CLI.Console.WriteLine($"[DarkGray][INF] [White]...{FileName}");
                Asset = AssetLoader.LoadAsset(asset);
                if (Asset == null || KismetExtension.GetExportCount(Asset) == 0)
                {
                    Asset = null; return;
                }
                var allValues = ExtractValues(asset);
                if (allValues.Length > 0)
                {
                    var uberJSON = new UberJSON(FileName);
                    foreach (var value in allValues)
                    {
                        uberJSON.Add(new KismetString() { KeyValue = value.KeyValue, Original = value.Value });
                    }
                    uberJSONCollection.Add(uberJSON);
                }
            }
            #region Сохранение UberJSON
            if (File.Exists(JsonFilePath))
            {
                // Производим слияние двух файлов чтобы не потерять их
                var mergeJson = UberJSONProcessor.ReadFile(JsonFilePath);
                var merged = mergeJson.Merge(uberJSONCollection.ToArray());
                if (merged != null)
                {
                    merged.SaveFile(JsonFilePath);
                }     
            }
            else
            {
                uberJSONCollection.ToArray().SaveFile(JsonFilePath);
            }
            #endregion
            CLI.Console.WriteLine($"[Green][SUCCESS] [White]File with extracted strings was successfully saved in:\n[DarkGray]{JsonFilePath}\n");
        }

        public static void ExtractAndWriteCSV(string assetPath, string FileName = "")
        {
            Asset = AssetLoader.LoadAsset(assetPath); List<string> AllExtractedStr = new List<string>();
            if (Asset == null) return;

            //AllExtractedStr.AddRange(ExtractValues(assetPath));
            FileName = FileName == string.Empty ? Path.GetFileNameWithoutExtension(assetPath) : FileName;
            if (AllExtractedStr.Count == 0) return;

            #region Запись CSV
            var csvFilePath = FileName.EndsWith(".csv") ? FileName : $"{EnvironmentHelper.AssemblyDirectory}\\{FileName}.csv";
            if (File.Exists(csvFilePath))
            {
                // Если CSV уже существует, просто добавить новые строки
                var csv = File.ReadAllLines(csvFilePath);
                var lines = AllExtractedStr.Except(csv.Select(x => x.Split('|')[0])); lines.ToList().Add("\n");
                File.AppendAllLines(csvFilePath, lines);
            }
            else
            {
                // Иначе просто записать строки в файл
                File.WriteAllText(csvFilePath, string.Join("\n", AllExtractedStr));
            }
            CLI.Console.WriteLine($"[Green][SUCCESS] [White] File with extracted strings was successfully saved in:\n[DarkGray]{csvFilePath}\n");
            #endregion
        }

        public static LObject[] ExtractEachStrProperty()
        {
            var strProperty = MapParser.ExtractStrProperties(Asset);
            if (strProperty != null && strProperty.Length > 0)
            {
                MapParser.OutputInformation("StrProperty", strProperty);
                return strProperty;
            }
            return Array.Empty<LObject>();
        }

        public static LObject[] ExtractEachTextProperty()
        {
            if (AllowTableExtract) // Так как это рискованная операция, относиться к --all
            {
                var textProperty = MapParser.ExtractTextProperties(Asset);
                if (textProperty != null && textProperty.Length > 0)
                {
                    MapParser.OutputInformation("TextProperty", textProperty);
                    return textProperty;
                }
            }
            return Array.Empty<LObject>();
        }

        public static LObject[] ExtractEachAnyTableValue()
        {
            if (AllowTableExtract)
            {
                var allTable = new List<LObject>();
                var stringTable = MapParser.ExtractStringTableEntries(Asset);
                var dataTable = MapParser.ExtractDataTableEntries(Asset);

                allTable.AddRange(stringTable);
                allTable.AddRange(dataTable);

                if (allTable.Count > 0)
                {
                    MapParser.OutputInformation("Table", allTable.ToArray());
                    return allTable.ToArray();
                }
            }
            return Array.Empty<LObject>();
        }

        public static LObject[] ExtractFromUbergraph(string assetPath)
        {
            var ubergraph = KismetExtension.GetUbergraphSerialized(Asset);
            if (ubergraph != null && ubergraph.Count > 0)
            {
                var kismets = MapParser.ParseUbergraph(ubergraph);
                if (kismets != null && kismets.Length > 0) MapParser.OutputInformation("Ubergraph", kismets);
                return kismets;
            }
            else
            {
                return Array.Empty<LObject>();
            }

        }

        public static LObject[] ExtractValues(string assetPath)
        {
            List<LObject> AllExtractedStr = new List<LObject>();
            #region Получение строк любого вида
            var propStr = ExtractEachStrProperty(); // Получаем строки из каждого StrProperty
            var ubergraphStr = ExtractFromUbergraph(assetPath); // Получаем строки из ExecuteUbergraph
            var tableValues = ExtractEachAnyTableValue(); // Получаем строки из каждой таблицы
            var textValues = ExtractEachTextProperty(); // Получаем строки из каждого TextProperty
            #endregion

            if (ubergraphStr != null) AllExtractedStr.AddRange(ubergraphStr);
            if (textValues != null) AllExtractedStr.AddRange(textValues);
            if (propStr != null) AllExtractedStr.AddRange(propStr);
            if (tableValues != null) AllExtractedStr.AddRange(tableValues);
            if (AllExtractedStr.Count > 0) System.Console.WriteLine();
            return AllExtractedStr.ToArray();
        }
    }
}