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

        public static void ExtractAllAndWriteUberJSON(string asset, bool allowUnderscore = false, bool allowLocalized = false) => ExtractAllAndWriteUberJSON(new string[] { asset });
        public static void ExtractAllAndWriteUberJSON(string[] assets, bool allowUnderscore = false, bool allowLocalized = false)
        {
            MapParser.AllowLocalizedSource = allowLocalized;
            MapParser.AllowUnderscore = allowUnderscore;
            var JsonFilePath = $"{EnvironmentHelper.AssemblyDirectory}\\UberJSON.json";
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
                var values = ExtractValues(asset);
                if (values.Length > 0)
                {
                    var uberJSON = new UberJSON(FileName);
                    uberJSON.AddRange(values.ToArray());
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
                    merged.SaveFile(JsonFilePath);
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

            AllExtractedStr.AddRange(ExtractValues(assetPath));
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

        public static string[] ExtractEachStrProperty()
        {
            var strProperty = MapParser.ExtractStrProperties(Asset);
            if (strProperty != null && strProperty.Length > 0)
            {

                CLI.Console.Write("  [DarkYellow][StrProperty] ");
                CLI.Console.Separator(36, true, ConsoleColor.DarkYellow);
                var values =  MapParser.ParseAsCSV(strProperty);
                return values;
            }
            return new string[] { };
        }

        public static string[] ExtractEachAnyTableValue()
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
                    CLI.Console.Write("  [DarkYellow][Table] ");
                    CLI.Console.Separator(36, true, ConsoleColor.DarkYellow);
                    var values = MapParser.ParseAsCSV(allTable.ToArray());
                    return values;
                }
            }
            return new string[] { };
        }

        public static string[] ExtractFromUbergraph(string assetPath)
        {
            var ubergraph = KismetExtension.GetUbergraphSerialized(Asset);
            if (ubergraph != null && ubergraph.Count > 0)
            {
                if (CLIHandler.Config.DebugMode)
                {
                    var _dummyJson = KismetExtension.GetUbergraphJson(Asset);
                    var serializer = new KismetExpressionSerializer(Asset);
                    var u = KismetExtension.GetUbergraph(Asset);

                    File.WriteAllText($"{Environment.CurrentDirectory}\\{Path.GetFileNameWithoutExtension(assetPath)}_DUMMY.json",
                        serializer.SerializeExpressionArray(u).ToString());

                }
                return MapParser.ParseUbergraph(ubergraph);
            }
            else
            {
                return Array.Empty<string>();
            }

        }

        public static string[] ExtractValues(string assetPath)
        {
            List<string> AllExtractedStr = new List<string>();
            #region Получение строк любого вида
            var propStr = ExtractEachStrProperty(); // Получаем строки из каждого StrProperty
            var ubergraphStr = ExtractFromUbergraph(assetPath); // Получаем строки из ExecuteUbergraph
            var tableValues = ExtractEachAnyTableValue();
            #endregion

            if (ubergraphStr != null) AllExtractedStr.AddRange(ubergraphStr);
            if (propStr != null) AllExtractedStr.AddRange(propStr);
            if (tableValues != null) AllExtractedStr.AddRange(tableValues);
            if (AllExtractedStr.Count > 0) System.Console.WriteLine();
            return AllExtractedStr.ToArray();
        }
    }
}